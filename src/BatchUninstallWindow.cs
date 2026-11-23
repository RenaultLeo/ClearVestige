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
using System.Windows.Media;

namespace ClearVestige
{
    /// <summary>
    /// Désinstallation multiple : exécute les désinstallateurs officiels de plusieurs applications
    /// l'un après l'autre (Windows n'autorise qu'une installation/désinstallation à la fois),
    /// puis regroupe tous les résidus détectés dans une seule liste à valider.
    /// </summary>
    public class BatchUninstallWindow : Window
    {
        private readonly List<InstalledApp> _allApps;
        private readonly ObservableCollection<BatchAppEntry> _queue = new ObservableCollection<BatchAppEntry>();
        private readonly ObservableCollection<LeftoverItem> _leftovers = new ObservableCollection<LeftoverItem>();
        private readonly bool _preferSilentInitially;

        private RadioButton _rbSafe;
        private RadioButton _rbModerate;
        private RadioButton _rbAdvanced;
        private CheckBox _cbSilent;
        private CheckBox _cbKillProcesses;
        private CheckBox _cbBackupRegistry;
        private CheckBox _cbRecycleBin;
        private CheckBox _cbRestorePoint;

        private Button _btnStart;
        private Button _btnCleanSelected;
        private Button _btnClose;
        private TextBlock _txtStepStatus;
        private TextBlock _txtSummaryCount;
        private ProgressBar _progressBar;
        private DataGrid _gridQueue;
        private DataGrid _gridLeftovers;
        private TextBox _txtLogs;
        private Expander _optionsExpander;

        private bool _isBusy;
        private bool _hasRun;

        public bool WasCleanedOrUninstalled { get; private set; }

