using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
                    // Sauvegarder la clé Run avant suppression (nom de fichier assaini contre les caractères interdits)
                    string safeName = Regex.Replace(item.Name ?? "Startup", @"[^\w\-]", "_");
                    string backupFile = Path.Combine(
                        UninstallerEngine.BackupRootDirectory,
                        string.Format("Startup_{0}_{1:yyyyMMdd_HHmmss}.reg", safeName, DateTime.Now));
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
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roamingApp = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            // 1. Temp Utilisateur
            AddJunkCategory(categories,
                "Fichiers Temporaires Utilisateur (%TEMP%)",
                Path.GetTempPath(),
                "Fichiers temporaires créés par les installateurs et applications de votre session", true);

            // 2. Temp Windows
            AddJunkCategory(categories,
                "Fichiers Temporaires Système (Windows\\Temp)",
                Path.Combine(winDir, "Temp"),
                "Fichiers temporaires système et logs d'installation MSI", true);

            // 3. CrashDumps
            AddJunkCategory(categories,
                "Rapports de plantage (CrashDumps)",
                Path.Combine(localApp, "CrashDumps"),
                "Fichiers .dmp générés lors de la fermeture inattendue d'applications", true);

            // 4. Rapports d'erreurs Windows (WER)
            AddJunkCategory(categories,
                "Archives de rapports d'erreurs Windows (WER)",
                Path.Combine(localApp, @"Microsoft\Windows\WER\ReportArchive"),
                "Anciens rapports de diagnostic Windows Error Reporting", true);

            // 5. Cache Discord
            AddJunkCategory(categories,
                "Cache Discord (Cache / Code Cache)",
                Path.Combine(roamingApp, @"discord\Cache"),
                "Images, médias et fichiers temporaires mis en cache par Discord", true);

            // 6. Caches Shader GPU (DirectX, NVIDIA, AMD)
            AddJunkCategory(categories,
                "Cache DirectX Shader (D3DSCache)",
                Path.Combine(localApp, "D3DSCache"),
                "Shaders graphiques DirectX précompilés (régénérés automatiquement)", true);
            AddJunkCategory(categories,
                "Cache NVIDIA Shader (DXCache)",
                Path.Combine(localApp, @"NVIDIA\DXCache"),
                "Cache de shaders NVIDIA DirectX", true);
            AddJunkCategory(categories,
                "Cache AMD Shader (DxCache)",
                Path.Combine(localApp, @"AMD\DxCache"),
                "Cache de shaders AMD DirectX", true);

            // 7. Caches Navigateurs (décochés par défaut pour laisser l'utilisateur choisir)
            AddJunkCategory(categories,
                "Cache Navigateur — Google Chrome",
                Path.Combine(localApp, @"Google\Chrome\User Data\Default\Cache\Cache_Data"),
                "Cache web Chrome (ne supprime ni les mots de passe, ni les favoris, ni l'historique)", false);
            AddJunkCategory(categories,
                "Cache Navigateur — Microsoft Edge",
                Path.Combine(localApp, @"Microsoft\Edge\User Data\Default\Cache\Cache_Data"),
                "Cache web Edge (conserve favoris, sessions et mots de passe)", false);
            AddJunkCategory(categories,
                "Cache Navigateur — Brave",
                Path.Combine(localApp, @"BraveSoftware\Brave-Browser\User Data\Default\Cache\Cache_Data"),
                "Cache web Brave (conserve favoris, sessions et mots de passe)", false);

            // 8. Cache Windows Delivery Optimization (mises à jour)
            AddJunkCategory(categories,
                "Cache Windows Delivery Optimization",
                Path.Combine(winDir, @"SoftwareDistribution\DeliveryOptimization\Cache"),
                "Anciens paquets de mise à jour Windows mis en cache (Admin requis)", false);

            // 9. Corbeille Windows
            AddRecycleBinCategory(categories);

            return categories;
        }

        private static void AddRecycleBinCategory(List<JunkCategory> list)
        {
            try
            {
                var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
                if (SHQueryRecycleBin(null, ref info) == 0 && (info.i64NumItems > 0 || info.i64Size > 0))
                {
                    list.Add(new JunkCategory
                    {
                        IsSelected = false,
                        IsRecycleBin = true,
                        Name = "Corbeille Windows",
                        FolderPath = "Corbeille Système ($Recycle.Bin)",
                        Description = "Vider définitivement tous les fichiers présents dans la Corbeille Windows",
                        FileCount = (int)Math.Min(int.MaxValue, info.i64NumItems),
                        TotalBytes = info.i64Size,
                        SizeFormatted = FormatUtils.FormatBytes(info.i64Size)
                    });
                }
            }
            catch { }
        }

        private static void AddJunkCategory(List<JunkCategory> list, string name, string folderPath, string description, bool defaultSelected)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

            int count = 0;
            long bytes = 0;
            CalculateDirectoryStats(folderPath, ref count, ref bytes, 3);
            if (count == 0 && bytes == 0 && !defaultSelected) return;

            list.Add(new JunkCategory
            {
                IsSelected = defaultSelected,
                IsRecycleBin = false,
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
                if (cat.IsRecycleBin)
                {
                    try
                    {
                        if (SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND) == 0)
                        {
                            report.DeletedFiles += cat.FileCount;
                            report.FreedBytes += cat.TotalBytes;
                        }
                    }
                    catch { report.FailedItems++; }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(cat.FolderPath) || !Directory.Exists(cat.FolderPath))
                    continue;

                CleanFolderContents(cat.FolderPath, report);
            }
            HistoryAndBackupManager.RecordCleanup("Nettoyeur Caches & Temp", report);
            return report;
        }

        private static void CleanFolderContents(string folderPath, CleanupReport report)
        {
            CleanFolderContentsRecursive(folderPath, report, false);
        }

        /// <summary>
        /// Supprime de manière granulaire tous les fichiers déverrouillés dans un dossier temporaire et ses sous-dossiers,
        /// afin qu'un seul fichier verrouillé dans un sous-dossier n'empêche pas le nettoyage des autres fichiers.
        /// </summary>
        private static void CleanFolderContentsRecursive(string folderPath, CleanupReport report, bool deleteFolderIfEmpty)
        {
            try
            {
                var di = new DirectoryInfo(folderPath);
                if (!di.Exists || (di.Attributes & FileAttributes.ReparsePoint) != 0)
                    return;

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
                        CleanFolderContentsRecursive(sub.FullName, report, true);
                    }
                    catch
                    {
                        report.FailedItems++;
                    }
                }

                if (deleteFolderIfEmpty)
                {
                    try
                    {
                        if (!di.EnumerateFileSystemInfos().Any())
                        {
                            di.Attributes = FileAttributes.Normal;
                            di.Delete(false);
                            report.DeletedDirectories++;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);
    }
}
