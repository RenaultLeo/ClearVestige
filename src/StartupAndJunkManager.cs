using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace ClearVestige
{
    public static class StartupAndJunkManager
    {
        public static List<StartupItem> ScanStartupItems()
        {
            var list = new List<StartupItem>();

            // 1. HKCU Run
            ScanRegistryStartup(
                RegistryHive.CurrentUser, RegistryView.Registry64,
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                "Registre Utilisateur (HKCU)", false, list);

            // 2. HKLM Run (64-bit)
            ScanRegistryStartup(
                RegistryHive.LocalMachine, RegistryView.Registry64,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                "Registre Système (64-bit)", true, list);

            // 3. HKLM Run (32-bit)
            ScanRegistryStartup(
                RegistryHive.LocalMachine, RegistryView.Registry32,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
                "Registre Système (32-bit)", true, list);

            // 4. Dossiers Démarrage (Startup)
            ScanStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Dossier Démarrage (Utilisateur)", false, list);
            ScanStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Dossier Démarrage (Tous les utilisateurs)", true, list);

            return list.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static void ScanRegistryStartup(
            RegistryHive hive,
            RegistryView view,
            string subKeyPath,
            string displayPath,
            string locationLabel,
            bool isLocalMachine,
            List<StartupItem> list)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var key = baseKey.OpenSubKey(subKeyPath, false))
                {
                    if (key == null) return;
                    foreach (var valName in key.GetValueNames())
                    {
                        if (string.IsNullOrWhiteSpace(valName)) continue;
                        string cmd = (key.GetValue(valName) ?? "").ToString();
                        if (string.IsNullOrWhiteSpace(cmd)) continue;

                        string exePath = AppScanner.ExtractExecutablePath(cmd);
                        bool exists = !string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath);

                        list.Add(new StartupItem
                        {
                            Name = valName,
                            Command = cmd,
                            ExecutablePath = exePath ?? cmd,
                            LocationLabel = locationLabel,
                            RegistryPath = displayPath,
                            IsLocalMachine = isLocalMachine,
                            IsStartupFolder = false,
                            ExistsOnDisk = exists
                        });
                    }
                }
            }
            catch { }
        }

        private static void ScanStartupFolder(string folderPath, string locationLabel, bool isLocalMachine, List<StartupItem> list)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;
            try
            {
                foreach (var file in Directory.GetFiles(folderPath))
                {
                    string fileName = Path.GetFileName(file);
                    if (string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase))
                        continue;

                    list.Add(new StartupItem
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Command = file,
                        ExecutablePath = file,
                        LocationLabel = locationLabel,
                        FilePath = file,
                        IsLocalMachine = isLocalMachine,
                        IsStartupFolder = true,
                        ExistsOnDisk = File.Exists(file)
                    });
                }
            }
            catch { }
        }

        public static bool RemoveStartupItem(StartupItem item, out string message)
        {
            try
            {
                if (item.IsStartupFolder)
                {
                    if (File.Exists(item.FilePath))
                    {
                        File.Delete(item.FilePath);
                    }
                    message = "Raccourci de démarrage supprimé : " + item.Name;
                    return true;
                }
                else
                {
                    // Sauvegarder la clé Run avant suppression
                    string backupFile = Path.Combine(
                        UninstallerEngine.BackupRootDirectory,
                        string.Format("Startup_{0}_{1:yyyyMMdd_HHmmss}.reg", item.Name, DateTime.Now));
                    UninstallerEngine.ExportRegistryKey(item.RegistryPath, backupFile);

                    RegistryHive hive;
                    RegistryView view;
                    string subKey;
                    if (UninstallerEngine.ParseRegistryDisplayPath(item.RegistryPath, out hive, out view, out subKey))
                    {
                        using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                        using (var key = baseKey.OpenSubKey(subKey, true))
                        {
                            if (key != null)
                            {
                                key.DeleteValue(item.Name, false);
                                message = "Entrée de démarrage supprimée (sauvegarde créée dans Backups).";
                                return true;
                            }
                        }
                    }
                    message = "Impossible d'ouvrir la clé en écriture (droits Administrateur requis pour HKLM).";
                    return false;
                }
            }
            catch (Exception ex)
            {
                message = "Erreur : " + ex.Message;
                return false;
            }
        }

        public static List<JunkCategory> ScanJunkCategories()
        {
            var categories = new List<JunkCategory>();

            // 1. Temp Utilisateur
            AddJunkCategory(categories,
                "Fichiers Temporaires Utilisateur (%TEMP%)",
                Path.GetTempPath(),
                "Fichiers temporaires créés par les installateurs et applications de votre session");

            // 2. Temp Windows
            string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            AddJunkCategory(categories,
                "Fichiers Temporaires Système (Windows\\Temp)",
                winTemp,
                "Fichiers temporaires système et logs d'installation MSI");

            // 3. CrashDumps
            string crashDumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
            AddJunkCategory(categories,
                "Rapports de plantage (CrashDumps)",
                crashDumps,
                "Fichiers .dmp générés lors de la fermeture inattendue d'applications");

            // 4. Rapports d'erreurs Windows (WER)
            string werArchive = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\WER\ReportArchive");
            AddJunkCategory(categories,
                "Archives de rapports d'erreurs Windows (WER)",
                werArchive,
                "Anciens rapports de diagnostic Windows Error Reporting");

            return categories;
        }

        private static void AddJunkCategory(List<JunkCategory> list, string name, string folderPath, string description)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

            int count = 0;
            long bytes = 0;
            CalculateDirectoryStats(folderPath, ref count, ref bytes, 3);

            list.Add(new JunkCategory
            {
                IsSelected = true,
                Name = name,
                FolderPath = folderPath,
                Description = description,
                FileCount = count,
                TotalBytes = bytes,
                SizeFormatted = FormatUtils.FormatBytes(bytes)
            });
        }

        private static void CalculateDirectoryStats(string dirPath, ref int fileCount, ref long totalBytes, int depth)
        {
            try
            {
                var di = new DirectoryInfo(dirPath);
                foreach (var fi in di.GetFiles())
                {
                    try
                    {
                        fileCount++;
                        totalBytes += fi.Length;
                    }
                    catch { }
                }

                if (depth > 0)
                {
                    foreach (var sub in di.GetDirectories())
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) == 0)
                            CalculateDirectoryStats(sub.FullName, ref fileCount, ref totalBytes, depth - 1);
                    }
                }
            }
            catch { }
        }

        public static CleanupReport CleanJunkCategories(IEnumerable<JunkCategory> selectedCategories)
        {
            var report = new CleanupReport();
            foreach (var cat in selectedCategories.Where(c => c.IsSelected))
            {
                if (string.IsNullOrWhiteSpace(cat.FolderPath) || !Directory.Exists(cat.FolderPath))
                    continue;

                CleanFolderContents(cat.FolderPath, report);
            }
            return report;
        }

        private static void CleanFolderContents(string folderPath, CleanupReport report)
        {
            try
            {
                var di = new DirectoryInfo(folderPath);
                foreach (var fi in di.GetFiles())
                {
                    try
                    {
                        long len = fi.Length;
                        fi.Attributes = FileAttributes.Normal;
                        fi.Delete();
                        report.DeletedFiles++;
                        report.FreedBytes += len;
                    }
                    catch
                    {
                        // Fichier en cours d'utilisation par un processus actif : normal dans %TEMP%
                        report.FailedItems++;
                    }
                }

                foreach (var sub in di.GetDirectories())
                {
                    try
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        long dirSize = AppScanner.FastCalculateDirectorySize(sub.FullName, 2);
                        sub.Delete(true);
                        report.DeletedDirectories++;
                        report.FreedBytes += dirSize;
                    }
                    catch
                    {
                        report.FailedItems++;
                    }
                }
            }
            catch { }
        }
    }
}
