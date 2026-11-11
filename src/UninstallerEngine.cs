using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace DeepUninstaller
{
    public class CleanupReport
    {
        public int DeletedDirectories { get; set; }
        public int DeletedFiles { get; set; }
        public int DeletedRegistryItems { get; set; }
        public int FailedItems { get; set; }
        public long FreedBytes { get; set; }
        public string BackupFolderPath { get; set; }
        public List<string> Logs { get; set; }

        public CleanupReport()
        {
            Logs = new List<string>();
        }
    }

    public static class UninstallerEngine
    {
        public static string BackupRootDirectory
        {
            get
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups");
                if (!Directory.Exists(dir))
                {
                    try { Directory.CreateDirectory(dir); } catch { }
                }
                return dir;
            }
        }

        public static bool IsRunningAsAdmin()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool RestartAsAdmin()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Vérifie si le dossier contenant DeepUninstaller.exe est un dossier dédié à l'application
        /// (et non un dossier partagé comme Bureau, Téléchargements, Documents ou la racine d'un disque).
        /// </summary>
        public static bool IsDedicatedAppDirectory(string dirPath)
        {
            if (string.IsNullOrWhiteSpace(dirPath) || AppScanner.IsProtectedSystemDirectory(dirPath))
                return false;

            try
            {
                string full = Path.GetFullPath(dirPath).TrimEnd('\\', '/');
                if (full.Length <= 3) return false;

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\', '/');
                if (string.IsNullOrEmpty(userProfile))
                    userProfile = (Environment.GetEnvironmentVariable("USERPROFILE") ?? "").TrimEnd('\\', '/');

                string[] sharedUserFolders = new[]
                {
                    userProfile,
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory).TrimEnd('\\', '/'),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory).TrimEnd('\\', '/'),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments).TrimEnd('\\', '/'),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments).TrimEnd('\\', '/'),
                    Path.Combine(userProfile, "Desktop"),
                    Path.Combine(userProfile, "Bureau"),
                    Path.Combine(userProfile, "Documents"),
                    Path.Combine(userProfile, "Downloads"),
                    Path.Combine(userProfile, "Téléchargements")
                };

                foreach (var shared in sharedUserFolders)
                {
                    if (!string.IsNullOrWhiteSpace(shared) && string.Equals(full, shared, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                string folderName = Path.GetFileName(full);
                string[] sharedLeafNames = { "Desktop", "Bureau", "Documents", "Downloads", "Téléchargements", "Users", "Utilisateurs" };
                foreach (var leaf in sharedLeafNames)
                {
                    if (string.Equals(folderName, leaf, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Auto-désinstallation totale de DeepUninstaller Pro :
        /// supprime toutes les sauvegardes, raccourcis, traces du Registre Windows (MuiCache, AppCompatFlags, AppSwitched),
        /// fichiers temporaires, Prefetch, ainsi que l'exécutable et le dossier complet de l'application.
        /// </summary>
        public static void ExecuteSelfUninstall()
        {
            string exePath = null;
            try { exePath = Process.GetCurrentProcess().MainModule.FileName; } catch { }
            if (string.IsNullOrWhiteSpace(exePath))
            {
                exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DeepUninstaller.exe");
            }

            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            bool deleteWholeFolder = IsDedicatedAppDirectory(appDir);

            // 1. Nettoyer toutes les traces d'exécution dans le Registre Windows (HKCU & HKLM)
            string[] traceKeys = new[]
            {
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
                @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store",
                @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\AppSwitched",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\ShowJumpView",
                @"Software\Microsoft\Windows\CurrentVersion\Run"
            };

            foreach (var subKeyPath in traceKeys)
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(subKeyPath, true))
                    {
                        if (key != null)
                        {
                            foreach (var valName in key.GetValueNames())
                            {
                                if (valName.IndexOf("DeepUninstaller", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    valName.IndexOf("Appli-Desintalleur", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    (!string.IsNullOrWhiteSpace(exePath) && valName.IndexOf(exePath, StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    try { key.DeleteValue(valName, false); } catch { }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\DeepUninstaller", false); } catch { }
            try { Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\DeepUninstaller", false); } catch { }

            // 2. Supprimer tout raccourci éventuel sur le Bureau et dans le Menu Démarrer
            string[] shortcutFolders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
            };

            foreach (var folder in shortcutFolders)
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
                try
                {
                    foreach (var f in Directory.GetFiles(folder, "*DeepUninstaller*", System.IO.SearchOption.AllDirectories))
                    {
                        try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { }
                    }
                }
                catch { }
            }

            // 3. Supprimer les fichiers temporaires et Prefetch associés à DeepUninstaller
            try
            {
                string tempDir = Path.GetTempPath();
                foreach (var f in Directory.GetFiles(tempDir, "*DeepUninstaller*", System.IO.SearchOption.TopDirectoryOnly))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { }
                }
                foreach (var d in Directory.GetDirectories(tempDir, "*DeepUninstaller*", System.IO.SearchOption.TopDirectoryOnly))
                {
                    try { ClearReadOnlyAttributes(new DirectoryInfo(d)); Directory.Delete(d, true); } catch { }
                }
            }
            catch { }

            try
            {
                string prefetchDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
                if (Directory.Exists(prefetchDir))
                {
                    foreach (var pf in Directory.GetFiles(prefetchDir, "DEEPUNINSTALLER*.pf", System.IO.SearchOption.TopDirectoryOnly))
                    {
                        try { File.SetAttributes(pf, FileAttributes.Normal); File.Delete(pf); } catch { }
                    }
                }
            }
            catch { }

            // 4. Supprimer uniquement le dossier Backups/ créé par l'application et les fichiers propres à la distribution de l'application
            string backupsDir = Path.Combine(appDir, "Backups");
            if (Directory.Exists(backupsDir))
            {
                try { ClearReadOnlyAttributes(new DirectoryInfo(backupsDir)); Directory.Delete(backupsDir, true); } catch { }
            }

            string[] appOwnFiles = new[]
            {
                "DeepUninstaller.old.exe",
                "Lancer_DeepUninstaller.bat",
                "Lancer_En_Administrateur.bat",
                "README.md",
                "SECURITY.md",
                "DISCLAIMER.md",
                "LICENSE"
            };
            foreach (var kf in appOwnFiles)
            {
                string fp = Path.Combine(appDir, kf);
                if (File.Exists(fp))
                {
                    try { File.SetAttributes(fp, FileAttributes.Normal); File.Delete(fp); } catch { }
                }
            }

            // 5. Supprimer DeepUninstaller.exe dès la fermeture du processus, puis supprimer le dossier parent
            // UNIQUEMENT s'il est vide (rmdir SANS /s pour ne jamais supprimer un autre fichier personnel présent dans le dossier)
            try
            {
                string cmdArgs;
                if (deleteWholeFolder)
                {
                    cmdArgs = string.Format("/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{0}\" > nul 2>&1 & rmdir \"{1}\" > nul 2>&1", exePath, appDir);
                }
                else
                {
                    cmdArgs = string.Format("/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{0}\" > nul 2>&1", exePath);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    WorkingDirectory = Path.GetTempPath(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
            catch { }

            Environment.Exit(0);
        }

        /// <summary>
        /// Ferme les processus actifs appartenant au dossier d'installation de l'application
        /// </summary>
        public static int KillRunningAppProcesses(string installLocation, List<string> logs = null)
        {
            if (string.IsNullOrWhiteSpace(installLocation) || AppScanner.IsProtectedSystemDirectory(installLocation))
                return 0;

            return StopServicesAndKillProcessesForPaths(new[] { installLocation }, logs);
        }

        /// <summary>
        /// Arrête les services Windows associés, tue les processus exécutés depuis les dossiers cibles
        /// et utilise l'API Windows Restart Manager (rstrtmgr.dll) pour libérer les fichiers verrouillés.
        /// </summary>
        public static int StopServicesAndKillProcessesForPaths(IEnumerable<string> targetPaths, List<string> logs = null)
        {
            if (targetPaths == null) return 0;

            var validDirs = new List<string>();
            var targetExeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidateLockedFiles = new List<string>();

            foreach (var raw in targetPaths)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try
                {
                    string clean = raw.TrimEnd('\\', '/');
                    if (Directory.Exists(clean) && !AppScanner.IsProtectedSystemDirectory(clean))
                    {
                        validDirs.Add(clean);
                        try
                        {
                            foreach (var f in Directory.GetFiles(clean, "*.*", System.IO.SearchOption.AllDirectories))
                            {
                                string ext = Path.GetExtension(f);
                                if (string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    string exeNoExt = Path.GetFileNameWithoutExtension(f);
                                    if (!string.IsNullOrWhiteSpace(exeNoExt) && exeNoExt.Length >= 4)
                                        targetExeNames.Add(exeNoExt);
                                    if (candidateLockedFiles.Count < 64)
                                        candidateLockedFiles.Add(f);
                                }
                                else if (string.Equals(ext, ".dll", StringComparison.OrdinalIgnoreCase) && candidateLockedFiles.Count < 64)
                                {
                                    candidateLockedFiles.Add(f);
                                }
                            }
                        }
                        catch { }
                    }
                    else if (File.Exists(clean))
                    {
                        candidateLockedFiles.Add(clean);
                    }
                }
                catch { }
            }

            if (validDirs.Count == 0 && candidateLockedFiles.Count == 0)
                return 0;

            int totalKilled = 0;

            // 1. Arrêter les services Windows dont l'exécutable pointe vers l'un des dossiers cibles
            StopMatchingWindowsServices(validDirs, logs);

            // 2. Tuer uniquement les processus de l'application (ne JAMAIS tuer un processus système Windows ou explorer.exe)
            int currentPid = Process.GetCurrentProcess().Id;
            var protectedProcessNames = GetProtectedSystemProcessNames();

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id == currentPid || proc.Id <= 4) continue;
                    if (IsSystemOrProtectedProcess(proc, protectedProcessNames)) continue;
                    string pName = proc.ProcessName ?? "";

                    bool shouldKill = false;
                    if (targetExeNames.Contains(pName))
                    {
                        shouldKill = true;
                    }
                    else
                    {
                        string procPath = null;
                        try { procPath = proc.MainModule != null ? proc.MainModule.FileName : null; } catch { }
                        if (!string.IsNullOrWhiteSpace(procPath))
                        {
                            foreach (var dir in validDirs)
                            {
                                if (procPath.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(procPath, dir, StringComparison.OrdinalIgnoreCase))
                                {
                                    shouldKill = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (shouldKill)
                    {
                        proc.Kill();
                        proc.WaitForExit(3000);
                        totalKilled++;
                        if (logs != null)
                            logs.Add(string.Format("[Processus arrêté] {0} (PID {1})", pName, proc.Id));
                    }
                }
                catch { }
                finally
                {
                    try { proc.Dispose(); } catch { }
                }
            }

            // 3. Utiliser Windows Restart Manager (rstrtmgr.dll) pour détecter tout processus tiers (hors système Windows) verrouillant les fichiers
            if (candidateLockedFiles.Count > 0)
            {
                totalKilled += KillLockingProcessesWithRestartManager(candidateLockedFiles, currentPid, protectedProcessNames, logs);
            }

            return totalKilled;
        }

        private static HashSet<string> GetProtectedSystemProcessNames()
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "explorer", "svchost", "csrss", "lsass", "wininit", "winlogon", "services", "smss",
                "System", "Idle", "dwm", "sihost", "taskhostw", "SearchHost", "SearchIndexer",
                "SearchProtocolHost", "SearchFilterHost", "StartMenuExperienceHost", "ShellExperienceHost",
                "RuntimeBroker", "dllhost", "ApplicationFrameHost", "ctfmon", "fontdrvhost", "audiodg",
                "spoolsv", "conhost", "WindowsTerminal", "OpenConsole", "cmd", "powershell", "pwsh",
                "msiexec", "taskmgr", "WmiPrvSE", "SecurityHealthService", "SecurityHealthSystray",
                "MsMpEng", "smartscreen", "Widgets", "WidgetService", "TextInputHost"
            };
        }

        private static bool IsSystemOrProtectedProcess(Process proc, HashSet<string> protectedProcessNames)
        {
            if (proc == null) return true;
            try
            {
                string pName = proc.ProcessName ?? "";
                if (protectedProcessNames != null && protectedProcessNames.Contains(pName))
                    return true;

                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string procPath = proc.MainModule != null ? proc.MainModule.FileName : null;
                if (!string.IsNullOrWhiteSpace(procPath) && !string.IsNullOrWhiteSpace(winDir))
                {
                    if (procPath.StartsWith(winDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static void StopMatchingWindowsServices(List<string> validDirs, List<string> logs)
        {
            if (validDirs == null || validDirs.Count == 0) return;
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var servicesKey = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", false))
                {
                    if (servicesKey == null) return;
                    foreach (var svcName in servicesKey.GetSubKeyNames())
                    {
                        try
                        {
                            using (var svcKey = servicesKey.OpenSubKey(svcName, false))
                            {
                                if (svcKey == null) continue;
                                string imagePath = (svcKey.GetValue("ImagePath") ?? "").ToString();
                                if (string.IsNullOrWhiteSpace(imagePath)) continue;

                                bool matches = validDirs.Any(d => imagePath.IndexOf(d, StringComparison.OrdinalIgnoreCase) >= 0);
                                if (matches)
                                {
                                    RunCommandSilent("sc.exe", string.Format("stop \"{0}\"", svcName), 4000);
                                    if (IsRunningAsAdmin())
                                    {
                                        RunCommandSilent("sc.exe", string.Format("delete \"{0}\"", svcName), 3000);
                                    }
                                    if (logs != null)
                                        logs.Add(string.Format("[Service Windows arrêté] {0}", svcName));
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static int KillLockingProcessesWithRestartManager(
            List<string> filePaths,
            int currentPid,
            HashSet<string> protectedProcessNames,
            List<string> logs)
        {
            int killed = 0;
            uint sessionHandle;
            string sessionKey = Guid.NewGuid().ToString();

            if (RmStartSession(out sessionHandle, 0, sessionKey) != 0)
                return 0;

            try
            {
                string[] resources = filePaths.ToArray();
                if (RmRegisterResources(sessionHandle, (uint)resources.Length, resources, 0, null, 0, null) != 0)
                    return 0;

                uint pnProcInfoNeeded = 0;
                uint pnProcInfo = 0;
                uint lpdwRebootReasons = 0;

                int res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);
                if (res == 234 && pnProcInfoNeeded > 0) // ERROR_MORE_DATA
                {
                    pnProcInfo = pnProcInfoNeeded;
                    var processInfo = new RM_PROCESS_INFO[pnProcInfo];
                    res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);
                    if (res == 0)
                    {
                        for (int i = 0; i < pnProcInfo; i++)
                        {
                            int pid = processInfo[i].Process.dwProcessId;
                            if (pid == currentPid || pid <= 4) continue;

                            try
                            {
                                using (var p = Process.GetProcessById(pid))
                                {
                                    if (IsSystemOrProtectedProcess(p, protectedProcessNames)) continue;
                                    string pName = p.ProcessName ?? "";

                                    p.Kill();
                                    p.WaitForExit(3000);
                                    killed++;
                                    if (logs != null)
                                        logs.Add(string.Format("[Verrou libéré (RestartManager)] Processus {0} (PID {1}) arrêté", pName, pid));
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                try { RmEndSession(sessionHandle); } catch { }
            }

            return killed;
        }

        /// <summary>
        /// Crée un point de restauration Windows (nécessite les droits Administrateur)
        /// </summary>
        public static bool CreateSystemRestorePoint(string description, out string message)
        {
            if (!IsRunningAsAdmin())
            {
                message = "Droits Administrateur requis pour créer un point de restauration Windows.";
                return false;
            }

            try
            {
                string safeDesc = (description ?? "DeepUninstaller").Replace("'", "");
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = string.Format("-NoProfile -ExecutionPolicy Bypass -Command \"Checkpoint-Computer -Description '{0}' -RestorePointType 'APPLICATION_UNINSTALL'\"", safeDesc),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(45000);
                    if (proc.ExitCode == 0)
                    {
                        message = "Point de restauration système créé avec succès.";
                        return true;
                    }
                    else
                    {
                        string err = proc.StandardError.ReadToEnd();
                        message = "Point de restauration ignoré : " + (string.IsNullOrWhiteSpace(err) ? "Fréquence minimale Windows (24h) ou restauration désactivée." : err.Trim());
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                message = "Impossible de créer le point de restauration : " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Lance le désinstallateur officiel de l'application et attend sa fin
        /// </summary>
        public static bool RunOfficialUninstaller(InstalledApp app, bool silentMode, out string statusMessage)
        {
            string rawCmd = silentMode && !string.IsNullOrWhiteSpace(app.QuietUninstallString)
                ? app.QuietUninstallString
                : app.UninstallString;

            if (string.IsNullOrWhiteSpace(rawCmd))
            {
                statusMessage = "Aucune commande de désinstallation trouvée dans le registre.";
                return false;
            }

            try
            {
                string fileName;
                string arguments;
                ParseCommandLine(rawCmd, silentMode, app.IsWindowsInstaller, out fileName, out arguments);

                // Enregistrer les processus existants avant le lancement pour détecter les sous-processus (ex: unins000.exe qui se copie dans %TEMP% et se ferme immédiatement)
                var baselinePids = new HashSet<int>();
                foreach (var p in Process.GetProcesses())
                {
                    try { baselinePids.Add(p.Id); } catch { }
                    finally { try { p.Dispose(); } catch { } }
                }

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        proc.WaitForExit();
                        int exitCode = proc.ExitCode;

                        // Attendre les éventuels sous-processus de désinstallation (NSIS, InnoSetup, InstallShield dans %TEMP% ou le dossier d'installation)
                        WaitForSpawnedUninstallerProcesses(app, baselinePids);

                        statusMessage = string.Format("Désinstallateur terminé (Code de sortie : {0}).", exitCode);
                        return true;
                    }
                }

                WaitForSpawnedUninstallerProcesses(app, baselinePids);
                statusMessage = "Le processus de désinstallation est terminé.";
                return true;
            }
            catch (Exception ex)
            {
                statusMessage = "Erreur lors du lancement du désinstallateur : " + ex.Message;
                return false;
            }
        }

        private static void WaitForSpawnedUninstallerProcesses(InstalledApp app, HashSet<int> baselinePids)
        {
            try
            {
                // Laisser 1,2 seconde au lanceur initial pour démarrer son sous-processus éventuel dans %TEMP%
                System.Threading.Thread.Sleep(1200);

                string tempDir = Path.GetTempPath().TrimEnd('\\', '/');
                string winTempDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp").TrimEnd('\\', '/');
                string installDir = (!string.IsNullOrWhiteSpace(app.InstallLocation) && !AppScanner.IsProtectedSystemDirectory(app.InstallLocation))
                    ? app.InstallLocation.TrimEnd('\\', '/')
                    : null;
                int currentPid = Process.GetCurrentProcess().Id;

                // Surveiller jusqu'à ce que tous les processus de désinstallation lancés après le clic soient fermés (max 15 min)
                for (int attempt = 0; attempt < 900; attempt++)
                {
                    bool anyUninstallerRunning = false;
                    foreach (var p in Process.GetProcesses())
                    {
                        try
                        {
                            if (p.Id == currentPid || p.Id <= 4 || baselinePids.Contains(p.Id))
                                continue;

                            string pName = p.ProcessName ?? "";
                            string pPath = null;
                            try { pPath = p.MainModule != null ? p.MainModule.FileName : null; } catch { }

                            // 1) Nouveau processus lancé depuis %TEMP% ou Windows\Temp (cas classique InnoSetup _iu*.tmp / NSIS Au_.exe)
                            if (!string.IsNullOrWhiteSpace(pPath) &&
                                (pPath.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase) ||
                                 pPath.StartsWith(winTempDir, StringComparison.OrdinalIgnoreCase)))
                            {
                                anyUninstallerRunning = true;
                                break;
                            }

                            // 2) Nouveau processus lancé depuis le dossier d'installation de l'application
                            if (!string.IsNullOrWhiteSpace(installDir) && !string.IsNullOrWhiteSpace(pPath) &&
                                pPath.StartsWith(installDir, StringComparison.OrdinalIgnoreCase))
                            {
                                anyUninstallerRunning = true;
                                break;
                            }

                            // 3) Nouveau processus nommé explicitement unins*, setup*, uninstall*, Au_
                            if (pName.IndexOf("unins", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                pName.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                pName.StartsWith("_iu", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(pName, "Au_", StringComparison.OrdinalIgnoreCase))
                            {
                                anyUninstallerRunning = true;
                                break;
                            }
                        }
                        catch { }
                        finally
                        {
                            try { p.Dispose(); } catch { }
                        }
                    }

                    if (!anyUninstallerRunning)
                        break;

                    System.Threading.Thread.Sleep(1000);
                }

                // Délai final de stabilisation (1,5s) pour que Windows libère les verrous de fichiers et mette à jour le Registre
                System.Threading.Thread.Sleep(1500);
            }
            catch { }
        }

        public static void ParseCommandLine(string rawCmd, bool silentMode, bool isMsi, out string fileName, out string arguments)
        {
            string cmd = rawCmd.Trim();

            // Cas Windows Installer (MsiExec.exe /I{GUID} -> /X{GUID})
            if (isMsi || cmd.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                fileName = "msiexec.exe";
                var guidMatch = Regex.Match(cmd, @"\{[0-9A-Fa-f\-]{36}\}");
                if (guidMatch.Success)
                {
                    arguments = "/X " + guidMatch.Value;
                    if (silentMode)
                        arguments += " /qb! /norestart";
                    return;
                }
            }

            // Cas chemin entre guillemets : "C:\Program Files\App\uninstall.exe" /arg1
            if (cmd.StartsWith("\""))
            {
                int closingQuote = cmd.IndexOf('"', 1);
                if (closingQuote > 1)
                {
                    fileName = cmd.Substring(1, closingQuote - 1);
                    arguments = cmd.Substring(closingQuote + 1).Trim();
                    if (silentMode && string.IsNullOrWhiteSpace(arguments))
                    {
                        arguments = "/S";
                    }
                    return;
                }
            }

            // Cas chemin non entouré de guillemets mais contenant des espaces avant .exe
            var exeMatch = Regex.Match(cmd, @"^(.*?\.(?:exe|bat|cmd|msi))\s*(.*)$", RegexOptions.IgnoreCase);
            if (exeMatch.Success)
            {
                fileName = exeMatch.Groups[1].Value.Trim();
                arguments = exeMatch.Groups[2].Value.Trim();
                return;
            }

            // Par défaut : couper au premier espace
            int firstSpace = cmd.IndexOf(' ');
            if (firstSpace > 0)
            {
                fileName = cmd.Substring(0, firstSpace);
                arguments = cmd.Substring(firstSpace + 1).Trim();
            }
            else
            {
                fileName = cmd;
                arguments = "";
            }
        }

        /// <summary>
        /// Supprime tous les éléments résiduels sélectionnés avec sauvegarde automatique du registre
        /// </summary>
        public static CleanupReport CleanSelectedLeftovers(
            string appName,
            IEnumerable<LeftoverItem> selectedItems,
            bool sendFilesToRecycleBin,
            bool backupRegistryBeforeDelete)
        {
            var report = new CleanupReport();
            var list = selectedItems.Where(i => i.IsSelected).ToList();
            if (list.Count == 0) return report;

            // Libérer proactivement tous les processus et services verrouillant les dossiers/fichiers sélectionnés
            var diskPaths = list
                .Where(i => i.Type == LeftoverType.Directory || i.Type == LeftoverType.File)
                .Select(i => i.Path)
                .ToList();
            if (diskPaths.Count > 0)
            {
                StopServicesAndKillProcessesForPaths(diskPaths, report.Logs);
            }

            // Créer un dossier de sauvegarde horodaté si des clés de registre sont sélectionnées
            string safeAppName = Regex.Replace(appName ?? "Application", @"[^\w\-]", "_");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string sessionBackupDir = null;

            if (backupRegistryBeforeDelete && list.Any(i => i.Type == LeftoverType.RegistryKey || i.Type == LeftoverType.RegistryValue))
            {
                sessionBackupDir = Path.Combine(BackupRootDirectory, string.Format("{0}_{1}", safeAppName, timestamp));
                try
                {
                    Directory.CreateDirectory(sessionBackupDir);
                    report.BackupFolderPath = sessionBackupDir;
                }
                catch { }
            }

            int regBackupIndex = 1;

            foreach (var item in list)
            {
                try
                {
                    switch (item.Type)
                    {
                        case LeftoverType.Directory:
                            if (AppScanner.IsProtectedSystemDirectory(item.Path))
                            {
                                report.FailedItems++;
                                report.Logs.Add("[Ignoré par sécurité] Dossier système protégé : " + item.Path);
                                break;
                            }
                            if (Directory.Exists(item.Path))
                            {
                                DeleteDirectorySafe(item.Path, sendFilesToRecycleBin, report.Logs);
                                report.DeletedDirectories++;
                                report.FreedBytes += item.SizeBytes;
                                report.Logs.Add("[Dossier supprimé] " + item.Path);
                            }
                            break;

                        case LeftoverType.File:
                        case LeftoverType.Shortcut:
                            if (File.Exists(item.Path))
                            {
                                DeleteFileSafe(item.Path, sendFilesToRecycleBin, report.Logs);
                                report.DeletedFiles++;
                                report.FreedBytes += item.SizeBytes;
                                report.Logs.Add("[Fichier supprimé] " + item.Path);
                            }
                            break;

                        case LeftoverType.RegistryKey:
                            if (!string.IsNullOrWhiteSpace(sessionBackupDir))
                            {
                                string regFile = Path.Combine(sessionBackupDir, string.Format("reg_backup_{0:00}.reg", regBackupIndex++));
                                ExportRegistryKey(item.Path, regFile);
                            }
                            if (DeleteRegistryKeyTree(item.Path))
                            {
                                report.DeletedRegistryItems++;
                                report.Logs.Add("[Clé Registre supprimée] " + item.Path);
                            }
                            else
                            {
                                report.FailedItems++;
                                report.Logs.Add("[Échec Clé Registre] " + item.Path + " (Droits Admin peut-être requis)");
                            }
                            break;

                        case LeftoverType.RegistryValue:
                            if (!string.IsNullOrWhiteSpace(sessionBackupDir))
                            {
                                string regFile = Path.Combine(sessionBackupDir, string.Format("reg_val_backup_{0:00}.reg", regBackupIndex++));
                                ExportRegistryKey(item.Path, regFile);
                            }
                            if (DeleteRegistrySingleValue(item.Path, item.ValueName))
                            {
                                report.DeletedRegistryItems++;
                                report.Logs.Add(string.Format("[Valeur Registre supprimée] {0} -> {1}", item.Path, item.ValueName));
                            }
                            else
                            {
                                report.FailedItems++;
                                report.Logs.Add(string.Format("[Échec Valeur Registre] {0} -> {1}", item.Path, item.ValueName));
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    report.FailedItems++;
                    report.Logs.Add(string.Format("[Erreur] {0} : {1}", item.Path, ex.Message));
                }
            }

            return report;
        }

        private static void DeleteDirectorySafe(string dirPath, bool useRecycleBin, List<string> logs = null)
        {
            // 1. Tenter l'envoi silencieux à la Corbeille SANS popup bloquante Windows ("Dossier en cours d'utilisation")
            if (useRecycleBin && dirPath.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0)
            {
                if (SendToRecycleBinSilent(dirPath) && !Directory.Exists(dirPath))
                    return;
            }

            // 2. Suppression directe ou granulaire (avec déverrouillage et évacuation des DLL chargées dans Explorer)
            try
            {
                ClearReadOnlyAttributes(new DirectoryInfo(dirPath));
                Directory.Delete(dirPath, true);
                if (!Directory.Exists(dirPath)) return;
            }
            catch
            {
                if (IsRunningAsAdmin())
                {
                    UnlockProtectedPath(dirPath, true);
                }
            }

            if (Directory.Exists(dirPath))
            {
                ForceEvictAndDeleteDirectory(dirPath, logs);
            }
        }

        private static void DeleteFileSafe(string filePath, bool useRecycleBin, List<string> logs = null)
        {
            if (useRecycleBin && filePath.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0)
            {
                if (SendToRecycleBinSilent(filePath) && !File.Exists(filePath))
                    return;
            }

            try
            {
                try { File.SetAttributes(filePath, FileAttributes.Normal); } catch { }
                File.Delete(filePath);
                if (!File.Exists(filePath)) return;
            }
            catch
            {
                if (IsRunningAsAdmin())
                {
                    UnlockProtectedPath(filePath, false);
                }
            }

            if (File.Exists(filePath))
            {
                EvictOrScheduleSingleFile(filePath, logs);
            }
        }

        /// <summary>
        /// Supprime récursivement tous les fichiers possibles et évacue vers %TEMP% (ou planifie au redémarrage)
        /// les DLL/EXE encore chargés dans explorer.exe afin que le dossier cible puisse être supprimé immédiatement.
        /// </summary>
        private static void ForceEvictAndDeleteDirectory(string dirPath, List<string> logs)
        {
            if (!Directory.Exists(dirPath)) return;

            StopServicesAndKillProcessesForPaths(new[] { dirPath }, logs);

            try
            {
                foreach (var subDir in Directory.GetDirectories(dirPath))
                {
                    try
                    {
                        var di = new DirectoryInfo(subDir);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            di.Delete();
                            continue;
                        }
                    }
                    catch { }

                    ForceEvictAndDeleteDirectory(subDir, logs);
                }
            }
            catch { }

            try
            {
                foreach (var file in Directory.GetFiles(dirPath))
                {
                    EvictOrScheduleSingleFile(file, logs);
                }
            }
            catch { }

            try
            {
                var rootInfo = new DirectoryInfo(dirPath);
                rootInfo.Attributes = FileAttributes.Normal;
                Directory.Delete(dirPath, true);
                return;
            }
            catch
            {
                // Si le dossier ne peut pas être supprimé sur place (poignée ouverte sur le dossier),
                // tenter de le déplacer vers %TEMP% sur le même volume (sans jamais tuer explorer.exe)
                try
                {
                    string rootDrive = Path.GetPathRoot(dirPath);
                    string tempDir = Path.GetTempPath();
                    if (string.Equals(rootDrive, Path.GetPathRoot(tempDir), StringComparison.OrdinalIgnoreCase))
                    {
                        string trashDir = Path.Combine(tempDir, "DeepUninstaller_EvictedDir_" + Guid.NewGuid().ToString("N"));
                        Directory.Move(dirPath, trashDir);
                        MoveFileEx(trashDir, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                        if (logs != null)
                            logs.Add("[Dossier verrouillé évacué vers %TEMP% & nettoyé au redémarrage] " + dirPath);
                        return;
                    }
                }
                catch { }

                // Dernier recours : programmer la suppression au redémarrage du noyau Windows
                MoveFileEx(dirPath, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                if (logs != null)
                    logs.Add("[Planifié au redémarrage] Dossier : " + dirPath);
            }
        }

        private static void EvictOrScheduleSingleFile(string filePath, List<string> logs)
        {
            if (!File.Exists(filePath)) return;

            try
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
                File.Delete(filePath);
                return;
            }
            catch { }

            // Si c'est une DLL d'extension Shell COM, la désenregistrer proprement du Registre sans toucher à explorer.exe
            if (filePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                RunCommandSilent("regsvr32.exe", string.Format("/u /s \"{0}\"", filePath), 2500);
            }

            if (IsRunningAsAdmin())
            {
                UnlockProtectedPath(filePath, false);
                try
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                    File.Delete(filePath);
                    return;
                }
                catch { }
            }

            // Astuce NTFS : un fichier .dll/.exe chargé en mémoire (ex: extension shell dans explorer.exe) ne peut pas
            // être supprimé directement, mais Windows NTFS autorise de le DÉPLACER/RENOMMER sur le même disque SANS tuer explorer.exe !
            // En le déplaçant dans %TEMP%, le dossier d'origine devient vide et peut être supprimé tout de suite.
            try
            {
                string rootDrive = Path.GetPathRoot(filePath);
                string tempDir = Path.GetTempPath();
                string targetTrashPath;
                if (string.Equals(rootDrive, Path.GetPathRoot(tempDir), StringComparison.OrdinalIgnoreCase))
                {
                    targetTrashPath = Path.Combine(tempDir, "DeepUninstaller_Evicted_" + Guid.NewGuid().ToString("N") + ".tmp");
                }
                else
                {
                    targetTrashPath = Path.Combine(rootDrive, "DeepUninstaller_Evicted_" + Guid.NewGuid().ToString("N") + ".tmp");
                }

                File.Move(filePath, targetTrashPath);
                MoveFileEx(targetTrashPath, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                if (logs != null)
                    logs.Add("[Fichier verrouillé évacué hors du dossier & nettoyé au redémarrage] " + Path.GetFileName(filePath));
                return;
            }
            catch { }

            // Dernier recours : suppression noyau au prochain démarrage
            MoveFileEx(filePath, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            if (logs != null)
                logs.Add("[Fichier verrouillé planifié pour suppression au redémarrage] " + filePath);
        }

        private static bool SendToRecycleBinSilent(string path)
        {
            try
            {
                var shf = new SHFILEOPSTRUCT
                {
                    wFunc = FO_DELETE,
                    pFrom = path + "\0\0",
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT
                };
                int ret = SHFileOperation(ref shf);
                return ret == 0 && !shf.fAnyOperationsAborted;
            }
            catch
            {
                return false;
            }
        }

        private static void RunCommandSilent(string fileName, string arguments, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p != null) p.WaitForExit(timeoutMs);
                }
            }
            catch { }
        }

        private static void UnlockProtectedPath(string targetPath, bool isDirectory)
        {
            if (string.IsNullOrWhiteSpace(targetPath) || AppScanner.IsProtectedSystemDirectory(targetPath))
                return;

            try
            {
                var psiTakeOwn = new ProcessStartInfo
                {
                    FileName = "takeown.exe",
                    Arguments = isDirectory
                        ? string.Format("/F \"{0}\" /R /A /D O", targetPath)
                        : string.Format("/F \"{0}\" /A", targetPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psiTakeOwn))
                {
                    if (p != null) p.WaitForExit(10000);
                }

                var psiIcacls = new ProcessStartInfo
                {
                    FileName = "icacls.exe",
                    Arguments = isDirectory
                        ? string.Format("\"{0}\" /grant *S-1-5-32-544:F /T /C /Q", targetPath)
                        : string.Format("\"{0}\" /grant *S-1-5-32-544:F /C /Q", targetPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psiIcacls))
                {
                    if (p != null) p.WaitForExit(10000);
                }
            }
            catch { }
        }

        private static void ClearReadOnlyAttributes(DirectoryInfo dir)
        {
            if (dir == null || !dir.Exists) return;
            try { dir.Attributes = FileAttributes.Normal; } catch { }

            try
            {
                foreach (var fi in dir.GetFiles())
                {
                    try { fi.Attributes = FileAttributes.Normal; } catch { }
                }
                foreach (var sub in dir.GetDirectories())
                {
                    if ((sub.Attributes & FileAttributes.ReparsePoint) == 0)
                        ClearReadOnlyAttributes(sub);
                }
            }
            catch { }
        }

        public static void ExportRegistryKey(string displayKeyPath, string outputRegFile)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = string.Format("export \"{0}\" \"{1}\" /y", displayKeyPath, outputRegFile),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(5000);
                }
            }
            catch { }
        }

        private static bool DeleteRegistryKeyTree(string displayKeyPath)
        {
            RegistryHive hive;
            RegistryView view;
            string subKey;
            if (!ParseRegistryDisplayPath(displayKeyPath, out hive, out view, out subKey))
                return false;

            // Protection critique contre la suppression de clés racines
            if (string.IsNullOrWhiteSpace(subKey) ||
                string.Equals(subKey, "Software", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(subKey, @"Software\Microsoft", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(subKey, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                {
                    baseKey.DeleteSubKeyTree(subKey, false);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool DeleteRegistrySingleValue(string displayKeyPath, string valueName)
        {
            if (string.IsNullOrWhiteSpace(valueName)) return false;

            RegistryHive hive;
            RegistryView view;
            string subKey;
            if (!ParseRegistryDisplayPath(displayKeyPath, out hive, out view, out subKey))
                return false;

            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var key = baseKey.OpenSubKey(subKey, true))
                {
                    if (key == null) return false;
                    key.DeleteValue(valueName, false);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool ParseRegistryDisplayPath(string displayPath, out RegistryHive hive, out RegistryView view, out string subKeyPath)
        {
            hive = RegistryHive.CurrentUser;
            view = RegistryView.Registry64;
            subKeyPath = null;

            if (string.IsNullOrWhiteSpace(displayPath)) return false;
            string p = displayPath.Trim();

            if (p.StartsWith(@"HKLM\SOFTWARE\WOW6432Node\", StringComparison.OrdinalIgnoreCase))
            {
                hive = RegistryHive.LocalMachine;
                view = RegistryView.Registry32;
                subKeyPath = @"SOFTWARE\" + p.Substring(@"HKLM\SOFTWARE\WOW6432Node\".Length);
                return true;
            }
            if (p.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase))
            {
                hive = RegistryHive.LocalMachine;
                view = RegistryView.Registry64;
                subKeyPath = p.Substring(@"HKLM\".Length);
                return true;
            }
            if (p.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase))
            {
                hive = RegistryHive.CurrentUser;
                view = RegistryView.Registry64;
                subKeyPath = p.Substring(@"HKCU\".Length);
                return true;
            }

            return false;
        }

        // =====================================================================
        // INTEROP WINDOWS NATIF (Restart Manager, Shell32 Silent Recycle, Kernel32)
        // =====================================================================
        private const int FO_DELETE = 0x0003;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;
        private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public int wFunc;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, int dwFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(
            uint pSessionHandle,
            uint nFiles,
            string[] rgsFilenames,
            uint nApplications,
            RM_UNIQUE_PROCESS[] rgApplications,
            uint nServices,
            string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(
            uint dwSessionHandle,
            out uint pnProcInfoNeeded,
            ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[] rgAffectedApps,
            ref uint lpdwRebootReasons);
    }
}
