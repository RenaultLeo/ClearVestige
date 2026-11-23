using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ClearVestige
{
    /// <summary>
    /// Outils système complémentaires :
    /// - Détection & suppression propre des Services Windows tiers orphelins (exécutable absent du disque)
    /// - Gestionnaire du Menu Clic-Droit Windows (Context Menu / Shell)
    /// - Résolution de raccourcis .lnk pour le Glisser-Déposer (Drag & Drop)
    /// </summary>
    public static class SystemToolsManager
    {
        public static List<OrphanServiceItem> ScanOrphanServices()
        {
            var list = new List<OrphanServiceItem>();
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\', '/');

            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var servicesKey = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", false))
                {
                    if (servicesKey == null) return list;

                    foreach (var svcName in servicesKey.GetSubKeyNames())
                    {
                        try
                        {
                            using (var svcKey = servicesKey.OpenSubKey(svcName, false))
                            {
                                if (svcKey == null) continue;

                                // Ne scanner que les services Win32 (Type 16 ou 32), jamais les pilotes noyau (1 ou 2)
                                object typeObj = svcKey.GetValue("Type");
                                int svcType = typeObj != null ? Convert.ToInt32(typeObj) : 0;
                                if ((svcType & 0x10) == 0 && (svcType & 0x20) == 0)
                                    continue;

                                string rawImg = (svcKey.GetValue("ImagePath") ?? "").ToString().Trim();
                                if (string.IsNullOrWhiteSpace(rawImg)) continue;

                                string expanded = Environment.ExpandEnvironmentVariables(rawImg);
                                string exePath = AppScanner.ExtractExecutablePath(expanded);
                                if (string.IsNullOrWhiteSpace(exePath)) continue;

                                // Exclure strictement tous les services Windows (svchost, System32, SysWOW64, C:\Windows\...)
                                if (!string.IsNullOrWhiteSpace(winDir) &&
                                    exePath.StartsWith(winDir + "\\", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                // Ignorer les chemins relatifs ou non-lecteur (ex: \SystemRoot\...)
                                if (exePath.Length < 4 || exePath[1] != ':')
                                    continue;

                                if (!File.Exists(exePath))
                                {
                                    string display = (svcKey.GetValue("DisplayName") ?? svcName).ToString();
                                    if (display.StartsWith("@")) display = svcName;

                                    list.Add(new OrphanServiceItem
                                    {
                                        ServiceName = svcName,
                                        DisplayName = display,
                                        ImagePath = rawImg,
                                        ResolvedExe = exePath,
                                        Status = "Orphelin (Fichier .exe introuvable)",
                                        IsSelected = false
                                    });
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return list.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static bool DeleteOrphanService(OrphanServiceItem svc, out string message)
        {
            if (svc == null || string.IsNullOrWhiteSpace(svc.ServiceName))
            {
                message = "Service invalide.";
                return false;
            }

            if (!UninstallerEngine.IsRunningAsAdmin())
            {
                message = "Droits Administrateur requis pour supprimer un service Windows.";
                return false;
            }

            try
            {
                // Sauvegarde .reg préalable
                string safeName = Regex.Replace(svc.ServiceName, @"[^\w\-]", "_");
                string regBackup = Path.Combine(
                    UninstallerEngine.BackupRootDirectory,
                    string.Format("Service_{0}_{1:yyyyMMdd_HHmmss}.reg", safeName, DateTime.Now));
                UninstallerEngine.ExportRegistryKey(@"HKLM\SYSTEM\CurrentControlSet\Services\" + svc.ServiceName, regBackup);

                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = string.Format("delete \"{0}\"", svc.ServiceName),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(5000);
                    if (p.ExitCode == 0)
                    {
                        message = "Service supprimé (sauvegarde .reg créée dans Backups/).";
                        return true;
                    }
                    message = "Échec sc delete : " + output.Trim();
                    return false;
                }
            }
            catch (Exception ex)
            {
                message = "Erreur : " + ex.Message;
                return false;
            }
        }

        public static List<ContextMenuItemEntry> ScanContextMenuEntries()
        {
            var list = new List<ContextMenuItemEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var locations = new[]
            {
                Tuple.Create(RegistryHive.ClassesRoot, @"*\shell", @"HKCR\*\shell", "Fichiers (*)"),
                Tuple.Create(RegistryHive.ClassesRoot, @"Directory\shell", @"HKCR\Directory\shell", "Dossiers"),
                Tuple.Create(RegistryHive.ClassesRoot, @"Directory\Background\shell", @"HKCR\Directory\Background\shell", "Fond de dossier"),
                Tuple.Create(RegistryHive.CurrentUser, @"Software\Classes\*\shell", @"HKCU\Software\Classes\*\shell", "Fichiers (Utilisateur)"),
                Tuple.Create(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell", @"HKCU\Software\Classes\Directory\shell", "Dossiers (Utilisateur)"),
                Tuple.Create(RegistryHive.CurrentUser, @"Software\Classes\Directory\Background\shell", @"HKCU\Software\Classes\Directory\Background\shell", "Fond de dossier (Utilisateur)")
            };

            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\', '/');
            var builtInShellNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "open", "edit", "print", "find", "cmd", "powershell", "explore", "runas", "runasuser",
                "pintohome", "pintostartscreen", "UpdateEncryptionSettings", "UpdateEncryptionSettingsWork",
                "Windows.ModernShare", "AnyCode", "OpenWith", "Properties", "Delete", "Rename", "Cut", "Copy"
            };

            foreach (var loc in locations)
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(loc.Item1, RegistryView.Registry64))
                    using (var shellKey = baseKey.OpenSubKey(loc.Item2, false))
                    {
                        if (shellKey == null) continue;
                        foreach (var subName in shellKey.GetSubKeyNames())
                        {
                            if (string.IsNullOrWhiteSpace(subName) || builtInShellNames.Contains(subName))
                                continue;

                            try
                            {
                                using (var entryKey = shellKey.OpenSubKey(subName, false))
                                {
                                    if (entryKey == null) continue;
                                    string command = "";
                                    using (var cmdKey = entryKey.OpenSubKey("command", false))
                                    {
                                        if (cmdKey != null)
                                            command = (cmdKey.GetValue("") ?? "").ToString().Trim();
                                    }

                                    if (string.IsNullOrWhiteSpace(command)) continue;

                                    string expanded = Environment.ExpandEnvironmentVariables(command);
                                    string exe = AppScanner.ExtractExecutablePath(expanded);

                                    // Ne pas lister les commandes Windows natives (explorer.exe, cmd.exe, etc.)
                                    if (!string.IsNullOrWhiteSpace(exe) && !string.IsNullOrWhiteSpace(winDir) &&
                                        exe.StartsWith(winDir + "\\", StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }

                                    string label = (entryKey.GetValue("") ?? entryKey.GetValue("MUIVerb") ?? subName).ToString().Trim();
                                    if (label.StartsWith("@")) label = subName;
                                    label = label.Replace("&", "");

                                    string fullPath = loc.Item3 + @"\" + subName;
                                    if (!seen.Add(fullPath)) continue;

                                    bool exists = !string.IsNullOrWhiteSpace(exe) && File.Exists(exe);

                                    list.Add(new ContextMenuItemEntry
                                    {
                                        Name = label,
                                        ScopeLabel = loc.Item4,
                                        RegistryKeyPath = fullPath,
                                        Command = command,
                                        ExistsOnDisk = exists,
                                        IsSelected = !exists
                                    });
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            return list.OrderBy(c => c.ExistsOnDisk).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static bool RemoveContextMenuEntry(ContextMenuItemEntry entry, out string message)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.RegistryKeyPath))
            {
                message = "Entrée invalide.";
                return false;
            }

            try
            {
                string safeName = Regex.Replace(entry.Name ?? "ContextMenu", @"[^\w\-]", "_");
                string backup = Path.Combine(
                    UninstallerEngine.BackupRootDirectory,
                    string.Format("ContextMenu_{0}_{1:yyyyMMdd_HHmmss}.reg", safeName, DateTime.Now));
                UninstallerEngine.ExportRegistryKey(entry.RegistryKeyPath, backup);

                RegistryHive hive = RegistryHive.ClassesRoot;
                string subPath = null;

                if (entry.RegistryKeyPath.StartsWith(@"HKCR\", StringComparison.OrdinalIgnoreCase))
                {
                    hive = RegistryHive.ClassesRoot;
                    subPath = entry.RegistryKeyPath.Substring(5);
                }
                else if (entry.RegistryKeyPath.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase))
                {
                    hive = RegistryHive.CurrentUser;
                    subPath = entry.RegistryKeyPath.Substring(5);
                }

                if (string.IsNullOrWhiteSpace(subPath) || subPath.EndsWith(@"\shell", StringComparison.OrdinalIgnoreCase))
                {
                    message = "Clé protégée.";
                    return false;
                }

                using (var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                {
                    baseKey.DeleteSubKeyTree(subPath, false);
                    message = "Entrée de menu clic-droit supprimée (sauvegarde .reg créée dans Backups/).";
                    return true;
                }
            }
            catch (Exception ex)
            {
                message = "Échec (droits Administrateur requis pour HKCR) : " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Résout la cible d'un raccourci .lnk (via WScript.Shell COM natif Windows) pour le Glisser-Déposer.
        /// </summary>
        public static string ResolveShortcutTarget(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return filePath;

            if (!filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                return filePath;

            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return filePath;
                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { filePath });
                string target = (string)shortcut.GetType().InvokeMember("TargetPath",
                    System.Reflection.BindingFlags.GetProperty, null, shortcut, null);

                if (!string.IsNullOrWhiteSpace(target) && (File.Exists(target) || Directory.Exists(target)))
                    return target;
            }
            catch { }

            return filePath;
        }
    }
}
