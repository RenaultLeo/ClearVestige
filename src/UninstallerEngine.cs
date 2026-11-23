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

namespace ClearVestige
{
    public class CleanupReport
    {
        public int DeletedDirectories { get; set; }
        public int DeletedFiles { get; set; }
        public int DeletedRegistryItems { get; set; }
        public int ScheduledForRebootItems { get; set; }
        public int FailedItems { get; set; }
        public long FreedBytes { get; set; }
        public string BackupFolderPath { get; set; }
        public List<string> Logs { get; set; }

        public CleanupReport()
        {
            Logs = new List<string>();
        }
    }

    internal enum DeleteOutcome
    {
        Deleted,
        ScheduledForReboot,
        Failed
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
        /// Vérifie si le dossier contenant ClearVestige.exe est un dossier dédié à l'application
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
        /// Auto-désinstallation totale de ClearVestige :
        /// supprime toutes les sauvegardes, raccourcis, traces du Registre Windows (MuiCache, AppCompatFlags, AppSwitched),
        /// fichiers temporaires, Prefetch, ainsi que l'exécutable et le dossier complet de l'application.
        /// </summary>
        public static void ExecuteSelfUninstall()
        {
            string exePath = null;
            try { exePath = Process.GetCurrentProcess().MainModule.FileName; } catch { }
            if (string.IsNullOrWhiteSpace(exePath))
            {
                exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ClearVestige.exe");
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
                                if (valName.IndexOf("ClearVestige", StringComparison.OrdinalIgnoreCase) >= 0 ||
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

            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\ClearVestige", false); } catch { }
            try { Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\ClearVestige", false); } catch { }

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
                    foreach (var f in Directory.GetFiles(folder, "*ClearVestige*", System.IO.SearchOption.AllDirectories))
                    {
                        try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { }
                    }
                }
                catch { }
            }

            // 3. Supprimer les fichiers temporaires et Prefetch associés à ClearVestige
            try
            {
                string tempDir = Path.GetTempPath();
                foreach (var f in Directory.GetFiles(tempDir, "*ClearVestige*", System.IO.SearchOption.TopDirectoryOnly))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { }
                }
                foreach (var d in Directory.GetDirectories(tempDir, "*ClearVestige*", System.IO.SearchOption.TopDirectoryOnly))
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
                    foreach (var pf in Directory.GetFiles(prefetchDir, "CLEARVESTIGE*.pf", System.IO.SearchOption.TopDirectoryOnly))
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
                "ClearVestige.old.exe",
                "Lancer_ClearVestige.bat",
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

            // 5. Supprimer ClearVestige.exe dès la fermeture du processus, puis supprimer le dossier parent
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
        /// (arrête les services associés sans les supprimer pour ne pas perturber le désinstallateur officiel).
        /// </summary>
        public static int KillRunningAppProcesses(string installLocation, List<string> logs = null)
        {
            if (string.IsNullOrWhiteSpace(installLocation) || AppScanner.IsProtectedSystemDirectory(installLocation))
                return 0;

            return StopServicesAndKillProcessesForPaths(new[] { installLocation }, logs, false);
        }

        /// <summary>
        /// Arrête les services Windows associés, ferme proprement puis tue les processus exécutés depuis les dossiers cibles
        /// et utilise l'API Windows Restart Manager (rstrtmgr.dll) pour libérer les fichiers verrouillés.
        /// </summary>
        public static int StopServicesAndKillProcessesForPaths(IEnumerable<string> targetPaths, List<string> logs = null, bool deleteServices = false)
        {
            if (targetPaths == null) return 0;

            var validDirs = new List<string>();
            var targetExeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidateLockedFiles = new List<string>();
            var lockableExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".exe", ".dll", ".asar", ".pak", ".db", ".sqlite", ".log"
            };

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
                                }
                                if (lockableExts.Contains(ext) && candidateLockedFiles.Count < 128)
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
            StopMatchingWindowsServices(validDirs, logs, deleteServices);

            // 2. Fermer proprement puis tuer uniquement les processus de l'application (ne JAMAIS tuer un processus système Windows ou explorer.exe)
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
                        bool closedGracefully = false;
                        try
                        {
                            if (proc.CloseMainWindow())
                            {
                                closedGracefully = proc.WaitForExit(800);
                            }
                        }
                        catch { }

                        if (!closedGracefully && !proc.HasExited)
                        {
                            proc.Kill();
                            proc.WaitForExit(3000);
                        }

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

        private static void StopMatchingWindowsServices(List<string> validDirs, List<string> logs, bool deleteServices)
        {
            if (validDirs == null || validDirs.Count == 0) return;
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\', '/');

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
                                string rawImagePath = (svcKey.GetValue("ImagePath") ?? "").ToString();
                                if (string.IsNullOrWhiteSpace(rawImagePath)) continue;

                                string expandedPath = Environment.ExpandEnvironmentVariables(rawImagePath);
                                string svcExe = AppScanner.ExtractExecutablePath(expandedPath) ?? expandedPath;

                                // Ne JAMAIS toucher à un service système Windows (svchost.exe, drivers dans C:\Windows\...)
                                if (!string.IsNullOrWhiteSpace(winDir) &&
                                    svcExe.StartsWith(winDir + "\\", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                bool matches = validDirs.Any(d =>
                                    svcExe.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(svcExe, d, StringComparison.OrdinalIgnoreCase));

                                if (matches)
                                {
                                    RunCommandSilent("sc.exe", string.Format("stop \"{0}\"", svcName), 4000);
                                    WaitForServiceStopped(svcName, 4000);

                                    if (deleteServices && IsRunningAsAdmin())
                                    {
                                        RunCommandSilent("sc.exe", string.Format("delete \"{0}\"", svcName), 3000);
                                        if (logs != null)
                                            logs.Add(string.Format("[Service Windows arrêté & supprimé] {0}", svcName));
                                    }
                                    else if (logs != null)
                                    {
                                        logs.Add(string.Format("[Service Windows arrêté] {0}", svcName));
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static void WaitForServiceStopped(string serviceName, int maxWaitMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < maxWaitMs)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = string.Format("query \"{0}\"", serviceName),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    };
                    using (var p = Process.Start(psi))
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(1500);
                        if (output.IndexOf("STOPPED", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            output.IndexOf("1060", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return;
                        }
                    }
                }
                catch { return; }
                System.Threading.Thread.Sleep(350);
            }
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
                string safeDesc = (description ?? "ClearVestige").Replace("'", "");
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
                string normalizedApp = LeftoverScanner.NormalizeAppName(app.DisplayName).Replace(" ", "");
                int currentPid = Process.GetCurrentProcess().Id;

                // Surveiller jusqu'à ce que tous les sous-processus de désinstallation soient fermés (max 15 min)
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

                            bool looksLikeUninstallerName =
                                pName.IndexOf("unins", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                pName.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                pName.IndexOf("install", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                pName.IndexOf("remove", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                pName.StartsWith("_iu", StringComparison.OrdinalIgnoreCase) ||
                                pName.StartsWith("~nsu", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(pName, "Au_", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(pName, "msiexec", StringComparison.OrdinalIgnoreCase) ||
                                (normalizedApp.Length >= 4 && pName.Replace(" ", "").IndexOf(normalizedApp, StringComparison.OrdinalIgnoreCase) >= 0);

                            // 1) Nouveau processus de désinstallation lancé depuis %TEMP% ou Windows\Temp (InnoSetup _iu*.tmp, NSIS Au_.exe, etc.)
                            if (looksLikeUninstallerName && !string.IsNullOrWhiteSpace(pPath) &&
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

                            // 3) Nouveau processus nommé explicitement unins*, _iu*, Au_, ~nsu*
                            if (pName.IndexOf("unins", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                pName.StartsWith("_iu", StringComparison.OrdinalIgnoreCase) ||
                                pName.StartsWith("~nsu", StringComparison.OrdinalIgnoreCase) ||
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
                    arguments = AppendSmartSilentFlags(fileName, arguments, silentMode);
                    return;
                }
            }

            // Cas chemin non entouré de guillemets mais contenant des espaces avant .exe
            var exeMatch = Regex.Match(cmd, @"^(.*?\.(?:exe|bat|cmd|msi))\s*(.*)$", RegexOptions.IgnoreCase);
            if (exeMatch.Success)
            {
                fileName = exeMatch.Groups[1].Value.Trim();
                arguments = AppendSmartSilentFlags(fileName, exeMatch.Groups[2].Value.Trim(), silentMode);
                return;
            }

            // Par défaut : couper au premier espace
            int firstSpace = cmd.IndexOf(' ');
            if (firstSpace > 0)
            {
                fileName = cmd.Substring(0, firstSpace);
                arguments = AppendSmartSilentFlags(fileName, cmd.Substring(firstSpace + 1).Trim(), silentMode);
            }
            else
            {
                fileName = cmd;
                arguments = AppendSmartSilentFlags(fileName, "", silentMode);
            }
        }

        private static string AppendSmartSilentFlags(string fileName, string arguments, bool silentMode)
        {
            if (!silentMode) return arguments;
            string exeName = "";
            try { exeName = Path.GetFileName(fileName ?? ""); } catch { }

            // Inno Setup (unins000.exe, unins001.exe...)
            if (Regex.IsMatch(exeName, @"^unins\d+\.exe$", RegexOptions.IgnoreCase))
            {
                if (arguments.IndexOf("SILENT", StringComparison.OrdinalIgnoreCase) < 0)
                    return (arguments + " /VERYSILENT /SUPPRESSMSGBOXES /NORESTART").Trim();
                return arguments;
            }

            // Squirrel (Discord, Slack, etc. : Update.exe --uninstall)
            if (string.Equals(exeName, "Update.exe", StringComparison.OrdinalIgnoreCase) &&
                arguments.IndexOf("--uninstall", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (arguments.IndexOf(" -s", StringComparison.OrdinalIgnoreCase) < 0)
                    return (arguments + " -s").Trim();
                return arguments;
            }

            // NSIS / Générique si aucun argument silencieux n'est présent
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return "/S";
            }

            return arguments;
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
                StopServicesAndKillProcessesForPaths(diskPaths, report.Logs, true);
            }

            // Ordonner intelligemment la suppression : fichiers/raccourcis d'abord, puis dossiers du plus profond au moins profond, puis Registre
            var orderedList = list
                .OrderBy(i =>
                    i.Type == LeftoverType.File || i.Type == LeftoverType.Shortcut ? 0 :
                    i.Type == LeftoverType.Directory ? 1 :
                    i.Type == LeftoverType.RegistryValue ? 2 : 3)
                .ThenByDescending(i => i.Path != null ? i.Path.Length : 0)
                .ToList();

            // Créer un dossier de sauvegarde horodaté si des clés de registre sont sélectionnées
            string safeAppName = Regex.Replace(appName ?? "Application", @"[^\w\-]", "_");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string sessionBackupDir = null;

            if (backupRegistryBeforeDelete && orderedList.Any(i => i.Type == LeftoverType.RegistryKey || i.Type == LeftoverType.RegistryValue))
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

            foreach (var item in orderedList)
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
                                DeleteOutcome dirOutcome = DeleteDirectorySafe(item.Path, sendFilesToRecycleBin, report.Logs);
                                if (dirOutcome == DeleteOutcome.Deleted)
                                {
                                    report.DeletedDirectories++;
                                    report.FreedBytes += item.SizeBytes;
                                    report.Logs.Add("[Dossier supprimé] " + item.Path);
                                }
                                else if (dirOutcome == DeleteOutcome.ScheduledForReboot)
                                {
                                    report.ScheduledForRebootItems++;
                                    report.Logs.Add("[Dossier planifié au redémarrage] " + item.Path);
                                }
                                else
                                {
                                    report.FailedItems++;
                                    report.Logs.Add("[Échec Dossier] Impossible de supprimer : " + item.Path);
                                }
                            }
                            break;

                        case LeftoverType.File:
                        case LeftoverType.Shortcut:
                            if (File.Exists(item.Path))
                            {
                                DeleteOutcome fileOutcome = DeleteFileSafe(item.Path, sendFilesToRecycleBin, report.Logs);
                                if (fileOutcome == DeleteOutcome.Deleted)
                                {
                                    report.DeletedFiles++;
                                    report.FreedBytes += item.SizeBytes;
                                    report.Logs.Add("[Fichier supprimé] " + item.Path);
                                }
                                else if (fileOutcome == DeleteOutcome.ScheduledForReboot)
                                {
                                    report.ScheduledForRebootItems++;
                                    report.Logs.Add("[Fichier planifié au redémarrage] " + item.Path);
                                }
                                else
                                {
                                    report.FailedItems++;
                                    report.Logs.Add("[Échec Fichier] Impossible de supprimer : " + item.Path);
                                }
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

        private static DeleteOutcome DeleteDirectorySafe(string dirPath, bool useRecycleBin, List<string> logs = null)
        {
            // 1. Tenter l'envoi silencieux à la Corbeille SANS popup bloquante Windows ("Dossier en cours d'utilisation")
            if (useRecycleBin && dirPath.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0)
            {
                if (SendToRecycleBinSilent(dirPath) && !Directory.Exists(dirPath))
                    return DeleteOutcome.Deleted;
            }

            // 2. Suppression directe ou granulaire (avec déverrouillage et évacuation des DLL chargées dans Explorer)
            try
            {
                var rootDi = new DirectoryInfo(dirPath);
                if ((rootDi.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    rootDi.Delete();
                    if (!Directory.Exists(dirPath)) return DeleteOutcome.Deleted;
                }
                ClearReadOnlyAttributes(rootDi);
                Directory.Delete(dirPath, true);
                if (!Directory.Exists(dirPath)) return DeleteOutcome.Deleted;
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
                return ForceEvictAndDeleteDirectory(dirPath, logs, true);
            }

            return DeleteOutcome.Deleted;
        }

        private static DeleteOutcome DeleteFileSafe(string filePath, bool useRecycleBin, List<string> logs = null)
        {
            if (useRecycleBin && filePath.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0)
            {
                if (SendToRecycleBinSilent(filePath) && !File.Exists(filePath))
                    return DeleteOutcome.Deleted;
            }

            try
            {
                try { File.SetAttributes(filePath, FileAttributes.Normal); } catch { }
                File.Delete(filePath);
                if (!File.Exists(filePath)) return DeleteOutcome.Deleted;
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
                return EvictOrScheduleSingleFile(filePath, logs);
            }

            return DeleteOutcome.Deleted;
        }

        /// <summary>
        /// Supprime récursivement tous les fichiers possibles (y compris chemins longs > 260 car.)
        /// et évacue vers %TEMP% (ou planifie au redémarrage) les DLL/EXE encore chargés dans explorer.exe.
        /// </summary>
        private static DeleteOutcome ForceEvictAndDeleteDirectory(string dirPath, List<string> logs, bool isRootCall)
        {
            if (!Directory.Exists(dirPath)) return DeleteOutcome.Deleted;

            if (isRootCall)
            {
                StopServicesAndKillProcessesForPaths(new[] { dirPath }, logs, true);
            }

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

                    ForceEvictAndDeleteDirectory(subDir, logs, false);
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
                if (!Directory.Exists(dirPath)) return DeleteOutcome.Deleted;
            }
            catch
            {
                // Fallback Win32 chemin long (\\?\...) pour contourner la limite MAX_PATH (260 car.)
                if (TryDeleteLongPathDirectory(dirPath) && !Directory.Exists(dirPath))
                    return DeleteOutcome.Deleted;

                // Si le dossier ne peut pas être supprimé sur place (poignée ouverte sur le dossier),
                // tenter de le déplacer vers %TEMP% sur le même volume (sans jamais tuer explorer.exe)
                try
                {
                    string rootDrive = Path.GetPathRoot(dirPath);
                    string tempDir = Path.GetTempPath();
                    if (string.Equals(rootDrive, Path.GetPathRoot(tempDir), StringComparison.OrdinalIgnoreCase))
                    {
                        string trashDir = Path.Combine(tempDir, "ClearVestige_EvictedDir_" + Guid.NewGuid().ToString("N"));
                        Directory.Move(dirPath, trashDir);
                        MoveFileEx(trashDir, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                        if (logs != null)
                            logs.Add("[Dossier verrouillé évacué vers %TEMP% & nettoyé au redémarrage] " + dirPath);
                        if (!Directory.Exists(dirPath)) return DeleteOutcome.Deleted;
                    }
                }
                catch { }

                // Dernier recours : programmer la suppression au redémarrage du noyau Windows
                if (MoveFileEx(dirPath, null, MOVEFILE_DELAY_UNTIL_REBOOT))
                    return DeleteOutcome.ScheduledForReboot;
            }

            return Directory.Exists(dirPath) ? DeleteOutcome.Failed : DeleteOutcome.Deleted;
        }

        private static DeleteOutcome EvictOrScheduleSingleFile(string filePath, List<string> logs)
        {
            if (!File.Exists(filePath)) return DeleteOutcome.Deleted;

            try
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
                File.Delete(filePath);
                if (!File.Exists(filePath)) return DeleteOutcome.Deleted;
            }
            catch
            {
                if (TryDeleteLongPathFile(filePath) && !File.Exists(filePath))
                    return DeleteOutcome.Deleted;
            }

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
                    if (!File.Exists(filePath)) return DeleteOutcome.Deleted;
                }
                catch { }
            }

            // Déplacement NTFS sur le même volume vers %TEMP% pour vider le dossier d'origine sans fermer explorer.exe
            try
            {
                string rootDrive = Path.GetPathRoot(filePath);
                string tempDir = Path.GetTempPath();
                string targetTrashPath;
                if (string.Equals(rootDrive, Path.GetPathRoot(tempDir), StringComparison.OrdinalIgnoreCase))
                {
                    targetTrashPath = Path.Combine(tempDir, "ClearVestige_Evicted_" + Guid.NewGuid().ToString("N") + ".tmp");
                }
                else
                {
                    targetTrashPath = Path.Combine(rootDrive, "ClearVestige_Evicted_" + Guid.NewGuid().ToString("N") + ".tmp");
                }

                File.Move(filePath, targetTrashPath);
                MoveFileEx(targetTrashPath, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                if (logs != null)
                    logs.Add("[Fichier verrouillé évacué hors du dossier & nettoyé au redémarrage] " + Path.GetFileName(filePath));
                return DeleteOutcome.Deleted;
            }
            catch { }

            // Dernier recours : suppression noyau au prochain démarrage
            if (MoveFileEx(filePath, null, MOVEFILE_DELAY_UNTIL_REBOOT))
                return DeleteOutcome.ScheduledForReboot;

            return DeleteOutcome.Failed;
        }

        private static string ToExtendedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (path.StartsWith(@"\\?\")) return path;
            if (path.StartsWith(@"\\")) return @"\\?\UNC\" + path.Substring(2);
            return @"\\?\" + path;
        }

        private static bool TryDeleteLongPathFile(string filePath)
        {
            try
            {
                string ext = ToExtendedPath(filePath);
                SetFileAttributesW(ext, 0x80); // FILE_ATTRIBUTE_NORMAL
                return DeleteFileW(ext);
            }
            catch { return false; }
        }

        private static bool TryDeleteLongPathDirectory(string dirPath)
        {
            try
            {
                string ext = ToExtendedPath(dirPath);
                SetFileAttributesW(ext, 0x80);
                return RemoveDirectoryW(ext);
            }
            catch { return false; }
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

        private static string GetTakeOwnYesChar()
        {
            string lang = System.Globalization.CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            if (lang == "fr") return "O";
            if (lang == "de" || lang == "nl") return "J";
            if (lang == "es" || lang == "pt" || lang == "it") return "S";
            return "Y";
        }

        private static void UnlockProtectedPath(string targetPath, bool isDirectory)
        {
            if (string.IsNullOrWhiteSpace(targetPath) || AppScanner.IsProtectedSystemDirectory(targetPath))
                return;

            try
            {
                string yesChar = GetTakeOwnYesChar();
                string takeOwnArgs = isDirectory
                    ? string.Format("/F \"{0}\" /R /A /D {1}", targetPath, yesChar)
                    : string.Format("/F \"{0}\" /A", targetPath);

                int exitCode = RunCommandSilentWithExitCode("takeown.exe", takeOwnArgs, 10000);
                if (isDirectory && exitCode != 0 && yesChar != "Y")
                {
                    RunCommandSilentWithExitCode("takeown.exe", string.Format("/F \"{0}\" /R /A /D Y", targetPath), 10000);
                }

                string icaclsArgs = isDirectory
                    ? string.Format("\"{0}\" /grant *S-1-5-32-544:F /T /C /Q", targetPath)
                    : string.Format("\"{0}\" /grant *S-1-5-32-544:F /C /Q", targetPath);
                RunCommandSilentWithExitCode("icacls.exe", icaclsArgs, 10000);
            }
            catch { }
        }

        private static int RunCommandSilentWithExitCode(string fileName, string arguments, int timeoutMs)
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
                    if (p != null && p.WaitForExit(timeoutMs))
                        return p.ExitCode;
                }
            }
            catch { }
            return -1;
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

            // Protection critique contre la suppression de clés racines ou système
            string normSub = subKey.Trim('\\');
            if (string.IsNullOrWhiteSpace(normSub) ||
                string.Equals(normSub, "Software", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normSub, @"Software\Classes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normSub, @"Software\Microsoft", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normSub, @"Software\Microsoft\Windows", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normSub, @"Software\Microsoft\Windows\CurrentVersion", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normSub, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normSub, @"Software\Microsoft\Windows\CurrentVersion\Run", StringComparison.OrdinalIgnoreCase) ||
                normSub.StartsWith(@"SYSTEM\", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normSub, "SYSTEM", StringComparison.OrdinalIgnoreCase))
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

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool DeleteFileW(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool RemoveDirectoryW(string lpPathName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetFileAttributesW(string lpFileName, uint dwFileAttributes);

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
