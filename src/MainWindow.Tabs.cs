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
using System.Windows.Media;

namespace ClearVestige
{
    public partial class MainWindow
    {
        // Contrôles Services Orphelins & Menu Clic-Droit
        private readonly ObservableCollection<OrphanServiceItem> _orphanServices = new ObservableCollection<OrphanServiceItem>();
        private DataGrid _gridOrphanServices;
        private TextBlock _txtOrphanServicesSummary;
        private Button _btnCleanOrphanServices;

        private readonly ObservableCollection<ContextMenuItemEntry> _contextMenuEntries = new ObservableCollection<ContextMenuItemEntry>();
        private DataGrid _gridContextMenu;
        private TextBlock _txtContextMenuSummary;
        private Button _btnCleanContextMenu;

        // Contrôles Snapshot Avant / Après
        private readonly ObservableCollection<LeftoverItem> _snapshotDiffItems = new ObservableCollection<LeftoverItem>();
        private DataGrid _gridSnapshotDiff;
        private TextBlock _txtSnapshotStatus;
        private Button _btnCompareSnapshot;
        private Button _btnCleanSnapshotDiff;

        // Contrôles Historique & Sauvegardes .reg
        private readonly ObservableCollection<HistoryEntry> _historyEntries = new ObservableCollection<HistoryEntry>();
        private readonly ObservableCollection<BackupFileInfo> _backupFiles = new ObservableCollection<BackupFileInfo>();
        private DataGrid _gridHistory;
        private DataGrid _gridBackups;
        private TextBlock _txtHistorySummary;

        // =========================================================================
        // ONGLET 3 : SYSTÈME & DÉMARRAGE (AUTORUN + SERVICES ORPHELINS + CLIC-DROIT)
        // =========================================================================
        private UIElement BuildSystemAndStartupTab()
        {
            var tc = new TabControl
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(6)
            };

            tc.Items.Add(CreateTabItem(Loc.T("🚀 Démarrage (Autorun)", "🚀 Startup (Autorun)"), BuildStartupManagerTab()));
            tc.Items.Add(CreateTabItem(Loc.T("🔌 Services Orphelins", "🔌 Orphan Services"), BuildOrphanServicesSubTab()));
            tc.Items.Add(CreateTabItem(Loc.T("🖱️ Menu Clic-Droit", "🖱️ Context Menu"), BuildContextMenuSubTab()));

            return tc;
        }

