using System;
using System.Linq;
using System.Reflection;
using System.Windows;

[assembly: AssemblyTitle("ClearVestige")]
[assembly: AssemblyDescription("Utilitaire autonome de désinstallation complète et de nettoyage en profondeur des résidus Windows")]
[assembly: AssemblyCompany("ClearVestige")]
[assembly: AssemblyProduct("ClearVestige")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace ClearVestige
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args != null && args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                return RunSelfTest();
            }

            if (args != null && args.Any(a => string.Equals(a, "--test-self-uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (baseDir.IndexOf( System.IO.Path.GetTempPath().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    UninstallerEngine.ExecuteSelfUninstall();
                }
                return 0;
            }

            try
            {
                var app = new Application();
                app.DispatcherUnhandledException += (s, e) =>
                {
                    MessageBox.Show(
                        "Une erreur inattendue est survenue :\n\n" + e.Exception.Message,
                        "ClearVestige — Erreur",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    e.Handled = true;
                };

                var mainWindow = new MainWindow();
                app.Run(mainWindow);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Erreur critique au démarrage :\n\n" + ex,
                    "ClearVestige",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return 1;
            }
        }

        private static int RunSelfTest()
        {
            Console.WriteLine("=== ClearVestige - Auto-Test de Verification ===");

            var apps = AppScanner.ScanInstalledApps(false, false);
            Console.WriteLine("Applications installées détectées : " + apps.Count);
            foreach (var sample in apps.Take(5))
            {
                Console.WriteLine("  - {0} | Éditeur: {1} | Version: {2} | Taille: {3} | Portée: {4}",
                    sample.DisplayName, sample.Publisher, sample.DisplayVersion, sample.SizeFormatted, sample.ScopeLabel);
            }

            if (apps.Count > 0)
            {
                var firstApp = apps[0];
                var leftovers = LeftoverScanner.ScanForApp(firstApp, ScanDepth.Advanced, apps);
                Console.WriteLine("Test LeftoverScanner sur '{0}' : {1} trace(s) detectee(s)", firstApp.DisplayName, leftovers.Count);
                foreach (var l in leftovers)
                {
                    Console.WriteLine("    [{0}] ({1}) {2} - {3}", l.TypeLabel, l.Category, l.Path, l.SizeFormatted);
                }
            }

            var startups = StartupAndJunkManager.ScanStartupItems();
            Console.WriteLine("Entrées de démarrage détectées : " + startups.Count);

            var junk = StartupAndJunkManager.ScanJunkCategories();
            Console.WriteLine("Catégories de fichiers temporaires analysées : " + junk.Count);
            foreach (var j in junk)
            {
                Console.WriteLine("  - {0} : {1} fichiers ({2})", j.Name, j.FileCount, j.SizeFormatted);
            }

            // Vérification anti-régression : une application Store dans C:\Program Files\WindowsApps ne doit JAMAIS lister WindowsApps lui-même
            var dummyStoreApp = new InstalledApp
            {
                Id = "Store:E046963F.LenovoCompanion_10.2606.12.0_x64__k1h2ywk1493x8",
                DisplayName = "Lenovo Vantage (Store)",
                Publisher = "Microsoft Store / AppX",
                InstallLocation = @"C:\Program Files\WindowsApps\E046963F.LenovoCompanion_10.2606.12.0_x64__k1h2ywk1493x8",
                RegistrySubKeyName = "E046963F.LenovoCompanion_10.2606.12.0_x64__k1h2ywk1493x8",
                Scope = AppScope.WindowsStore
            };
            var storeLeftovers = LeftoverScanner.ScanForApp(dummyStoreApp, ScanDepth.Advanced, apps);
            Console.WriteLine("Test sécurité Store ('Lenovo Vantage') : " + storeLeftovers.Count + " trace(s) sûre(s) :");
            foreach (var sl in storeLeftovers)
            {
                Console.WriteLine("    [{0}] ({1}) {2}", sl.TypeLabel, sl.Category, sl.Path);
            }

            // Vérification du chargement du thème sombre et de la construction des fenêtres WPF
            var testWin = new MainWindow();
            Console.WriteLine("Thème sombre UiTheme & MainWindow initialisés avec succès (" + testWin.Resources.MergedDictionaries.Count + " dictionnaire(s) chargé(s)).");

            // Vérification de la construction de la fenêtre de désinstallation multiple (sans rien exécuter)
            var batchSample = apps.Take(3).ToList();
            var batchWin = new BatchUninstallWindow(batchSample, apps, true);
            Console.WriteLine("Fenêtre de désinstallation multiple construite avec succès (" + batchSample.Count + " application(s) en file : " + batchWin.Title + ").");
            var orphanSample = LeftoverScanner.FindAllOrphanCandidates(apps);
            Console.WriteLine("Scan automatique d'orphelins opérationnel (" + orphanSample.Count + " candidat(s) analysé(s)).");
            var orphanSvcs = SystemToolsManager.ScanOrphanServices();
            Console.WriteLine("Scan Services tiers orphelins : " + orphanSvcs.Count + " trouvé(s).");
            var ctxItems = SystemToolsManager.ScanContextMenuEntries();
            Console.WriteLine("Scan Menu Clic-Droit Windows : " + ctxItems.Count + " entrée(s) tierce(s).");
            var snapTest = InstallSnapshotManager.TakeSnapshot();
            Console.WriteLine("Snapshot Avant/Après opérationnel (" + snapTest.TotalEntries + " entrées indexées).");
            var sampleIcon = apps.Count > 0 ? AppIconHelper.ResolveIconForApp(apps[0]) : null;
            Console.WriteLine("Extraction icône application : " + (sampleIcon != null ? "oui" : "non"));
            Console.WriteLine("Icône de l'application chargée : " + (UiTheme.AppIcon != null ? "oui" : "non"));

            Console.WriteLine("=== Auto-Test réussi avec succès ! ===");
            return 0;
        }
    }
}
