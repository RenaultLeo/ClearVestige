using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace ClearVestige
{
    /// <summary>
    /// Gère l'historique local des nettoyages/désinstallations (Backups\history.tsv)
    /// ainsi que la consultation, prévisualisation, restauration en 1 clic et purge des sauvegardes .reg.
    /// </summary>
    public static class HistoryAndBackupManager
    {
        private static string HistoryFilePath
        {
            get { return Path.Combine(UninstallerEngine.BackupRootDirectory, "history.tsv"); }
        }

        public static void RecordCleanup(string appName, CleanupReport report)
        {
            if (report == null) return;
            if (report.DeletedDirectories == 0 && report.DeletedFiles == 0 &&
                report.DeletedRegistryItems == 0 && report.ScheduledForRebootItems == 0)
            {
                return;
            }

            try
            {
                string safeApp = (appName ?? "Application").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
                string safeBackup = (report.BackupFolderPath ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
                string line = string.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\r\n",
                    DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                    safeApp,
                    report.DeletedDirectories,
                    report.DeletedFiles,
                    report.DeletedRegistryItems,
                    report.FreedBytes,
                    safeBackup);
                File.AppendAllText(HistoryFilePath, line, Encoding.UTF8);
            }
            catch { }
        }

        public static List<HistoryEntry> LoadHistory()
        {
            var list = new List<HistoryEntry>();
            try
            {
                if (!File.Exists(HistoryFilePath)) return list;
                var lines = File.ReadAllLines(HistoryFilePath, Encoding.UTF8);
                foreach (var raw in lines)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var parts = raw.Split('\t');
                    if (parts.Length < 6) continue;

                    int d = 0, f = 0, r = 0;
                    long b = 0;
                    int.TryParse(parts[2], out d);
                    int.TryParse(parts[3], out f);
                    int.TryParse(parts[4], out r);
                    long.TryParse(parts[5], out b);

                    list.Add(new HistoryEntry
                    {
                        Timestamp = parts[0],
                        AppName = parts[1],
                        DeletedDirs = d,
                        DeletedFiles = f,
                        DeletedRegistry = r,
                        FreedBytes = b,
                        BackupFolder = parts.Length > 6 ? parts[6] : ""
                    });
                }
            }
            catch { }

            list.Reverse();
            return list;
        }

        public static long GetTotalFreedBytesFromHistory()
        {
            return LoadHistory().Sum(h => h.FreedBytes);
        }

        public static void ClearHistory()
        {
            try
            {
                if (File.Exists(HistoryFilePath))
                    File.Delete(HistoryFilePath);
            }
            catch { }
        }

        public static List<BackupFileInfo> ScanRegistryBackups()
        {
            var list = new List<BackupFileInfo>();
            string root = UninstallerEngine.BackupRootDirectory;
            if (!Directory.Exists(root)) return list;

            try
            {
                foreach (var file in Directory.GetFiles(root, "*.reg", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        string parent = fi.Directory != null ? fi.Directory.Name : "Backups";
                        if (string.Equals(fi.DirectoryName.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                            parent = "(Racine Backups)";

                        list.Add(new BackupFileInfo
                        {
                            SessionName = parent,
                            FileName = fi.Name,
                            FullPath = fi.FullName,
                            DateFormatted = fi.LastWriteTime.ToString("dd/MM/yyyy HH:mm"),
                            SizeBytes = fi.Length
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return list.OrderByDescending(b => b.DateFormatted).ToList();
        }

        public static string ReadBackupPreview(string regFilePath, int maxChars = 16000)
        {
            if (string.IsNullOrWhiteSpace(regFilePath) || !File.Exists(regFilePath))
                return "(Fichier introuvable)";

            try
            {
                string content = File.ReadAllText(regFilePath);
                if (content.Length > maxChars)
                    return content.Substring(0, maxChars) + "\r\n\r\n... [Aperçu tronqué — fichier complet : " + FormatUtils.FormatBytes(new FileInfo(regFilePath).Length) + "]";
                return content;
            }
            catch (Exception ex)
            {
                return "Erreur de lecture : " + ex.Message;
            }
        }

        public static bool RestoreRegistryBackup(string regFilePath, out string message)
        {
            if (string.IsNullOrWhiteSpace(regFilePath) || !File.Exists(regFilePath))
            {
                message = "Fichier .reg introuvable.";
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = string.Format("import \"{0}\"", regFilePath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using (var p = Process.Start(psi))
                {
                    string err = p.StandardError.ReadToEnd();
                    p.WaitForExit(10000);
                    if (p.ExitCode == 0)
                    {
                        message = "Sauvegarde restaurée avec succès dans le Registre Windows !";
                        return true;
                    }
                    message = "Échec de la restauration (droits Administrateur requis pour HKLM) : " + err.Trim();
                    return false;
                }
            }
            catch (Exception ex)
            {
                message = "Erreur lors de l'importation : " + ex.Message;
                return false;
            }
        }

        public static int PurgeOldBackups(int olderThanDays = 0)
        {
            int deleted = 0;
            string root = UninstallerEngine.BackupRootDirectory;
            if (!Directory.Exists(root)) return 0;

            DateTime cutoff = olderThanDays > 0 ? DateTime.Now.AddDays(-olderThanDays) : DateTime.MaxValue;

            try
            {
                foreach (var file in Directory.GetFiles(root, "*.reg", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (olderThanDays <= 0 || fi.LastWriteTime < cutoff)
                        {
                            fi.Attributes = FileAttributes.Normal;
                            fi.Delete();
                            deleted++;
                        }
                    }
                    catch { }
                }

                // Supprimer les sous-dossiers de session devenus vides
                foreach (var dir in Directory.GetDirectories(root))
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                            Directory.Delete(dir, false);
                    }
                    catch { }
                }
            }
            catch { }

            return deleted;
        }
    }
}