        public BatchUninstallWindow(IEnumerable<InstalledApp> appsToRemove, List<InstalledApp> allApps, bool preferSilent)
        {
            _allApps = allApps ?? new List<InstalledApp>();
            _preferSilentInitially = preferSilent;
            foreach (var app in appsToRemove)
            {
                if (app != null) _queue.Add(new BatchAppEntry { App = app });
            }

            InitializeComponent();
            Closing += (s, e) =>
            {
                if (_isBusy)
                {
                    e.Cancel = true;
                    MessageBox.Show(this, "Une désinstallation est en cours. Veuillez attendre la fin de l'opération.", "Opération en cours", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            };
        }

        private void InitializeComponent()
        {
            UiTheme.ApplyDarkTheme(this);
            UiTheme.ApplyAppIcon(this);

            Title = string.Format("Désinstallation multiple — {0} application(s)", _queue.Count);
            Width = 1100;
            Height = 920;
            MinWidth = 900;
            MinHeight = 760;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = BrushFromHex("#0F172A");
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");

            var outerGrid = new Grid();
            outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBar = UiTheme.CreateCompactTitleBar(this, "CLEARVESTIGE", string.Format("Désinstallation multiple ({0} applications)", _queue.Count), true);
            Grid.SetRow(titleBar, 0);
            outerGrid.Children.Add(titleBar);

            var root = new Grid { Margin = new Thickness(20, 14, 20, 18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 0 En-tête
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 1 Options
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 2 Barre d'action
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 170 }); // 3 File d'attente
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.3, GridUnitType.Star), MinHeight = 170 }); // 4 Résidus
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(90) });                    // 5 Journal
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 6 Pied de page

            // ---------- 0. En-tête ----------
            var header = CreateCard(new Thickness(0, 0, 0, 12));
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = string.Format("Désinstallation multiple — {0} application(s) sélectionnée(s)", _queue.Count),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Les désinstallateurs officiels sont lancés l'un après l'autre (Windows n'en autorise qu'un à la fois). " +
                       "Les résidus de toutes les applications sont ensuite regroupés ci-dessous pour validation.",
                FontSize = 12,
                Foreground = BrushFromHex("#94A3B8"),
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ---------- 1. Options ----------
            var optionsGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

            var depthCard = CreateCard(new Thickness(0));
            var depthStack = new StackPanel();
            depthStack.Children.Add(CreateCardTitle("Niveau d'analyse des résidus"));
            _rbSafe = CreateRadioButton("Niveau 1 (Rapide) — Dossier d'installation & clé Ajout/Suppression", false);
            _rbModerate = CreateRadioButton("Niveau 2 (Standard) — + AppData, ProgramData, Raccourcis, Clés Software", false);
            _rbAdvanced = CreateRadioButton("Niveau 3 (Profond) — + Temp, CrashDumps, Prefetch, Démarrage, MuiCache", true);
            depthStack.Children.Add(_rbSafe);
            depthStack.Children.Add(_rbModerate);
            depthStack.Children.Add(_rbAdvanced);
            depthCard.Child = depthStack;
            Grid.SetColumn(depthCard, 0);
            optionsGrid.Children.Add(depthCard);

            var safetyCard = CreateCard(new Thickness(0));
            var safetyStack = new StackPanel();
            safetyStack.Children.Add(CreateCardTitle("Options & Sécurité"));
            _cbSilent = CreateCheckBox("Désinstallation silencieuse quand l'application le permet", _preferSilentInitially);
            _cbKillProcesses = CreateCheckBox("Fermer les processus actifs des applications sélectionnées", true);
            _cbBackupRegistry = CreateCheckBox("Sauvegarder les clés de registre supprimées (.reg dans Backups/)", true);
            _cbRecycleBin = CreateCheckBox("Envoyer les fichiers et dossiers supprimés à la Corbeille", true);
            _cbRestorePoint = CreateCheckBox("Créer un Point de Restauration Système avant de commencer (Admin)", false);
            safetyStack.Children.Add(_cbSilent);
            safetyStack.Children.Add(_cbKillProcesses);
            safetyStack.Children.Add(_cbBackupRegistry);
            safetyStack.Children.Add(_cbRecycleBin);
            safetyStack.Children.Add(_cbRestorePoint);
            safetyCard.Child = safetyStack;
            Grid.SetColumn(safetyCard, 2);
            optionsGrid.Children.Add(safetyCard);

            // Les options sont repliables : elles se referment au lancement pour laisser la place aux listes
            _optionsExpander = new Expander
            {
                Header = "Options d'analyse et de sécurité",
                IsExpanded = true,
                Foreground = BrushFromHex("#E2E8F0"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12),
                Content = optionsGrid
            };
            optionsGrid.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(_optionsExpander, 1);
            root.Children.Add(_optionsExpander);

            // ---------- 2. Barre d'action ----------
            var stepCard = CreateCard(new Thickness(0, 0, 0, 12));
            var stepGrid = new Grid();
            stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _btnStart = CreateButton(string.Format("Lancer la désinstallation des {0} applications", _queue.Count), "#2563EB", "#FFFFFF");
            _btnStart.Click += async (s, e) => await RunBatchAsync();
            Grid.SetColumn(_btnStart, 0);
            stepGrid.Children.Add(_btnStart);

            var statusStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            _txtStepStatus = new TextBlock
            {
                Text = "Prêt. Vérifiez la liste puis lancez la désinstallation.",
                Foreground = BrushFromHex("#38BDF8"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _progressBar = new ProgressBar
            {
                Height = 6,
                Margin = new Thickness(0, 6, 0, 0),
                Background = BrushFromHex("#0F172A"),
                Foreground = BrushFromHex("#38BDF8"),
                BorderThickness = new Thickness(0),
                Minimum = 0,
                Maximum = Math.Max(1, _queue.Count),
                Visibility = Visibility.Collapsed
            };
            statusStack.Children.Add(_txtStepStatus);
            statusStack.Children.Add(_progressBar);
            Grid.SetColumn(statusStack, 1);
            stepGrid.Children.Add(statusStack);
            stepCard.Child = stepGrid;
            Grid.SetRow(stepCard, 2);
            root.Children.Add(stepCard);

            // ---------- 3. File d'attente ----------
            var queueContainer = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            queueContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            queueContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var queueTitle = CreateCardTitle("Applications à désinstaller");
            Grid.SetRow(queueTitle, 0);
            queueContainer.Children.Add(queueTitle);

            _gridQueue = CreateDataGrid();
            _gridQueue.ItemsSource = _queue;
            _gridQueue.Columns.Add(new DataGridTextColumn { Header = "Application", Binding = new Binding("DisplayName"), IsReadOnly = true, Width = new DataGridLength(1.6, DataGridLengthUnitType.Star) });
            _gridQueue.Columns.Add(new DataGridTextColumn { Header = "Éditeur", Binding = new Binding("Publisher"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _gridQueue.Columns.Add(new DataGridTextColumn { Header = "Taille", Binding = new Binding("SizeFormatted"), IsReadOnly = true, Width = new DataGridLength(85) });
            _gridQueue.Columns.Add(new DataGridTextColumn { Header = "Mode", Binding = new Binding("ModeLabel"), IsReadOnly = true, Width = new DataGridLength(130) });
            _gridQueue.Columns.Add(new DataGridTextColumn { Header = "État", Binding = new Binding("Status"), IsReadOnly = true, Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
            _gridQueue.Columns.Add(new DataGridTextColumn { Header = "Résidus", Binding = new Binding("LeftoversLabel"), IsReadOnly = true, Width = new DataGridLength(90) });
            Grid.SetRow(_gridQueue, 1);
            queueContainer.Children.Add(_gridQueue);
            Grid.SetRow(queueContainer, 3);
            root.Children.Add(queueContainer);

            // ---------- 4. Résidus regroupés ----------
            var leftoversContainer = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            leftoversContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftoversContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            _txtSummaryCount = new TextBlock
            {
                Text = "Résidus détectés : 0 élément",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = BrushFromHex("#E2E8F0"),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_txtSummaryCount, Dock.Left);
            toolbar.Children.Add(_txtSummaryCount);

            var selButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            selButtons.Children.Add(CreateSmallButton("Tout cocher", (s, e) => SetSelectionAll(true)));
            selButtons.Children.Add(CreateSmallButton("Cocher uniquement Sûrs", (s, e) => SetSelectionSafeOnly()));
            selButtons.Children.Add(CreateSmallButton("Tout décocher", (s, e) => SetSelectionAll(false)));
            selButtons.Children.Add(CreateSmallButton("Ouvrir l'emplacement", OnOpenSelectedLeftoverLocation));
            DockPanel.SetDock(selButtons, Dock.Right);
            toolbar.Children.Add(selButtons);
            Grid.SetRow(toolbar, 0);
            leftoversContainer.Children.Add(toolbar);

            _gridLeftovers = CreateDataGrid();
            _gridLeftovers.ItemsSource = _leftovers;
            _gridLeftovers.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Choix",
                Binding = new Binding("IsSelected") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(55)
            });
            _gridLeftovers.Columns.Add(new DataGridTextColumn { Header = "Application", Binding = new Binding("OwnerAppName"), IsReadOnly = true, Width = new DataGridLength(150) });
            _gridLeftovers.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding("TypeLabel"), IsReadOnly = true, Width = new DataGridLength(100) });
            _gridLeftovers.Columns.Add(new DataGridTextColumn { Header = "Catégorie", Binding = new Binding("Category"), IsReadOnly = true, Width = new DataGridLength(160) });
            _gridLeftovers.Columns.Add(new DataGridTextColumn { Header = "Chemin / Clé de Registre", Binding = new Binding("Path"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _gridLeftovers.Columns.Add(new DataGridTextColumn { Header = "Taille", Binding = new Binding("SizeFormatted"), IsReadOnly = true, Width = new DataGridLength(80) });
            _gridLeftovers.Columns.Add(new DataGridTextColumn { Header = "Fiabilité", Binding = new Binding("Confidence"), IsReadOnly = true, Width = new DataGridLength(120) });
            Grid.SetRow(_gridLeftovers, 1);
            leftoversContainer.Children.Add(_gridLeftovers);
            Grid.SetRow(leftoversContainer, 4);
            root.Children.Add(leftoversContainer);

            // Séparateur déplaçable entre la file d'attente et la liste des résidus
            var splitter = new GridSplitter
            {
                Height = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeNS,
                ToolTip = "Glisser pour redimensionner les deux listes"
            };
            Grid.SetRow(splitter, 3);
            root.Children.Add(splitter);

            // ---------- 5. Journal ----------
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
                Text = string.Format("[ClearVestige] Désinstallation multiple prête : {0} application(s)\r\n", _queue.Count)
            };
            logBorder.Child = _txtLogs;
            Grid.SetRow(logBorder, 5);
            root.Children.Add(logBorder);

            // ---------- 6. Pied de page ----------
            var footer = new DockPanel { LastChildFill = false };
            _btnClose = CreateButton("Fermer", "#334155", "#E2E8F0");
            _btnClose.Click += (s, e) => Close();
            DockPanel.SetDock(_btnClose, Dock.Right);
            footer.Children.Add(_btnClose);

            _btnCleanSelected = CreateButton("Supprimer les résidus cochés (0)", "#DC2626", "#FFFFFF");
            _btnCleanSelected.IsEnabled = false;
            _btnCleanSelected.Margin = new Thickness(0, 0, 12, 0);
            _btnCleanSelected.Click += async (s, e) => await CleanSelectedLeftoversAsync();
            DockPanel.SetDock(_btnCleanSelected, Dock.Left);
            footer.Children.Add(_btnCleanSelected);
            Grid.SetRow(footer, 6);
            root.Children.Add(footer);

            Grid.SetRow(root, 1);
            outerGrid.Children.Add(root);

            Content = UiTheme.ConfigureCustomChrome(this, 46, outerGrid, true);
        }

        // =====================================================================
        // DÉROULEMENT DE LA DÉSINSTALLATION MULTIPLE
        // =====================================================================
        private async Task RunBatchAsync()
        {
            if (_queue.Count == 0) return;

            bool silent = _cbSilent.IsChecked == true;
            var names = string.Join("\n", _queue.Take(15).Select(q => "• " + q.DisplayName).ToArray());
            if (_queue.Count > 15) names += string.Format("\n… et {0} autre(s)", _queue.Count - 15);

            var confirm = MessageBox.Show(
                this,
                string.Format("Désinstaller les {0} applications suivantes ?\n\n{1}\n\n{2}",
                    _queue.Count, names,
                    silent
                        ? "Mode silencieux : les applications qui le permettent seront désinstallées sans fenêtre. Les autres afficheront leur propre assistant."
                        : "Chaque désinstallateur officiel s'ouvrira à tour de rôle : suivez ses instructions."),
                "Confirmer la désinstallation multiple",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            SetBusy(true);
            _hasRun = true;
            _optionsExpander.IsExpanded = false;
            try
            {
                bool killProcs = _cbKillProcesses.IsChecked == true;
                ScanDepth depth = GetSelectedScanDepth();
                _progressBar.Value = 0;

                // Étape 1 : point de restauration unique pour tout le lot
                if (_cbRestorePoint.IsChecked == true)
                {
                    _txtStepStatus.Text = "Création d'un point de restauration système Windows...";
                    string desc = string.Format("Avant désinstallation multiple ({0} applications)", _queue.Count);
                    string restoreMsg = await Task.Run(() =>
                    {
                        string msg;
                        UninstallerEngine.CreateSystemRestorePoint(desc, out msg);
                        return msg;
                    });
                    AppendLog(restoreMsg);
                }

                // Étape 2 : désinstallateurs officiels, un par un
                int index = 0;
                foreach (var entry in _queue)
                {
                    index++;
                    var app = entry.App;
                    _gridQueue.SelectedItem = entry;
                    _gridQueue.ScrollIntoView(entry);
                    _txtStepStatus.Text = string.Format("[{0}/{1}] Désinstallation de {2}...", index, _queue.Count, app.DisplayName);
                    entry.Status = "Désinstallation en cours...";
                    AppendLog(string.Format("=== [{0}/{1}] {2} ===", index, _queue.Count, app.DisplayName));

                    if (killProcs && !string.IsNullOrWhiteSpace(app.InstallLocation))
                    {
                        var procLogs = new List<string>();
                        string loc = app.InstallLocation;
                        await Task.Run(() => UninstallerEngine.KillRunningAppProcesses(loc, procLogs));
                        foreach (var l in procLogs) AppendLog(l);
                    }

                    bool hasCommand = !string.IsNullOrWhiteSpace(app.UninstallString) || !string.IsNullOrWhiteSpace(app.QuietUninstallString);
                    if (!hasCommand)
                    {
                        entry.Status = "Aucun désinstallateur officiel (résidus seulement)";
                        AppendLog("Aucune commande de désinstallation : seuls les résidus seront analysés.");
                    }
                    else
                    {
                        string uninstMsg = "";
                        bool useSilent = silent && app.HasQuietUninstall;
                        bool ok = await Task.Run(() => UninstallerEngine.RunOfficialUninstaller(app, useSilent, out uninstMsg));
                        AppendLog(uninstMsg);
                        entry.UninstallSucceeded = ok;
                        entry.Status = ok ? "Désinstallateur terminé" : "Échec / annulé";
                        if (ok) WasCleanedOrUninstalled = true;
                    }

                    _progressBar.Value = index;
                }

                // Étape 3 : relire la liste des applications réellement encore installées
                _txtStepStatus.Text = "Vérification des applications encore présentes...";
                var stillInstalled = await Task.Run(() => AppScanner.ScanInstalledApps(true, true));
                var stillIds = new HashSet<string>(stillInstalled.Select(a => a.Id ?? ""), StringComparer.OrdinalIgnoreCase);

                // Étape 4 : analyse des résidus de chaque application (uniquement si elle a bien disparu)
                _leftovers.Clear();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                index = 0;
                foreach (var entry in _queue)
                {
                    index++;
                    var app = entry.App;
                    bool hadCommand = !string.IsNullOrWhiteSpace(app.UninstallString) || !string.IsNullOrWhiteSpace(app.QuietUninstallString);
                    entry.StillInstalled = hadCommand && !string.IsNullOrWhiteSpace(app.Id) && stillIds.Contains(app.Id);

                    if (entry.StillInstalled)
                    {
                        entry.Status = "Toujours installée — résidus non analysés";
                        entry.LeftoversLabel = "—";
                        AppendLog(string.Format("{0} est toujours installée (désinstallation annulée ?) : ses fichiers sont conservés par sécurité.", app.DisplayName));
                        continue;
                    }

                    _txtStepStatus.Text = string.Format("[{0}/{1}] Analyse des résidus de {2}...", index, _queue.Count, app.DisplayName);
                    entry.Status = "Analyse des résidus...";
                    var found = await Task.Run(() => LeftoverScanner.ScanForApp(app, depth, stillInstalled));

                    int added = 0;
                    foreach (var item in found)
                    {
                        string key = string.Format("{0}|{1}|{2}", item.Type, item.Path, item.ValueName ?? "");
                        if (!seen.Add(key)) continue;
                        item.OwnerAppName = app.DisplayName;
                        item.PropertyChanged += (s, ev) =>
                        {
                            if (ev.PropertyName == "IsSelected") UpdateSelectionCounters();
                        };
                        _leftovers.Add(item);
                        added++;
                    }
                    entry.LeftoversLabel = added.ToString();
                    entry.Status = added == 0 ? "Terminé — aucun résidu" : "Terminé — résidus à valider";
                    AppendLog(string.Format("{0} : {1} résidu(s) détecté(s).", app.DisplayName, added));
                }

                UpdateSelectionCounters();
                int stillCount = _queue.Count(q => q.StillInstalled);
                string summary = string.Format("Terminé : {0} application(s) traitée(s), {1} résidu(s) détecté(s){2}. Vérifiez la sélection puis supprimez.",
                    _queue.Count - stillCount, _leftovers.Count,
                    stillCount > 0 ? string.Format(", {0} toujours installée(s)", stillCount) : "");
                _txtStepStatus.Text = summary;
                AppendLog("=== " + summary + " ===");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task CleanSelectedLeftoversAsync()
        {
            var selected = _leftovers.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) return;

            bool useRecycleBin = _cbRecycleBin.IsChecked == true;
            bool backupReg = _cbBackupRegistry.IsChecked == true;
            var groups = selected.GroupBy(i => i.OwnerAppName ?? "Application").ToList();

            var confirm = MessageBox.Show(
                this,
                string.Format("Supprimer les {0} résidus cochés ({1} application(s)) ?\n\n• Fichiers/Dossiers : {2}\n• Registre : {3}",
                    selected.Count, groups.Count,
                    useRecycleBin ? "Envoyés à la Corbeille Windows" : "Suppression définitive",
                    backupReg ? "Sauvegardé dans Backups/ avant suppression" : "Suppression directe"),
                "Confirmation du nettoyage des résidus",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            SetBusy(true);
            try
            {
                int dirs = 0, files = 0, regs = 0, reboot = 0, failed = 0;
                long freed = 0;
                var backupFolders = new List<string>();

                foreach (var group in groups)
                {
                    string appName = group.Key;
                    var items = group.ToList();
                    _txtStepStatus.Text = "Nettoyage des résidus de " + appName + "...";

                    if (_cbKillProcesses.IsChecked == true)
                    {
                        var entry = _queue.FirstOrDefault(q => q.DisplayName == appName);
                        if (entry != null && !string.IsNullOrWhiteSpace(entry.App.InstallLocation))
                        {
                            string loc = entry.App.InstallLocation;
                            await Task.Run(() => UninstallerEngine.KillRunningAppProcesses(loc));
                        }
                    }

                    var report = await Task.Run(() => UninstallerEngine.CleanSelectedLeftovers(appName, items, useRecycleBin, backupReg));
                    foreach (var log in report.Logs) AppendLog(log);
                    if (!string.IsNullOrWhiteSpace(report.BackupFolderPath)) backupFolders.Add(report.BackupFolderPath);

                    dirs += report.DeletedDirectories;
                    files += report.DeletedFiles;
                    regs += report.DeletedRegistryItems;
                    reboot += report.ScheduledForRebootItems;
                    failed += report.FailedItems;
                    freed += report.FreedBytes;

                    foreach (var item in items) _leftovers.Remove(item);
                    var queueEntry = _queue.FirstOrDefault(x => x.DisplayName == appName);
                    if (queueEntry != null)
                    {
                        int left = _leftovers.Count(l => l.OwnerAppName == appName);
                        queueEntry.LeftoversLabel = left.ToString();
                        queueEntry.Status = left == 0 ? "Nettoyée" : "Nettoyée (résidus non cochés restants)";
                    }
                }

                WasCleanedOrUninstalled = true;
                UpdateSelectionCounters();

                string summary = string.Format("Nettoyage terminé ! {0} dossier(s), {1} fichier(s), {2} entrée(s) de registre supprimés ({3} libérés){4}{5}.",
                    dirs, files, regs, FormatUtils.FormatBytes(freed),
                    reboot > 0 ? string.Format(", {0} planifié(s) au redémarrage", reboot) : "",
                    failed > 0 ? string.Format(", {0} élément(s) ignoré(s)", failed) : "");
                _txtStepStatus.Text = summary;
                AppendLog("=== " + summary + " ===");

                MessageBox.Show(
                    this,
                    summary + (backupFolders.Count > 0 ? "\n\nSauvegardes du registre créées dans :\n" + UninstallerEngine.BackupRootDirectory : ""),
                    "Désinstallation multiple terminée",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            finally
            {
                SetBusy(false);
            }
        }

        // =====================================================================
        // UTILITAIRES UI
        // =====================================================================
        private ScanDepth GetSelectedScanDepth()
        {
            if (_rbSafe.IsChecked == true) return ScanDepth.Safe;
            if (_rbModerate.IsChecked == true) return ScanDepth.Moderate;
            return ScanDepth.Advanced;
        }

        private void SetSelectionAll(bool select)
        {
            foreach (var item in _leftovers) item.IsSelected = select;
            UpdateSelectionCounters();
        }

        private void SetSelectionSafeOnly()
        {
            foreach (var item in _leftovers) item.IsSelected = item.IsHighConfidence;
            UpdateSelectionCounters();
        }

        private void UpdateSelectionCounters()
        {
            int total = _leftovers.Count;
            int checkedCount = _leftovers.Count(i => i.IsSelected);
            long selectedBytes = _leftovers.Where(i => i.IsSelected).Sum(i => i.SizeBytes);
            _txtSummaryCount.Text = string.Format("Résidus détectés : {0} élément(s)   |   Sélectionnés : {1} ({2})",
                total, checkedCount, FormatUtils.FormatBytes(selectedBytes));
            _btnCleanSelected.Content = string.Format("Supprimer les résidus cochés ({0})", checkedCount);
            _btnCleanSelected.IsEnabled = !_isBusy && checkedCount > 0;
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            _btnStart.IsEnabled = !busy && !_hasRun;
            _btnClose.IsEnabled = !busy;
            _btnCleanSelected.IsEnabled = !busy && _leftovers.Any(i => i.IsSelected);
            _rbSafe.IsEnabled = _rbModerate.IsEnabled = _rbAdvanced.IsEnabled = !busy && !_hasRun;
            _cbSilent.IsEnabled = _cbRestorePoint.IsEnabled = !busy && !_hasRun;
            _progressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AppendLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            _txtLogs.AppendText(string.Format("[{0:HH:mm:ss}] {1}\r\n", DateTime.Now, line));
            _txtLogs.ScrollToEnd();
        }

        private void OnOpenSelectedLeftoverLocation(object sender, RoutedEventArgs e)
        {
            var item = _gridLeftovers.SelectedItem as LeftoverItem;
            if (item == null) return;
            try
            {
                if (item.Type == LeftoverType.Directory && Directory.Exists(item.Path))
                    Process.Start("explorer.exe", "\"" + item.Path + "\"");
                else if ((item.Type == LeftoverType.File || item.Type == LeftoverType.Shortcut) && File.Exists(item.Path))
                    Process.Start("explorer.exe", "/select,\"" + item.Path + "\"");
                else if (item.Type == LeftoverType.RegistryKey || item.Type == LeftoverType.RegistryValue)
                {
                    Clipboard.SetText(item.Path);
                    MessageBox.Show(this, "Chemin de la clé copié dans le presse-papiers :\n" + item.Path, "Clé de Registre", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch { }
        }

        private static Border CreateCard(Thickness margin)
        {
            return new Border
            {
                Background = BrushFromHex("#1E293B"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = margin
            };
        }

        private static TextBlock CreateCardTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = BrushFromHex("#E2E8F0"),
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private static DataGrid CreateDataGrid()
        {
            return new DataGrid
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
        }

        private static RadioButton CreateRadioButton(string text, bool isChecked)
        {
            return new RadioButton { Content = text, IsChecked = isChecked, Foreground = BrushFromHex("#CBD5E1"), Margin = new Thickness(0, 3, 0, 3), FontSize = 12 };
        }

        private static CheckBox CreateCheckBox(string text, bool isChecked)
        {
            return new CheckBox { Content = text, IsChecked = isChecked, Foreground = BrushFromHex("#CBD5E1"), Margin = new Thickness(0, 3, 0, 3), FontSize = 12 };
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
            return UiTheme.BrushFromHex(hex);
        }
    }
}
