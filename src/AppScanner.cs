using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DeepUninstaller
{
    public static class AppScanner
    {
        private const string UninstallSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public static List<InstalledApp> ScanInstalledApps(bool includeSystemComponents = false, bool includeStoreApps = false)
        {
            var results = new List<InstalledApp>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. HKLM 64-bit
            ScanRegistryView(RegistryHive.LocalMachine, RegistryView.Registry64, AppScope.System64, "Système (64-bit)", includeSystemComponents, results, seenKeys);

            // 2. HKLM 32-bit (WOW6432Node)
            ScanRegistryView(RegistryHive.LocalMachine, RegistryView.Registry32, AppScope.System32, "Système (32-bit)", includeSystemComponents, results, seenKeys);

            // 3. HKCU (Utilisateur actuel)
            ScanRegistryView(RegistryHive.CurrentUser, RegistryView.Registry64, AppScope.CurrentUser, "Utilisateur", includeSystemComponents, results, seenKeys);
            ScanRegistryView(RegistryHive.CurrentUser, RegistryView.Registry32, AppScope.CurrentUser, "Utilisateur", includeSystemComponents, results, seenKeys);

            // 4. Optionnel : Applications Windows Store (AppX)
            if (includeStoreApps)
            {
                ScanWindowsStoreApps(results, seenKeys);
            }

            // Déduplication intelligente par DisplayName + DisplayVersion + Scope si nécessaire
            return results
                .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static void ScanRegistryView(
            RegistryHive hive,
            RegistryView view,
            AppScope scope,
            string scopeLabel,
            bool includeSystemComponents,
            List<InstalledApp> results,
            HashSet<string> seenKeys)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var uninstallKey = baseKey.OpenSubKey(UninstallSubKey, false))
                {
                    if (uninstallKey == null) return;

                    foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                    {
                        try
                        {
                            using (var appKey = uninstallKey.OpenSubKey(subKeyName, false))
                            {
                                if (appKey == null) continue;

                                string displayName = GetRegString(appKey, "DisplayName");
                                if (string.IsNullOrWhiteSpace(displayName)) continue;

                                // Filtrer les composants système cachés sauf si demandé
                                int systemComponent = GetRegInt(appKey, "SystemComponent");
                                if (!includeSystemComponents && systemComponent == 1) continue;

                                string releaseType = GetRegString(appKey, "ReleaseType");
                                string parentKeyName = GetRegString(appKey, "ParentKeyName");
                                if (!includeSystemComponents &&
                                    (!string.IsNullOrEmpty(parentKeyName) ||
                                     string.Equals(releaseType, "Security Update", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(releaseType, "Update Rollup", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(releaseType, "Hotfix", StringComparison.OrdinalIgnoreCase)))
                                {
                                    continue;
                                }

                                string uninstallString = GetRegString(appKey, "UninstallString");
                                string quietUninstallString = GetRegString(appKey, "QuietUninstallString");
                                if (string.IsNullOrWhiteSpace(uninstallString) && string.IsNullOrWhiteSpace(quietUninstallString))
                                {
                                    continue;
                                }

                                // Éviter les doublons entre vues 64/32 sur HKCU
                                string uniqueId = string.Format("{0}:{1}:{2}", hive, subKeyName, displayName);
                                if (seenKeys.Contains(uniqueId)) continue;
                                seenKeys.Add(uniqueId);

                                string publisher = GetRegString(appKey, "Publisher");
                                string version = GetRegString(appKey, "DisplayVersion");
                                string installDateRaw = GetRegString(appKey, "InstallDate");
                                string installLocation = CleanDirectoryPath(GetRegString(appKey, "InstallLocation"));
                                string displayIcon = GetRegString(appKey, "DisplayIcon");
                                int windowsInstaller = GetRegInt(appKey, "WindowsInstaller");

                                // Inférer le dossier d'installation s'il n'est pas renseigné
                                if (string.IsNullOrWhiteSpace(installLocation))
                                {
                                    installLocation = InferInstallLocation(displayIcon, uninstallString);
                                }

                                // Taille estimée (en Ko dans le registre)
                                long sizeBytes = 0;
                                int estimatedSizeKb = GetRegInt(appKey, "EstimatedSize");
                                if (estimatedSizeKb > 0)
                                {
                                    sizeBytes = (long)estimatedSizeKb * 1024L;
                                }
                                else if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
                                {
                                    sizeBytes = FastCalculateDirectorySize(installLocation, 2);
                                }

                                // Date d'installation
                                DateTime? parsedDate = ParseInstallDate(installDateRaw, installLocation);
                                string formattedDate = parsedDate.HasValue
                                    ? parsedDate.Value.ToString("dd/MM/yyyy")
                                    : "—";

                                string hivePrefix = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
                                string regPathDisplay = (hive == RegistryHive.LocalMachine && view == RegistryView.Registry32)
                                    ? @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" + subKeyName
                                    : hivePrefix + @"\" + UninstallSubKey + @"\" + subKeyName;

                                var app = new InstalledApp
                                {
                                    Id = uniqueId,
                                    DisplayName = displayName.Trim(),
                                    Publisher = string.IsNullOrWhiteSpace(publisher) ? "Inconnu" : publisher.Trim(),
                                    DisplayVersion = string.IsNullOrWhiteSpace(version) ? "—" : version.Trim(),
                                    InstallDateRaw = installDateRaw,
                                    InstallDateParsed = parsedDate,
                                    InstallDateFormatted = formattedDate,
                                    SizeBytes = sizeBytes,
                                    SizeFormatted = FormatUtils.FormatBytes(sizeBytes),
                                    InstallLocation = installLocation ?? "",
                                    UninstallString = uninstallString ?? "",
                                    QuietUninstallString = quietUninstallString ?? "",
                                    DisplayIcon = displayIcon ?? "",
                                    RegistryKeyPath = regPathDisplay,
                                    RegistrySubKeyName = subKeyName,
                                    IsHiveLocalMachine = (hive == RegistryHive.LocalMachine),
                                    Is64BitView = (view == RegistryView.Registry64),
                                    IsWindowsInstaller = (windowsInstaller == 1) ||
                                        (uninstallString != null && uninstallString.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0),
                                    Scope = scope,
                                    ScopeLabel = scopeLabel
                                };

                                results.Add(app);
                            }
                        }
                        catch
                        {
                            // Ignorer une sous-clé inaccessible
                        }
                    }
                }
            }
            catch
            {
                // Ignorer si la ruche est inaccessible
            }
        }

        private static void ScanWindowsStoreApps(List<InstalledApp> results, HashSet<string> seenKeys)
        {
            try
            {
                const string appxKeyPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                using (var packagesKey = baseKey.OpenSubKey(appxKeyPath, false))
                {
                    if (packagesKey == null) return;

                    foreach (var pkgFullName in packagesKey.GetSubKeyNames())
                    {
                        try
                        {
                            using (var pkgKey = packagesKey.OpenSubKey(pkgFullName, false))
                            {
                                if (pkgKey == null) continue;
                                string pkgRootFolder = GetRegString(pkgKey, "PackageRootFolder");
                                string displayName = GetRegString(pkgKey, "DisplayName");

                                // Extraire le nom lisible depuis l'identifiant du package (ex: Microsoft.WindowsCalculator_11.2405.2.0_x64__8wekyb3d8bbwe)
                                string[] parts = pkgFullName.Split('_');
                                string shortName = parts.Length > 0 ? parts[0] : pkgFullName;
                                string version = parts.Length > 1 ? parts[1] : "—";

                                // Filtrer les frameworks techniques purs
                                if (shortName.StartsWith("Microsoft.UI.Xaml", StringComparison.OrdinalIgnoreCase) ||
                                    shortName.StartsWith("Microsoft.VCLibs", StringComparison.OrdinalIgnoreCase) ||
                                    shortName.StartsWith("Microsoft.NET.Native", StringComparison.OrdinalIgnoreCase) ||
                                    shortName.StartsWith("Microsoft.WindowsAppRuntime", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                string friendlyName = (!string.IsNullOrWhiteSpace(displayName) && !displayName.StartsWith("@{"))
                                    ? displayName
                                    : shortName;

                                string uniqueId = "Store:" + pkgFullName;
                                if (seenKeys.Contains(uniqueId)) continue;
                                seenKeys.Add(uniqueId);

                                long sizeBytes = 0;
                                if (!string.IsNullOrWhiteSpace(pkgRootFolder) && Directory.Exists(pkgRootFolder))
                                {
                                    sizeBytes = FastCalculateDirectorySize(pkgRootFolder, 1);
                                }

                                results.Add(new InstalledApp
                                {
                                    Id = uniqueId,
                                    DisplayName = friendlyName + " (Store)",
                                    Publisher = "Microsoft Store / AppX",
                                    DisplayVersion = version,
                                    InstallDateFormatted = "—",
                                    SizeBytes = sizeBytes,
                                    SizeFormatted = FormatUtils.FormatBytes(sizeBytes),
                                    InstallLocation = pkgRootFolder ?? "",
                                    UninstallString = string.Format("powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"Remove-AppxPackage -Package '{0}'\"", pkgFullName),
                                    QuietUninstallString = string.Format("powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"Remove-AppxPackage -Package '{0}'\"", pkgFullName),
                                    RegistryKeyPath = @"HKCU\" + appxKeyPath + @"\" + pkgFullName,
                                    RegistrySubKeyName = pkgFullName,
                                    IsHiveLocalMachine = false,
                                    Is64BitView = true,
                                    IsWindowsInstaller = false,
                                    Scope = AppScope.WindowsStore,
                                    ScopeLabel = "Microsoft Store"
                                });
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static string GetRegString(RegistryKey key, string valueName)
        {
            object val = key.GetValue(valueName);
            return val != null ? val.ToString() : null;
        }

        private static int GetRegInt(RegistryKey key, string valueName)
        {
            object val = key.GetValue(valueName);
            if (val is int) return (int)val;
            int parsed;
            if (val != null && int.TryParse(val.ToString(), out parsed)) return parsed;
            return 0;
        }

        private static string CleanDirectoryPath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) return null;
            string cleaned = rawPath.Trim().Trim('"');
            cleaned = cleaned.TrimEnd('\\', '/');
            if (Directory.Exists(cleaned)) return cleaned;
            return null;
        }

        private static string InferInstallLocation(string displayIcon, string uninstallString)
        {
            string[] candidates = { displayIcon, uninstallString };
            foreach (var raw in candidates)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string exePath = ExtractExecutablePath(raw);
                if (string.IsNullOrWhiteSpace(exePath)) continue;

                try
                {
                    string dir = Path.GetDirectoryName(exePath);
                    if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;

                    // Ne jamais utiliser System32, Windows, ou InstallShield commun comme dossier d'application !
                    if (IsProtectedSystemDirectory(dir)) continue;

                    return dir;
                }
                catch { }
            }
            return null;
        }

        public static bool IsProtectedSystemDirectory(string dirPath)
        {
            if (string.IsNullOrWhiteSpace(dirPath)) return true;
            try
            {
                string full = Path.GetFullPath(dirPath).TrimEnd('\\', '/');
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\', '/');
                if (full.StartsWith(winDir, StringComparison.OrdinalIgnoreCase)) return true;

                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd('\\', '/');
                string progFiles86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).TrimEnd('\\', '/');
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).TrimEnd('\\', '/');
                if (string.IsNullOrEmpty(localAppData))
                    localAppData = (Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "").TrimEnd('\\', '/');
                string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).TrimEnd('\\', '/');
                if (string.IsNullOrEmpty(roamingAppData))
                    roamingAppData = (Environment.GetEnvironmentVariable("APPDATA") ?? "").TrimEnd('\\', '/');
                string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData).TrimEnd('\\', '/');
                if (string.IsNullOrEmpty(programData))
                    programData = (Environment.GetEnvironmentVariable("ProgramData") ?? "").TrimEnd('\\', '/');

                string[] protectedExact = {
                    progFiles,
                    progFiles86,
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles).TrimEnd('\\', '/'),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86).TrimEnd('\\', '/'),
                    roamingAppData,
                    localAppData,
                    programData,
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\', '/'),
                    (Environment.GetEnvironmentVariable("USERPROFILE") ?? "").TrimEnd('\\', '/'),
                    Path.Combine(progFiles, "WindowsApps"),
                    Path.Combine(progFiles, "ModifiableWindowsApps"),
                    Path.Combine(localAppData, "Packages"),
                    Path.Combine(localAppData, "Programs"),
                    Path.Combine(localAppData, "Microsoft"),
                    Path.Combine(localAppData, @"Microsoft\WindowsApps"),
                    Path.Combine(roamingAppData, "Microsoft"),
                    Path.Combine(programData, "Microsoft"),
                    Path.Combine(programData, "Packages")
                };

                foreach (var p in protectedExact)
                {
                    if (!string.IsNullOrEmpty(p) && string.Equals(full, p, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // Protéger les conteneurs AppData partagés quel que soit le compte utilisateur Windows (ex: UAC élevé sous un autre profil)
                string[] protectedSuffixes = {
                    @"\AppData",
                    @"\AppData\Local",
                    @"\AppData\Roaming",
                    @"\AppData\LocalLow",
                    @"\AppData\Local\Programs",
                    @"\AppData\Local\Packages",
                    @"\AppData\Local\Microsoft",
                    @"\AppData\Local\Microsoft\WindowsApps",
                    @"\AppData\Roaming\Microsoft"
                };
                foreach (var suffix in protectedSuffixes)
                {
                    if (full.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // Protéger tout dossier système WindowsApps racine sur n'importe quel lecteur
                string folderName = Path.GetFileName(full);
                if (string.Equals(folderName, "WindowsApps", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(folderName, "ModifiableWindowsApps", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(folderName, "Packages", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(folderName, "SystemApps", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (full.IndexOf(@"InstallShield Installation Information", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    full.IndexOf(@"Package Cache", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                // Racine de lecteur (ex: C:\)
                if (full.Length <= 3) return true;
            }
            catch
            {
                return true;
            }
            return false;
        }

        public static string ExtractExecutablePath(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return null;
            string s = commandLine.Trim();

            // Si commence par un guillemet
            if (s.StartsWith("\""))
            {
                int endQuote = s.IndexOf('"', 1);
                if (endQuote > 1)
                {
                    string candidate = s.Substring(1, endQuote - 1);
                    if (File.Exists(candidate)) return candidate;
                }
            }

            // Sinon, chercher .exe ou .ico suivi d'espace/virgule/fin
            var match = Regex.Match(s, @"^([a-zA-Z]:\\[^:*?""<>|]+?\.(?:exe|ico|dll|msi))", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string candidate = match.Groups[1].Value;
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        private static DateTime? ParseInstallDate(string rawDate, string installLocation)
        {
            if (!string.IsNullOrWhiteSpace(rawDate))
            {
                DateTime dt;
                if (DateTime.TryParseExact(rawDate.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                {
                    if (dt.Year >= 1995 && dt.Year <= 2100) return dt;
                }
                if (DateTime.TryParse(rawDate.Trim(), out dt))
                {
                    if (dt.Year >= 1995 && dt.Year <= 2100) return dt;
                }
            }

            if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
            {
                try
                {
                    return Directory.GetCreationTime(installLocation);
                }
                catch { }
            }
            return null;
        }

        public static long FastCalculateDirectorySize(string dirPath, int maxDepth = 2)
        {
            if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath)) return 0;
            long total = 0;
            try
            {
                var dirInfo = new DirectoryInfo(dirPath);
                foreach (var fi in dirInfo.GetFiles())
                {
                    try { total += fi.Length; } catch { }
                }

                if (maxDepth > 0)
                {
                    foreach (var subDir in dirInfo.GetDirectories())
                    {
                        try
                        {
                            if ((subDir.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            total += FastCalculateDirectorySize(subDir.FullName, maxDepth - 1);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return total;
        }
    }
}
