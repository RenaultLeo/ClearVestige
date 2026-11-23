using System;
using System.Collections.Generic;
using System.IO;

namespace ClearVestige
{
    /// <summary>
    /// Gestionnaire bilingue léger FR / EN sans dépendance externe (sauvegarde la préférence dans Backups\lang.cfg).
    /// </summary>
    public static class Loc
    {
        private static string _currentLang = LoadSavedLanguage();

        public static string CurrentLang
        {
            get { return _currentLang; }
            set
            {
                string norm = string.Equals(value, "EN", StringComparison.OrdinalIgnoreCase) ? "EN" : "FR";
                _currentLang = norm;
                SaveLanguage(norm);
            }
        }

        public static bool IsEnglish
        {
            get { return string.Equals(_currentLang, "EN", StringComparison.OrdinalIgnoreCase); }
        }

        public static void ToggleLanguage()
        {
            CurrentLang = IsEnglish ? "FR" : "EN";
        }

        public static string T(string fr, string en)
        {
            return IsEnglish ? en : fr;
        }

        private static string ConfigPath
        {
            get { return Path.Combine(UninstallerEngine.BackupRootDirectory, "lang.cfg"); }
        }

        private static string LoadSavedLanguage()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string val = (File.ReadAllText(ConfigPath) ?? "").Trim();
                    if (string.Equals(val, "EN", StringComparison.OrdinalIgnoreCase))
                        return "EN";
                }
            }
            catch { }
            return "FR";
        }

        private static void SaveLanguage(string lang)
        {
            try { File.WriteAllText(ConfigPath, lang); } catch { }
        }
    }
}
