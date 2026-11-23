using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ClearVestige
{
    public static class LeftoverScanner
    {
        private static readonly HashSet<string> ProtectedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft", "Microsoft Corporation", "Windows", "System", "System32", "SysWOW64",
            "Common Files", "Internet Explorer", "Windows Defender", "Windows NT", "Windows Mail",
            "Windows Media Player", "Windows Photo Viewer", "Windows Portable Devices", "WindowsPowerShell",
            "Package Cache", "InstallShield Installation Information", "Uninstall Information",
            "Google", "Intel", "NVIDIA", "NVIDIA Corporation", "AMD", "Realtek", "HP", "Dell", "Lenovo", "ASUS", "Acer",
            "Classes", "Clients", "Policies", "RegisteredApplications", "Default", "Public", "All Users",
            "Temp", "Tmp", "Programs", "Desktop", "Start Menu", "Startup", "AppData", "Local", "Roaming", "LocalLow",
            "Application", "Applications", "bin", "app", "current", "lib", "data", "resources", "tools",
            "x64", "x86", "win32", "win64", "Release", "Debug", "Client", "Server", "Service",
            "Updater", "Launcher", "Setup", "Installer", "Uninstall", "Users", "Program Files", "Program Files (x86)", "ProgramData",
            "WindowsApps", "ModifiableWindowsApps", "Packages", "SystemApps", "Windows Kits", "Windows Security", "Windows Sidebar",
            "Reference Assemblies", "MSBuild", "Microsoft.NET", "dotnet", "PowerShell"
        };

        public static List<LeftoverItem> ScanForApp(
            InstalledApp app,
            ScanDepth depth,
            List<InstalledApp> allInstalledApps = null)
        {
            var items = new List<LeftoverItem>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string cleanName = NormalizeAppName(app.DisplayName);
            string installDir = app.InstallLocation;
            string installFolderName = null;
            if (!string.IsNullOrWhiteSpace(installDir) && !AppScanner.IsProtectedSystemDirectory(installDir))
            {
                try
                {
                    installFolderName = Path.GetFileName(installDir.TrimEnd('\\', '/'));
                }
                catch { }
            }

            // Déterminer si l'éditeur est unique à cette application ou partagé par d'autres applications installées
            string cleanPublisher = NormalizePublisherName(app.Publisher);
            bool publisherSharedByOtherApps = true;
            if (!string.IsNullOrWhiteSpace(cleanPublisher) && !IsProtectedToken(cleanPublisher) && allInstalledApps != null)
            {
                publisherSharedByOtherApps = allInstalledApps.Any(other =>
                    other.Id != app.Id &&
                    !string.IsNullOrWhiteSpace(other.Publisher) &&
                    other.Publisher.IndexOf(cleanPublisher, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var nameTokens = new List<string>();
            if (IsValidSearchToken(cleanName))
            {
                nameTokens.Add(cleanName);
                string noSpaces = cleanName.Replace(" ", "").Replace("-", "").Replace("_", "");
                if (IsValidSearchToken(noSpaces) && !nameTokens.Contains(noSpaces, StringComparer.OrdinalIgnoreCase))
                {
                    nameTokens.Add(noSpaces);
                }
            }
            if (IsValidSearchToken(installFolderName) &&
                !nameTokens.Contains(installFolderName, StringComparer.OrdinalIgnoreCase))
            {
                nameTokens.Add(installFolderName);
                string noSpacesFolder = installFolderName.Replace(" ", "").Replace("-", "").Replace("_", "");
                if (IsValidSearchToken(noSpacesFolder) && !nameTokens.Contains(noSpacesFolder, StringComparer.OrdinalIgnoreCase))
                {
                    nameTokens.Add(noSpacesFolder);
                }
            }

            // Extraire les identifiants de package Store (ex: E046963F.LenovoCompanion_10.2606... -> E046963F.LenovoCompanion et LenovoCompanion)
            var vendorFolders = new List<string>();
            var vendorScopedTokens = new List<string>();

            if (!string.IsNullOrWhiteSpace(cleanPublisher) &&
                !string.Equals(cleanPublisher, "Windows", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(cleanPublisher, "System32", StringComparison.OrdinalIgnoreCase))
            {
                vendorFolders.Add(cleanPublisher);
            }

            // Si le nom de l'application commence par un nom d'éditeur (ex: "Lenovo Vantage" -> éditeur "Lenovo", sous-produit "Vantage")
            if (!string.IsNullOrWhiteSpace(cleanName))
            {
                string[] words = cleanName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length >= 2 && words[0].Length >= 2)
                {
                    string firstWord = words[0];
                    string restWords = string.Join(" ", words.Skip(1)).Trim();
                    if (!vendorFolders.Contains(firstWord, StringComparer.OrdinalIgnoreCase) &&
                        !string.Equals(firstWord, "Windows", StringComparison.OrdinalIgnoreCase))
                    {
                        vendorFolders.Add(firstWord);
                    }
                    if (restWords.Length >= 3 && !IsProtectedToken(restWords))
                    {
                        vendorScopedTokens.Add(restWords);
                        string restNoSpace = restWords.Replace(" ", "");
                        if (!vendorScopedTokens.Contains(restNoSpace, StringComparer.OrdinalIgnoreCase))
                            vendorScopedTokens.Add(restNoSpace);

                        // Premier mot du produit si pertinent (ex: "Vantage Service" -> "Vantage")
                        if (words.Length >= 3 && words[1].Length >= 4 && !IsProtectedToken(words[1]))
                        {
                            vendorScopedTokens.Add(words[1]);
                        }
                    }
                }
            }

            string[] pkgCandidates = { app.RegistrySubKeyName, installFolderName };
            foreach (var rawPkg in pkgCandidates)
            {
                if (string.IsNullOrWhiteSpace(rawPkg) || rawPkg.IndexOf('_') <= 0) continue;
                string pkgPrefix = rawPkg.Split('_')[0];
                if (IsValidSearchToken(pkgPrefix) && !nameTokens.Contains(pkgPrefix, StringComparer.OrdinalIgnoreCase))
                {
                    nameTokens.Add(pkgPrefix);
                }
                int dotIdx = pkgPrefix.IndexOf('.');
                if (dotIdx > 0 && dotIdx < pkgPrefix.Length - 3)
                {
                    string afterDot = pkgPrefix.Substring(dotIdx + 1);
                    if (IsValidSearchToken(afterDot) && !nameTokens.Contains(afterDot, StringComparer.OrdinalIgnoreCase))
                    {
                        nameTokens.Add(afterDot);
                    }
                    if (IsValidSearchToken(afterDot) && !vendorScopedTokens.Contains(afterDot, StringComparer.OrdinalIgnoreCase))
                    {
                        vendorScopedTokens.Add(afterDot);
                    }
                }
            }

            // Extraire également les segments parents significatifs du chemin d'installation (ex: Program Files\BraveSoftware\Brave-Browser\Application)
            if (!string.IsNullOrWhiteSpace(installDir))
            {
                foreach (var seg in ExtractMeaningfulPathSegments(installDir))
                {
                    if (IsValidSearchToken(seg) && !nameTokens.Contains(seg, StringComparer.OrdinalIgnoreCase))
                    {
                        nameTokens.Add(seg);
                    }
                }
            }

            var combinedVendorTokens = new List<string>(nameTokens);
            foreach (var vst in vendorScopedTokens)
            {
                if (!combinedVendorTokens.Contains(vst, StringComparer.OrdinalIgnoreCase))
                    combinedVendorTokens.Add(vst);
            }

            // Collecter les noms d'exécutables connus du dossier d'installation pour le scan avancé (Prefetch/MuiCache)
            var knownExeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(installDir) && Directory.Exists(installDir))
            {
                try
                {
                    foreach (var exe in Directory.GetFiles(installDir, "*.exe", SearchOption.TopDirectoryOnly))
                    {
                        knownExeNames.Add(Path.GetFileName(exe));
                    }
                }
                catch { }
            }
            if (!string.IsNullOrWhiteSpace(app.DisplayIcon))
            {
                string iconExe = AppScanner.ExtractExecutablePath(app.DisplayIcon);
                if (!string.IsNullOrWhiteSpace(iconExe) && iconExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    knownExeNames.Add(Path.GetFileName(iconExe));
                }
            }

            // =========================================================================
            // NIVEAU 1 : MODE SÛR (Dossier d'installation & Clé Uninstall principale)
            // =========================================================================
            if (!string.IsNullOrWhiteSpace(installDir) &&
                Directory.Exists(installDir) &&
                !AppScanner.IsProtectedSystemDirectory(installDir))
            {
                AddDirectoryLeftover(items, seenPaths, installDir, "Dossier d'installation", "Dossier principal de l'application", true);
            }

            // Vérifier si la clé de registre Uninstall existe encore
            if (!string.IsNullOrWhiteSpace(app.RegistrySubKeyName) && app.Scope != AppScope.WindowsStore)
            {
                RegistryHive hive = app.IsHiveLocalMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
                RegistryView view = app.Is64BitView ? RegistryView.Registry64 : RegistryView.Registry32;
                string subKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + app.RegistrySubKeyName;
                if (RegistrySubKeyExists(hive, view, subKeyPath))
                {
                    AddRegistryKeyLeftover(items, seenPaths, app.RegistryKeyPath, "Clé de désinstallation", "Entrée Ajout/Suppression de programmes", true);
                }
            }

                if (depth == ScanDepth.Safe)
                return FilterSharedDirectories(items, app, allInstalledApps);

            // =========================================================================
            // NIVEAU 2 : MODE MODÉRÉ (AppData, ProgramData, Raccourcis, Clés Software)
            // =========================================================================
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localLow = Path.Combine(userProfile, "AppData", "LocalLow");
            string localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");

            string[] dataRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),       // Roaming
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),  // Local
                localPrograms,                                                              // Local\Programs
                localLow,                                                                   // LocalLow
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), // ProgramData
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),          // Program Files
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)        // Program Files (x86)
            };

            foreach (var root in dataRoots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

                // 1) Dossiers et fichiers directs correspondant au nom de l'application
                ScanMatchingSubDirectories(root, nameTokens, items, seenPaths, "Données d'application", true);

                // 2) Sous-dossiers dans <Root>\<Vendor>\<AppName> (ex: ProgramData\Lenovo\Vantage, AppData\Local\Google\Chrome)
                foreach (var vendor in vendorFolders)
                {
                    if (string.IsNullOrWhiteSpace(vendor)) continue;
                    string pubDir = Path.Combine(root, vendor);
                    if (Directory.Exists(pubDir))
                    {
                        ScanMatchingSubDirectories(pubDir, combinedVendorTokens, items, seenPaths, "Données d'application (" + vendor + ")", true);

                        // Si l'éditeur n'est pas un constructeur système partagé et ne contient que cette application
                        if (!IsProtectedToken(vendor) && !publisherSharedByOtherApps && !AppScanner.IsProtectedSystemDirectory(pubDir))
                        {
                            try
                            {
                                var subDirs = Directory.GetDirectories(pubDir);
                                if (subDirs.Length == 0 || subDirs.All(d => MatchesAnyToken(Path.GetFileName(d), combinedVendorTokens)))
                                {
                                    AddDirectoryLeftover(items, seenPaths, pubDir, "Dossier Éditeur", "Dossier éditeur dédié à cette application", false);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }

            // Scan approfondi dans WindowsApps et Packages (dossiers ET fichiers résiduels propres à l'application)
            ScanStoreAndPackageContainers(nameTokens, vendorFolders, vendorScopedTokens, items, seenPaths);

            // Raccourcis Menu Démarrer et Bureau
            string[] shortcutRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            };

            foreach (var sRoot in shortcutRoots)
            {
                if (string.IsNullOrWhiteSpace(sRoot) || !Directory.Exists(sRoot)) continue;
                ScanShortcuts(sRoot, nameTokens, installDir, items, seenPaths);
            }

            // Clés de registre HKCU\Software et HKLM\SOFTWARE
            ScanSoftwareRegistryKeys(RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU", nameTokens, vendorFolders, combinedVendorTokens, publisherSharedByOtherApps, items, seenPaths);
            ScanSoftwareRegistryKeys(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM", nameTokens, vendorFolders, combinedVendorTokens, publisherSharedByOtherApps, items, seenPaths);
            ScanSoftwareRegistryKeys(RegistryHive.LocalMachine, RegistryView.Registry32, @"HKLM\WOW6432Node", nameTokens, vendorFolders, combinedVendorTokens, publisherSharedByOtherApps, items, seenPaths);

            if (depth == ScanDepth.Moderate)
                return FilterSharedDirectories(items, app, allInstalledApps);

            // =========================================================================
            // NIVEAU 3 : MODE AVANCÉ (Temp, CrashDumps, Prefetch, Run, MuiCache, Store)
            // =========================================================================
            // 1. Dossier Temp utilisateur
            string tempPath = Path.GetTempPath();
            if (!string.IsNullOrWhiteSpace(tempPath) && Directory.Exists(tempPath))
            {
                ScanMatchingSubDirectories(tempPath, nameTokens, items, seenPaths, "Fichiers Temporaires", false);
            }

            // 2. CrashDumps
            string crashDumpsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
            if (Directory.Exists(crashDumpsDir))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(crashDumpsDir, "*.dmp"))
                    {
                        string fName = Path.GetFileName(file);
                        if (MatchesAnyToken(fName, nameTokens) ||
                            knownExeNames.Any(exe => fName.StartsWith(exe, StringComparison.OrdinalIgnoreCase)))
                        {
                            AddFileLeftover(items, seenPaths, file, "Rapports de crash (CrashDumps)", "Dump mémoire de l'application", true);
                        }
                    }
                }
                catch { }
            }

            // 3. Windows Prefetch (si accessible en Administrateur)
            string prefetchDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
            if (Directory.Exists(prefetchDir) && knownExeNames.Count > 0)
            {
                try
                {
                    foreach (var pfFile in Directory.GetFiles(prefetchDir, "*.pf"))
                    {
                        string pfName = Path.GetFileName(pfFile);
                        if (knownExeNames.Any(exe => pfName.StartsWith(exe + "-", StringComparison.OrdinalIgnoreCase)))
                        {
                            AddFileLeftover(items, seenPaths, pfFile, "Cache Windows Prefetch", "Trace d'exécution Prefetch", true);
                        }
                    }
                }
                catch { }
            }

            // 4. Entrées de démarrage (Run / RunOnce) et Caches d'exécution du Registre
            ScanRegistryValuesReferencingPath(
                RegistryHive.CurrentUser, RegistryView.Registry64,
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "Démarrage automatique Windows",
                installDir, nameTokens, items, seenPaths);

            ScanRegistryValuesReferencingPath(
                RegistryHive.LocalMachine, RegistryView.Registry64,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Démarrage automatique Windows (Système)",
                installDir, nameTokens, items, seenPaths);

            ScanRegistryValuesReferencingPath(
                RegistryHive.LocalMachine, RegistryView.Registry32,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Démarrage automatique Windows (32-bit)",
                installDir, nameTokens, items, seenPaths);

            if (!string.IsNullOrWhiteSpace(installDir))
            {
                // MuiCache
                ScanRegistryValueNamesReferencingPath(
                    RegistryHive.CurrentUser, RegistryView.Registry64,
                    @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
                    @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
                    "Cache d'interface Windows (MuiCache)",
                    installDir, items, seenPaths);

                // AppCompatFlags Compatibility Assistant Store
                ScanRegistryValueNamesReferencingPath(
                    RegistryHive.CurrentUser, RegistryView.Registry64,
                    @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store",
                    @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store",
                    "Cache de compatibilité (AppCompatFlags)",
                    installDir, items, seenPaths);

                // Explorer FeatureUsage AppSwitched
                ScanRegistryValueNamesReferencingPath(
                    RegistryHive.CurrentUser, RegistryView.Registry64,
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\AppSwitched",
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\AppSwitched",
                    "Historique d'exécution Explorer (AppSwitched)",
                    installDir, items, seenPaths);
            }

            return FilterSharedDirectories(items, app, allInstalledApps);
        }

        private static List<LeftoverItem> FilterSharedDirectories(
            List<LeftoverItem> items,
            InstalledApp currentApp,
            List<InstalledApp> allInstalledApps)
        {
            if (allInstalledApps != null && allInstalledApps.Count > 0)
            {
                items.RemoveAll(item =>
                {
                    if (item.Type != LeftoverType.Directory || string.IsNullOrWhiteSpace(item.Path))
                        return false;

                    string normItem = item.Path.TrimEnd('\\', '/');
                    return allInstalledApps.Any(other =>
                    {
                        if (other == null || other.Id == currentApp.Id || string.IsNullOrWhiteSpace(other.InstallLocation))
                            return false;
                        string normOther = other.InstallLocation.TrimEnd('\\', '/');
                        return string.Equals(normOther, normItem, StringComparison.OrdinalIgnoreCase) ||
                               normOther.StartsWith(normItem + "\\", StringComparison.OrdinalIgnoreCase);
                    });
                });
            }
            return items;
        }

        /// <summary>
        /// Détecte automatiquement les dossiers et clés de registre orphelins (qui n'appartiennent à AUCUNE application installée)
        /// </summary>
        public static List<OrphanCandidate> FindAllOrphanCandidates(List<InstalledApp> installedApps)
        {
            var orphans = new List<OrphanCandidate>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Constituer la liste blanche des tokens et dossiers appartenant à des applications actives
            var activeTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var activeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (installedApps != null)
            {
                foreach (var app in installedApps)
                {
                    if (app == null) continue;

                    if (!string.IsNullOrWhiteSpace(app.DisplayName))
                    {
                        string norm = NormalizeAppName(app.DisplayName);
                        if (norm.Length >= 3 && !IsProtectedToken(norm)) activeTokens.Add(norm);

                        string pub = NormalizePublisherName(app.Publisher);
                        if (pub.Length >= 3 && !IsProtectedToken(pub)) activeTokens.Add(pub);

                        var parts = app.DisplayName.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                        {
                            if (part.Length >= 4 && !IsProtectedToken(part)) activeTokens.Add(part);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(app.InstallLocation))
                    {
                        try
                        {
                            string full = Path.GetFullPath(app.InstallLocation).TrimEnd('\\', '/');
                            activeDirs.Add(full);
                            string folderName = Path.GetFileName(full);
                            if (folderName.Length >= 3 && !IsProtectedToken(folderName)) activeTokens.Add(folderName);
                        }
                        catch { }
                    }
                }
            }

            // Mots-clés / noms protégés à ne jamais considérer comme orphelins
            var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Microsoft", "Windows", "Common Files", "Internet Explorer", "Windows Defender",
                "Windows Defender Advanced Threat Protection", "Windows Mail", "Windows Media Player",
                "Windows NT", "Windows Photo Viewer", "WindowsPowerShell", "MSBuild", "Reference Assemblies",
                "dotnet", "System", "Temp", "CrashDumps", "Packages", "WindowsApps", "ModifiableWindowsApps",
                "Default", "Public", "All Users", "Application Data", "History", "Local Settings", "Templates",
                "Google", "Intel", "NVIDIA", "AMD", "Realtek", "Lenovo", "Dell", "HP", "ASUS", "Acer"
            };

            // 2. Parcourir les dossiers d'installation et de données
            var targetRoots = new List<Tuple<string, string>>
            {
                Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Dossier Program Files"),
                Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Dossier Program Files (x86)"),
                Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Données ProgramData"),
                Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Données AppData Roaming"),
                Tuple.Create(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Données AppData Local")
            };

            foreach (var rootPair in targetRoots)
            {
                string root = rootPair.Item1;
                string cat = rootPair.Item2;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

                try
                {
                    foreach (var dir in Directory.GetDirectories(root))
                    {
                        try
                        {
                            string dirName = Path.GetFileName(dir);
                            if (string.IsNullOrWhiteSpace(dirName) || dirName.StartsWith(".") || protectedNames.Contains(dirName) || IsProtectedToken(dirName))
                                continue;

                            string fullDir = Path.GetFullPath(dir).TrimEnd('\\', '/');
                            if (AppScanner.IsProtectedSystemDirectory(fullDir) || seenPaths.Contains(fullDir))
                                continue;

                            // Vérifier si ce dossier appartient directement à une application installée
                            bool isActive = activeDirs.Any(ad => string.Equals(ad, fullDir, StringComparison.OrdinalIgnoreCase) ||
                                                                 ad.StartsWith(fullDir + "\\", StringComparison.OrdinalIgnoreCase));
                            if (isActive) continue;

                            // Vérifier si le nom du dossier matche un token actif
                            bool matchesToken = activeTokens.Any(t =>
                                string.Equals(dirName, t, StringComparison.OrdinalIgnoreCase) ||
                                (t.Length >= 5 && dirName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (dirName.Length >= 5 && t.IndexOf(dirName, StringComparison.OrdinalIgnoreCase) >= 0));

                            if (matchesToken) continue;

                            // C'est un candidat orphelin potentiel !
                            var di = new DirectoryInfo(fullDir);
                            long size = 0;
                            try { size = AppScanner.FastCalculateDirectorySize(fullDir, 2); } catch { }

                            seenPaths.Add(fullDir);
                            orphans.Add(new OrphanCandidate
                            {
                                Name = dirName,
                                Path = fullDir,
                                Category = cat,
                                Reason = "Aucun programme installé correspondant détecté",
                                SizeBytes = size,
                                SizeFormatted = FormatUtils.FormatBytes(size),
                                LastModified = di.LastWriteTime,
                                IsDirectory = true,
                                IsRegistry = false,
                                IsSelected = false // Décoché par défaut par sécurité, l'utilisateur choisit
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // 3. Analyser le Registre HKCU\Software et HKLM\Software pour repérer les clés logicielles orphelines
            ScanOrphanRegistryHive(RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU", activeTokens, seenPaths, orphans);
            ScanOrphanRegistryHive(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM", activeTokens, seenPaths, orphans);
            ScanOrphanRegistryHive(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM (WOW6432Node)", activeTokens, seenPaths, orphans);

            return orphans.OrderByDescending(o => o.SizeBytes).ToList();
        }

        private static void ScanOrphanRegistryHive(
            RegistryHive hive,
            RegistryView view,
            string label,
            HashSet<string> activeTokens,
            HashSet<string> seenPaths,
            List<OrphanCandidate> orphans)
        {
            var protectedRegKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Classes", "Clients", "Microsoft", "Policies", "RegisteredApplications", "Windows",
                "AppDataLow", "Google", "Intel", "NVIDIA", "AMD", "Realtek", "Lenovo", "Dell", "HP", "ASUS", "Acer"
            };

            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var softwareKey = baseKey.OpenSubKey("SOFTWARE", false))
                {
                    if (softwareKey == null) return;
                    foreach (var keyName in softwareKey.GetSubKeyNames())
                    {
                        if (string.IsNullOrWhiteSpace(keyName) || protectedRegKeys.Contains(keyName) || IsProtectedToken(keyName))
                            continue;

                        bool matches = activeTokens.Any(t =>
                            string.Equals(keyName, t, StringComparison.OrdinalIgnoreCase) ||
                            (t.Length >= 5 && keyName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (keyName.Length >= 5 && t.IndexOf(keyName, StringComparison.OrdinalIgnoreCase) >= 0));

                        if (matches) continue;

                        string displayPath;
                        if (hive == RegistryHive.CurrentUser)
                            displayPath = @"HKCU\Software\" + keyName;
                        else if (view == RegistryView.Registry32)
                            displayPath = @"HKLM\SOFTWARE\WOW6432Node\" + keyName;
                        else
                            displayPath = @"HKLM\SOFTWARE\" + keyName;
                        if (!seenPaths.Add(displayPath)) continue;

                        orphans.Add(new OrphanCandidate
                        {
                            Name = keyName,
                            Path = displayPath,
                            Category = "Clé Registre Logiciel",
                            Reason = "Clé logicielle présente sans installation active associée",
                            SizeBytes = 0,
                            SizeFormatted = "—",
                            LastModified = null,
                            IsDirectory = false,
                            IsRegistry = true,
                            IsSelected = false
                        });
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Recherche d'application orpheline / Désinstallation forcée par nom ou chemin de dossier/exécutable
        /// </summary>
        public static List<LeftoverItem> ScanForcedTarget(string targetName, string targetPath, ScanDepth depth, List<InstalledApp> allApps)
        {
            string resolvedDir = null;
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                string cleaned = targetPath.Trim().Trim('"');
                if (File.Exists(cleaned))
                {
                    resolvedDir = Path.GetDirectoryName(cleaned);
                    if (string.IsNullOrWhiteSpace(targetName))
                        targetName = Path.GetFileNameWithoutExtension(cleaned);
                }
                else if (Directory.Exists(cleaned))
                {
                    resolvedDir = cleaned;
                    if (string.IsNullOrWhiteSpace(targetName))
                        targetName = Path.GetFileName(cleaned.TrimEnd('\\', '/'));
                }
            }

            var dummyApp = new InstalledApp
            {
                Id = "FORCED_TARGET",
                DisplayName = targetName ?? "",
                Publisher = "",
                InstallLocation = resolvedDir ?? "",
                Scope = AppScope.System64
            };

            return ScanForApp(dummyApp, depth, allApps);
        }

        private static void ScanMatchingSubDirectories(
            string rootDir,
            List<string> tokens,
            List<LeftoverItem> items,
            HashSet<string> seenPaths,
            string category,
            bool highConfidence)
        {
            if (tokens == null || tokens.Count == 0) return;
            try
            {
                foreach (var dir in Directory.GetDirectories(rootDir))
                {
                    string folderName = Path.GetFileName(dir);
                    if (IsProtectedToken(folderName)) continue;

                    if (MatchesAnyToken(folderName, tokens))
                    {
                        AddDirectoryLeftover(items, seenPaths, dir, category, "Dossier résiduel détecté", highConfidence);
                    }
                }

                foreach (var file in Directory.GetFiles(rootDir))
                {
                    string fileNameNoExt = Path.GetFileNameWithoutExtension(file);
                    if (IsProtectedToken(fileNameNoExt)) continue;

                    if (MatchesAnyToken(fileNameNoExt, tokens))
                    {
                        AddFileLeftover(items, seenPaths, file, category, "Fichier résiduel détecté", highConfidence);
                    }
                }
            }
            catch { }
        }

        private static void ScanStoreAndPackageContainers(
            List<string> nameTokens,
            List<string> vendorFolders,
            List<string> vendorScopedTokens,
            List<LeftoverItem> items,
            HashSet<string> seenPaths)
        {
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            string[] packageContainers = new[]
            {
                Path.Combine(progFiles, "WindowsApps"),
                Path.Combine(progFiles, @"WindowsApps\Deleted"),
                Path.Combine(progFiles, @"WindowsApps\DeletedAllUserPackages"),
                Path.Combine(localAppData, "Packages"),
                Path.Combine(programData, "Packages"),
                Path.Combine(localAppData, @"Microsoft\WindowsApps")
            };

            foreach (var container in packageContainers)
            {
                if (string.IsNullOrWhiteSpace(container) || !Directory.Exists(container)) continue;

                string catLabel = container.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "Dossier WindowsApps (Store)"
                    : "Données de Package (Store)";

                try
                {
                    foreach (var dir in Directory.GetDirectories(container))
                    {
                        string dirName = Path.GetFileName(dir);
                        if (IsProtectedToken(dirName)) continue;

                        if (MatchesPackageEntry(dirName, nameTokens, vendorFolders, vendorScopedTokens))
                        {
                            AddDirectoryLeftover(items, seenPaths, dir, catLabel, "Package ou dossier résiduel dans " + Path.GetFileName(container), true);
                        }
                    }
                }
                catch { }

                try
                {
                    foreach (var file in Directory.GetFiles(container))
                    {
                        string fileName = Path.GetFileName(file);
                        string fileNameNoExt = Path.GetFileNameWithoutExtension(file);
                        if (IsProtectedToken(fileNameNoExt)) continue;

                        if (MatchesPackageEntry(fileName, nameTokens, vendorFolders, vendorScopedTokens) ||
                            MatchesPackageEntry(fileNameNoExt, nameTokens, vendorFolders, vendorScopedTokens))
                        {
                            AddFileLeftover(items, seenPaths, file, catLabel, "Fichier ou alias résiduel dans " + Path.GetFileName(container), true);
                        }
                    }
                }
                catch { }
            }
        }

        private static bool MatchesPackageEntry(
            string entryName,
            List<string> nameTokens,
            List<string> vendorFolders,
            List<string> vendorScopedTokens)
        {
            if (string.IsNullOrWhiteSpace(entryName)) return false;

            if (MatchesAnyToken(entryName, nameTokens))
                return true;

            // Vérifier si un token de package (ex: LenovoCompanion) apparaît après un point dans E046963F.LenovoCompanion_...
            if (nameTokens != null)
            {
                foreach (var token in nameTokens)
                {
                    if (!IsValidSearchToken(token) || token.Length < 5) continue;
                    string tokenNoSpace = token.Replace(" ", "");
                    if (entryName.IndexOf("." + tokenNoSpace + "_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        entryName.IndexOf("." + tokenNoSpace + ".", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        entryName.StartsWith(tokenNoSpace + "_", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            // Vérifier la présence combinée Éditeur + Produit (ex: "Lenovo" + "Vantage") dans le nom du package
            if (vendorFolders != null && vendorScopedTokens != null && vendorFolders.Count > 0 && vendorScopedTokens.Count > 0)
            {
                bool hasVendor = vendorFolders.Any(v => !string.IsNullOrWhiteSpace(v) && v.Length >= 3 && entryName.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hasVendor)
                {
                    bool hasProduct = vendorScopedTokens.Any(p => !string.IsNullOrWhiteSpace(p) && p.Length >= 4 && entryName.IndexOf(p.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) >= 0);
                    if (hasProduct) return true;
                }
            }

            return false;
        }

        private static void ScanShortcuts(
            string rootDir,
            List<string> tokens,
            string installDir,
            List<LeftoverItem> items,
            HashSet<string> seenPaths)
        {
            if (tokens == null || tokens.Count == 0) return;
            try
            {
                // Vérifier les sous-dossiers de Menu Démarrer qui portent le nom de l'application
                foreach (var subDir in Directory.GetDirectories(rootDir))
                {
                    string dirName = Path.GetFileName(subDir);
                    if (IsProtectedToken(dirName)) continue;

                    if (MatchesAnyToken(dirName, tokens))
                    {
                        AddDirectoryLeftover(items, seenPaths, subDir, "Menu Démarrer", "Dossier de raccourcis du Menu Démarrer", true);
                    }
                }

                // Vérifier les fichiers .lnk et .url
                foreach (var file in Directory.GetFiles(rootDir))
                {
                    string ext = Path.GetExtension(file);
                    if (!string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(ext, ".url", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string fileNameNoExt = Path.GetFileNameWithoutExtension(file);
                    if (MatchesAnyToken(fileNameNoExt, tokens))
                    {
                        AddShortcutLeftover(items, seenPaths, file, "Raccourci Bureau / Menu Démarrer", "Raccourci de l'application", true);
                    }
                }
            }
            catch { }
        }

        private static void ScanSoftwareRegistryKeys(
            RegistryHive hive,
            RegistryView view,
            string displayPrefix,
            List<string> nameTokens,
            List<string> vendorFolders,
            List<string> combinedVendorTokens,
            bool publisherShared,
            List<LeftoverItem> items,
            HashSet<string> seenPaths)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var softwareKey = baseKey.OpenSubKey("Software", false))
                {
                    if (softwareKey == null) return;

                    foreach (var subName in softwareKey.GetSubKeyNames())
                    {
                        string fullRegDisplay = (displayPrefix == @"HKLM\WOW6432Node")
                            ? @"HKLM\SOFTWARE\WOW6432Node\" + subName
                            : displayPrefix + @"\Software\" + subName;

                        // 1. Correspondance avec un dossier éditeur (ex: Software\Lenovo\VantageService) -> inspecter les sous-clés même si l'éditeur est protégé
                        if (vendorFolders != null && vendorFolders.Contains(subName, StringComparer.OrdinalIgnoreCase))
                        {
                            try
                            {
                                using (var pubKey = softwareKey.OpenSubKey(subName, false))
                                {
                                    if (pubKey != null)
                                    {
                                        var pubSubKeys = pubKey.GetSubKeyNames();
                                        foreach (var childName in pubSubKeys)
                                        {
                                            if (IsProtectedToken(childName)) continue;
                                            if (MatchesAnyToken(childName, combinedVendorTokens))
                                            {
                                                string childDisplay = fullRegDisplay + @"\" + childName;
                                                AddRegistryKeyLeftover(items, seenPaths, childDisplay, "Registre Logiciel (" + subName + ")", "Sous-clé de l'application chez l'éditeur", true);
                                            }
                                        }

                                        if (!IsProtectedToken(subName) && !publisherShared && (pubSubKeys.Length == 0 || pubSubKeys.All(k => MatchesAnyToken(k, combinedVendorTokens))))
                                        {
                                            AddRegistryKeyLeftover(items, seenPaths, fullRegDisplay, "Registre Éditeur", "Clé éditeur dédiée uniquement à cette application", false);
                                        }
                                    }
                                }
                            }
                            catch { }
                        }

                        if (IsProtectedToken(subName)) continue;

                        // 2. Correspondance directe avec le nom de l'application
                        if (MatchesAnyToken(subName, nameTokens))
                        {
                            AddRegistryKeyLeftover(items, seenPaths, fullRegDisplay, "Registre Logiciel", "Clé de configuration de l'application", true);
                        }
                    }
                }
            }
            catch { }
        }

        private static void ScanRegistryValuesReferencingPath(
            RegistryHive hive,
            RegistryView view,
            string subKeyPath,
            string displayPrefix,
            string category,
            string installDir,
            List<string> nameTokens,
            List<LeftoverItem> items,
            HashSet<string> seenPaths)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var key = baseKey.OpenSubKey(subKeyPath, false))
                {
                    if (key == null) return;
                    string hiveName = hive == RegistryHive.LocalMachine
                        ? (view == RegistryView.Registry32 ? @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run" : @"HKLM\" + subKeyPath)
                        : @"HKCU\" + subKeyPath;

                    foreach (var valName in key.GetValueNames())
                    {
                        string valData = (key.GetValue(valName) ?? "").ToString();
                        bool match = false;

                        if (!string.IsNullOrWhiteSpace(installDir) &&
                            valData.IndexOf(installDir, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            match = true;
                        }
                        else if (MatchesAnyToken(valName, nameTokens))
                        {
                            match = true;
                        }

                        if (match)
                        {
                            string uniqueKey = hiveName + "::" + valName;
                            if (seenPaths.Add(uniqueKey))
                            {
                                items.Add(new LeftoverItem
                                {
                                    IsSelected = true,
                                    Type = LeftoverType.RegistryValue,
                                    TypeLabel = "Valeur Registre",
                                    Category = category,
                                    Path = hiveName,
                                    ValueName = valName,
                                    Description = string.Format("{0} = {1}", valName, valData),
                                    SizeBytes = 0,
                                    SizeFormatted = "—",
                                    Confidence = "Élevée (Sûr)",
                                    IsHighConfidence = true
                                });
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void ScanRegistryValueNamesReferencingPath(
            RegistryHive hive,
            RegistryView view,
            string subKeyPath,
            string fullKeyDisplay,
            string category,
            string installDir,
            List<LeftoverItem> items,
            HashSet<string> seenPaths)
        {
            if (string.IsNullOrWhiteSpace(installDir)) return;
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var key = baseKey.OpenSubKey(subKeyPath, false))
                {
                    if (key == null) return;
                    foreach (var valName in key.GetValueNames())
                    {
                        if (valName.IndexOf(installDir, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string uniqueKey = fullKeyDisplay + "::" + valName;
                            if (seenPaths.Add(uniqueKey))
                            {
                                items.Add(new LeftoverItem
                                {
                                    IsSelected = true,
                                    Type = LeftoverType.RegistryValue,
                                    TypeLabel = "Trace Registre",
                                    Category = category,
                                    Path = fullKeyDisplay,
                                    ValueName = valName,
                                    Description = "Référence à : " + valName,
                                    SizeBytes = 0,
                                    SizeFormatted = "—",
                                    Confidence = "Élevée (Sûr)",
                                    IsHighConfidence = true
                                });
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static bool RegistrySubKeyExists(RegistryHive hive, RegistryView view, string subKeyPath)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var subKey = baseKey.OpenSubKey(subKeyPath, false))
                {
                    return subKey != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void AddDirectoryLeftover(
            List<LeftoverItem> items,
            HashSet<string> seenPaths,
            string dirPath,
            string category,
            string description,
            bool highConfidence)
        {
            if (string.IsNullOrWhiteSpace(dirPath) || AppScanner.IsProtectedSystemDirectory(dirPath)) return;
            string full = Path.GetFullPath(dirPath).TrimEnd('\\', '/');
            if (!seenPaths.Add(full)) return;

            long size = AppScanner.FastCalculateDirectorySize(full, 3);
            items.Add(new LeftoverItem
            {
                IsSelected = highConfidence,
                Type = LeftoverType.Directory,
                TypeLabel = "Dossier",
                Category = category,
                Path = full,
                Description = description,
                SizeBytes = size,
                SizeFormatted = FormatUtils.FormatBytes(size),
                Confidence = highConfidence ? "Élevée (Sûr)" : "Moyenne (Vérifier)",
                IsHighConfidence = highConfidence
            });
        }

        private static void AddFileLeftover(
            List<LeftoverItem> items,
            HashSet<string> seenPaths,
            string filePath,
            string category,
            string description,
            bool highConfidence)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            string full = Path.GetFullPath(filePath);
            if (!seenPaths.Add(full)) return;

            long size = 0;
            try { size = new FileInfo(full).Length; } catch { }

            items.Add(new LeftoverItem
            {
                IsSelected = highConfidence,
                Type = LeftoverType.File,
                TypeLabel = "Fichier",
                Category = category,
                Path = full,
                Description = description,
                SizeBytes = size,
                SizeFormatted = FormatUtils.FormatBytes(size),
                Confidence = highConfidence ? "Élevée (Sûr)" : "Moyenne (Vérifier)",
                IsHighConfidence = highConfidence
            });
        }

        private static void AddShortcutLeftover(
            List<LeftoverItem> items,
            HashSet<string> seenPaths,
            string filePath,
            string category,
            string description,
            bool highConfidence)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            string full = Path.GetFullPath(filePath);
            if (!seenPaths.Add(full)) return;

            long size = 0;
            try { size = new FileInfo(full).Length; } catch { }

            items.Add(new LeftoverItem
            {
                IsSelected = highConfidence,
                Type = LeftoverType.Shortcut,
                TypeLabel = "Raccourci",
                Category = category,
                Path = full,
                Description = description,
                SizeBytes = size,
                SizeFormatted = FormatUtils.FormatBytes(size),
                Confidence = highConfidence ? "Élevée (Sûr)" : "Moyenne (Vérifier)",
                IsHighConfidence = highConfidence
            });
        }

        private static void AddRegistryKeyLeftover(
            List<LeftoverItem> items,
            HashSet<string> seenPaths,
            string regPath,
            string category,
            string description,
            bool highConfidence)
        {
            if (string.IsNullOrWhiteSpace(regPath)) return;
            if (!seenPaths.Add(regPath)) return;

            items.Add(new LeftoverItem
            {
                IsSelected = highConfidence,
                Type = LeftoverType.RegistryKey,
                TypeLabel = "Clé Registre",
                Category = category,
                Path = regPath,
                Description = description,
                SizeBytes = 0,
                SizeFormatted = "—",
                Confidence = highConfidence ? "Élevée (Sûr)" : "Moyenne (Vérifier)",
                IsHighConfidence = highConfidence
            });
        }

        private static List<string> ExtractMeaningfulPathSegments(string installDir)
        {
            var segments = new List<string>();
            if (string.IsNullOrWhiteSpace(installDir)) return segments;

            try
            {
                string full = Path.GetFullPath(installDir).TrimEnd('\\', '/');
                string[] baseRoots = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ModifiableWindowsApps"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                };

                foreach (var root in baseRoots)
                {
                    if (string.IsNullOrWhiteSpace(root)) continue;
                    string normRoot = root.TrimEnd('\\', '/') + "\\";
                    if (full.StartsWith(normRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = full.Substring(normRoot.Length);
                        foreach (var part in relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (IsValidSearchToken(part))
                                segments.Add(part);
                        }
                        break;
                    }
                }
            }
            catch { }

            return segments;
        }

        public static string NormalizeAppName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "";
            string s = rawName.Trim();

            // Retirer "(Store)", "(x64)", "(64-bit)", "(32-bit)", versions à la fin
            s = Regex.Replace(s, @"\s*\((?:Store|x64|x86|64-bit|32-bit|64 bits|32 bits|User|Machine)[^)]*\)", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s+v?\d+(?:\.\d+)+.*$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s+\d{4}$", ""); // ex: 2024
            return s.Trim();
        }

        public static string NormalizePublisherName(string rawPublisher)
        {
            if (string.IsNullOrWhiteSpace(rawPublisher) ||
                string.Equals(rawPublisher, "Inconnu", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawPublisher, "Microsoft Store / AppX", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
            string s = rawPublisher.Trim();
            s = Regex.Replace(s, @",?\s*(?:Inc\.?|LLC|Ltd\.?|Corp\.?|Corporation|GmbH|S\.A\.?|S\.A\.S\.?|B\.V\.?)$", "", RegexOptions.IgnoreCase);
            return s.Trim();
        }

        private static bool IsProtectedToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return true;
            string t = token.Trim();
            if (t.Length < 3) return true;
            return ProtectedKeywords.Contains(t);
        }

        private static bool IsValidSearchToken(string token)
        {
            return !string.IsNullOrWhiteSpace(token) && token.Trim().Length >= 3 && !IsProtectedToken(token);
        }

        private static bool MatchesAnyToken(string candidateName, List<string> tokens)
        {
            if (string.IsNullOrWhiteSpace(candidateName) || tokens == null || tokens.Count == 0)
                return false;

            string cand = candidateName.Trim();
            if (IsProtectedToken(cand)) return false;

            foreach (var token in tokens)
            {
                if (!IsValidSearchToken(token)) continue;

                // Correspondance exacte (insensible à la casse)
                if (string.Equals(cand, token, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Correspondance sans espaces (ex: "ClearVestige" vs "Clear Vestige")
                string candNoSpace = cand.Replace(" ", "").Replace("-", "").Replace("_", "");
                string tokenNoSpace = token.Replace(" ", "").Replace("-", "").Replace("_", "");
                if (tokenNoSpace.Length >= 4 && string.Equals(candNoSpace, tokenNoSpace, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Si le candidat contient le token en tant que sous-chaîne significative (ex: "Microsoft OneDrive" pour "onedrive" ou "one drive")
                if (tokenNoSpace.Length >= 5 && candNoSpace.IndexOf(tokenNoSpace, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                // Si le nom du candidat commence par le token suivi d'un séparateur ou numéro (et que le token fait au moins 5 caractères)
                if (token.Length >= 5 && cand.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                {
                    char nextChar = cand.Length > token.Length ? cand[token.Length] : '\0';
                    if (nextChar == ' ' || nextChar == '-' || nextChar == '_' || nextChar == '.' || char.IsDigit(nextChar))
                        return true;
                }
            }

            return false;
        }
    }
}
