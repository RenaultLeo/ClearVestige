using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace ClearVestige
{
    public class SystemSnapshot
    {
        public DateTime CreatedAt { get; set; }
        public HashSet<string> Directories { get; set; }
        public HashSet<string> Shortcuts { get; set; }
        public HashSet<string> RegistryKeys { get; set; }
        public HashSet<string> RunValues { get; set; }
        public HashSet<string> ScheduledTasks { get; set; }

        public SystemSnapshot()
        {
            CreatedAt = DateTime.Now;
            Directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Shortcuts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            RegistryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            RunValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ScheduledTasks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public int TotalEntries
        {
            get { return Directories.Count + Shortcuts.Count + RegistryKeys.Count + RunValues.Count + ScheduledTasks.Count; }
        }
    }

    /// <summary>
    /// Mode « Snapshot Avant / Après » (Surveillance d'installation) :
    /// capture l'état exact des dossiers d'applications, raccourcis, clés Registre, entrées Run et Tâches planifiées
    /// avant l'installation d'un logiciel, puis identifie au pixel près chaque ajout effectué par l'installateur.
    /// </summary>
    public static class InstallSnapshotManager
    {
        private static string SnapshotFilePath
        {
            get { return Path.Combine(UninstallerEngine.BackupRootDirectory, "snapshot_latest.dat"); }
        }

        public static bool HasSavedSnapshot()
        {
            return File.Exists(SnapshotFilePath);
        }

        public static SystemSnapshot TakeSnapshot()
        {
            var snap = new SystemSnapshot { CreatedAt = DateTime.Now };

            // 1. Dossiers d'installation et données (profondeur 2 : Root\Vendor et Root\Vendor\Product)
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                localApp,
                Path.Combine(localApp, "Programs")
            };

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                try
                {
                    foreach (var d1 in Directory.GetDirectories(root))
                    {
                        snap.Directories.Add(d1);
                        try
                        {
                            var di = new DirectoryInfo(d1);
                            if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            foreach (var d2 in Directory.GetDirectories(d1))
                            {
                                snap.Directories.Add(d2);
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // 2. Raccourcis Bureau & Menu Démarrer
            string[] shortcutRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
            };
            foreach (var sr in shortcutRoots)
            {
                if (string.IsNullOrWhiteSpace(sr) || !Directory.Exists(sr)) continue;
                try
                {
                    foreach (var f in Directory.GetFiles(sr, "*.lnk", SearchOption.AllDirectories))
                        snap.Shortcuts.Add(f);
                    foreach (var f in Directory.GetFiles(sr, "*.url", SearchOption.AllDirectories))
                        snap.Shortcuts.Add(f);
                }
                catch { }
            }

            // 3. Clés Registre Software & Uninstall
            CaptureSoftwareKeys(RegistryHive.CurrentUser, RegistryView.Registry64, @"HKCU\Software", snap.RegistryKeys);
            CaptureSoftwareKeys(RegistryHive.LocalMachine, RegistryView.Registry64, @"HKLM\SOFTWARE", snap.RegistryKeys);
            CaptureSoftwareKeys(RegistryHive.LocalMachine, RegistryView.Registry32, @"HKLM\SOFTWARE\WOW6432Node", snap.RegistryKeys);

            // 4. Valeurs Démarrage Run
            CaptureRunValues(RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Run", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", snap.RunValues);
            CaptureRunValues(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", snap.RunValues);
            CaptureRunValues(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", snap.RunValues);

            // 5. Tâches planifiées hors Windows
            string tasksRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\Tasks");
            if (Directory.Exists(tasksRoot))
            {
                try
                {
                    foreach (var f in Directory.GetFiles(tasksRoot, "*", SearchOption.AllDirectories))
                    {
                        string rel = f.Substring(tasksRoot.Length);
                        if (rel.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase)) continue;
                        snap.ScheduledTasks.Add(rel);
                    }
                }
                catch { }
            }

            return snap;
        }

        private static void CaptureSoftwareKeys(RegistryHive hive, RegistryView view, string prefix, HashSet<string> target)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var sw = baseKey.OpenSubKey("Software", false))
                {
                    if (sw == null) return;
                    foreach (var sub1 in sw.GetSubKeyNames())
                    {
                        string path1 = prefix + @"\" + sub1;
                        target.Add(path1);
                        if (string.Equals(sub1, "Classes", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(sub1, "WOW6432Node", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        try
                        {
                            using (var k1 = sw.OpenSubKey(sub1, false))
                            {
                                if (k1 == null) continue;
                                foreach (var sub2 in k1.GetSubKeyNames())
                                {
                                    target.Add(path1 + @"\" + sub2);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static void CaptureRunValues(RegistryHive hive, RegistryView view, string subPath, string displayPrefix, HashSet<string> target)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var k = baseKey.OpenSubKey(subPath, false))
                {
                    if (k == null) return;
                    foreach (var v in k.GetValueNames())
                    {
                        if (!string.IsNullOrWhiteSpace(v))
                            target.Add(displayPrefix + "|" + v);
                    }
                }
            }
            catch { }
        }

        public static void SaveSnapshot(SystemSnapshot snap)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TIME|" + snap.CreatedAt.ToString("o"));
            foreach (var d in snap.Directories) sb.AppendLine("DIR|" + d);
            foreach (var s in snap.Shortcuts) sb.AppendLine("LNK|" + s);
            foreach (var r in snap.RegistryKeys) sb.AppendLine("REG|" + r);
            foreach (var v in snap.RunValues) sb.AppendLine("RUN|" + v);
            foreach (var t in snap.ScheduledTasks) sb.AppendLine("TSK|" + t);
            File.WriteAllText(SnapshotFilePath, sb.ToString(), Encoding.UTF8);
        }

        public static SystemSnapshot LoadSavedSnapshot()
        {
            if (!File.Exists(SnapshotFilePath)) return null;
            var snap = new SystemSnapshot();
            try
            {
                foreach (var line in File.ReadAllLines(SnapshotFilePath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int idx = line.IndexOf('|');
                    if (idx <= 0) continue;
                    string tag = line.Substring(0, idx);
                    string val = line.Substring(idx + 1);
                    if (tag == "TIME")
                    {
                        DateTime dt;
                        if (DateTime.TryParse(val, out dt)) snap.CreatedAt = dt;
                    }
                    else if (tag == "DIR") snap.Directories.Add(val);
                    else if (tag == "LNK") snap.Shortcuts.Add(val);
                    else if (tag == "REG") snap.RegistryKeys.Add(val);
                    else if (tag == "RUN") snap.RunValues.Add(val);
                    else if (tag == "TSK") snap.ScheduledTasks.Add(val);
                }
                return snap;
            }
            catch
            {
                return null;
            }
        }

        public static List<LeftoverItem> ComputeDiffAsLeftovers(SystemSnapshot before, SystemSnapshot after)
        {
            var items = new List<LeftoverItem>();
            if (before == null || after == null) return items;

            // 1. Nouveaux dossiers (éviter d'ajouter les sous-dossiers si leur parent direct est déjà nouveau)
            var newDirs = after.Directories
                .Where(d => !before.Directories.Contains(d) && Directory.Exists(d) && !AppScanner.IsProtectedSystemDirectory(d))
                .OrderBy(d => d.Length)
                .ToList();

            var keptDirs = new List<string>();
            foreach (var d in newDirs)
            {
                if (keptDirs.Any(parent => d.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase)))
                    continue;
                keptDirs.Add(d);
                long sz = AppScanner.FastCalculateDirectorySize(d, 3);
                items.Add(new LeftoverItem
                {
                    IsSelected = true,
                    Type = LeftoverType.Directory,
                    TypeLabel = "Dossier",
                    Category = "Snapshot — Nouveau dossier",
                    Path = d,
                    Description = "Créé après le snapshot du " + before.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                    SizeBytes = sz,
                    SizeFormatted = FormatUtils.FormatBytes(sz),
                    Confidence = "Élevée (Snapshot)",
                    IsHighConfidence = true
                });
            }

            // 2. Nouveaux raccourcis
            foreach (var lnk in after.Shortcuts.Where(s => !before.Shortcuts.Contains(s) && File.Exists(s)))
            {
                long sz = 0;
                try { sz = new FileInfo(lnk).Length; } catch { }
                items.Add(new LeftoverItem
                {
                    IsSelected = true,
                    Type = LeftoverType.Shortcut,
                    TypeLabel = "Raccourci",
                    Category = "Snapshot — Nouveau raccourci",
                    Path = lnk,
                    Description = "Raccourci créé depuis le snapshot",
                    SizeBytes = sz,
                    SizeFormatted = FormatUtils.FormatBytes(sz),
                    Confidence = "Élevée (Snapshot)",
                    IsHighConfidence = true
                });
            }

            // 3. Nouvelles valeurs Run
            foreach (var rv in after.RunValues.Where(v => !before.RunValues.Contains(v)))
            {
                int sep = rv.IndexOf('|');
                if (sep <= 0) continue;
                string keyPath = rv.Substring(0, sep);
                string valName = rv.Substring(sep + 1);
                items.Add(new LeftoverItem
                {
                    IsSelected = true,
                    Type = LeftoverType.RegistryValue,
                    TypeLabel = "Valeur Registre",
                    Category = "Snapshot — Démarrage (Run)",
                    Path = keyPath,
                    ValueName = valName,
                    Description = "Entrée de démarrage ajoutée depuis le snapshot",
                    SizeBytes = 0,
                    SizeFormatted = "—",
                    Confidence = "Élevée (Snapshot)",
                    IsHighConfidence = true
                });
            }

            // 4. Nouvelles clés Registre Software
            var newRegs = after.RegistryKeys
                .Where(r => !before.RegistryKeys.Contains(r))
                .OrderBy(r => r.Length)
                .ToList();
            var keptRegs = new List<string>();
            foreach (var r in newRegs)
            {
                if (keptRegs.Any(parent => r.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase)))
                    continue;
                keptRegs.Add(r);
                items.Add(new LeftoverItem
                {
                    IsSelected = true,
                    Type = LeftoverType.RegistryKey,
                    TypeLabel = "Clé Registre",
                    Category = "Snapshot — Nouvelle clé",
                    Path = r,
                    Description = "Clé de registre créée depuis le snapshot",
                    SizeBytes = 0,
                    SizeFormatted = "—",
                    Confidence = "Élevée (Snapshot)",
                    IsHighConfidence = true
                });
            }

            // 5. Nouvelles tâches planifiées
            foreach (var t in after.ScheduledTasks.Where(x => !before.ScheduledTasks.Contains(x)))
            {
                items.Add(new LeftoverItem
                {
                    IsSelected = true,
                    Type = LeftoverType.ScheduledTask,
                    TypeLabel = "Tâche Planifiée",
                    Category = "Snapshot — Nouvelle tâche",
                    Path = t,
                    Description = "Tâche planifiée créée depuis le snapshot",
                    SizeBytes = 0,
                    SizeFormatted = "—",
                    Confidence = "Élevée (Snapshot)",
                    IsHighConfidence = true
                });
            }

            return items;
        }
    }
}
