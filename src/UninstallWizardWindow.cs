using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace ClearVestige
{
    public class UninstallWizardWindow : Window
    {
        private readonly InstalledApp _app;
        private readonly List<InstalledApp> _allApps;
        private readonly bool _skipOfficialUninstallerInitially;
        private readonly bool _preferSilent;
        private readonly ObservableCollection<LeftoverItem> _leftovers = new ObservableCollection<LeftoverItem>();

        private RadioButton _rbSafe;
        private RadioButton _rbModerate;
        private RadioButton _rbAdvanced;
        private CheckBox _cbKillProcesses;
        private CheckBox _cbBackupRegistry;
        private CheckBox _cbRecycleBin;
        private CheckBox _cbRestorePoint;

        private Button _btnRunOfficialAndScan;
        private Button _btnScanOnly;
        private Button _btnCleanSelected;
        private TextBlock _txtStepStatus;
        private TextBlock _txtSummaryCount;
        private DataGrid _gridLeftovers;
        private TextBox _txtLogs;
        private ProgressBar _progressBar;

        public bool WasCleanedOrUninstalled { get; private set; }

        public UninstallWizardWindow(
            InstalledApp app,
            List<InstalledApp> allApps,
            bool skipOfficialUninstallerInitially = false,
            bool preferSilent = false)
        {
            _app = app;
            _allApps = allApps;
            _skipOfficialUninstallerInitially = skipOfficialUninstallerInitially;
            _preferSilent = preferSilent;

            InitializeComponent();
            Loaded += OnWindowLoaded;
        }

        private void InitializeComponent()
        {
            UiTheme.ApplyDarkTheme(this);

            Title = "Désinstallation Complète — " + _app.DisplayName;
            UiTheme.ApplyAppIcon(this);
            UiTheme.ClampWindowToWorkArea(this, 1020, 720, 780, 540);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = BrushFromHex("#0F172A");
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");

            var outerGrid = new Grid();
            outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBar = UiTheme.CreateCompactTitleBar(this, "CLEARVESTIGE", "Désinstallation — " + _app.DisplayName, true);
            Grid.SetRow(titleBar, 0);
            outerGrid.Children.Add(titleBar);

            var rootGrid = new Grid { Margin = new Thickness(20, 14, 20, 18) };
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // En-tête application
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Options & Niveau de scan
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Barre d'étape et actions scan
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Tableau des résidus
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(120) }); // Journal d'exécution
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Pied de page

            // =========================================================
            // 1. EN-TÊTE DE L'APPLICATION
            // =========================================================
            var headerBorder = new Border
            {
                Background = BrushFromHex("#1E293B"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var headerStack = new StackPanel();
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock
            {
                Text = _app.DisplayName,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            titleRow.Children.Add(new Border
            {
                Background = BrushFromHex("#3B82F6"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(12, 2, 0, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = _app.ScopeLabel,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White
                }
            });
            headerStack.Children.Add(titleRow);

            var detailsText = new TextBlock
            {
                Text = string.Format("Éditeur : {0}   |   Version : {1}   |   Taille estimée : {2}   |   Dossier : {3}",
                    _app.Publisher,
                    _app.DisplayVersion,
                    _app.SizeFormatted,
                    string.IsNullOrWhiteSpace(_app.InstallLocation) ? "(Non spécifié)" : _app.InstallLocation),
                FontSize = 12,
                Foreground = BrushFromHex("#94A3B8"),
                Margin = new Thickness(0, 6, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            headerStack.Children.Add(detailsText);
            headerBorder.Child = headerStack;
            Grid.SetRow(headerBorder, 0);
            rootGrid.Children.Add(headerBorder);

            // =========================================================
            // 2. OPTIONS DE SCAN ET SÉCURITÉ
            // =========================================================
            var optionsGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

            // Carte Profondeur du Scan
            var depthBorder = new Border
            {
                Background = BrushFromHex("#1E293B"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10)
            };
            var depthStack = new StackPanel();
            depthStack.Children.Add(new TextBlock
            {
                Text = "Niveau d'analyse en profondeur des résidus",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = BrushFromHex("#E2E8F0"),
                Margin = new Thickness(0, 0, 0, 8)
            });

            _rbSafe = CreateRadioButton("Niveau 1 (Rapide) — Dossier d'installation & clé Ajout/Suppression uniquement", false);
            _rbModerate = CreateRadioButton("Niveau 2 (Standard) — + AppData, ProgramData, Raccourcis, Clés Software", false);
            _rbAdvanced = CreateRadioButton("Niveau 3 (Profond) — + Temp, CrashDumps, Prefetch, Démarrage, MuiCache", true);

            depthStack.Children.Add(_rbSafe);
            depthStack.Children.Add(_rbModerate);
            depthStack.Children.Add(_rbAdvanced);
            depthBorder.Child = depthStack;
            Grid.SetColumn(depthBorder, 0);
            optionsGrid.Children.Add(depthBorder);

            // Carte Sécurité & Sauvegarde
            var safetyBorder = new Border
            {
                Background = BrushFromHex("#1E293B"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10)
            };
            var safetyStack = new StackPanel();
            safetyStack.Children.Add(new TextBlock
            {
                Text = "Protections & Sécurité avant nettoyage",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = BrushFromHex("#E2E8F0"),
                Margin = new Thickness(0, 0, 0, 8)
            });

            _cbKillProcesses = CreateCheckBox("Arrêter automatiquement les processus actifs de l'application", true);
            _cbBackupRegistry = CreateCheckBox("Sauvegarder les clés de registre supprimées (.reg dans Backups/)", true);
            _cbRecycleBin = CreateCheckBox("Envoyer les fichiers et dossiers supprimés à la Corbeille Windows", true);
            _cbRestorePoint = CreateCheckBox("Créer un Point de Restauration Système Windows (Admin)", false);

            safetyStack.Children.Add(_cbKillProcesses);
            safetyStack.Children.Add(_cbBackupRegistry);
            safetyStack.Children.Add(_cbRecycleBin);
            safetyStack.Children.Add(_cbRestorePoint);
            safetyBorder.Child = safetyStack;
            Grid.SetColumn(safetyBorder, 2);
            optionsGrid.Children.Add(safetyBorder);

            Grid.SetRow(optionsGrid, 1);
            rootGrid.Children.Add(optionsGrid);

            // =========================================================
            // 3. BARRE D'ÉTAPES ET BOUTONS D'ANALYSE
            // =========================================================
            var stepBorder = new Border
            {
                Background = BrushFromHex("#1E293B"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var stepGrid = new Grid();
            stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _btnRunOfficialAndScan = CreateButton(
                _preferSilent ? "1. Désinstallation Silencieuse + Scan Résidus" : "1. Lancer le Désinstallateur Officiel + Scan Résidus",
                "#2563EB", "#FFFFFF");
            _btnRunOfficialAndScan.Margin = new Thickness(0, 0, 10, 0);
            _btnRunOfficialAndScan.Click += async (s, e) => await ExecuteFullWorkflowAsync(true);
            Grid.SetColumn(_btnRunOfficialAndScan, 0);
            stepGrid.Children.Add(_btnRunOfficialAndScan);

            _btnScanOnly = CreateButton("Scanner uniquement les résidus (Sans désinstallateur)", "#475569", "#FFFFFF");
            _btnScanOnly.Click += async (s, e) => await ExecuteFullWorkflowAsync(false);
            Grid.SetColumn(_btnScanOnly, 1);
            stepGrid.Children.Add(_btnScanOnly);

            var statusStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            };
            _txtStepStatus = new TextBlock
            {
                Text = "Prêt. Cliquez sur le bouton 1 pour désinstaller puis scanner les résidus.",
                Foreground = BrushFromHex("#38BDF8"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5
            };
            _progressBar = new ProgressBar
            {
                Height = 6,
                Margin = new Thickness(0, 6, 0, 0),
                Background = BrushFromHex("#0F172A"),
                Foreground = BrushFromHex("#38BDF8"),
                BorderThickness = new Thickness(0),
                Visibility = Visibility.Collapsed
            };
            statusStack.Children.Add(_txtStepStatus);
            statusStack.Children.Add(_progressBar);
            Grid.SetColumn(statusStack, 2);
            stepGrid.Children.Add(statusStack);

            stepBorder.Child = stepGrid;
            Grid.SetRow(stepBorder, 2);
            rootGrid.Children.Add(stepBorder);

            // =========================================================
            // 4. LISTE DES RÉSIDUS DÉTECTÉS (FICHIERS + REGISTRE)
            // =========================================================
            var leftoversContainer = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            leftoversContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftoversContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var toolbarDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            _txtSummaryCount = new TextBlock
            {
                Text = "Résidus détectés : 0 élément",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = BrushFromHex("#E2E8F0"),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_txtSummaryCount, Dock.Left);
            toolbarDock.Children.Add(_txtSummaryCount);

            var selButtonsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnSelectAll = CreateSmallButton("Tout cocher", (s, e) => SetSelectionAll(true));
            var btnSelectSafe = CreateSmallButton("Cocher uniquement Sûrs", (s, e) => SetSelectionSafeOnly());
            var btnSelectNone = CreateSmallButton("Tout décocher", (s, e) => SetSelectionAll(false));
            var btnOpenLeftover = CreateSmallButton("Ouvrir l'emplacement", OnOpenSelectedLeftoverLocation);

            selButtonsPanel.Children.Add(btnSelectAll);
            selButtonsPanel.Children.Add(btnSelectSafe);
            selButtonsPanel.Children.Add(btnSelectNone);
            selButtonsPanel.Children.Add(btnOpenLeftover);
            DockPanel.SetDock(selButtonsPanel, Dock.Right);
            toolbarDock.Children.Add(selButtonsPanel);

            Grid.SetRow(toolbarDock, 0);
            leftoversContainer.Children.Add(toolbarDock);

            _gridLeftovers = CreateLeftoversDataGrid();
            _gridLeftovers.ItemsSource = _leftovers;
            Grid.SetRow(_gridLeftovers, 1);
            leftoversContainer.Children.Add(_gridLeftovers);

            Grid.SetRow(leftoversContainer, 3);
            rootGrid.Children.Add(leftoversContainer);

            // =========================================================
            // 5. JOURNAL D'EXÉCUTION (LOGS)
            // =========================================================
            var logBorder = new Border
            {
                Background = BrushFromHex("#090D16"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(8)
            };
            _txtLogs = new TextBox
            {
                Background = Brushes.Transparent,
                Foreground = BrushFromHex("#A7F3D0"),
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11.5,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = "[ClearVestige] Assistant prêt pour : " + _app.DisplayName + "\r\n"
            };
            logBorder.Child = _txtLogs;
            Grid.SetRow(logBorder, 4);
            rootGrid.Children.Add(logBorder);

            // =========================================================
            // 6. PIED DE PAGE (BOUTON SUPPRIMER LES RÉSIDUS & FERMER)
            // =========================================================
            var footerDock = new DockPanel();
            var btnClose = CreateButton("Fermer", "#334155", "#E2E8F0");
            btnClose.Click += (s, e) => Close();
            DockPanel.SetDock(btnClose, Dock.Right);
            footerDock.Children.Add(btnClose);

            _btnCleanSelected = CreateButton("Supprimer définitivement les résidus cochés (0)", "#DC2626", "#FFFFFF");
            _btnCleanSelected.IsEnabled = false;
            _btnCleanSelected.Margin = new Thickness(0, 0, 12, 0);
            _btnCleanSelected.Click += async (s, e) => await CleanSelectedLeftoversAsync();
            DockPanel.SetDock(_btnCleanSelected, Dock.Left);
            footerDock.Children.Add(_btnCleanSelected);

            Grid.SetRow(footerDock, 5);
            rootGrid.Children.Add(footerDock);

            Grid.SetRow(rootGrid, 1);
            outerGrid.Children.Add(rootGrid);

            Content = UiTheme.ConfigureCustomChrome(this, 46, outerGrid, true);
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (_skipOfficialUninstallerInitially)
            {
                await ExecuteFullWorkflowAsync(false);
            }
        }

        private ScanDepth GetSelectedScanDepth()
        {
            if (_rbSafe.IsChecked == true) return ScanDepth.Safe;
            if (_rbModerate.IsChecked == true) return ScanDepth.Moderate;
            return ScanDepth.Advanced;
        }

        private async Task ExecuteFullWorkflowAsync(bool runOfficialUninstaller)
        {
            SetBusy(true);
            try
            {
                bool killProcs = _cbKillProcesses.IsChecked == true;
                bool createRestore = _cbRestorePoint.IsChecked == true;
                ScanDepth depth = GetSelectedScanDepth();

                // Étape A : Point de restauration système optionnel
                if (createRestore)
                {
                    _txtStepStatus.Text = "Création d'un point de restauration système Windows...";
                    AppendLog("Création d'un point de restauration Windows en cours...");
                    string restoreMsg = await Task.Run(() =>
                    {
                        string msg;
                        UninstallerEngine.CreateSystemRestorePoint("Avant désinstallation de " + _app.DisplayName, out msg);
                        return msg;
                    });
                    AppendLog(restoreMsg);
                }

                // Étape B : Arrêt des processus actifs de l'application
                if (killProcs && !string.IsNullOrWhiteSpace(_app.InstallLocation))
                {
                    _txtStepStatus.Text = "Vérification et arrêt des processus actifs de l'application...";
                    var procLogs = new List<string>();
                    int killed = await Task.Run(() => UninstallerEngine.KillRunningAppProcesses(_app.InstallLocation, procLogs));
                    foreach (var l in procLogs) AppendLog(l);
                    if (killed > 0)
                        AppendLog(string.Format("{0} processus actif(s) arrêté(s).", killed));
                }

                // Étape C : Désinstallateur officiel
                if (runOfficialUninstaller)
                {
                    _txtStepStatus.Text = "Exécution du désinstallateur officiel en cours (veuillez suivre ses instructions)...";
                    AppendLog("Lancement de la commande : " + (_preferSilent && !string.IsNullOrWhiteSpace(_app.QuietUninstallString) ? _app.QuietUninstallString : _app.UninstallString));

                    string uninstMsg = "";
                    bool ok = await Task.Run(() => UninstallerEngine.RunOfficialUninstaller(_app, _preferSilent, out uninstMsg));
                    AppendLog(uninstMsg);
                    if (ok) WasCleanedOrUninstalled = true;
                }

                // Étape D : Scan approfondi des résidus
                _txtStepStatus.Text = "Analyse approfondie des fichiers, dossiers et clés de registre résiduels...";
                AppendLog(string.Format("Démarrage du scan des résidus (Mode : {0})...", depth));

                var foundItems = await Task.Run(() => LeftoverScanner.ScanForApp(_app, depth, _allApps));

                _leftovers.Clear();
                foreach (var item in foundItems)
                {
                    item.PropertyChanged += (s, ev) =>
                    {
                        if (ev.PropertyName == "IsSelected") UpdateSelectionCounters();
                    };
                    _leftovers.Add(item);
                }

                UpdateSelectionCounters();

                if (_leftovers.Count == 0)
                {
                    _txtStepStatus.Text = "Aucune trace résiduelle trouvée ! L'application est complètement propre.";
                    AppendLog("Scan terminé : 0 résidu détecté sur le disque et dans le registre.");
                }
                else
                {
                    _txtStepStatus.Text = string.Format("Scan terminé : {0} élément(s) résiduel(s) détecté(s). Vérifiez la sélection puis cliquez sur Supprimer.", _leftovers.Count);
                    AppendLog(string.Format("Scan terminé : {0} élément(s) résiduel(s) trouvé(s).", _leftovers.Count));
                }
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task CleanSelectedLeftoversAsync()
        {
            var selected = _leftovers.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Veuillez cocher au moins un élément résiduel à supprimer.", "Aucun élément sélectionné", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool useRecycleBin = _cbRecycleBin.IsChecked == true;
            bool backupReg = _cbBackupRegistry.IsChecked == true;

            var confirm = MessageBox.Show(
                this,
                string.Format("Voulez-vous supprimer les {0} éléments résiduels sélectionnés pour « {1} » ?\n\n" +
                              "• Fichiers/Dossiers : {2}\n" +
                              "• Clés/Valeurs Registre : {3}",
                              selected.Count,
                              _app.DisplayName,
                              useRecycleBin ? "Envoyés à la Corbeille Windows" : "Suppression définitive",
                              backupReg ? "Sauvegardées automatiquement dans Backups/ avant suppression" : "Suppression directe"),
                "Confirmation du nettoyage des résidus",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            SetBusy(true);
            try
            {
                _txtStepStatus.Text = "Nettoyage des fichiers, dossiers et clés de registre résiduels en cours...";

                // S'assurer qu'aucun processus résiduel ne verrouille les fichiers
                if (_cbKillProcesses.IsChecked == true && !string.IsNullOrWhiteSpace(_app.InstallLocation))
                {
                    await Task.Run(() => UninstallerEngine.KillRunningAppProcesses(_app.InstallLocation));
                }

                string appName = _app.DisplayName;
                var report = await Task.Run(() => UninstallerEngine.CleanSelectedLeftovers(appName, selected, useRecycleBin, backupReg));

                foreach (var log in report.Logs)
                {
                    AppendLog(log);
                }

                if (!string.IsNullOrWhiteSpace(report.BackupFolderPath))
                {
                    AppendLog("[Sauvegarde Registre] Enregistrée dans : " + report.BackupFolderPath);
                }

                WasCleanedOrUninstalled = true;

                // Retirer de la liste les éléments qui ont été nettoyés
                ScanDepth currentDepth = GetSelectedScanDepth();
                var remaining = await Task.Run(() => LeftoverScanner.ScanForApp(_app, currentDepth, _allApps));
                _leftovers.Clear();
                foreach (var r in remaining)
                {
                    r.PropertyChanged += (s, ev) =>
                    {
                        if (ev.PropertyName == "IsSelected") UpdateSelectionCounters();
                    };
                    _leftovers.Add(r);
                }
                UpdateSelectionCounters();

                string extraReboot = report.ScheduledForRebootItems > 0
                    ? string.Format(", {0} planifié(s) au redémarrage", report.ScheduledForRebootItems)
                    : "";
                string extraFailed = report.FailedItems > 0
                    ? string.Format(", {0} échec(s)", report.FailedItems)
                    : "";
                string summary = string.Format(
                    "Nettoyage terminé ! {0} dossier(s), {1} fichier(s), {2} entrée(s) de registre supprimés ({3} libérés){4}{5}.",
                    report.DeletedDirectories,
                    report.DeletedFiles,
                    report.DeletedRegistryItems,
                    FormatUtils.FormatBytes(report.FreedBytes),
                    extraReboot,
                    extraFailed);

                _txtStepStatus.Text = summary;
                AppendLog("=== " + summary + " ===");

                MessageBox.Show(
                    this,
                    summary + (string.IsNullOrWhiteSpace(report.BackupFolderPath) ? "" : "\n\nSauvegarde du registre créée dans :\n" + report.BackupFolderPath),
                    "Désinstallation Complète Terminée",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetSelectionAll(bool select)
        {
            foreach (var item in _leftovers)
                item.IsSelected = select;
            UpdateSelectionCounters();
        }

        private void SetSelectionSafeOnly()
        {
            foreach (var item in _leftovers)
                item.IsSelected = item.IsHighConfidence;
            UpdateSelectionCounters();
        }

        private void UpdateSelectionCounters()
        {
            int total = _leftovers.Count;
            int checkedCount = _leftovers.Count(i => i.IsSelected);
            long selectedBytes = _leftovers.Where(i => i.IsSelected).Sum(i => i.SizeBytes);

            _txtSummaryCount.Text = string.Format(
                "Résidus détectés : {0} élément(s)   |   Sélectionnés : {1} ({2})",
                total, checkedCount, FormatUtils.FormatBytes(selectedBytes));

            _btnCleanSelected.Content = string.Format("Supprimer définitivement les résidus cochés ({0})", checkedCount);
            _btnCleanSelected.IsEnabled = checkedCount > 0;
        }

        private void OnOpenSelectedLeftoverLocation(object sender, RoutedEventArgs e)
        {
            var item = _gridLeftovers.SelectedItem as LeftoverItem;
            if (item == null) return;

            try
            {
                if (item.Type == LeftoverType.Directory && Directory.Exists(item.Path))
                {
                    Process.Start("explorer.exe", "\"" + item.Path + "\"");
                }
                else if ((item.Type == LeftoverType.File || item.Type == LeftoverType.Shortcut) && File.Exists(item.Path))
                {
                    Process.Start("explorer.exe", "/select,\"" + item.Path + "\"");
                }
                else if (item.Type == LeftoverType.RegistryKey || item.Type == LeftoverType.RegistryValue)
                {
                    Clipboard.SetText(item.Path);
                    MessageBox.Show(this, "Chemin de la clé copié dans le presse-papiers :\n" + item.Path, "Clé de Registre", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch { }
        }

        private void SetBusy(bool busy)
        {
            _btnRunOfficialAndScan.IsEnabled = !busy;
            _btnScanOnly.IsEnabled = !busy;
            _btnCleanSelected.IsEnabled = !busy && _leftovers.Any(i => i.IsSelected);
            _progressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            _progressBar.IsIndeterminate = busy;
        }

        private void AppendLog(string line)
        {
            _txtLogs.AppendText(string.Format("[{0:HH:mm:ss}] {1}\r\n", DateTime.Now, line));
            _txtLogs.ScrollToEnd();
        }

        private DataGrid CreateLeftoversDataGrid()
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                AlternationCount = 2,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = BrushFromHex("#1E293B"),
                Background = BrushFromHex("#0F172A"),
                RowBackground = BrushFromHex("#162032"),
                AlternatingRowBackground = BrushFromHex("#1A263B"),
                Foreground = Brushes.White,
                BorderBrush = BrushFromHex("#26354D"),
                BorderThickness = new Thickness(1),
                RowHeaderWidth = 0,
                FontSize = 12.5
            };

            grid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Choix",
                Binding = new Binding("IsSelected") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(55)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Type",
                Binding = new Binding("TypeLabel"),
                IsReadOnly = true,
                Width = new DataGridLength(110)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Catégorie",
                Binding = new Binding("Category"),
                IsReadOnly = true,
                Width = new DataGridLength(165),
                ElementStyle = UiTheme.CreateCellTextTooltipStyle("Category")
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Chemin / Clé de Registre",
                Binding = new Binding("DisplayPath"),
                IsReadOnly = true,
                Width = new DataGridLength(2.2, DataGridLengthUnitType.Star),
                MinWidth = 260,
                ElementStyle = UiTheme.CreateCellTextTooltipStyle("FullTooltip")
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Taille",
                Binding = new Binding("SizeFormatted"),
                IsReadOnly = true,
                Width = new DataGridLength(85)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Fiabilité",
                Binding = new Binding("Confidence"),
                IsReadOnly = true,
                Width = new DataGridLength(125)
            });

            return grid;
        }

        private static RadioButton CreateRadioButton(string text, bool isChecked)
        {
            return new RadioButton
            {
                Content = text,
                IsChecked = isChecked,
                Foreground = BrushFromHex("#CBD5E1"),
                Margin = new Thickness(0, 3, 0, 3),
                FontSize = 12
            };
        }

        private static CheckBox CreateCheckBox(string text, bool isChecked)
        {
            return new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Foreground = BrushFromHex("#CBD5E1"),
                Margin = new Thickness(0, 3, 0, 3),
                FontSize = 12
            };
        }

        private static Button CreateButton(string text, string bgHex, string fgHex)
        {
            return new Button
            {
                Content = text,
                Background = BrushFromHex(bgHex),
                Foreground = BrushFromHex(fgHex),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5,
                Padding = new Thickness(14, 8, 14, 8),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
        }

        private static Button CreateSmallButton(string text, RoutedEventHandler onClick)
        {
            var btn = new Button
            {
                Content = text,
                Background = BrushFromHex("#334155"),
                Foreground = BrushFromHex("#E2E8F0"),
                FontSize = 11.5,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(6, 0, 0, 0),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            btn.Click += onClick;
            return btn;
        }

        private static SolidColorBrush BrushFromHex(string hex)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
        }
    }
}
