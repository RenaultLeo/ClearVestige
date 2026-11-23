using System;
using System.ComponentModel;
using System.Windows.Media;

namespace ClearVestige
{
    public enum AppScope
    {
        System64,
        System32,
        CurrentUser,
        WindowsStore
    }

    public enum LeftoverType
    {
        Directory,
        File,
        Shortcut,
        RegistryKey,
        RegistryValue,
        ScheduledTask
    }

    public enum ScanDepth
    {
        Safe,       // Uniquement dossier d'installation et clé Uninstall
        Moderate,   // + AppData, ProgramData, Raccourcis, Clés HKCU/HKLM Software dédiées
        Advanced    // + MuiCache, AppCompatFlags, Run/Startup, Temp, Tâches planifiées, traces étendues
    }

    public class InstalledApp : INotifyPropertyChanged
    {
        private bool _isChecked;
        private ImageSource _appIconImage;

        public bool IsChecked
        {
            get { return _isChecked; }
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged("IsChecked");
                }
            }
        }

        public ImageSource AppIconImage
        {
            get { return _appIconImage; }
            set
            {
                if (_appIconImage != value)
                {
                    _appIconImage = value;
                    OnPropertyChanged("AppIconImage");
                }
            }
        }

        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Publisher { get; set; }
        public string DisplayVersion { get; set; }
        public string InstallDateRaw { get; set; }
        public string InstallDateFormatted { get; set; }
        public DateTime? InstallDateParsed { get; set; }
        public long SizeBytes { get; set; }
        public string SizeFormatted { get; set; }
        public string InstallLocation { get; set; }
        public string UninstallString { get; set; }
        public string QuietUninstallString { get; set; }
        public string DisplayIcon { get; set; }
        public string RegistryKeyPath { get; set; }
        public string RegistrySubKeyName { get; set; }
        public bool IsHiveLocalMachine { get; set; }
        public bool Is64BitView { get; set; }
        public bool IsWindowsInstaller { get; set; }
        public AppScope Scope { get; set; }
        public string ScopeLabel { get; set; }

        public bool HasQuietUninstall
        {
            get { return !string.IsNullOrWhiteSpace(QuietUninstallString) || IsWindowsInstaller; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public class LeftoverItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged("IsSelected");
                }
            }
        }

        public LeftoverType Type { get; set; }
        public string TypeLabel { get; set; }
        public string Category { get; set; }
        public string Path { get; set; }
        public string ValueName { get; set; }
        public string Description { get; set; }
        public string DisplayPath
        {
            get
            {
                if (Type == LeftoverType.RegistryValue && !string.IsNullOrWhiteSpace(ValueName))
                    return Path + "  \u2192  " + ValueName;
                return Path;
            }
        }
        public string FullTooltip
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Description))
                    return DisplayPath;
                return DisplayPath + "\n" + Description;
            }
        }
        public long SizeBytes { get; set; }
        public string SizeFormatted { get; set; }
        public string Confidence { get; set; } // "Élevée (Sûr)" ou "Moyenne (Vérifier)"
        public bool IsHighConfidence { get; set; }
        public string OwnerAppName { get; set; } // Application d'origine (utilisé par la désinstallation multiple)

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// Élément orphelin détecté automatiquement par le scanner d'orphelins
    /// </summary>
    public class OrphanCandidate : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        public bool IsSelected
        {
            get { return _isSelected; }
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged("IsSelected"); } }
        }

        public string Name { get; set; }
        public string Path { get; set; }
        public string Category { get; set; } // "Dossier Program Files", "Données AppData", "Données ProgramData", "Clé Registre Logiciel"
        public string Reason { get; set; } // Pourquoi cet élément est considéré comme orphelin
        public long SizeBytes { get; set; }
        public string SizeFormatted { get; set; }
        public DateTime? LastModified { get; set; }
        public string LastModifiedFormatted
        {
            get { return LastModified.HasValue ? LastModified.Value.ToString("dd/MM/yyyy") : "—"; }
        }
        public bool IsDirectory { get; set; }
        public bool IsRegistry { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// Ligne de suivi d'une application dans la file de désinstallation multiple.
    /// </summary>
    public class BatchAppEntry : INotifyPropertyChanged
    {
        private string _status = "En attente";
        private string _leftoversLabel = "—";

        public InstalledApp App { get; set; }
        public string DisplayName { get { return App != null ? App.DisplayName : ""; } }
        public string Publisher { get { return App != null ? App.Publisher : ""; } }
        public string SizeFormatted { get { return App != null ? App.SizeFormatted : ""; } }
        public string ModeLabel
        {
            get { return App != null && App.HasQuietUninstall ? "Silencieux possible" : "Interactif"; }
        }
        public bool UninstallSucceeded { get; set; }
        public bool StillInstalled { get; set; }

        public string Status
        {
            get { return _status; }
            set { if (_status != value) { _status = value; OnPropertyChanged("Status"); } }
        }

        public string LeftoversLabel
        {
            get { return _leftoversLabel; }
            set { if (_leftoversLabel != value) { _leftoversLabel = value; OnPropertyChanged("LeftoversLabel"); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public class StartupItem : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Command { get; set; }
        public string ExecutablePath { get; set; }
        public string LocationLabel { get; set; }
        public string RegistryPath { get; set; }
        public bool IsLocalMachine { get; set; }
        public bool IsStartupFolder { get; set; }
        public string FilePath { get; set; }
        public bool ExistsOnDisk { get; set; }
        public string StatusLabel
        {
            get { return ExistsOnDisk ? "Actif (Fichier présent)" : "Orphelin (Fichier introuvable)"; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public class JunkCategory : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged("IsSelected");
                }
            }
        }

        public string Name { get; set; }
        public string FolderPath { get; set; }
        public string Description { get; set; }
        public int FileCount { get; set; }
        public long TotalBytes { get; set; }
        public string SizeFormatted { get; set; }
        public bool IsRecycleBin { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public class OrphanServiceItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get { return _isSelected; }
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged("IsSelected"); } }
        }
        public string ServiceName { get; set; }
        public string DisplayName { get; set; }
        public string ImagePath { get; set; }
        public string ResolvedExe { get; set; }
        public string Status { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public class ContextMenuItemEntry : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get { return _isSelected; }
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged("IsSelected"); } }
        }
        public string Name { get; set; }
        public string ScopeLabel { get; set; }
        public string RegistryKeyPath { get; set; }
        public string Command { get; set; }
        public bool ExistsOnDisk { get; set; }
        public string StatusLabel
        {
            get { return ExistsOnDisk ? "Actif" : "Orphelin (Exécutable introuvable)"; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public class HistoryEntry
    {
        public string Timestamp { get; set; }
        public string AppName { get; set; }
        public int DeletedDirs { get; set; }
        public int DeletedFiles { get; set; }
        public int DeletedRegistry { get; set; }
        public long FreedBytes { get; set; }
        public string FreedFormatted { get { return FormatUtils.FormatBytes(FreedBytes); } }
        public string BackupFolder { get; set; }
        public string DetailsSummary
        {
            get
            {
                return string.Format("{0} doss., {1} fich., {2} reg.", DeletedDirs, DeletedFiles, DeletedRegistry);
            }
        }
    }

    public class BackupFileInfo
    {
        public string SessionName { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public string DateFormatted { get; set; }
        public long SizeBytes { get; set; }
        public string SizeFormatted { get { return FormatUtils.FormatBytes(SizeBytes); } }
    }

    public static class FormatUtils
    {
        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "—";
            string[] units = { "o", "Ko", "Mo", "Go", "To" };
            double val = bytes;
            int idx = 0;
            while (val >= 1024 && idx < units.Length - 1)
            {
                val /= 1024;
                idx++;
            }
            return string.Format("{0:0.##} {1}", val, units[idx]);
        }
    }
}