        private UIElement BuildOrphanServicesSubTab()
        {
            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var topDock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = Loc.T("🔌 Services Windows tiers orphelins (exécutable supprimé)", "🔌 Orphan Third-Party Windows Services (missing executable)"),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            _txtOrphanServicesSummary = new TextBlock
            {
                Text = Loc.T("Cliquez sur « Scanner » pour détecter les services tiers dont le fichier .exe n'existe plus (les services C:\\Windows sont protégés).",
                             "Click 'Scan' to detect third-party services whose .exe file no longer exists (C:\\Windows services are protected)."),
                FontSize = 12,
                Foreground = BrushFromHex("#94A3B8"),
                Margin = new Thickness(0, 3, 0, 0)
            };
            stack.Children.Add(_txtOrphanServicesSummary);
            topDock.Children.Add(stack);

            var btnScan = CreateActionButton(Loc.T("🔍 Scanner les services", "🔍 Scan Services"), "#0284C7", "#FFFFFF");
            btnScan.Click += async (s, e) => await RefreshOrphanServicesAsync();
            DockPanel.SetDock(btnScan, Dock.Right);
            topDock.Children.Add(btnScan);

            Grid.SetRow(topDock, 0);
            grid.Children.Add(topDock);

            _gridOrphanServices = CreateStyledDataGrid();
            _gridOrphanServices.ItemsSource = _orphanServices;
            _gridOrphanServices.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = Loc.T("Choix", "Pick"),
                Binding = new Binding("IsSelected") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(55)
            });
            _gridOrphanServices.Columns.Add(new DataGridTextColumn { Header = Loc.T("Nom Service", "Service Name"), Binding = new Binding("ServiceName"), Width = new DataGridLength(170), IsReadOnly = true, ElementStyle = UiTheme.CreateCellTextTooltipStyle("ServiceName") });
            _gridOrphanServices.Columns.Add(new DataGridTextColumn { Header = Loc.T("Nom affiché", "Display Name"), Binding = new Binding("DisplayName"), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star), IsReadOnly = true, ElementStyle = UiTheme.CreateCellTextTooltipStyle("DisplayName") });
            _gridOrphanServices.Columns.Add(new DataGridTextColumn { Header = Loc.T("Exécutable manquant", "Missing Executable"), Binding = new Binding("ResolvedExe"), Width = new DataGridLength(1.8, DataGridLengthUnitType.Star), MinWidth = 220, IsReadOnly = true, ElementStyle = UiTheme.CreateCellTextTooltipStyle("ImagePath") });
            _gridOrphanServices.Columns.Add(new DataGridTextColumn { Header = Loc.T("État", "Status"), Binding = new Binding("Status"), Width = new DataGridLength(180), IsReadOnly = true });
            Grid.SetRow(_gridOrphanServices, 1);
            grid.Children.Add(_gridOrphanServices);

            var bot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            _btnCleanOrphanServices = CreateActionButton(Loc.T("🗑️ Supprimer les services cochés (Admin + .reg)", "🗑️ Delete Checked Services (Admin + .reg)"), "#DC2626", "#FFFFFF");
            _btnCleanOrphanServices.IsEnabled = false;
            _btnCleanOrphanServices.Click += async (s, e) => await DeleteSelectedOrphanServicesAsync();
            DockPanel.SetDock(_btnCleanOrphanServices, Dock.Right);
            bot.Children.Add(_btnCleanOrphanServices);
            Grid.SetRow(bot, 2);
            grid.Children.Add(bot);

            return grid;
        }

        private async Task RefreshOrphanServicesAsync()
        {
            _txtOrphanServicesSummary.Text = Loc.T("Analyse des services Windows en cours...", "Scanning Windows services...");
            var items = await Task.Run(() => SystemToolsManager.ScanOrphanServices());
            _orphanServices.Clear();
            foreach (var it in items)
            {
                it.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == "IsSelected")
                        _btnCleanOrphanServices.IsEnabled = _orphanServices.Any(x => x.IsSelected);
                };
                _orphanServices.Add(it);
            }
            _btnCleanOrphanServices.IsEnabled = _orphanServices.Any(x => x.IsSelected);
            _txtOrphanServicesSummary.Text = string.Format(
                Loc.T("{0} service(s) tiers orphelin(s) détecté(s).", "{0} orphan third-party service(s) detected."),
                _orphanServices.Count);
        }

        private async Task DeleteSelectedOrphanServicesAsync()
        {
            var selected = _orphanServices.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0) return;

            if (MessageBox.Show(this,
                string.Format(Loc.T("Supprimer {0} service(s) orphelin(s) ? (Une sauvegarde .reg sera créée dans Backups/)",
                                    "Delete {0} orphan service(s)? (A .reg backup will be saved in Backups/)"), selected.Count),
                Loc.T("Confirmation", "Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            int ok = 0, fail = 0;
            string lastErr = "";
            await Task.Run(() =>
            {
                foreach (var svc in selected)
                {
                    string msg;
                    if (SystemToolsManager.DeleteOrphanService(svc, out msg)) ok++;
                    else { fail++; lastErr = msg; }
                }
            });

            await RefreshOrphanServicesAsync();
            RefreshHistoryAndBackups();
            MessageBox.Show(this,
                string.Format(Loc.T("Opération terminée : {0} supprimé(s), {1} échec(s).{2}", "Done: {0} deleted, {1} failed.{2}"),
                    ok, fail, string.IsNullOrWhiteSpace(lastErr) ? "" : "\n\n" + lastErr),
                Loc.T("Services Orphelins", "Orphan Services"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private UIElement BuildContextMenuSubTab()
        {
            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var topDock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = Loc.T("🖱️ Entrées tierces du Menu Clic-Droit de l'Explorateur Windows", "🖱️ Third-Party Windows Explorer Context Menu Entries"),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            _txtContextMenuSummary = new TextBlock
            {
                Text = Loc.T("Les entrées orphelines (exécutable manquant) sont pré-cochées pour un nettoyage sûr avec sauvegarde .reg.",
                             "Orphan entries (missing executable) are pre-checked for safe cleanup with .reg backup."),
                FontSize = 12,
                Foreground = BrushFromHex("#94A3B8"),
                Margin = new Thickness(0, 3, 0, 0)
            };
            stack.Children.Add(_txtContextMenuSummary);
            topDock.Children.Add(stack);

            var btnScan = CreateActionButton(Loc.T("🔍 Scanner le Clic-Droit", "🔍 Scan Context Menu"), "#0284C7", "#FFFFFF");
            btnScan.Click += async (s, e) => await RefreshContextMenuEntriesAsync();
            DockPanel.SetDock(btnScan, Dock.Right);
            topDock.Children.Add(btnScan);

            Grid.SetRow(topDock, 0);
            grid.Children.Add(topDock);

            _gridContextMenu = CreateStyledDataGrid();
            _gridContextMenu.ItemsSource = _contextMenuEntries;
            _gridContextMenu.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = Loc.T("Choix", "Pick"),
                Binding = new Binding("IsSelected") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(55)
            });
            _gridContextMenu.Columns.Add(new DataGridTextColumn { Header = Loc.T("Intitulé", "Label"), Binding = new Binding("Name"), Width = new DataGridLength(170), IsReadOnly = true, ElementStyle = UiTheme.CreateCellTextTooltipStyle("Name") });
            _gridContextMenu.Columns.Add(new DataGridTextColumn { Header = Loc.T("Zone", "Scope"), Binding = new Binding("ScopeLabel"), Width = new DataGridLength(145), IsReadOnly = true });
            _gridContextMenu.Columns.Add(new DataGridTextColumn { Header = Loc.T("État", "Status"), Binding = new Binding("StatusLabel"), Width = new DataGridLength(190), IsReadOnly = true });
            _gridContextMenu.Columns.Add(new DataGridTextColumn { Header = Loc.T("Commande / Clé Registre", "Command / Registry Key"), Binding = new Binding("Command"), Width = new DataGridLength(2.0, DataGridLengthUnitType.Star), MinWidth = 240, IsReadOnly = true, ElementStyle = UiTheme.CreateCellTextTooltipStyle("RegistryKeyPath") });
            Grid.SetRow(_gridContextMenu, 1);
            grid.Children.Add(_gridContextMenu);

            var bot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            _btnCleanContextMenu = CreateActionButton(Loc.T("🗑️ Supprimer les entrées cochées (avec sauvegarde .reg)", "🗑️ Remove Checked Entries (with .reg backup)"), "#DC2626", "#FFFFFF");
            _btnCleanContextMenu.IsEnabled = false;
            _btnCleanContextMenu.Click += async (s, e) => await DeleteSelectedContextMenuEntriesAsync();
            DockPanel.SetDock(_btnCleanContextMenu, Dock.Right);
            bot.Children.Add(_btnCleanContextMenu);
            Grid.SetRow(bot, 2);
            grid.Children.Add(bot);

            return grid;
        }

        private async Task RefreshContextMenuEntriesAsync()
        {
            _txtContextMenuSummary.Text = Loc.T("Analyse des menus contextuels...", "Scanning context menu entries...");
            var items = await Task.Run(() => SystemToolsManager.ScanContextMenuEntries());
            _contextMenuEntries.Clear();
            foreach (var it in items)
            {
                it.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == "IsSelected")
                        _btnCleanContextMenu.IsEnabled = _contextMenuEntries.Any(x => x.IsSelected);
                };
                _contextMenuEntries.Add(it);
            }
            _btnCleanContextMenu.IsEnabled = _contextMenuEntries.Any(x => x.IsSelected);
            int broken = _contextMenuEntries.Count(x => !x.ExistsOnDisk);
            _txtContextMenuSummary.Text = string.Format(
                Loc.T("{0} entrée(s) tierce(s) trouvée(s) dont {1} orpheline(s).", "{0} third-party entry(ies) found ({1} orphan)."),
                _contextMenuEntries.Count, broken);
        }

        private async Task DeleteSelectedContextMenuEntriesAsync()
        {
            var selected = _contextMenuEntries.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0) return;

            if (MessageBox.Show(this,
                string.Format(Loc.T("Supprimer {0} entrée(s) du menu clic-droit ? (Sauvegardées automatiquement dans Backups/)",
                                    "Remove {0} context menu entry(ies)? (Automatically backed up in Backups/)"), selected.Count),
                Loc.T("Confirmation", "Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            int ok = 0, fail = 0;
            string lastErr = "";
            await Task.Run(() =>
            {
                foreach (var entry in selected)
                {
                    string msg;
                    if (SystemToolsManager.RemoveContextMenuEntry(entry, out msg)) ok++;
                    else { fail++; lastErr = msg; }
                }
            });

            await RefreshContextMenuEntriesAsync();
            RefreshHistoryAndBackups();
            MessageBox.Show(this,
                string.Format(Loc.T("Terminé : {0} entrée(s) retirée(s), {1} échec(s).{2}", "Done: {0} removed, {1} failed.{2}"),
                    ok, fail, string.IsNullOrWhiteSpace(lastErr) ? "" : "\n\n" + lastErr),
                Loc.T("Menu Clic-Droit", "Context Menu"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // =========================================================================
        // ONGLET 5 : SNAPSHOT AVANT/APRÈS + HISTORIQUE & RESTAURATION (.REG)
        // =========================================================================
        private UIElement BuildSnapshotAndHistoryTab()
        {
            var tc = new TabControl
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(6)
            };

            tc.Items.Add(CreateTabItem(Loc.T("📸 Snapshot Avant / Après (Install Monitor)", "📸 Before / After Snapshot (Install Monitor)"), BuildSnapshotSubTab()));
            tc.Items.Add(CreateTabItem(Loc.T("🗂️ Historique & Restauration (.reg)", "🗂️ History & .reg Restore"), BuildHistoryAndBackupsSubTab()));

            return tc;
        }

        private UIElement BuildSnapshotSubTab()
        {
            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var card = new Border
            {
                Background = BrushFromHex("#1E293B"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var dock = new DockPanel();
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var btnTakeSnap = CreateActionButton(Loc.T("📸 1. Prendre Snapshot AVANT", "📸 1. Take BEFORE Snapshot"), "#2563EB", "#FFFFFF");
            btnTakeSnap.Margin = new Thickness(0, 0, 8, 0);
            btnTakeSnap.Click += async (s, e) => await OnTakeBeforeSnapshotAsync();

            _btnCompareSnapshot = CreateActionButton(Loc.T("🔎 2. Comparer APRÈS installation", "🔎 2. Compare AFTER Install"), "#0891B2", "#FFFFFF");
            _btnCompareSnapshot.IsEnabled = InstallSnapshotManager.HasSavedSnapshot();
            _btnCompareSnapshot.Click += async (s, e) => await OnCompareAfterSnapshotAsync();

            btnPanel.Children.Add(btnTakeSnap);
            btnPanel.Children.Add(_btnCompareSnapshot);
            DockPanel.SetDock(btnPanel, Dock.Right);
            dock.Children.Add(btnPanel);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            info.Children.Add(new TextBlock
            {
                Text = Loc.T("📸 Surveillance d'installation (Snapshot Avant / Après)", "📸 Install Monitor (Before / After Snapshot)"),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            var existing = InstallSnapshotManager.LoadSavedSnapshot();
            _txtSnapshotStatus = new TextBlock
            {
                Text = existing != null
                    ? string.Format(Loc.T("Snapshot prêt du {0:dd/MM/yyyy HH:mm} ({1} éléments). Installez votre logiciel puis cliquez sur « 2. Comparer APRÈS ».",
                                          "Saved snapshot from {0:yyyy-MM-dd HH:mm} ({1} items). Install your app then click '2. Compare AFTER'."),
                                    existing.CreatedAt, existing.TotalEntries)
                    : Loc.T("1) Prenez un snapshot AVANT d'installer un logiciel.  2) Après test, cliquez sur « Comparer APRÈS » pour voir chaque ajout exact et tout annuler.",
                            "1) Take a snapshot BEFORE installing an app.  2) Afterward, click 'Compare AFTER' to view every exact addition and roll back cleanly."),
                FontSize = 12,
                Foreground = BrushFromHex("#38BDF8"),
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            info.Children.Add(_txtSnapshotStatus);
            dock.Children.Add(info);

            card.Child = dock;
            Grid.SetRow(card, 0);
            grid.Children.Add(card);

            _gridSnapshotDiff = CreateStyledDataGrid();
            _gridSnapshotDiff.ItemsSource = _snapshotDiffItems;
            _gridSnapshotDiff.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = Loc.T("Choix", "Pick"),
                Binding = new Binding("IsSelected") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(55)
            });
            _gridSnapshotDiff.Columns.Add(new DataGridTextColumn { Header = Loc.T("Type", "Type"), Binding = new Binding("TypeLabel"), Width = new DataGridLength(115), IsReadOnly = true });
            _gridSnapshotDiff.Columns.Add(new DataGridTextColumn { Header = Loc.T("Catégorie", "Category"), Binding = new Binding("Category"), Width = new DataGridLength(180), IsReadOnly = true, ElementStyle = UiTheme.CreateCellTextTooltipStyle("Category") });
            _gridSnapshotDiff.Columns.Add(new DataGridTextColumn { Header = Loc.T("Élément ajouté depuis le Snapshot", "Added Since Snapshot"), Binding = new Binding("DisplayPath"), Width = new DataGridLength(2.2, DataGridLengthUnitType.Star), MinWidth = 260, IsReadOnly = true, ElementStyle = UiTheme.CreateCellTextTooltipStyle("FullTooltip") });
            _gridSnapshotDiff.Columns.Add(new DataGridTextColumn { Header = Loc.T("Taille", "Size"), Binding = new Binding("SizeFormatted"), Width = new DataGridLength(90), IsReadOnly = true });
            Grid.SetRow(_gridSnapshotDiff, 1);
            grid.Children.Add(_gridSnapshotDiff);

            var bot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            _btnCleanSnapshotDiff = CreateActionButton(Loc.T("🔥 Supprimer tous les ajouts cochés (Annuler l'installation)", "🔥 Remove Checked Additions (Roll Back Install)"), "#DC2626", "#FFFFFF");
            _btnCleanSnapshotDiff.IsEnabled = false;
            _btnCleanSnapshotDiff.Click += async (s, e) => await CleanSnapshotDiffAsync();
            DockPanel.SetDock(_btnCleanSnapshotDiff, Dock.Right);
            bot.Children.Add(_btnCleanSnapshotDiff);
            Grid.SetRow(bot, 2);
            grid.Children.Add(bot);

            return grid;
        }

        private async Task OnTakeBeforeSnapshotAsync()
        {
            _txtSnapshotStatus.Text = Loc.T("Capture du snapshot système en cours...", "Capturing system snapshot...");
            var snap = await Task.Run(() =>
            {
                var s = InstallSnapshotManager.TakeSnapshot();
                InstallSnapshotManager.SaveSnapshot(s);
                return s;
            });
            _btnCompareSnapshot.IsEnabled = true;
            _snapshotDiffItems.Clear();
            _btnCleanSnapshotDiff.IsEnabled = false;
            _txtSnapshotStatus.Text = string.Format(
                Loc.T("✓ Snapshot enregistré le {0:dd/MM/yyyy HH:mm} ({1} dossiers/clés/tâches indexés). Vous pouvez installer votre logiciel puis cliquer sur « 2. Comparer APRÈS ».",
                      "✓ Snapshot saved at {0:yyyy-MM-dd HH:mm} ({1} folders/keys/tasks indexed). Install your app then click '2. Compare AFTER'."),
                snap.CreatedAt, snap.TotalEntries);
        }

        private async Task OnCompareAfterSnapshotAsync()
        {
            var before = InstallSnapshotManager.LoadSavedSnapshot();
            if (before == null)
            {
                MessageBox.Show(this, Loc.T("Aucun snapshot AVANT trouvé. Cliquez d'abord sur le bouton 1.", "No BEFORE snapshot found. Click button 1 first."), "Snapshot", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _txtSnapshotStatus.Text = Loc.T("Comparaison Avant / Après en cours...", "Comparing Before / After...");
            var diffs = await Task.Run(() =>
            {
                var after = InstallSnapshotManager.TakeSnapshot();
                return InstallSnapshotManager.ComputeDiffAsLeftovers(before, after);
            });

            _snapshotDiffItems.Clear();
            foreach (var d in diffs)
            {
                d.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == "IsSelected")
                        _btnCleanSnapshotDiff.IsEnabled = _snapshotDiffItems.Any(x => x.IsSelected);
                };
                _snapshotDiffItems.Add(d);
            }

            _btnCleanSnapshotDiff.IsEnabled = _snapshotDiffItems.Any(x => x.IsSelected);
            _txtSnapshotStatus.Text = string.Format(
                Loc.T("Comparaison terminée : {0} nouvel(aux) élément(s) détecté(s) depuis le snapshot du {1:dd/MM/yyyy HH:mm}.",
                      "Comparison complete: {0} new item(s) detected since snapshot from {1:yyyy-MM-dd HH:mm}."),
                _snapshotDiffItems.Count, before.CreatedAt);
        }

        private async Task CleanSnapshotDiffAsync()
        {
            var selected = _snapshotDiffItems.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) return;

            if (MessageBox.Show(this,
                string.Format(Loc.T("Supprimer les {0} nouveaux éléments cochés ? (Corbeille + Sauvegarde .reg)",
                                    "Remove {0} checked newly added items? (Recycle Bin + .reg Backup)"), selected.Count),
                "Snapshot Rollback", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            var report = await Task.Run(() => UninstallerEngine.CleanSelectedLeftovers("Snapshot_Rollback", selected, true, true));
            await OnCompareAfterSnapshotAsync();
            RefreshHistoryAndBackups();
            MessageBox.Show(this,
                string.Format(Loc.T("Rollback terminé ! {0} dossier(s), {1} fichier(s), {2} clé(s)/tâche(s) supprimés ({3} libérés).",
                                    "Rollback done! {0} folder(s), {1} file(s), {2} registry/task(s) removed ({3} freed)."),
                    report.DeletedDirectories, report.DeletedFiles, report.DeletedRegistryItems, FormatUtils.FormatBytes(report.FreedBytes)),
                "Snapshot Rollback", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private UIElement BuildHistoryAndBackupsSubTab()
        {
            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // En-tête Historique
            var hDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            _txtHistorySummary = new TextBlock
            {
                Text = Loc.T("🗂️ Historique des nettoyages & Espace total libéré", "🗂️ Cleanup History & Total Space Freed"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = BrushFromHex("#34D399"),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_txtHistorySummary, Dock.Left);
            hDock.Children.Add(_txtHistorySummary);

            var hBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            hBtns.Children.Add(CreateSmallPillButton(Loc.T("🔄 Actualiser", "🔄 Refresh"), (s, e) => RefreshHistoryAndBackups()));
            hBtns.Children.Add(CreateSmallPillButton(Loc.T("Effacer l'historique", "Clear History"), (s, e) =>
            {
                HistoryAndBackupManager.ClearHistory();
                RefreshHistoryAndBackups();
            }));
            DockPanel.SetDock(hBtns, Dock.Right);
            hDock.Children.Add(hBtns);
            Grid.SetRow(hDock, 0);
            grid.Children.Add(hDock);

            _gridHistory = CreateStyledDataGrid();
            _gridHistory.ItemsSource = _historyEntries;
            _gridHistory.Columns.Add(new DataGridTextColumn { Header = Loc.T("Date", "Date"), Binding = new Binding("Timestamp"), Width = new DataGridLength(130), IsReadOnly = true });
            _gridHistory.Columns.Add(new DataGridTextColumn { Header = Loc.T("Application / Opération", "Application / Action"), Binding = new Binding("AppName"), Width = new DataGridLength(1.6, DataGridLengthUnitType.Star), IsReadOnly = true, ElementStyle = UiTheme.CreateCellTextTooltipStyle("AppName") });
            _gridHistory.Columns.Add(new DataGridTextColumn { Header = Loc.T("Détails", "Details"), Binding = new Binding("DetailsSummary"), Width = new DataGridLength(180), IsReadOnly = true });
            _gridHistory.Columns.Add(new DataGridTextColumn { Header = Loc.T("Libéré", "Freed"), Binding = new Binding("FreedFormatted"), Width = new DataGridLength(100), IsReadOnly = true });
            Grid.SetRow(_gridHistory, 1);
            grid.Children.Add(_gridHistory);

            // En-tête Sauvegardes .reg
            var bTitle = new TextBlock
            {
                Text = Loc.T("💾 Sauvegardes Registre (.reg) — Prévisualiser & Restaurer en 1 clic", "💾 Registry Backups (.reg) — Preview & 1-Click Restore"),
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = BrushFromHex("#E2E8F0"),
                Margin = new Thickness(0, 12, 0, 6)
            };
            Grid.SetRow(bTitle, 2);
            grid.Children.Add(bTitle);

            _gridBackups = CreateStyledDataGrid();
            _gridBackups.ItemsSource = _backupFiles;
            _gridBackups.Columns.Add(new DataGridTextColumn { Header = Loc.T("Session / Application", "Session / App"), Binding = new Binding("SessionName"), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star), IsReadOnly = true, ElementStyle = UiTheme.CreateCellTextTooltipStyle("SessionName") });
            _gridBackups.Columns.Add(new DataGridTextColumn { Header = Loc.T("Fichier .reg", ".reg File"), Binding = new Binding("FileName"), Width = new DataGridLength(180), IsReadOnly = true });
            _gridBackups.Columns.Add(new DataGridTextColumn { Header = Loc.T("Date", "Date"), Binding = new Binding("DateFormatted"), Width = new DataGridLength(130), IsReadOnly = true });
            _gridBackups.Columns.Add(new DataGridTextColumn { Header = Loc.T("Taille", "Size"), Binding = new Binding("SizeFormatted"), Width = new DataGridLength(85), IsReadOnly = true });
            Grid.SetRow(_gridBackups, 3);
            grid.Children.Add(_gridBackups);

            var botPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };

            var btnPreview = CreateActionButton(Loc.T("👁️ Prévisualiser .reg", "👁️ Preview .reg"), "#334155", "#E2E8F0");
            btnPreview.Margin = new Thickness(0, 0, 8, 0);
            btnPreview.Click += (s, e) =>
            {
                var b = _gridBackups.SelectedItem as BackupFileInfo;
                if (b == null) return;
                ShowRegPreviewDialog(b);
            };

            var btnRestore = CreateActionButton(Loc.T("♻️ Restaurer dans le Registre", "♻️ Restore to Registry"), "#10B981", "#FFFFFF");
            btnRestore.Margin = new Thickness(0, 0, 8, 0);
            btnRestore.Click += (s, e) =>
            {
                var b = _gridBackups.SelectedItem as BackupFileInfo;
                if (b == null) return;
                if (MessageBox.Show(this,
                    string.Format(Loc.T("Importer « {0} » dans le Registre Windows ?", "Import '{0}' back into Windows Registry?"), b.FileName),
                    Loc.T("Restaurer sauvegarde", "Restore Backup"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    string msg;
                    bool ok = HistoryAndBackupManager.RestoreRegistryBackup(b.FullPath, out msg);
                    MessageBox.Show(this, msg, Loc.T("Restauration", "Restore"), MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
            };

            var btnPurge = CreateActionButton(Loc.T("🗑️ Purger les .reg", "🗑️ Purge .reg"), "#7F1D1D", "#FECACA");
            btnPurge.Click += (s, e) =>
            {
                if (MessageBox.Show(this,
                    Loc.T("Supprimer toutes les sauvegardes .reg dans Backups/ ?", "Delete all .reg backup files in Backups/?"),
                    Loc.T("Purger", "Purge"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    int n = HistoryAndBackupManager.PurgeOldBackups(0);
                    RefreshHistoryAndBackups();
                    MessageBox.Show(this, string.Format(Loc.T("{0} fichier(s) .reg supprimé(s).", "{0} .reg file(s) deleted."), n), "Backups", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            };

            botPanel.Children.Add(btnPreview);
            botPanel.Children.Add(btnRestore);
            botPanel.Children.Add(btnPurge);
            Grid.SetRow(botPanel, 4);
            grid.Children.Add(botPanel);

            return grid;
        }

        private void RefreshHistoryAndBackups()
        {
            if (_gridHistory == null || _gridBackups == null) return;

            var hist = HistoryAndBackupManager.LoadHistory();
            _historyEntries.Clear();
            foreach (var h in hist) _historyEntries.Add(h);

            long totalFreed = hist.Sum(h => h.FreedBytes);
            _txtHistorySummary.Text = string.Format(
                Loc.T("🗂️ Historique : {0} opération(s)   •   Espace total libéré : {1}",
                      "🗂️ History: {0} operation(s)   •   Total space freed: {1}"),
                hist.Count, FormatUtils.FormatBytes(totalFreed));

            var backups = HistoryAndBackupManager.ScanRegistryBackups();
            _backupFiles.Clear();
            foreach (var b in backups) _backupFiles.Add(b);
        }

        private void ShowRegPreviewDialog(BackupFileInfo b)
        {
            var dlg = new Window
            {
                Title = b.FileName,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = BrushFromHex("#0F172A"),
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI")
            };
            UiTheme.ApplyDarkTheme(dlg);
            UiTheme.ApplyAppIcon(dlg);
            UiTheme.ClampWindowToWorkArea(dlg, 720, 500, 520, 360);

            var outer = new Grid();
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var bar = UiTheme.CreateCompactTitleBar(dlg, "APERÇU .REG", b.SessionName + " / " + b.FileName, true);
            Grid.SetRow(bar, 0);
            outer.Children.Add(bar);

            var box = new TextBox
            {
                Text = HistoryAndBackupManager.ReadBackupPreview(b.FullPath),
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Background = BrushFromHex("#090D16"),
                Foreground = BrushFromHex("#A7F3D0"),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(14),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap
            };
            Grid.SetRow(box, 1);
            outer.Children.Add(box);

            dlg.Content = UiTheme.ConfigureCustomChrome(dlg, 46, outer, true);
            dlg.ShowDialog();
        }

        // =========================================================================
        // EXPORTS (RAPPORT HTML + SCRIPT WINGET) & DRAG-AND-DROP & LANGUE FR/EN
        // =========================================================================
        private void OnExportHtmlReportClick(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.T("Exporter le Rapport HTML ClearVestige", "Export ClearVestige HTML Report"),
                Filter = "Rapport HTML (*.html)|*.html",
                FileName = string.Format("ClearVestige_Rapport_{0:yyyyMMdd_HHmm}.html", DateTime.Now)
            };
            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    var hist = HistoryAndBackupManager.LoadHistory();
                    ReportAndExportManager.GenerateHtmlReport(dlg.FileName, _allApps, hist);
                    if (MessageBox.Show(this,
                        Loc.T("Rapport HTML généré avec succès !\n\nVoulez-vous l'ouvrir dans votre navigateur maintenant ?",
                              "HTML report generated successfully!\n\nOpen it in your browser now?"),
                        Loc.T("Rapport HTML", "HTML Report"), MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo { FileName = dlg.FileName, UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Erreur : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void OnExportWingetClick(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.T("Exporter le script de réinstallation Winget", "Export Winget Reinstall Script"),
                Filter = "Script PowerShell (*.ps1)|*.ps1",
                FileName = string.Format("Reinstaller_Applis_Winget_{0:yyyyMMdd}.ps1", DateTime.Now)
            };
            if (dlg.ShowDialog(this) == true)
            {
                _lblStatusFooter.Text = Loc.T("Interrogation de Winget en cours...", "Querying Winget packages...");
                string path = dlg.FileName;
                var snapshotApps = _allApps.ToList();
                string msg = await Task.Run(() =>
                {
                    string m;
                    ReportAndExportManager.ExportWingetReinstallScript(path, snapshotApps, out m);
                    return m;
                });
                _lblStatusFooter.Text = msg;
                MessageBox.Show(this, msg + "\n\n" + path, "Export Winget", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnToggleLanguageClick(object sender, RoutedEventArgs e)
        {
            Loc.ToggleLanguage();
            var next = new MainWindow();
            Application.Current.MainWindow = next;
            next.Show();
            Close();
        }

        private void OnWindowDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private async void OnWindowDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (dropped == null || dropped.Length == 0) return;

            string rawPath = dropped[0];
            string resolved = SystemToolsManager.ResolveShortcutTarget(rawPath);
            string targetDir = File.Exists(resolved) ? Path.GetDirectoryName(resolved) : resolved;
            string exeName = File.Exists(resolved) ? Path.GetFileNameWithoutExtension(resolved) : Path.GetFileName(resolved.TrimEnd('\\', '/'));

            // 1. Chercher une correspondance dans les applications installées
            InstalledApp match = _allApps.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.InstallLocation) &&
                !AppScanner.IsProtectedSystemDirectory(a.InstallLocation) &&
                (string.Equals(a.InstallLocation.TrimEnd('\\'), targetDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                 targetDir.StartsWith(a.InstallLocation.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
                ?? _allApps.FirstOrDefault(a =>
                    !string.IsNullOrWhiteSpace(exeName) && exeName.Length >= 4 &&
                    !string.IsNullOrWhiteSpace(a.DisplayName) &&
                    a.DisplayName.IndexOf(exeName, StringComparison.OrdinalIgnoreCase) >= 0);

            if (match != null)
            {
                var wizard = new UninstallWizardWindow(match, _allApps, false, false) { Owner = this };
                wizard.ShowDialog();
                if (wizard.WasCleanedOrUninstalled)
                {
                    await RefreshAllDataAsync();
                }
                return;
            }

            // 2. Sinon basculer sur l'onglet Orphelins (Traque Ciblée) et lancer automatiquement le scan
            if (_mainTabControl != null && _mainTabControl.Items.Count > 1)
                _mainTabControl.SelectedIndex = 1;

            _txtForcedName.Text = exeName ?? "";
            _txtForcedPath.Text = targetDir ?? "";
            await RunForcedScanAsync();
        }
    }
}
