using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace ClearVestige
{
    public partial class MainWindow : Window
    {
        private List<InstalledApp> _allApps = new List<InstalledApp>();
        private readonly ObservableCollection<InstalledApp> _filteredApps = new ObservableCollection<InstalledApp>();
        private readonly ObservableCollection<LeftoverItem> _forcedLeftovers = new ObservableCollection<LeftoverItem>();
        private readonly ObservableCollection<StartupItem> _startupItems = new ObservableCollection<StartupItem>();
        private readonly ObservableCollection<JunkCategory> _junkCategories = new ObservableCollection<JunkCategory>();

        private string _activeFilterTag = "ALL";
        private TabControl _mainTabControl;
        private Border _chromeFrameBorder;
        private bool _isRebuildingUi;

        // Contrôles Tableau de bord
        private TextBlock _lblHeaderSubtitle;
        private TextBlock _lblTotalApps;
        private TextBlock _lblTotalSize;
        private TextBlock _lblStartupCount;
        private TextBlock _lblStatusFooter;

        // Contrôles Onglet 1 : Applications
        private TextBox _txtSearch;
        private CheckBox _cbIncludeStore;
        private CheckBox _cbIncludeSystem;
        private DataGrid _gridApps;
        private TextBlock _txtSelectedAppTitle;
        private TextBlock _txtSelectedAppDetails;
        private Button _btnUninstallComplete;
        private Button _btnUninstallSilent;
        private Button _btnScanLeftoversOnly;
        private Button _btnOpenFolder;
        private Button _btnCheckedFilter;
        private Button _btnClearChecked;
        private readonly List<Button> _filterButtons = new List<Button>();

        // Contrôles Onglet 2 : Recherche d'Application Orpheline / Désinstallation Forcée
        private TextBox _txtForcedName;
        private TextBox _txtForcedPath;
        private DataGrid _gridForcedLeftovers;
        private TextBlock _txtForcedSummary;
        private Button _btnCleanForced;
        private readonly ObservableCollection<OrphanCandidate> _autoOrphans = new ObservableCollection<OrphanCandidate>();
        private DataGrid _gridAutoOrphans;
        private TextBlock _txtAutoOrphansSummary;
        private Button _btnCleanAutoOrphans;
        private Button _btnScanAutoOrphans;

        // Contrôles Onglet 3 : Démarrage
        private DataGrid _gridStartup;

        // Contrôles Onglet 4 : Nettoyeur
        private DataGrid _gridJunk;
        private TextBlock _txtJunkSummary;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += async (s, e) => await RefreshAllDataAsync();
        }

        private void InitializeComponent()
        {
            UiTheme.ApplyDarkTheme(this);

            Title = Loc.T("ClearVestige v2.0 — Désinstallateur Complet & Nettoyeur de Résidus",
                          "ClearVestige v2.0 — Deep Uninstaller & Leftover Cleaner");
            UiTheme.ApplyAppIcon(this);
            UiTheme.ClampWindowToWorkArea(this, 1280, 810, 940, 580);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BrushFromHex("#0F172A");
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");

            // Support du Glisser-Déposer (.lnk, .exe ou dossier directement sur la fenêtre)
            AllowDrop = true;
            PreviewDragOver += OnWindowDragOver;
            Drop += OnWindowDrop;

            _chromeFrameBorder = UiTheme.ConfigureCustomChrome(this, 64, BuildMainOuterGrid(), true);
            Content = _chromeFrameBorder;
        }

        private Grid BuildMainOuterGrid()
        {
            _filterButtons.Clear();

            // Grille externe : Ligne 0 = Barre de titre sombre intégrée (style SysPulse), Ligne 1 = Contenu principal
            var outerGrid = new Grid();
            outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // =========================================================================
            // 0. BARRE DE TITRE SOMBRE INTÉGRÉE (STYLE SYSPULSE MONITOR)
            // =========================================================================
            var titleBarBorder = new Border
            {
                Background = BrushFromHex("#0B1120"),
                BorderBrush = BrushFromHex("#1E293B"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(18, 0, 12, 0),
                Height = 64
            };

            var headerDock = new DockPanel { LastChildFill = true };

            // Partie droite : Badge Admin pill + Actualiser + Sauvegardes .reg + Séparateur + Contrôles fenêtre
            var rightHeaderStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            bool isAdmin = UninstallerEngine.IsRunningAsAdmin();
            if (isAdmin)
            {
                var adminPill = new Border
                {
                    Background = BrushFromHex("#064E3B"),
                    BorderBrush = BrushFromHex("#10B981"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(15),
                    Padding = new Thickness(13, 6, 13, 6),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "🛡️ MODE ADMIN",
                        Foreground = BrushFromHex("#6EE7B7"),
                        FontWeight = FontWeights.Bold,
                        FontSize = 11.5
                    }
                };
                WindowChrome.SetIsHitTestVisibleInChrome(adminPill, true);
                rightHeaderStack.Children.Add(adminPill);
            }
            else
            {
                var btnElevate = new Button
                {
                    Content = "⚡ MODE ADMIN",
                    Background = BrushFromHex("#78350F"),
                    Foreground = BrushFromHex("#FDE68A"),
                    BorderBrush = BrushFromHex("#F59E0B"),
                    BorderThickness = new Thickness(1),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    Padding = new Thickness(13, 6, 13, 6),
                    Margin = new Thickness(0, 0, 10, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = "Relancer ClearVestige avec les droits Administrateur (recommandé pour les logiciels système HKLM)"
                };
                WindowChrome.SetIsHitTestVisibleInChrome(btnElevate, true);
                btnElevate.Click += (s, e) =>
                {
                    if (UninstallerEngine.RestartAsAdmin())
                        Application.Current.Shutdown();
                };
                rightHeaderStack.Children.Add(btnElevate);
            }

            var btnLang = new Button
            {
                Content = Loc.IsEnglish ? "🌐 EN" : "🌐 FR",
                Background = BrushFromHex("#131D31"),
                Foreground = BrushFromHex("#CBD5E1"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                ToolTip = Loc.T("Passer l'interface en Anglais (EN)", "Switch UI language to French (FR)")
            };
            WindowChrome.SetIsHitTestVisibleInChrome(btnLang, true);
            btnLang.Click += OnToggleLanguageClick;
            rightHeaderStack.Children.Add(btnLang);

            var btnHeaderRefresh = new Button
            {
                Content = Loc.T("🔄 Actualiser", "🔄 Refresh"),
                Background = BrushFromHex("#131D31"),
                Foreground = BrushFromHex("#E2E8F0"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Padding = new Thickness(11, 7, 11, 7),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                ToolTip = Loc.T("Actualiser l'inventaire des applications, le démarrage et les caches", "Refresh apps inventory, startup and caches")
            };
            WindowChrome.SetIsHitTestVisibleInChrome(btnHeaderRefresh, true);
            btnHeaderRefresh.Click += async (s, e) => await RefreshAllDataAsync();
            rightHeaderStack.Children.Add(btnHeaderRefresh);

            var btnHtmlReport = new Button
            {
                Content = Loc.T("📄 Rapport HTML", "📄 HTML Report"),
                Background = BrushFromHex("#0E7490"),
                Foreground = Brushes.White,
                BorderBrush = BrushFromHex("#22D3EE"),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                ToolTip = Loc.T("Exporter un rapport HTML sombre complet (inventaire + historique des nettoyages)", "Export standalone dark HTML report (inventory + cleanup history)")
            };
            WindowChrome.SetIsHitTestVisibleInChrome(btnHtmlReport, true);
            btnHtmlReport.Click += OnExportHtmlReportClick;
            rightHeaderStack.Children.Add(btnHtmlReport);

            var btnOpenBackups = new Button
            {
                Content = "📂 .reg",
                Background = BrushFromHex("#131D31"),
                Foreground = BrushFromHex("#E2E8F0"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Padding = new Thickness(10, 7, 10, 7),
                Cursor = Cursors.Hand,
                ToolTip = Loc.T("Ouvrir le dossier local des sauvegardes automatiques du Registre (.reg)", "Open local Registry backups folder (.reg)")
            };
            WindowChrome.SetIsHitTestVisibleInChrome(btnOpenBackups, true);
            btnOpenBackups.Click += (s, e) =>
            {
                try { Process.Start("explorer.exe", "\"" + UninstallerEngine.BackupRootDirectory + "\""); } catch { }
            };
            rightHeaderStack.Children.Add(btnOpenBackups);

            rightHeaderStack.Children.Add(UiTheme.CreateWindowCaptionControls(this, true));

            DockPanel.SetDock(rightHeaderStack, Dock.Right);
            headerDock.Children.Add(rightHeaderStack);

            // Partie gauche : Icône arrondie + CLEARVESTIGE + infos PC temps réel
            var leftHeaderStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (UiTheme.AppIcon != null)
            {
                var logo = new Image
                {
                    Source = UiTheme.AppIcon,
                    Width = 38,
                    Height = 38,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
                leftHeaderStack.Children.Add(logo);
            }

            var titleStack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock
            {
                Text = "CLEARVESTIGE",
                FontSize = 16.5,
                FontWeight = FontWeights.ExtraBold,
                Foreground = Brushes.White
            });
            _lblHeaderSubtitle = new TextBlock
            {
                Text = string.Format("{0}  •  v1.5  •  Analyse des applications en cours...", Environment.MachineName),
                FontSize = 11.5,
                Foreground = BrushFromHex("#94A3B8"),
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            titleStack.Children.Add(_lblHeaderSubtitle);
            leftHeaderStack.Children.Add(titleStack);

            headerDock.Children.Add(leftHeaderStack);
            titleBarBorder.Child = headerDock;
            Grid.SetRow(titleBarBorder, 0);
            outerGrid.Children.Add(titleBarBorder);

            // =========================================================================
            // 1. CONTENU PRINCIPAL (CARTES MÉTRIQUES + ONGLETS + BARRE D'ÉTAT)
            // =========================================================================
            var rootGrid = new Grid { Margin = new Thickness(20, 14, 20, 14) };
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Cartes métriques
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Onglets principaux
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Barre d'état inférieure

            // Cartes statistiques
            var topStack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            var cardsGrid = new Grid();
            for (int i = 0; i < 4; i++)
            {
                cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (i < 3) cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            }

            _lblTotalApps = new TextBlock { Text = "...", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = BrushFromHex("#38BDF8") };
            _lblTotalSize = new TextBlock { Text = "...", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = BrushFromHex("#A78BFA") };
            _lblStartupCount = new TextBlock { Text = "...", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = BrushFromHex("#34D399") };
            var lblProtectionMode = new TextBlock { Text = Loc.T("Actif (Auto .reg + Corbeille)", "Active (Auto .reg + Recycle Bin)"), FontSize = 16, FontWeight = FontWeights.Bold, Foreground = BrushFromHex("#FBBF24"), Margin = new Thickness(0, 4, 0, 0) };

            AddStatCard(cardsGrid, 0, Loc.T("APPLICATIONS AFFICHÉES", "DISPLAYED APPS"), _lblTotalApps);
            AddStatCard(cardsGrid, 2, Loc.T("ESPACE OCCUPÉ ESTIMÉ", "ESTIMATED DISK USAGE"), _lblTotalSize);
            AddStatCard(cardsGrid, 4, Loc.T("PROGRAMMES AU DÉMARRAGE", "STARTUP PROGRAMS"), _lblStartupCount);
            AddStatCard(cardsGrid, 6, Loc.T("BOUCLIER DE PROTECTION", "PROTECTION SHIELD"), lblProtectionMode);

            topStack.Children.Add(cardsGrid);
            Grid.SetRow(topStack, 0);
            rootGrid.Children.Add(topStack);

            // =========================================================================
            // 2. ONGLETS PRINCIPAUX (5 ONGLETS COMPACTS + AUTO-DÉSINSTALLATION)
            // =========================================================================
            _mainTabControl = new TabControl
            {
                Background = BrushFromHex("#0F172A"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0)
            };

            _mainTabControl.Items.Add(CreateTabItem(Loc.T("📦 Applications", "📦 Apps"), BuildInstalledAppsTab()));
            _mainTabControl.Items.Add(CreateTabItem(Loc.T("🔍 Orphelins", "🔍 Orphans"), BuildForcedUninstallTab()));
            _mainTabControl.Items.Add(CreateTabItem(Loc.T("🚀 Système", "🚀 System"), BuildSystemAndStartupTab()));
            _mainTabControl.Items.Add(CreateTabItem(Loc.T("🧹 Nettoyeur", "🧹 Cleaner"), BuildJunkCleanerTab()));
            _mainTabControl.Items.Add(CreateTabItem(Loc.T("📸 Snapshot & Historique", "📸 Snapshot & History"), BuildSnapshotAndHistoryTab()));

            Grid.SetRow(_mainTabControl, 1);
            rootGrid.Children.Add(_mainTabControl);

            // Bouton d'auto-désinstallation complète placé directement sur la ligne des onglets principaux (à droite)
            var btnSelfUninstall = new Button
            {
                Content = Loc.T("🗑️ Désinstaller", "🗑️ Uninstall App"),
                Background = BrushFromHex("#7F1D1D"),
                Foreground = BrushFromHex("#FECACA"),
                BorderBrush = BrushFromHex("#DC2626"),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5,
                Padding = new Thickness(14, 9, 14, 9),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand,
                ToolTip = Loc.T("Désinstaller complètement ClearVestige et supprimer toutes ses traces du PC",
                                "Completely uninstall ClearVestige and remove all its traces from this PC")
            };
            Panel.SetZIndex(btnSelfUninstall, 10);
            btnSelfUninstall.Click += (s, e) => ShowSelfUninstallDialog();
            Grid.SetRow(btnSelfUninstall, 1);
            rootGrid.Children.Add(btnSelfUninstall);

            // =========================================================================
            // 3. BARRE D'ÉTAT INFÉRIEURE
            // =========================================================================
            var statusBorder = new Border
            {
                Background = BrushFromHex("#1E293B"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 10, 0, 0)
            };
            _lblStatusFooter = new TextBlock
            {
                Text = "Chargement de la liste des applications...",
                Foreground = BrushFromHex("#94A3B8"),
                FontSize = 12
            };
            statusBorder.Child = _lblStatusFooter;
            Grid.SetRow(statusBorder, 2);
            rootGrid.Children.Add(statusBorder);

            Grid.SetRow(rootGrid, 1);
            outerGrid.Children.Add(rootGrid);

            return outerGrid;
        }

        // =========================================================================
        // ONGLET 1 : APPLICATIONS INSTALLÉES
        // =========================================================================
        private UIElement BuildInstalledAppsTab()
        {
            var grid = new Grid { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Recherche & options
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Filtres rapides
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Tableau des applications
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Barre d'action inférieure

            // Ligne 1 : Barre de recherche + Options
            var searchDock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

            var searchBorder = new Border
            {
                Background = BrushFromHex("#1E293B"),
                BorderBrush = BrushFromHex("#475569"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
                Width = 360
            };
            var searchInner = new DockPanel();
            searchInner.Children.Add(new TextBlock
            {
                Text = "🔍 ",
                Foreground = BrushFromHex("#94A3B8"),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13
            });
            _txtSearch = new TextBox
            {
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _txtSearch.TextChanged += (s, e) => { if (!_isRebuildingUi) ApplyAppFilters(); };
            searchInner.Children.Add(_txtSearch);
            searchBorder.Child = searchInner;
            DockPanel.SetDock(searchBorder, Dock.Left);
            searchDock.Children.Add(searchBorder);

            var btnClearSearch = CreateSmallPillButton(Loc.T("Effacer", "Clear"), (s, e) => _txtSearch.Text = "");
            btnClearSearch.Margin = new Thickness(8, 0, 16, 0);
            DockPanel.SetDock(btnClearSearch, Dock.Left);
            searchDock.Children.Add(btnClearSearch);

            _cbIncludeStore = new CheckBox
            {
                Content = Loc.T("Inclure Microsoft Store", "Include Microsoft Store"),
                Foreground = BrushFromHex("#CBD5E1"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };
            _cbIncludeStore.Checked += async (s, e) => { if (!_isRebuildingUi) await RefreshAppsOnlyAsync(); };
            _cbIncludeStore.Unchecked += async (s, e) => { if (!_isRebuildingUi) await RefreshAppsOnlyAsync(); };
            DockPanel.SetDock(_cbIncludeStore, Dock.Left);
            searchDock.Children.Add(_cbIncludeStore);

            _cbIncludeSystem = new CheckBox
            {
                Content = Loc.T("Inclure composants système", "Include System Components"),
                Foreground = BrushFromHex("#CBD5E1"),
                VerticalAlignment = VerticalAlignment.Center
            };
            _cbIncludeSystem.Checked += async (s, e) => { if (!_isRebuildingUi) await RefreshAppsOnlyAsync(); };
            _cbIncludeSystem.Unchecked += async (s, e) => { if (!_isRebuildingUi) await RefreshAppsOnlyAsync(); };
            DockPanel.SetDock(_cbIncludeSystem, Dock.Left);
            searchDock.Children.Add(_cbIncludeSystem);

            var rightButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnExportWinget = CreateActionButton(Loc.T("📦 Export Winget", "📦 Winget Script"), "#334155", "#E2E8F0");
            btnExportWinget.Margin = new Thickness(0, 0, 8, 0);
            btnExportWinget.ToolTip = Loc.T("Exporter un script PowerShell Winget pour réinstaller vos applications en 1 clic",
                                            "Export a Winget PowerShell script to reinstall your apps in 1 click");
            btnExportWinget.Click += OnExportWingetClick;
            var btnExportCsv = CreateActionButton(Loc.T("📄 CSV", "📄 CSV"), "#334155", "#E2E8F0");
            btnExportCsv.Margin = new Thickness(0, 0, 8, 0);
            btnExportCsv.Click += OnExportCsvClick;
            var btnRefresh = CreateActionButton(Loc.T("🔄 Actualiser", "🔄 Refresh"), "#334155", "#E2E8F0");
            btnRefresh.Click += async (s, e) => await RefreshAllDataAsync();
            rightButtons.Children.Add(btnExportWinget);
            rightButtons.Children.Add(btnExportCsv);
            rightButtons.Children.Add(btnRefresh);
            DockPanel.SetDock(rightButtons, Dock.Right);
            searchDock.Children.Add(rightButtons);

            Grid.SetRow(searchDock, 0);
            grid.Children.Add(searchDock);

            // Ligne 2 : Filtres par catégorie + Filtre "Cochées (N)" persistant à travers les recherches
            var filterPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            AddCategoryFilterButton(filterPanel, Loc.T("Toutes", "All"), "ALL", true);
            AddCategoryFilterButton(filterPanel, Loc.T("64-bit", "64-bit"), "SYS64", false);
            AddCategoryFilterButton(filterPanel, Loc.T("32-bit", "32-bit"), "SYS32", false);
            AddCategoryFilterButton(filterPanel, Loc.T("Utilisateur", "User"), "USER", false);
            AddCategoryFilterButton(filterPanel, Loc.T("> 100 Mo", "> 100 MB"), "LARGE", false);
            AddCategoryFilterButton(filterPanel, Loc.T("Récentes (30j)", "Recent (30d)"), "RECENT", false);
            _btnCheckedFilter = AddCategoryFilterButton(filterPanel, Loc.T("✓ Cochées (0)", "✓ Checked (0)"), "CHECKED", false);

            _btnClearChecked = new Button
            {
                Content = Loc.T("Tout décocher", "Uncheck all"),
                Background = BrushFromHex("#334155"),
                Foreground = BrushFromHex("#FCA5A5"),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(6, 0, 0, 4),
                FontSize = 11.5,
                Cursor = Cursors.Hand,
                Visibility = Visibility.Collapsed
            };
            _btnClearChecked.Click += (s, e) =>
            {
                foreach (var a in _allApps) a.IsChecked = false;
                if (_activeFilterTag == "CHECKED")
                {
                    _activeFilterTag = "ALL";
                    foreach (var b in _filterButtons)
                    {
                        bool active = (string)b.Tag == "ALL";
                        b.Background = active ? BrushFromHex("#2563EB") : BrushFromHex("#1E293B");
                        b.Foreground = active ? Brushes.White : BrushFromHex("#CBD5E1");
                        b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
                    }
                    ApplyAppFilters();
                }
                UpdateCheckedAppsUI();
            };
            filterPanel.Children.Add(_btnClearChecked);

            Grid.SetRow(filterPanel, 1);
            grid.Children.Add(filterPanel);

            // Ligne 3 : Grille des applications (avec cases à cocher persistantes + vraies icônes d'applications)
            _gridApps = CreateStyledDataGrid();
            _gridApps.SelectionMode = DataGridSelectionMode.Extended;
            _gridApps.ItemsSource = _filteredApps;
            _gridApps.SelectionChanged += OnSelectedAppChanged;
            _gridApps.MouseDoubleClick += (s, e) =>
            {
                if (!UiTheme.IsClickInsideCheckBox(e.OriginalSource))
                    OpenUninstallWizardForSelected(false, false);
            };

            _gridApps.Columns.Add(UiTheme.CreateSingleClickCheckBoxColumn(Loc.T("Choix", "Pick"), "IsChecked", 52));

            // Colonne Nom + Icône réelle de l'application (20x20)
            var nameWithIconTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse(@"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
    <StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
        <Image Source='{Binding AppIconImage}' Width='18' Height='18' Margin='0,0,8,0' RenderOptions.BitmapScalingMode='HighQuality' VerticalAlignment='Center'/>
        <TextBlock Text='{Binding DisplayName}' TextTrimming='CharacterEllipsis' VerticalAlignment='Center' ToolTip='{Binding DisplayName}'/>
    </StackPanel>
</DataTemplate>");

            _gridApps.Columns.Add(new DataGridTemplateColumn
            {
                Header = Loc.T("Nom de l'application", "Application"),
                CellTemplate = nameWithIconTemplate,
                SortMemberPath = "DisplayName",
                Width = new DataGridLength(2.2, DataGridLengthUnitType.Star),
                MinWidth = 200,
                IsReadOnly = true
            });
            _gridApps.Columns.Add(new DataGridTextColumn
            {
                Header = Loc.T("Éditeur", "Publisher"),
                Binding = new Binding("Publisher"),
                Width = new DataGridLength(1.3, DataGridLengthUnitType.Star),
                IsReadOnly = true,
                ElementStyle = UiTheme.CreateCellTextTooltipStyle("Publisher")
            });
            _gridApps.Columns.Add(new DataGridTextColumn
            {
                Header = "Version",
                Binding = new Binding("DisplayVersion"),
                Width = new DataGridLength(110),
                IsReadOnly = true
            });
            _gridApps.Columns.Add(new DataGridTextColumn
            {
                Header = Loc.T("Taille", "Size"),
                Binding = new Binding("SizeFormatted"),
                SortMemberPath = "SizeBytes",
                Width = new DataGridLength(100),
                IsReadOnly = true
            });
            _gridApps.Columns.Add(new DataGridTextColumn
            {
                Header = Loc.T("Installée le", "Installed"),
                Binding = new Binding("InstallDateFormatted"),
                SortMemberPath = "InstallDateParsed",
                Width = new DataGridLength(105),
                IsReadOnly = true
            });
            _gridApps.Columns.Add(new DataGridTextColumn
            {
                Header = Loc.T("Portée", "Scope"),
                Binding = new Binding("ScopeLabel"),
                Width = new DataGridLength(125),
                IsReadOnly = true
            });
            _gridApps.Columns.Add(new DataGridTextColumn
            {
                Header = Loc.T("Dossier d'installation", "Install Location"),
                Binding = new Binding("InstallLocation"),
                Width = new DataGridLength(1.8, DataGridLengthUnitType.Star),
                MinWidth = 220,
                IsReadOnly = true,
                ElementStyle = UiTheme.CreateCellTextTooltipStyle("InstallLocation")
            });

            // Menu contextuel clic-droit
            var ctxMenu = new ContextMenu();
            AddContextMenuItem(ctxMenu, "🗑️ Désinstallation Complète en Profondeur", (s, e) => OpenUninstallWizardForSelected(false, false));
            AddContextMenuItem(ctxMenu, "⚡ Désinstallation Silencieuse + Scan Résidus", (s, e) => OpenUninstallWizardForSelected(false, true));
            AddContextMenuItem(ctxMenu, "🔍 Scanner uniquement les résidus (Sans désinstallateur)", (s, e) => OpenUninstallWizardForSelected(true, false));
            ctxMenu.Items.Add(new Separator());
            AddContextMenuItem(ctxMenu, "📂 Ouvrir le dossier d'installation", (s, e) => OpenSelectedAppFolder());
            AddContextMenuItem(ctxMenu, "📋 Copier le chemin de la clé Registre", (s, e) => CopySelectedAppRegistryKey());
            _gridApps.ContextMenu = ctxMenu;

            Grid.SetRow(_gridApps, 2);
            grid.Children.Add(_gridApps);

            // Ligne 4 : Barre d'actions inférieure
            var actionBorder = new Border
            {
                Background = BrushFromHex("#1E293B"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 12, 0, 0)
            };

            var actionDock = new DockPanel();
            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _btnUninstallComplete = CreateActionButton("🗑️ Désinstallation en Profondeur", "#DC2626", "#FFFFFF");
            _btnUninstallComplete.Margin = new Thickness(0, 0, 10, 0);
            _btnUninstallComplete.IsEnabled = false;
            _btnUninstallComplete.Click += (s, e) => OpenUninstallWizardForSelected(false, false);

            _btnUninstallSilent = CreateActionButton("⚡ Désinstallation Silencieuse", "#2563EB", "#FFFFFF");
            _btnUninstallSilent.Margin = new Thickness(0, 0, 10, 0);
            _btnUninstallSilent.IsEnabled = false;
            _btnUninstallSilent.Click += (s, e) => OpenUninstallWizardForSelected(false, true);

            _btnScanLeftoversOnly = CreateActionButton(Loc.T("🔍 Scanner Résidus Seuls", "🔍 Scan Leftovers Only"), "#475569", "#FFFFFF");
            _btnScanLeftoversOnly.Margin = new Thickness(0, 0, 10, 0);
            _btnScanLeftoversOnly.IsEnabled = false;
            _btnScanLeftoversOnly.Click += (s, e) => OpenUninstallWizardForSelected(true, false);

            _btnOpenFolder = CreateActionButton(Loc.T("📂 Ouvrir Dossier", "📂 Open Folder"), "#334155", "#E2E8F0");
            _btnOpenFolder.IsEnabled = false;
            _btnOpenFolder.Click += (s, e) => OpenSelectedAppFolder();

            buttonsPanel.Children.Add(_btnUninstallComplete);
            buttonsPanel.Children.Add(_btnUninstallSilent);
            buttonsPanel.Children.Add(_btnScanLeftoversOnly);
            buttonsPanel.Children.Add(_btnOpenFolder);
            DockPanel.SetDock(buttonsPanel, Dock.Right);
            actionDock.Children.Add(buttonsPanel);

            var selInfoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            _txtSelectedAppTitle = new TextBlock
            {
                Text = "Sélectionnez une application dans la liste",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = Brushes.White
            };
            _txtSelectedAppDetails = new TextBlock
            {
                Text = "Double-cliquez sur une application, ou Ctrl / Maj + clic pour en sélectionner plusieurs à désinstaller d'un coup.",
                FontSize = 11.5,
                Foreground = BrushFromHex("#94A3B8"),
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            selInfoStack.Children.Add(_txtSelectedAppTitle);
            selInfoStack.Children.Add(_txtSelectedAppDetails);
            actionDock.Children.Add(selInfoStack);

            actionBorder.Child = actionDock;
            Grid.SetRow(actionBorder, 3);
            grid.Children.Add(actionBorder);

            return grid;
        }

        // =========================================================================
        // ONGLET 2 : RECHERCHE D'APPLICATIONS ORPHELINES & DÉSINSTALLATION FORCÉE
        // =========================================================================
        private UIElement BuildForcedUninstallTab()
        {
            var tabControl = new TabControl
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(6)
            };

            tabControl.Items.Add(CreateTabItem("🎯 Traque Ciblée (Nom / Dossier)", BuildTargetedOrphanSubTab()));
            tabControl.Items.Add(CreateTabItem("⚡ Détection Automatique Globale", BuildAutoOrphanSubTab()));

            return tabControl;
        }

        private UIElement BuildTargetedOrphanSubTab()
        {
            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var infoBorder = new Border
            {
                Background = BrushFromHex("#1E293B"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var formStack = new StackPanel();
            formStack.Children.Add(new TextBlock
            {
                Text = "🎯 Traque Ciblée : Éradication Manuelle d'une Application",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            formStack.Children.Add(new TextBlock
            {
                Text = "Indiquez le nom de l'application ou sélectionnez son dossier/exécutable résiduel pour rechercher en profondeur tous ses restes (AppData, ProgramData, Clés Registre, Temp, Prefetch).",
                FontSize = 12,
                Foreground = BrushFromHex("#94A3B8"),
                Margin = new Thickness(0, 4, 0, 12)
            });

            var inputsGrid = new Grid();
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            inputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Colonne 1 : Champ Nom avec label au-dessus
            var col1Stack = new StackPanel();
            col1Stack.Children.Add(new TextBlock
            {
                Text = "Nom ou mot-clé de l'application :",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex("#CBD5E1"),
                Margin = new Thickness(2, 0, 0, 4)
            });
            _txtForcedName = CreateInputBox("Tapez par exemple : OneDrive, FileZilla, VLC, Chrome...");
            col1Stack.Children.Add(_txtForcedName);
            Grid.SetColumn(col1Stack, 0);
            inputsGrid.Children.Add(col1Stack);

            // Colonne 2 : Champ Chemin avec label au-dessus
            var col2Stack = new StackPanel();
            col2Stack.Children.Add(new TextBlock
            {
                Text = "Dossier ou fichier résiduel sur le disque (optionnel) :",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex("#CBD5E1"),
                Margin = new Thickness(2, 0, 0, 4)
            });
            _txtForcedPath = CreateInputBox("Collez ou sélectionnez un dossier ou exécutable résiduel");
            col2Stack.Children.Add(_txtForcedPath);
            Grid.SetColumn(col2Stack, 2);
            inputsGrid.Children.Add(col2Stack);

            var btnBrowseFolder = CreateActionButton("📁 Choisir Dossier", "#334155", "#E2E8F0");
            btnBrowseFolder.VerticalAlignment = VerticalAlignment.Bottom;
            btnBrowseFolder.ToolTip = "Sélectionner directement un dossier résiduel sur le disque (ex: C:\\Program Files\\...)";
            btnBrowseFolder.Click += (s, e) =>
            {
                using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
                {
                    fbd.Description = "Sélectionner le dossier orphelin ou résiduel à analyser";
                    fbd.ShowNewFolderButton = false;
                    if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        _txtForcedPath.Text = fbd.SelectedPath;
                        if (string.IsNullOrWhiteSpace(_txtForcedName.Text))
                        {
                            try { _txtForcedName.Text = Path.GetFileName(fbd.SelectedPath.TrimEnd('\\', '/')); } catch { }
                        }
                    }
                }
            };
            Grid.SetColumn(btnBrowseFolder, 4);
            inputsGrid.Children.Add(btnBrowseFolder);

            var btnBrowseFile = CreateActionButton("📄 Choisir .exe", "#334155", "#E2E8F0");
            btnBrowseFile.VerticalAlignment = VerticalAlignment.Bottom;
            btnBrowseFile.ToolTip = "Sélectionner un fichier .exe ou autre de l'application";
            btnBrowseFile.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Sélectionner un exécutable ou fichier de l'application à éradiquer",
                    Filter = "Exécutables (*.exe)|*.exe|Tous les fichiers (*.*)|*.*"
                };
                if (dlg.ShowDialog(this) == true)
                {
                    _txtForcedPath.Text = Path.GetDirectoryName(dlg.FileName);
                    if (string.IsNullOrWhiteSpace(_txtForcedName.Text))
                        _txtForcedName.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
                }
            };
            Grid.SetColumn(btnBrowseFile, 6);
            inputsGrid.Children.Add(btnBrowseFile);

            var btnStartForcedScan = CreateActionButton("🔍 Traquer Traces", "#2563EB", "#FFFFFF");
            btnStartForcedScan.VerticalAlignment = VerticalAlignment.Bottom;
            btnStartForcedScan.ToolTip = "Lancer l'analyse en profondeur des disques et du Registre pour ce nom/dossier";
            btnStartForcedScan.Click += async (s, e) => await RunForcedScanAsync();
            Grid.SetColumn(btnStartForcedScan, 8);
            inputsGrid.Children.Add(btnStartForcedScan);

            formStack.Children.Add(inputsGrid);
            infoBorder.Child = formStack;
            Grid.SetRow(infoBorder, 0);
            grid.Children.Add(infoBorder);

            // Barre de sélection
            var barDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            _txtForcedSummary = new TextBlock
            {
                Text = "Prêt. Saisissez le nom ou le dossier d'un programme à éradiquer.",
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex("#E2E8F0"),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_txtForcedSummary, Dock.Left);
            barDock.Children.Add(_txtForcedSummary);

            var selBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            selBtns.Children.Add(CreateSmallPillButton("Tout cocher", (s, e) =>
            {
                foreach (var i in _forcedLeftovers) i.IsSelected = true;
                UpdateForcedCounters();
            }));
            selBtns.Children.Add(CreateSmallPillButton("Tout décocher", (s, e) =>
            {
                foreach (var i in _forcedLeftovers) i.IsSelected = false;
                UpdateForcedCounters();
            }));
            DockPanel.SetDock(selBtns, Dock.Right);
            barDock.Children.Add(selBtns);

            Grid.SetRow(barDock, 1);
            grid.Children.Add(barDock);

            // Grille des résultats forcés
            _gridForcedLeftovers = CreateStyledDataGrid();
            _gridForcedLeftovers.ItemsSource = _forcedLeftovers;
            _gridForcedLeftovers.Columns.Add(UiTheme.CreateSingleClickCheckBoxColumn(Loc.T("Choix", "Pick"), "IsSelected", 56));
            _gridForcedLeftovers.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding("TypeLabel"), Width = new DataGridLength(110), IsReadOnly = true });
            _gridForcedLeftovers.Columns.Add(new DataGridTextColumn { Header = "Catégorie", Binding = new Binding("Category"), Width = new DataGridLength(180), IsReadOnly = true, ElementStyle = UiTheme.CreateCellTextTooltipStyle("Category") });
            _gridForcedLeftovers.Columns.Add(new DataGridTextColumn { Header = "Chemin / Clé de Registre", Binding = new Binding("DisplayPath"), Width = new DataGridLength(2.2, DataGridLengthUnitType.Star), MinWidth = 260, IsReadOnly = true, ElementStyle = UiTheme.CreateCellTextTooltipStyle("FullTooltip") });
            _gridForcedLeftovers.Columns.Add(new DataGridTextColumn { Header = "Taille", Binding = new Binding("SizeFormatted"), Width = new DataGridLength(90), IsReadOnly = true });
            _gridForcedLeftovers.Columns.Add(new DataGridTextColumn { Header = "Fiabilité", Binding = new Binding("Confidence"), Width = new DataGridLength(120), IsReadOnly = true });

            Grid.SetRow(_gridForcedLeftovers, 2);
            grid.Children.Add(_gridForcedLeftovers);

            // Bouton supprimer
            var bottomDock = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            _btnCleanForced = CreateActionButton("🔥 Éradiquer les traces sélectionnées (Corbeille + Sauvegarde .reg)", "#DC2626", "#FFFFFF");
            _btnCleanForced.IsEnabled = false;
            _btnCleanForced.Click += async (s, e) => await CleanForcedItemsAsync();
            DockPanel.SetDock(_btnCleanForced, Dock.Right);
            bottomDock.Children.Add(_btnCleanForced);

            Grid.SetRow(bottomDock, 3);
            grid.Children.Add(bottomDock);

            return grid;
        }

        private UIElement BuildAutoOrphanSubTab()
        {
            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var infoBorder = new Border
            {
                Background = BrushFromHex("#1E293B"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var dock = new DockPanel();
            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            infoStack.Children.Add(new TextBlock
            {
                Text = "⚡ Détection Automatique Globale des Dossiers & Clés Orphelins",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = "Scanne l'ensemble de Program Files, ProgramData, AppData et du Registre pour détecter les dossiers et clés abandonnés par d'anciennes applications déjà désinstallées.",
                FontSize = 12,
                Foreground = BrushFromHex("#94A3B8"),
                Margin = new Thickness(0, 4, 0, 0)
            });
            dock.Children.Add(infoStack);

            _btnScanAutoOrphans = CreateActionButton("⚡ Lancer l'Analyse Automatique", "#0284C7", "#FFFFFF");
            _btnScanAutoOrphans.Click += async (s, e) => await RunAutoOrphansScanAsync();
            DockPanel.SetDock(_btnScanAutoOrphans, Dock.Right);
            dock.Children.Add(_btnScanAutoOrphans);

            infoBorder.Child = dock;
            Grid.SetRow(infoBorder, 0);
            grid.Children.Add(infoBorder);

            // Barre de sélection
            var barDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            _txtAutoOrphansSummary = new TextBlock
            {
                Text = "Cliquez sur « Lancer l'Analyse Automatique » pour scanner le système.",
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex("#E2E8F0"),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_txtAutoOrphansSummary, Dock.Left);
            barDock.Children.Add(_txtAutoOrphansSummary);

            var selBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            selBtns.Children.Add(CreateSmallPillButton("Tout cocher", (s, e) =>
            {
                foreach (var i in _autoOrphans) i.IsSelected = true;
                UpdateAutoOrphansCounters();
            }));
            selBtns.Children.Add(CreateSmallPillButton("Tout décocher", (s, e) =>
            {
                foreach (var i in _autoOrphans) i.IsSelected = false;
                UpdateAutoOrphansCounters();
            }));
            DockPanel.SetDock(selBtns, Dock.Right);
            barDock.Children.Add(selBtns);

            Grid.SetRow(barDock, 1);
            grid.Children.Add(barDock);

            // Grille des orphelins auto
            _gridAutoOrphans = CreateStyledDataGrid();
            _gridAutoOrphans.ItemsSource = _autoOrphans;
            _gridAutoOrphans.Columns.Add(UiTheme.CreateSingleClickCheckBoxColumn(Loc.T("Choix", "Pick"), "IsSelected", 56));
            _gridAutoOrphans.Columns.Add(new DataGridTextColumn { Header = "Nom / Élément", Binding = new Binding("Name"), Width = new DataGridLength(170), IsReadOnly = true, ElementStyle = UiTheme.CreateCellTextTooltipStyle("Name") });
            _gridAutoOrphans.Columns.Add(new DataGridTextColumn { Header = "Emplacement", Binding = new Binding("Category"), Width = new DataGridLength(175), IsReadOnly = true, ElementStyle = UiTheme.CreateCellTextTooltipStyle("Category") });
            _gridAutoOrphans.Columns.Add(new DataGridTextColumn { Header = "Chemin Disque ou Registre", Binding = new Binding("Path"), Width = new DataGridLength(2.2, DataGridLengthUnitType.Star), MinWidth = 260, IsReadOnly = true, ElementStyle = UiTheme.CreateCellTextTooltipStyle("Path") });
            _gridAutoOrphans.Columns.Add(new DataGridTextColumn { Header = "Taille", Binding = new Binding("SizeFormatted"), Width = new DataGridLength(90), IsReadOnly = true });
            _gridAutoOrphans.Columns.Add(new DataGridTextColumn { Header = "Modifié le", Binding = new Binding("LastModifiedFormatted"), Width = new DataGridLength(105), IsReadOnly = true });

            // Clic droit pour ouvrir dans l'Explorateur
            var ctx = new ContextMenu();
            AddContextMenuItem(ctx, "📂 Ouvrir l'emplacement dans l'Explorateur", (s, e) =>
            {
                var cand = _gridAutoOrphans.SelectedItem as OrphanCandidate;
                if (cand != null && cand.IsDirectory && Directory.Exists(cand.Path))
                {
                    try { Process.Start("explorer.exe", "\"" + cand.Path + "\""); } catch { }
                }
            });
            _gridAutoOrphans.ContextMenu = ctx;

            Grid.SetRow(_gridAutoOrphans, 2);
            grid.Children.Add(_gridAutoOrphans);

            // Bouton supprimer orphelins auto
            var bottomDock = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            _btnCleanAutoOrphans = CreateActionButton("🗑️ Supprimer les éléments orphelins cochés", "#DC2626", "#FFFFFF");
            _btnCleanAutoOrphans.IsEnabled = false;
            _btnCleanAutoOrphans.Click += async (s, e) => await CleanAutoOrphansAsync();
            DockPanel.SetDock(_btnCleanAutoOrphans, Dock.Right);
            bottomDock.Children.Add(_btnCleanAutoOrphans);

            Grid.SetRow(bottomDock, 3);
            grid.Children.Add(bottomDock);

            return grid;
        }

        // =========================================================================
        // ONGLET 3 : GESTIONNAIRE DE DÉMARRAGE (AUTORUN)
        // =========================================================================
        private UIElement BuildStartupManagerTab()
        {
            var grid = new Grid { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = "🚀 Programmes lancés automatiquement au démarrage de Windows",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = "Identifiez et supprimez les entrées de démarrage inutiles ou orphelines laissées par d'anciennes applications.",
                FontSize = 12,
                Foreground = BrushFromHex("#94A3B8"),
                Margin = new Thickness(0, 3, 0, 0)
            });
            DockPanel.SetDock(infoStack, Dock.Left);
            headerDock.Children.Add(infoStack);

            var btnRefreshStartup = CreateActionButton("🔄 Actualiser", "#334155", "#E2E8F0");
            btnRefreshStartup.HorizontalAlignment = HorizontalAlignment.Right;
            btnRefreshStartup.Click += (s, e) => RefreshStartupItems();
            DockPanel.SetDock(btnRefreshStartup, Dock.Right);
            headerDock.Children.Add(btnRefreshStartup);

            Grid.SetRow(headerDock, 0);
            grid.Children.Add(headerDock);

            _gridStartup = CreateStyledDataGrid();
            _gridStartup.ItemsSource = _startupItems;
            _gridStartup.Columns.Add(new DataGridTextColumn { Header = "Nom", Binding = new Binding("Name"), Width = new DataGridLength(180), IsReadOnly = true });
            _gridStartup.Columns.Add(new DataGridTextColumn { Header = "État", Binding = new Binding("StatusLabel"), Width = new DataGridLength(180), IsReadOnly = true });
            _gridStartup.Columns.Add(new DataGridTextColumn { Header = "Emplacement", Binding = new Binding("LocationLabel"), Width = new DataGridLength(210), IsReadOnly = true });
            _gridStartup.Columns.Add(new DataGridTextColumn { Header = "Commande exécutée", Binding = new Binding("Command"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });

            Grid.SetRow(_gridStartup, 1);
            grid.Children.Add(_gridStartup);

            var bottomPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var btnOpenStartupLoc = CreateActionButton("📂 Ouvrir l'emplacement du fichier", "#334155", "#E2E8F0");
            btnOpenStartupLoc.Margin = new Thickness(0, 0, 10, 0);
            btnOpenStartupLoc.Click += (s, e) =>
            {
                var item = _gridStartup.SelectedItem as StartupItem;
                if (item != null && !string.IsNullOrWhiteSpace(item.ExecutablePath) && File.Exists(item.ExecutablePath))
                {
                    try { Process.Start("explorer.exe", "/select,\"" + item.ExecutablePath + "\""); } catch { }
                }
            };

            var btnRemoveStartup = CreateActionButton("🗑️ Supprimer du démarrage (Avec sauvegarde .reg)", "#DC2626", "#FFFFFF");
            btnRemoveStartup.Click += (s, e) =>
            {
                var item = _gridStartup.SelectedItem as StartupItem;
                if (item == null) return;

                if (MessageBox.Show(this,
                    string.Format("Voulez-vous retirer « {0} » du démarrage automatique de Windows ?", item.Name),
                    "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    string msg;
                    if (StartupAndJunkManager.RemoveStartupItem(item, out msg))
                    {
                        RefreshStartupItems();
                        MessageBox.Show(this, msg, "Entrée supprimée", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(this, msg, "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            };

            bottomPanel.Children.Add(btnOpenStartupLoc);
            bottomPanel.Children.Add(btnRemoveStartup);
            Grid.SetRow(bottomPanel, 2);
            grid.Children.Add(bottomPanel);

            return grid;
        }

        // =========================================================================
        // ONGLET 4 : NETTOYEUR DE FICHIERS INUTILES
        // =========================================================================
        private UIElement BuildJunkCleanerTab()
        {
            var grid = new Grid { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = "🧹 Nettoyeur de Fichiers Temporaires, Caches d'Installateurs & Rapports de Crash",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            _txtJunkSummary = new TextBlock
            {
                Text = "Analyse des dossiers temporaires...",
                FontSize = 12,
                Foreground = BrushFromHex("#38BDF8"),
                Margin = new Thickness(0, 3, 0, 0)
            };
            infoStack.Children.Add(_txtJunkSummary);
            DockPanel.SetDock(infoStack, Dock.Left);
            headerDock.Children.Add(infoStack);

            var btnRescanJunk = CreateActionButton("🔄 Réanalyser l'espace récupérable", "#334155", "#E2E8F0");
            btnRescanJunk.HorizontalAlignment = HorizontalAlignment.Right;
            btnRescanJunk.Click += async (s, e) => await RefreshJunkCategoriesAsync();
            DockPanel.SetDock(btnRescanJunk, Dock.Right);
            headerDock.Children.Add(btnRescanJunk);

            Grid.SetRow(headerDock, 0);
            grid.Children.Add(headerDock);

            _gridJunk = CreateStyledDataGrid();
            _gridJunk.ItemsSource = _junkCategories;
            _gridJunk.Columns.Add(UiTheme.CreateSingleClickCheckBoxColumn(Loc.T("Inclure", "Include"), "IsSelected", 65));
            _gridJunk.Columns.Add(new DataGridTextColumn { Header = "Catégorie", Binding = new Binding("Name"), Width = new DataGridLength(280), IsReadOnly = true });
            _gridJunk.Columns.Add(new DataGridTextColumn { Header = "Dossier", Binding = new Binding("FolderPath"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
            _gridJunk.Columns.Add(new DataGridTextColumn { Header = "Fichiers", Binding = new Binding("FileCount"), Width = new DataGridLength(90), IsReadOnly = true });
            _gridJunk.Columns.Add(new DataGridTextColumn { Header = "Espace occupé", Binding = new Binding("SizeFormatted"), Width = new DataGridLength(120), IsReadOnly = true });

            Grid.SetRow(_gridJunk, 1);
            grid.Children.Add(_gridJunk);

            var bottomDock = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var btnCleanJunk = CreateActionButton("🧹 Nettoyer les fichiers temporaires sélectionnés", "#10B981", "#FFFFFF");
            btnCleanJunk.HorizontalAlignment = HorizontalAlignment.Right;
            btnCleanJunk.Click += async (s, e) =>
            {
                var snapshot = _junkCategories.ToList();
                var report = await Task.Run(() => StartupAndJunkManager.CleanJunkCategories(snapshot));
                await RefreshJunkCategoriesAsync();
                MessageBox.Show(this,
                    string.Format("Nettoyage terminé !\n\n• Fichiers supprimés : {0}\n• Dossiers supprimés : {1}\n• Espace disque libéré : {2}",
                        report.DeletedFiles, report.DeletedDirectories, FormatUtils.FormatBytes(report.FreedBytes)),
                    "Nettoyage Terminé", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            DockPanel.SetDock(btnCleanJunk, Dock.Right);
            bottomDock.Children.Add(btnCleanJunk);

            Grid.SetRow(bottomDock, 2);
            grid.Children.Add(bottomDock);

            return grid;
        }

        // =========================================================================
        // LOGIQUE ET ÉVÉNEMENTS
        // =========================================================================
        private async Task RefreshAllDataAsync()
        {
            await RefreshAppsOnlyAsync();
            RefreshStartupItems();
            await RefreshJunkCategoriesAsync();
            RefreshHistoryAndBackups();
        }

        private async Task RefreshAppsOnlyAsync()
        {
            _lblStatusFooter.Text = Loc.T(
                "Analyse des applications installées dans le Registre Windows (64-bit, 32-bit, Utilisateur)...",
                "Scanning installed applications in Windows Registry (64-bit, 32-bit, Current User)...");
            bool includeSystem = _cbIncludeSystem.IsChecked == true;
            bool includeStore = _cbIncludeStore.IsChecked == true;

            // Conserver les cases cochées même après un rafraîchissement
            var previouslyCheckedIds = new HashSet<string>(
                _allApps.Where(a => a.IsChecked && !string.IsNullOrWhiteSpace(a.Id)).Select(a => a.Id),
                StringComparer.OrdinalIgnoreCase);

            var scanned = await Task.Run(() => AppScanner.ScanInstalledApps(includeSystem, includeStore));
            foreach (var app in scanned)
            {
                app.AppIconImage = AppIconHelper.DefaultAppIcon;
                if (!string.IsNullOrWhiteSpace(app.Id) && previouslyCheckedIds.Contains(app.Id))
                    app.IsChecked = true;

                app.PropertyChanged += (s, ev) =>
                {
                    if (ev.PropertyName == "IsChecked")
                        UpdateCheckedAppsUI();
                };
            }

            _allApps = scanned;
            ApplyAppFilters();
            UpdateCheckedAppsUI();

            // Charger les vraies icônes Windows en arrière-plan sans jamais bloquer l'interface
            var appsSnapshot = _allApps.ToList();
            var unused = Task.Run(() =>
            {
                foreach (var app in appsSnapshot)
                {
                    var icon = AppIconHelper.ResolveIconForApp(app);
                    if (icon != null && icon != AppIconHelper.DefaultAppIcon)
                    {
                        var targetApp = app;
                        var resolvedIcon = icon;
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            targetApp.AppIconImage = resolvedIcon;
                        }));
                    }
                }
            });
        }

        private void ApplyAppFilters()
        {
            string query = (_txtSearch != null ? _txtSearch.Text : "").Trim();
            IEnumerable<InstalledApp> enumerable = _allApps;

            // Filtre par onglet de catégorie
            switch (_activeFilterTag)
            {
                case "SYS64":
                    enumerable = enumerable.Where(a => a.Scope == AppScope.System64);
                    break;
                case "SYS32":
                    enumerable = enumerable.Where(a => a.Scope == AppScope.System32);
                    break;
                case "USER":
                    enumerable = enumerable.Where(a => a.Scope == AppScope.CurrentUser);
                    break;
                case "LARGE":
                    enumerable = enumerable.Where(a => a.SizeBytes >= 100L * 1024L * 1024L);
                    break;
                case "RECENT":
                    DateTime cutoff = DateTime.Now.AddDays(-30);
                    enumerable = enumerable.Where(a => a.InstallDateParsed.HasValue && a.InstallDateParsed.Value >= cutoff);
                    break;
                case "CHECKED":
                    enumerable = enumerable.Where(a => a.IsChecked);
                    break;
            }

            // Filtre par recherche texte
            if (!string.IsNullOrWhiteSpace(query))
            {
                enumerable = enumerable.Where(a =>
                    (a.DisplayName != null && a.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (a.Publisher != null && a.Publisher.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (a.InstallLocation != null && a.InstallLocation.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            var list = enumerable.ToList();
            _filteredApps.Clear();
            foreach (var app in list)
                _filteredApps.Add(app);

            long totalBytes = list.Sum(a => a.SizeBytes);
            _lblTotalApps.Text = string.Format("{0} / {1}", list.Count, _allApps.Count);
            _lblTotalSize.Text = FormatUtils.FormatBytes(totalBytes);
            if (_lblHeaderSubtitle != null)
            {
                _lblHeaderSubtitle.Text = string.Format("{0}  •  v2.0  •  {1} applications  •  {2}",
                    Environment.MachineName, _allApps.Count, FormatUtils.FormatBytes(_allApps.Sum(a => a.SizeBytes)));
            }
            _lblStatusFooter.Text = string.Format(
                Loc.T("Prêt — {0} application(s) affichée(s) sur {1}. Cochez plusieurs applis (conservées entre vos recherches) ou glissez-déposez un raccourci/dossier ici.",
                      "Ready — {0} of {1} app(s) shown. Check multiple apps (kept across searches) or drag & drop a shortcut/folder here."),
                list.Count, _allApps.Count);

            if (_gridApps != null && _gridApps.SelectedItem == null && _filteredApps.Count > 0)
            {
                _gridApps.SelectedIndex = 0;
            }
        }

        private List<InstalledApp> GetCheckedApps()
        {
            return _allApps.Where(a => a.IsChecked).ToList();
        }

        private List<InstalledApp> GetSelectedApps()
        {
            // Priorité aux applications explicitement cochées (persistantes entre recherches !)
            var checkedApps = GetCheckedApps();
            if (checkedApps.Count > 0)
                return checkedApps;

            var result = new List<InstalledApp>();
            if (_gridApps == null) return result;
            foreach (var item in _gridApps.SelectedItems)
            {
                var app = item as InstalledApp;
                if (app != null) result.Add(app);
            }
            return result;
        }

        private void UpdateCheckedAppsUI()
        {
            var checkedApps = GetCheckedApps();
            int checkedCount = checkedApps.Count;

            if (_btnCheckedFilter != null)
            {
                _btnCheckedFilter.Content = string.Format(Loc.T("✓ Cochées ({0})", "✓ Checked ({0})"), checkedCount);
            }
            if (_btnClearChecked != null)
            {
                _btnClearChecked.Visibility = checkedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            OnSelectedAppChanged(null, null);
        }

        private void OnSelectedAppChanged(object sender, SelectionChangedEventArgs e)
        {
            var targetApps = GetSelectedApps();
            int count = targetApps.Count;
            bool usingCheckboxes = GetCheckedApps().Count > 0;

            if (count > 1 || (usingCheckboxes && count >= 1))
            {
                _btnUninstallComplete.IsEnabled = true;
                _btnUninstallSilent.IsEnabled = true;
                _btnScanLeftoversOnly.IsEnabled = count == 1;
                _btnOpenFolder.IsEnabled = count == 1 && !string.IsNullOrWhiteSpace(targetApps[0].InstallLocation) && Directory.Exists(targetApps[0].InstallLocation);
                _btnUninstallComplete.Content = count > 1
                    ? string.Format(Loc.T("🗑️ Désinstaller ({0})", "🗑️ Uninstall ({0})"), count)
                    : Loc.T("🗑️ Désinstallation en Profondeur", "🗑️ Deep Uninstall");
                _btnUninstallSilent.Content = count > 1
                    ? string.Format(Loc.T("⚡ Silencieuse ({0})", "⚡ Silent ({0})"), count)
                    : Loc.T("⚡ Désinstallation Silencieuse", "⚡ Silent Uninstall");

                long total = targetApps.Sum(a => a.SizeBytes);
                _txtSelectedAppTitle.Text = string.Format(
                    Loc.T("{0} application(s) cochée(s) / sélectionnée(s) ({1})", "{0} app(s) checked / selected ({1})"),
                    count, FormatUtils.FormatBytes(total));
                _txtSelectedAppDetails.Text = string.Join(", ", targetApps.Select(a => a.DisplayName).ToArray());
                return;
            }

            _btnUninstallComplete.Content = Loc.T("🗑️ Désinstallation en Profondeur", "🗑️ Deep Uninstall");
            _btnUninstallSilent.Content = Loc.T("⚡ Désinstallation Silencieuse", "⚡ Silent Uninstall");

            var app = _gridApps.SelectedItem as InstalledApp;
            bool hasSelection = app != null;

            _btnUninstallComplete.IsEnabled = hasSelection;
            _btnUninstallSilent.IsEnabled = hasSelection;
            _btnScanLeftoversOnly.IsEnabled = hasSelection;
            _btnOpenFolder.IsEnabled = hasSelection && !string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation);

            if (app == null)
            {
                _txtSelectedAppTitle.Text = Loc.T("Sélectionnez ou cochez une application dans la liste", "Select or check an application in the list");
                _txtSelectedAppDetails.Text = Loc.T("Cochez plusieurs applis (la sélection est conservée quand vous changez de recherche !) ou glissez-déposez un raccourci sur la fenêtre.",
                                                    "Check multiple apps (checked items stay saved across searches!) or drag & drop a shortcut onto the window.");
            }
            else
            {
                _txtSelectedAppTitle.Text = string.Format("{0} ({1})", app.DisplayName, app.DisplayVersion);
                _txtSelectedAppDetails.Text = string.Format("Clé : {0}   |   Commande : {1}",
                    app.RegistryKeyPath,
                    string.IsNullOrWhiteSpace(app.UninstallString) ? app.QuietUninstallString : app.UninstallString);
            }
        }

        private async void OpenUninstallWizardForSelected(bool skipOfficialUninstaller, bool preferSilent)
        {
            var selectedApps = GetSelectedApps();
            if (selectedApps.Count > 1 && !skipOfficialUninstaller)
            {
                var batch = new BatchUninstallWindow(selectedApps, _allApps, preferSilent)
                {
                    Owner = this
                };
                batch.ShowDialog();
                if (batch.WasCleanedOrUninstalled)
                {
                    await RefreshAllDataAsync();
                }
                return;
            }

            var app = selectedApps.Count == 1 ? selectedApps[0] : (_gridApps.SelectedItem as InstalledApp);
            if (app == null) return;

            var wizard = new UninstallWizardWindow(app, _allApps, skipOfficialUninstaller, preferSilent)
            {
                Owner = this
            };
            wizard.ShowDialog();

            if (wizard.WasCleanedOrUninstalled)
            {
                await RefreshAllDataAsync();
            }
        }

        private void OpenSelectedAppFolder()
        {
            var app = _gridApps.SelectedItem as InstalledApp;
            if (app == null || string.IsNullOrWhiteSpace(app.InstallLocation) || !Directory.Exists(app.InstallLocation))
            {
                MessageBox.Show(this, "Le dossier d'installation de cette application n'est pas renseigné ou n'existe plus sur le disque.", "Dossier introuvable", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                Process.Start("explorer.exe", "\"" + app.InstallLocation + "\"");
            }
            catch { }
        }

        private void CopySelectedAppRegistryKey()
        {
            var app = _gridApps.SelectedItem as InstalledApp;
            if (app == null || string.IsNullOrWhiteSpace(app.RegistryKeyPath)) return;
            Clipboard.SetText(app.RegistryKeyPath);
            _lblStatusFooter.Text = "Clé de registre copiée dans le presse-papiers : " + app.RegistryKeyPath;
        }

        private void OnExportCsvClick(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exporter la liste des applications en CSV",
                Filter = "Fichier CSV (*.csv)|*.csv",
                FileName = string.Format("Applications_Installees_{0:yyyyMMdd}.csv", DateTime.Now)
            };

            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Nom;Editeur;Version;Taille;DateInstallation;Portee;DossierInstallation;CleRegistre");
                    foreach (var a in _filteredApps)
                    {
                        sb.AppendFormat("{0};{1};{2};{3};{4};{5};{6};{7}\r\n",
                            EscapeCsv(a.DisplayName),
                            EscapeCsv(a.Publisher),
                            EscapeCsv(a.DisplayVersion),
                            EscapeCsv(a.SizeFormatted),
                            EscapeCsv(a.InstallDateFormatted),
                            EscapeCsv(a.ScopeLabel),
                            EscapeCsv(a.InstallLocation),
                            EscapeCsv(a.RegistryKeyPath));
                    }
                    File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show(this, "Liste exportée avec succès vers :\n" + dlg.FileName, "Export CSV", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Erreur lors de l'export : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            return val.Replace(";", ",").Replace("\r", " ").Replace("\n", " ");
        }

        private async Task RunForcedScanAsync()
        {
            string targetName = (_txtForcedName.Text ?? "").Trim();
            string targetPath = (_txtForcedPath.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(targetName) && string.IsNullOrWhiteSpace(targetPath))
            {
                MessageBox.Show(this, "Veuillez saisir le nom de l'application (minimum 3 caractères) ou sélectionner son dossier / exécutable.", "Information requise", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _txtForcedSummary.Text = "Recherche approfondie des traces en cours...";
            var found = await Task.Run(() => LeftoverScanner.ScanForcedTarget(targetName, targetPath, ScanDepth.Advanced, _allApps));

            _forcedLeftovers.Clear();
            foreach (var item in found)
            {
                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == "IsSelected") UpdateForcedCounters();
                };
                _forcedLeftovers.Add(item);
            }
            UpdateForcedCounters();
        }

        private void UpdateForcedCounters()
        {
            int count = _forcedLeftovers.Count;
            int selected = _forcedLeftovers.Count(i => i.IsSelected);
            long bytes = _forcedLeftovers.Where(i => i.IsSelected).Sum(i => i.SizeBytes);

            _txtForcedSummary.Text = string.Format("Résultat de la traque : {0} trace(s) détectée(s) — {1} sélectionnée(s) ({2})",
                count, selected, FormatUtils.FormatBytes(bytes));
            _btnCleanForced.IsEnabled = selected > 0;
        }

        private async Task CleanForcedItemsAsync()
        {
            var selected = _forcedLeftovers.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) return;

            string label = !string.IsNullOrWhiteSpace(_txtForcedName.Text) ? _txtForcedName.Text.Trim() : "ForcedTarget";
            if (MessageBox.Show(this,
                string.Format("Voulez-vous supprimer les {0} traces sélectionnées pour « {1} » ?\n(Les fichiers iront dans la Corbeille et le Registre sera sauvegardé dans Backups/)", selected.Count, label),
                "Confirmation Désinstallation Forcée", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            var report = await Task.Run(() => UninstallerEngine.CleanSelectedLeftovers(label, selected, true, true));
            await RunForcedScanAsync();

            MessageBox.Show(this,
                string.Format("Éradication terminée !\n• Dossiers supprimés : {0}\n• Fichiers supprimés : {1}\n• Entrées registre supprimées : {2}\n• Planifiés au redémarrage : {3}\n• Échecs / ignorés : {4}\n• Espace libéré : {5}",
                    report.DeletedDirectories, report.DeletedFiles, report.DeletedRegistryItems, report.ScheduledForRebootItems, report.FailedItems, FormatUtils.FormatBytes(report.FreedBytes)),
                "Désinstallation Forcée Terminée", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task RunAutoOrphansScanAsync()
        {
            _btnScanAutoOrphans.IsEnabled = false;
            _txtAutoOrphansSummary.Text = "Analyse approfondie de Program Files, ProgramData, AppData et Registre en cours...";
            try
            {
                var candidates = await Task.Run(() => LeftoverScanner.FindAllOrphanCandidates(_allApps));
                _autoOrphans.Clear();
                foreach (var c in candidates)
                {
                    c.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == "IsSelected") UpdateAutoOrphansCounters();
                    };
                    _autoOrphans.Add(c);
                }
                UpdateAutoOrphansCounters();
            }
            finally
            {
                _btnScanAutoOrphans.IsEnabled = true;
            }
        }

        private void UpdateAutoOrphansCounters()
        {
            int total = _autoOrphans.Count;
            int selected = _autoOrphans.Count(i => i.IsSelected);
            long bytes = _autoOrphans.Where(i => i.IsSelected).Sum(i => i.SizeBytes);

            _txtAutoOrphansSummary.Text = string.Format("Éléments orphelins détectés : {0} — {1} sélectionné(s) ({2})",
                total, selected, FormatUtils.FormatBytes(bytes));
            _btnCleanAutoOrphans.IsEnabled = selected > 0;
        }

        private async Task CleanAutoOrphansAsync()
        {
            var selected = _autoOrphans.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) return;

            if (MessageBox.Show(this,
                string.Format("Supprimer définitivement les {0} éléments orphelins sélectionnés ?\n(Les dossiers iront dans la Corbeille Windows et le Registre sera sauvegardé dans Backups/)", selected.Count),
                "Confirmation Suppression des Orphelins", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            _btnCleanAutoOrphans.IsEnabled = false;
            try
            {
                var leftoverItems = new List<LeftoverItem>();
                foreach (var cand in selected)
                {
                    leftoverItems.Add(new LeftoverItem
                    {
                        Type = cand.IsDirectory ? LeftoverType.Directory : LeftoverType.RegistryKey,
                        Path = cand.Path,
                        SizeBytes = cand.SizeBytes,
                        Category = cand.Category,
                        IsSelected = true
                    });
                }

                var report = await Task.Run(() => UninstallerEngine.CleanSelectedLeftovers("Orphans_Cleaned", leftoverItems, true, true));
                await RunAutoOrphansScanAsync();

                MessageBox.Show(this,
                    string.Format("Nettoyage des orphelins terminé !\n• Dossiers supprimés : {0}\n• Clés registre supprimées : {1}\n• Planifiés au redémarrage : {2}\n• Échecs / ignorés : {3}\n• Espace libéré : {4}",
                        report.DeletedDirectories, report.DeletedRegistryItems, report.ScheduledForRebootItems, report.FailedItems, FormatUtils.FormatBytes(report.FreedBytes)),
                    "Nettoyage Terminé", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                _btnCleanAutoOrphans.IsEnabled = _autoOrphans.Any(i => i.IsSelected);
            }
        }

        private void RefreshStartupItems()
        {
            var items = StartupAndJunkManager.ScanStartupItems();
            _startupItems.Clear();
            foreach (var i in items)
                _startupItems.Add(i);

            _lblStartupCount.Text = _startupItems.Count.ToString();
        }

        private async Task RefreshJunkCategoriesAsync()
        {
            var cats = await Task.Run(() => StartupAndJunkManager.ScanJunkCategories());
            _junkCategories.Clear();
            foreach (var c in cats)
                _junkCategories.Add(c);

            long totalJunk = _junkCategories.Sum(c => c.TotalBytes);
            int totalFiles = _junkCategories.Sum(c => c.FileCount);
            _txtJunkSummary.Text = string.Format("Espace récupérable détecté : {0} répartis dans {1} fichiers temporaires",
                FormatUtils.FormatBytes(totalJunk), totalFiles);
        }

        // =========================================================================
        // HELPERS D'INTERFACE GRAPHIQUE
        // =========================================================================
        private void AddStatCard(Grid parentGrid, int colIndex, string label, TextBlock valueBlock)
        {
            var border = new Border
            {
                Background = BrushFromHex("#1E293B"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = BrushFromHex("#94A3B8")
            });
            valueBlock.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(valueBlock);
            border.Child = stack;
            Grid.SetColumn(border, colIndex);
            parentGrid.Children.Add(border);
        }

        private Button AddCategoryFilterButton(Panel parent, string text, string tag, bool isActive)
        {
            var btn = new Button
            {
                Content = text,
                Tag = tag,
                Background = isActive ? BrushFromHex("#2563EB") : BrushFromHex("#1E293B"),
                Foreground = isActive ? Brushes.White : BrushFromHex("#CBD5E1"),
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                FontSize = 12,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 4),
                BorderThickness = new Thickness(1),
                BorderBrush = BrushFromHex("#334155"),
                Cursor = Cursors.Hand
            };

            btn.Click += (s, e) =>
            {
                _activeFilterTag = tag;
                foreach (var b in _filterButtons)
                {
                    bool active = (string)b.Tag == _activeFilterTag;
                    b.Background = active ? BrushFromHex("#2563EB") : BrushFromHex("#1E293B");
                    b.Foreground = active ? Brushes.White : BrushFromHex("#CBD5E1");
                    b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
                }
                ApplyAppFilters();
            };

            _filterButtons.Add(btn);
            parent.Children.Add(btn);
            return btn;
        }

        private static TabItem CreateTabItem(string header, UIElement content)
        {
            return new TabItem
            {
                Header = header,
                Content = content
            };
        }

        private static DataGrid CreateStyledDataGrid()
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

            return grid;
        }

        private static TextBox CreateInputBox(string tooltip)
        {
            return new TextBox
            {
                Background = BrushFromHex("#0F172A"),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderBrush = BrushFromHex("#475569"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 13,
                ToolTip = tooltip,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private static Button CreateActionButton(string text, string bgHex, string fgHex)
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

        private static Button CreateSmallPillButton(string text, RoutedEventHandler onClick)
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

        private static void AddContextMenuItem(ContextMenu menu, string header, RoutedEventHandler onClick)
        {
            var item = new MenuItem { Header = header };
            item.Click += onClick;
            menu.Items.Add(item);
        }

        private void ShowSelfUninstallDialog()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            bool dedicatedFolder = UninstallerEngine.IsDedicatedAppDirectory(appDir);

            var dlg = new Window
            {
                Title = "Désinstallation Complète — ClearVestige",
                Width = 580,
                Height = 480,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = BrushFromHex("#0F172A"),
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI")
            };
            UiTheme.ApplyDarkTheme(dlg);
            UiTheme.ApplyAppIcon(dlg);

            var dlgOuter = new Grid();
            dlgOuter.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            dlgOuter.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var dlgBar = UiTheme.CreateCompactTitleBar(dlg, "CLEARVESTIGE", "Auto-désinstallation", false);
            Grid.SetRow(dlgBar, 0);
            dlgOuter.Children.Add(dlgBar);

            var root = new Grid { Margin = new Thickness(22, 16, 22, 18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // En-tête d'avertissement
            var headerBorder = new Border
            {
                Background = BrushFromHex("#450A0A"),
                BorderBrush = BrushFromHex("#DC2626"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "⚠️ Désinstaller complètement ClearVestige",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = BrushFromHex("#FCA5A5")
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Cette action est irréversible et supprimera intégralement l'application ainsi que toutes ses traces sur ce PC.",
                FontSize = 12,
                Foreground = BrushFromHex("#FECACA"),
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            headerBorder.Child = headerStack;
            Grid.SetRow(headerBorder, 0);
            root.Children.Add(headerBorder);

            // Liste de ce qui sera supprimé
            var detailsBorder = new Border
            {
                Background = BrushFromHex("#1E293B"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var detailsStack = new StackPanel();
            detailsStack.Children.Add(new TextBlock
            {
                Text = "Éléments qui seront définitivement supprimés :",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5,
                Foreground = BrushFromHex("#E2E8F0"),
                Margin = new Thickness(0, 0, 0, 6)
            });
            string[] itemsToDelete = new[]
            {
                "• Exécutable ClearVestige.exe et lanceurs .bat dans : " + appDir,
                "• Dossier Backups/ créé par l'application (sauvegardes .reg)",
                "• Fichiers temporaires (ClearVestige_*) créés dans %TEMP% et raccourcis éventuels",
                "• Traces d'exécution Windows de ClearVestige.exe (MuiCache, AppCompatFlags, Prefetch)",
                "• Le dossier parent sera retiré uniquement s'il ne contient aucun autre fichier personnel"
            };
            foreach (var line in itemsToDelete)
            {
                detailsStack.Children.Add(new TextBlock
                {
                    Text = line,
                    FontSize = 12,
                    Foreground = BrushFromHex("#CBD5E1"),
                    Margin = new Thickness(0, 2, 0, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }
            detailsBorder.Child = detailsStack;
            Grid.SetRow(detailsBorder, 1);
            root.Children.Add(detailsBorder);

            // Zone de confirmation par saisie de "uninstall"
            var confirmStack = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            confirmStack.Children.Add(new TextBlock
            {
                Text = "Pour confirmer et éviter tout clic accidentel, tapez exactement « uninstall » ci-dessous :",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex("#F8FAFC"),
                Margin = new Thickness(0, 0, 0, 6)
            });

            var txtConfirm = CreateInputBox("Tapez uninstall ici pour déverrouiller la suppression");
            txtConfirm.FontSize = 14;
            txtConfirm.FontWeight = FontWeights.Bold;
            confirmStack.Children.Add(txtConfirm);
            Grid.SetRow(confirmStack, 2);
            root.Children.Add(confirmStack);

            // Boutons d'action
            var footerDock = new DockPanel { LastChildFill = false };
            var btnCancel = CreateActionButton("Annuler", "#334155", "#E2E8F0");
            btnCancel.Click += (s, e) => dlg.Close();
            DockPanel.SetDock(btnCancel, Dock.Right);
            footerDock.Children.Add(btnCancel);

            var btnConfirmUninstall = CreateActionButton("🗑️ Désinstaller définitivement", "#DC2626", "#FFFFFF");
            btnConfirmUninstall.IsEnabled = false;
            btnConfirmUninstall.Margin = new Thickness(0, 0, 10, 0);
            btnConfirmUninstall.Click += (s, e) =>
            {
                if (string.Equals((txtConfirm.Text ?? "").Trim(), "uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    dlg.IsEnabled = false;
                    UninstallerEngine.ExecuteSelfUninstall();
                }
            };
            DockPanel.SetDock(btnConfirmUninstall, Dock.Right);
            footerDock.Children.Add(btnConfirmUninstall);

            txtConfirm.TextChanged += (s, e) =>
            {
                btnConfirmUninstall.IsEnabled = string.Equals((txtConfirm.Text ?? "").Trim(), "uninstall", StringComparison.OrdinalIgnoreCase);
            };
            txtConfirm.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && btnConfirmUninstall.IsEnabled)
                {
                    dlg.IsEnabled = false;
                    UninstallerEngine.ExecuteSelfUninstall();
                }
            };

            Grid.SetRow(footerDock, 3);
            root.Children.Add(footerDock);

            Grid.SetRow(root, 1);
            dlgOuter.Children.Add(root);

            dlg.Content = UiTheme.ConfigureCustomChrome(dlg, 46, dlgOuter, false);
            dlg.Loaded += (s, e) => txtConfirm.Focus();
            dlg.ShowDialog();
        }

        private static SolidColorBrush BrushFromHex(string hex)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
        }
    }
}
