using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;

namespace ClearVestige
{
    public static class UiTheme
    {
        private static ResourceDictionary _darkDictionary;

        public static SolidColorBrush BrushFromHex(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }

        private static ImageSource _appIcon;
        private static bool _appIconLoaded;

        /// <summary>
        /// Icône de l'application (ressource intégrée « ClearVestige.app.ico »), ou null si absente.
        /// </summary>
        public static ImageSource AppIcon
        {
            get
            {
                if (!_appIconLoaded)
                {
                    _appIconLoaded = true;
                    try
                    {
                        var asm = System.Reflection.Assembly.GetExecutingAssembly();
                        using (var stream = asm.GetManifestResourceStream("ClearVestige.app.ico"))
                        {
                            if (stream != null)
                            {
                                var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(
                                    stream,
                                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                                System.Windows.Media.Imaging.BitmapFrame best = null;
                                foreach (var frame in decoder.Frames)
                                {
                                    if (best == null || frame.PixelWidth > best.PixelWidth) best = frame;
                                }
                                if (best != null && best.CanFreeze) best.Freeze();
                                _appIcon = best;
                            }
                        }
                    }
                    catch { _appIcon = null; }
                }
                return _appIcon;
            }
        }

        public static void ApplyAppIcon(Window window)
        {
            if (window == null) return;
            var icon = AppIcon;
            if (icon != null) window.Icon = icon;
        }

        /// <summary>
        /// Adapte automatiquement les dimensions d'une fenêtre à la zone utile réelle de l'écran
        /// (prend en compte la barre des tâches et le zoom DPI 125%/150%/175% des écrans portables).
        /// </summary>
        public static void ClampWindowToWorkArea(
            Window window,
            double preferredWidth,
            double preferredHeight,
            double minWidth,
            double minHeight)
        {
            if (window == null) return;
            Rect workArea = SystemParameters.WorkArea;
            double maxW = Math.Max(640, workArea.Width - 28);
            double maxH = Math.Max(480, workArea.Height - 28);

            window.MinWidth = Math.Min(minWidth, Math.Max(640, workArea.Width * 0.78));
            window.MinHeight = Math.Min(minHeight, Math.Max(460, workArea.Height * 0.72));
            window.Width = Math.Min(preferredWidth, maxW);
            window.Height = Math.Min(preferredHeight, maxH);

            // Garantir après affichage que la fenêtre reste intégralement visible dans la zone utile
            window.Loaded += (s, e) =>
            {
                if (window.WindowState != WindowState.Normal) return;
                Rect wa = SystemParameters.WorkArea;
                if (window.Width > wa.Width - 16) window.Width = wa.Width - 16;
                if (window.Height > wa.Height - 16) window.Height = wa.Height - 16;
                if (window.Left < wa.Left) window.Left = wa.Left + 8;
                if (window.Top < wa.Top) window.Top = wa.Top + 8;
                if (window.Left + window.Width > wa.Right)
                    window.Left = Math.Max(wa.Left, wa.Left + (wa.Width - window.Width) / 2.0);
                if (window.Top + window.Height > wa.Bottom)
                    window.Top = Math.Max(wa.Top, wa.Top + (wa.Height - window.Height) / 2.0);
            };
        }

        /// <summary>
        /// Crée un style d'élément texte DataGrid avec info-bulle complète au survol (utile pour les chemins longs).
        /// </summary>
        public static Style CreateCellTextTooltipStyle(string tooltipBindingPath)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            if (!string.IsNullOrWhiteSpace(tooltipBindingPath))
            {
                style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(tooltipBindingPath)));
            }
            return style;
        }

        /// <summary>
        /// Crée une colonne de cases à cocher réactive en 1 seul clic (remplace DataGridCheckBoxColumn qui exige 2 clics en WPF).
        /// </summary>
        public static DataGridTemplateColumn CreateSingleClickCheckBoxColumn(string header, string bindingProperty, double width)
        {
            string xaml = string.Format(
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                "<CheckBox IsChecked='{{Binding {0}, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}}' " +
                "HorizontalAlignment='Center' VerticalAlignment='Center' Cursor='Hand' Focusable='False'/>" +
                "</DataTemplate>",
                bindingProperty);

            return new DataGridTemplateColumn
            {
                Header = header,
                CellTemplate = (DataTemplate)XamlReader.Parse(xaml),
                SortMemberPath = bindingProperty,
                Width = new DataGridLength(width),
                CanUserResize = false
            };
        }

        /// <summary>
        /// Indique si un clic souris provient d'une CheckBox (pour éviter qu'un double-clic rapide sur des cases lance le double-clic de la ligne).
        /// </summary>
        public static bool IsClickInsideCheckBox(object originalSource)
        {
            var dep = originalSource as DependencyObject;
            while (dep != null)
            {
                if (dep is CheckBox) return true;
                dep = VisualTreeHelper.GetParent(dep);
            }
            return false;
        }

        /// <summary>
        /// Configure une barre de titre sombre personnalisée (style SysPulse) via WPF WindowChrome
        /// tout en conservant le redimensionnement, Aero Snap et le comportement Maximisé natif Windows.
        /// </summary>
        public static Border ConfigureCustomChrome(Window window, double captionHeight, UIElement innerContent, bool allowResize)
        {
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = allowResize ? ResizeMode.CanResize : ResizeMode.NoResize;

            var chrome = new WindowChrome
            {
                CaptionHeight = captionHeight,
                ResizeBorderThickness = allowResize ? new Thickness(6) : new Thickness(0),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(window, chrome);

            var frameBorder = new Border
            {
                Background = BrushFromHex("#0F172A"),
                BorderBrush = BrushFromHex("#1E293B"),
                BorderThickness = new Thickness(1),
                Child = innerContent
            };

            window.StateChanged += (s, e) =>
            {
                if (window.WindowState == WindowState.Maximized)
                {
                    frameBorder.Margin = new Thickness(7);
                    frameBorder.BorderThickness = new Thickness(0);
                }
                else
                {
                    frameBorder.Margin = new Thickness(0);
                    frameBorder.BorderThickness = new Thickness(1);
                }
            };

            return frameBorder;
        }

        /// <summary>
        /// Crée le séparateur vertical et les boutons natifs Réduire / Agrandir / Fermer (style SysPulse).
        /// </summary>
        public static StackPanel CreateWindowCaptionControls(Window window, bool allowMaximize)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Séparateur vertical subtil avant les contrôles de fenêtre
            var divider = new Border
            {
                Width = 1,
                Height = 22,
                Background = BrushFromHex("#26354D"),
                Margin = new Thickness(10, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(divider);

            var btnMin = CreateChromeButton("\u2500", "Réduire", false);
            btnMin.FontSize = 11;
            btnMin.Click += (s, e) => window.WindowState = WindowState.Minimized;
            panel.Children.Add(btnMin);

            if (allowMaximize)
            {
                var btnMax = CreateChromeButton("\u25A1", "Agrandir / Restaurer", false);
                btnMax.FontSize = 12;
                btnMax.Click += (s, e) =>
                {
                    window.WindowState = window.WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                };
                window.StateChanged += (s, e) =>
                {
                    btnMax.Content = window.WindowState == WindowState.Maximized ? "\u2750" : "\u25A1";
                };
                panel.Children.Add(btnMax);
            }

            var btnClose = CreateChromeButton("\u2715", "Fermer", true);
            btnClose.FontSize = 12;
            btnClose.Click += (s, e) => window.Close();
            panel.Children.Add(btnClose);

            return panel;
        }

        private static Button CreateChromeButton(string glyph, string tooltip, bool isClose)
        {
            var btn = new Button
            {
                Content = glyph,
                Width = 34,
                Height = 30,
                Margin = new Thickness(2, 0, 0, 0),
                ToolTip = tooltip,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            WindowChrome.SetIsHitTestVisibleInChrome(btn, true);

            if (_darkDictionary != null)
            {
                string key = isClose ? "ChromeCloseButton" : "ChromeCaptionButton";
                if (_darkDictionary.Contains(key))
                {
                    btn.Style = (Style)_darkDictionary[key];
                }
            }
            return btn;
        }

        /// <summary>
        /// Barre de titre sombre compacte (44 px) pour les fenêtres secondaires et assistants.
        /// </summary>
        public static Border CreateCompactTitleBar(Window window, string titleText, string subtitleText, bool allowMaximize)
        {
            var bar = new Border
            {
                Background = BrushFromHex("#0B1120"),
                BorderBrush = BrushFromHex("#1E293B"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 0, 10, 0),
                Height = 46
            };

            var dock = new DockPanel { LastChildFill = true };

            var rightControls = CreateWindowCaptionControls(window, allowMaximize);
            DockPanel.SetDock(rightControls, Dock.Right);
            dock.Children.Add(rightControls);

            var leftStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (AppIcon != null)
            {
                var ico = new Image
                {
                    Source = AppIcon,
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                RenderOptions.SetBitmapScalingMode(ico, BitmapScalingMode.HighQuality);
                leftStack.Children.Add(ico);
            }

            var textStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = titleText,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });

            if (!string.IsNullOrWhiteSpace(subtitleText))
            {
                textStack.Children.Add(new TextBlock
                {
                    Text = "  •  " + subtitleText,
                    FontSize = 11.5,
                    Foreground = BrushFromHex("#94A3B8"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            leftStack.Children.Add(textStack);
            dock.Children.Add(leftStack);

            bar.Child = dock;
            return bar;
        }

        public static void ApplyDarkTheme(FrameworkElement target)
        {
            if (_darkDictionary == null)
            {
                _darkDictionary = LoadDarkResourceDictionary();
            }

            if (target != null && !target.Resources.MergedDictionaries.Contains(_darkDictionary))
            {
                target.Resources.MergedDictionaries.Add(_darkDictionary);
            }
        }

        private static ResourceDictionary LoadDarkResourceDictionary()
        {
            const string xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

    <!-- =================================================================== -->
    <!-- STYLE GLOBAL BOUTON (Plus jamais de fond blanc en désactivé/survol) -->
    <!-- =================================================================== -->
    <Style TargetType='{x:Type Button}'>
        <Setter Property='Background' Value='#334155'/>
        <Setter Property='Foreground' Value='#F8FAFC'/>
        <Setter Property='BorderBrush' Value='#475569'/>
        <Setter Property='BorderThickness' Value='1'/>
        <Setter Property='Padding' Value='14,8'/>
        <Setter Property='FontSize' Value='12.5'/>
        <Setter Property='FontWeight' Value='SemiBold'/>
        <Setter Property='Cursor' Value='Hand'/>
        <Setter Property='SnapsToDevicePixels' Value='True'/>
        <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='{x:Type Button}'>
                    <Border x:Name='btnBorder'
                            Background='{TemplateBinding Background}'
                            BorderBrush='{TemplateBinding BorderBrush}'
                            BorderThickness='{TemplateBinding BorderThickness}'
                            CornerRadius='6'
                            Padding='{TemplateBinding Padding}'
                            SnapsToDevicePixels='True'>
                        <ContentPresenter x:Name='contentPresenter'
                                          HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'
                                          VerticalAlignment='{TemplateBinding VerticalContentAlignment}'
                                          RecognizesAccessKey='True'
                                          TextElement.Foreground='{TemplateBinding Foreground}'/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'>
                            <Setter TargetName='btnBorder' Property='BorderBrush' Value='#93C5FD'/>
                            <Setter TargetName='btnBorder' Property='Opacity' Value='0.92'/>
                        </Trigger>
                        <Trigger Property='IsPressed' Value='True'>
                            <Setter TargetName='btnBorder' Property='Opacity' Value='0.78'/>
                        </Trigger>
                        <Trigger Property='IsEnabled' Value='False'>
                            <Setter TargetName='btnBorder' Property='Background' Value='#1E293B'/>
                            <Setter TargetName='btnBorder' Property='BorderBrush' Value='#334155'/>
                            <Setter TargetName='btnBorder' Property='BorderThickness' Value='1'/>
                            <Setter TargetName='contentPresenter' Property='Opacity' Value='0.55'/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- =================================================================== -->
    <!-- STYLE GLOBAL TABCONTROL & TABITEM (Onglets sombres modernes)        -->
    <!-- =================================================================== -->
    <Style TargetType='{x:Type TabControl}'>
        <Setter Property='Background' Value='#0F172A'/>
        <Setter Property='BorderBrush' Value='#26354D'/>
        <Setter Property='BorderThickness' Value='1'/>
        <Setter Property='Padding' Value='0'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='{x:Type TabControl}'>
                    <Grid ClipToBounds='True' SnapsToDevicePixels='True' KeyboardNavigation.TabNavigation='Local'>
                        <Grid.RowDefinitions>
                            <RowDefinition Height='Auto'/>
                            <RowDefinition Height='*'/>
                        </Grid.RowDefinitions>
                        <TabPanel x:Name='HeaderPanel'
                                  Grid.Row='0'
                                  Panel.ZIndex='1'
                                  Margin='0,0,0,8'
                                  IsItemsHost='True'
                                  KeyboardNavigation.TabIndex='1'
                                  Background='Transparent'/>
                        <Border x:Name='ContentPanel'
                                Grid.Row='1'
                                Background='#111A2E'
                                BorderBrush='#26354D'
                                BorderThickness='1'
                                CornerRadius='8'
                                KeyboardNavigation.TabNavigation='Local'
                                KeyboardNavigation.DirectionalNavigation='Contained'
                                KeyboardNavigation.TabIndex='2'>
                            <ContentPresenter x:Name='PART_SelectedContentHost'
                                              ContentSource='SelectedContent'
                                              Margin='{TemplateBinding Padding}'
                                              SnapsToDevicePixels='{TemplateBinding SnapsToDevicePixels}'/>
                        </Border>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType='{x:Type TabItem}'>
        <Setter Property='Foreground' Value='#94A3B8'/>
        <Setter Property='Background' Value='#1E293B'/>
        <Setter Property='BorderBrush' Value='#334155'/>
        <Setter Property='BorderThickness' Value='1'/>
        <Setter Property='Margin' Value='0,0,8,0'/>
        <Setter Property='Padding' Value='16,9'/>
        <Setter Property='FontSize' Value='13'/>
        <Setter Property='FontWeight' Value='SemiBold'/>
        <Setter Property='Cursor' Value='Hand'/>
        <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='{x:Type TabItem}'>
                    <Border x:Name='tabBorder'
                            Background='{TemplateBinding Background}'
                            BorderBrush='{TemplateBinding BorderBrush}'
                            BorderThickness='{TemplateBinding BorderThickness}'
                            CornerRadius='7'
                            Padding='{TemplateBinding Padding}'
                            Margin='{TemplateBinding Margin}'
                            SnapsToDevicePixels='True'>
                        <ContentPresenter x:Name='ContentSite'
                                          ContentSource='Header'
                                          HorizontalAlignment='Center'
                                          VerticalAlignment='Center'
                                          RecognizesAccessKey='True'
                                          TextElement.Foreground='{TemplateBinding Foreground}'/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'>
                            <Setter TargetName='tabBorder' Property='Background' Value='#26354D'/>
                            <Setter TargetName='tabBorder' Property='BorderBrush' Value='#475569'/>
                            <Setter Property='Foreground' Value='#F1F5F9'/>
                        </Trigger>
                        <Trigger Property='IsSelected' Value='True'>
                            <Setter TargetName='tabBorder' Property='Background' Value='#2563EB'/>
                            <Setter TargetName='tabBorder' Property='BorderBrush' Value='#60A5FA'/>
                            <Setter Property='Foreground' Value='#FFFFFF'/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- =================================================================== -->
    <!-- STYLE CHECKBOX & RADIOBUTTON SOMBRES                                -->
    <!-- =================================================================== -->
    <Style TargetType='{x:Type CheckBox}'>
        <Setter Property='Foreground' Value='#E2E8F0'/>
        <Setter Property='FontSize' Value='12.5'/>
        <Setter Property='Cursor' Value='Hand'/>
        <Setter Property='VerticalContentAlignment' Value='Center'/>
    </Style>

    <Style TargetType='{x:Type RadioButton}'>
        <Setter Property='Foreground' Value='#E2E8F0'/>
        <Setter Property='FontSize' Value='12.5'/>
        <Setter Property='Cursor' Value='Hand'/>
        <Setter Property='VerticalContentAlignment' Value='Center'/>
    </Style>

    <!-- =================================================================== -->
    <!-- STYLE DATAGRID, EN-TÊTES REDIMENSIONNABLES, LIGNES ET CELLULES      -->
    <!-- =================================================================== -->
    <Style TargetType='{x:Type DataGrid}'>
        <Setter Property='CanUserResizeColumns' Value='True'/>
        <Setter Property='CanUserReorderColumns' Value='True'/>
        <Setter Property='HorizontalScrollBarVisibility' Value='Auto'/>
        <Setter Property='VerticalScrollBarVisibility' Value='Auto'/>
    </Style>

    <Style x:Key='ColumnHeaderGripperStyle' TargetType='{x:Type Thumb}'>
        <Setter Property='Width' Value='8'/>
        <Setter Property='Background' Value='Transparent'/>
        <Setter Property='Cursor' Value='SizeWE'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='{x:Type Thumb}'>
                    <Border Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
                        <Border x:Name='gripLine' Width='2' HorizontalAlignment='Center' Background='Transparent'/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'>
                            <Setter TargetName='gripLine' Property='Background' Value='#38BDF8'/>
                        </Trigger>
                        <Trigger Property='IsDragging' Value='True'>
                            <Setter TargetName='gripLine' Property='Background' Value='#60A5FA'/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType='{x:Type DataGridColumnHeader}'>
        <Setter Property='Background' Value='#0B1120'/>
        <Setter Property='Foreground' Value='#94A3B8'/>
        <Setter Property='FontWeight' Value='SemiBold'/>
        <Setter Property='FontSize' Value='12'/>
        <Setter Property='Padding' Value='10,9'/>
        <Setter Property='BorderBrush' Value='#26354D'/>
        <Setter Property='BorderThickness' Value='0,0,1,1'/>
        <Setter Property='Cursor' Value='Hand'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='{x:Type DataGridColumnHeader}'>
                    <Grid>
                        <Border x:Name='hdrBorder'
                                Background='{TemplateBinding Background}'
                                BorderBrush='{TemplateBinding BorderBrush}'
                                BorderThickness='{TemplateBinding BorderThickness}'
                                Padding='{TemplateBinding Padding}'
                                SnapsToDevicePixels='True'>
                            <DockPanel>
                                <TextBlock x:Name='sortArrow'
                                           DockPanel.Dock='Right'
                                           Text=''
                                           Foreground='#38BDF8'
                                           FontWeight='Bold'
                                           Margin='6,0,0,0'
                                           VerticalAlignment='Center'
                                           Visibility='Collapsed'/>
                                <ContentPresenter VerticalAlignment='Center'
                                                  SnapsToDevicePixels='{TemplateBinding SnapsToDevicePixels}'
                                                  TextElement.Foreground='{TemplateBinding Foreground}'/>
                            </DockPanel>
                        </Border>
                        <Thumb x:Name='PART_LeftHeaderGripper'
                               HorizontalAlignment='Left'
                               Style='{StaticResource ColumnHeaderGripperStyle}'/>
                        <Thumb x:Name='PART_RightHeaderGripper'
                               HorizontalAlignment='Right'
                               Style='{StaticResource ColumnHeaderGripperStyle}'/>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'>
                            <Setter TargetName='hdrBorder' Property='Background' Value='#1E293B'/>
                            <Setter Property='Foreground' Value='#F8FAFC'/>
                        </Trigger>
                        <Trigger Property='SortDirection' Value='Ascending'>
                            <Setter TargetName='sortArrow' Property='Text' Value='▲'/>
                            <Setter TargetName='sortArrow' Property='Visibility' Value='Visible'/>
                            <Setter Property='Foreground' Value='#38BDF8'/>
                        </Trigger>
                        <Trigger Property='SortDirection' Value='Descending'>
                            <Setter TargetName='sortArrow' Property='Text' Value='▼'/>
                            <Setter TargetName='sortArrow' Property='Visibility' Value='Visible'/>
                            <Setter Property='Foreground' Value='#38BDF8'/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType='{x:Type DataGridRow}'>
        <Setter Property='Background' Value='#162032'/>
        <Setter Property='Foreground' Value='#E2E8F0'/>
        <Setter Property='BorderThickness' Value='0'/>
        <Setter Property='SnapsToDevicePixels' Value='True'/>
        <Style.Triggers>
            <Trigger Property='ItemsControl.AlternationIndex' Value='1'>
                <Setter Property='Background' Value='#1A263B'/>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'>
                <Setter Property='Background' Value='#26354D'/>
                <Setter Property='Foreground' Value='#FFFFFF'/>
            </Trigger>
            <Trigger Property='IsSelected' Value='True'>
                <Setter Property='Background' Value='#1D4ED8'/>
                <Setter Property='Foreground' Value='#FFFFFF'/>
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style TargetType='{x:Type DataGridCell}'>
        <Setter Property='Background' Value='Transparent'/>
        <Setter Property='Foreground' Value='#E2E8F0'/>
        <Setter Property='BorderThickness' Value='0'/>
        <Setter Property='Padding' Value='10,7'/>
        <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='{x:Type DataGridCell}'>
                    <Border x:Name='cellBorder'
                            Background='{TemplateBinding Background}'
                            BorderThickness='0'
                            Padding='{TemplateBinding Padding}'
                            SnapsToDevicePixels='True'>
                        <ContentPresenter VerticalAlignment='Center'
                                          SnapsToDevicePixels='{TemplateBinding SnapsToDevicePixels}'
                                          TextElement.Foreground='{TemplateBinding Foreground}'/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property='IsSelected' Value='True'>
                            <Setter TargetName='cellBorder' Property='Background' Value='#1D4ED8'/>
                            <Setter Property='Foreground' Value='#FFFFFF'/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- =================================================================== -->
    <!-- STYLE MENU CONTEXTUEL & INFOBULLES (TOOLTIP)                        -->
    <!-- =================================================================== -->
    <Style TargetType='{x:Type ContextMenu}'>
        <Setter Property='Background' Value='#1E293B'/>
        <Setter Property='Foreground' Value='#F8FAFC'/>
        <Setter Property='BorderBrush' Value='#475569'/>
        <Setter Property='BorderThickness' Value='1'/>
        <Setter Property='Padding' Value='4'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='{x:Type ContextMenu}'>
                    <Border Background='#1E293B'
                            BorderBrush='#475569'
                            BorderThickness='1'
                            CornerRadius='6'
                            Padding='4'
                            SnapsToDevicePixels='True'>
                        <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType='{x:Type MenuItem}'>
        <Setter Property='Background' Value='Transparent'/>
        <Setter Property='Foreground' Value='#F1F5F9'/>
        <Setter Property='Padding' Value='12,7'/>
        <Setter Property='FontSize' Value='12.5'/>
        <Setter Property='Cursor' Value='Hand'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='{x:Type MenuItem}'>
                    <Border x:Name='menuBorder'
                            Background='{TemplateBinding Background}'
                            CornerRadius='4'
                            Padding='{TemplateBinding Padding}'
                            SnapsToDevicePixels='True'>
                        <ContentPresenter ContentSource='Header'
                                          VerticalAlignment='Center'
                                          RecognizesAccessKey='True'
                                          TextElement.Foreground='{TemplateBinding Foreground}'/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property='IsHighlighted' Value='True'>
                            <Setter TargetName='menuBorder' Property='Background' Value='#2563EB'/>
                            <Setter Property='Foreground' Value='#FFFFFF'/>
                        </Trigger>
                        <Trigger Property='IsEnabled' Value='False'>
                            <Setter Property='Foreground' Value='#64748B'/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType='{x:Type ToolTip}'>
        <Setter Property='Background' Value='#0F172A'/>
        <Setter Property='Foreground' Value='#F8FAFC'/>
        <Setter Property='BorderBrush' Value='#475569'/>
        <Setter Property='BorderThickness' Value='1'/>
        <Setter Property='Padding' Value='10,6'/>
        <Setter Property='FontSize' Value='12'/>
    </Style>

    <!-- =================================================================== -->
    <!-- STYLE BARRE DE DÉFILEMENT (SCROLLBAR) SOMBRE                        -->
    <!-- =================================================================== -->
    <Style x:Key='DarkScrollThumb' TargetType='{x:Type Thumb}'>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='{x:Type Thumb}'>
                    <Border x:Name='thumbBorder'
                            Background='#334155'
                            CornerRadius='4'
                            Margin='2'/>
                    <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'>
                            <Setter TargetName='thumbBorder' Property='Background' Value='#475569'/>
                        </Trigger>
                        <Trigger Property='IsDragging' Value='True'>
                            <Setter TargetName='thumbBorder' Property='Background' Value='#64748B'/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key='ChromeCaptionButton' TargetType='{x:Type Button}'>
        <Setter Property='Background' Value='Transparent'/>
        <Setter Property='Foreground' Value='#94A3B8'/>
        <Setter Property='BorderThickness' Value='0'/>
        <Setter Property='Cursor' Value='Hand'/>
        <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='{x:Type Button}'>
                    <Border x:Name='bdr'
                            Background='{TemplateBinding Background}'
                            CornerRadius='6'
                            SnapsToDevicePixels='True'>
                        <ContentPresenter HorizontalAlignment='Center'
                                          VerticalAlignment='Center'
                                          TextElement.Foreground='{TemplateBinding Foreground}'/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'>
                            <Setter TargetName='bdr' Property='Background' Value='#1E293B'/>
                            <Setter Property='Foreground' Value='#F8FAFC'/>
                        </Trigger>
                        <Trigger Property='IsPressed' Value='True'>
                            <Setter TargetName='bdr' Property='Background' Value='#334155'/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key='ChromeCloseButton' TargetType='{x:Type Button}'>
        <Setter Property='Background' Value='Transparent'/>
        <Setter Property='Foreground' Value='#94A3B8'/>
        <Setter Property='BorderThickness' Value='0'/>
        <Setter Property='Cursor' Value='Hand'/>
        <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='{x:Type Button}'>
                    <Border x:Name='bdr'
                            Background='{TemplateBinding Background}'
                            CornerRadius='6'
                            SnapsToDevicePixels='True'>
                        <ContentPresenter HorizontalAlignment='Center'
                                          VerticalAlignment='Center'
                                          TextElement.Foreground='{TemplateBinding Foreground}'/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'>
                            <Setter TargetName='bdr' Property='Background' Value='#DC2626'/>
                            <Setter Property='Foreground' Value='#FFFFFF'/>
                        </Trigger>
                        <Trigger Property='IsPressed' Value='True'>
                            <Setter TargetName='bdr' Property='Background' Value='#B91C1C'/>
                            <Setter Property='Foreground' Value='#FFFFFF'/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType='{x:Type ScrollBar}'>
        <Setter Property='Background' Value='#0F172A'/>
        <Setter Property='Width' Value='10'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='{x:Type ScrollBar}'>
                    <Grid Background='#0B1120'>
                        <Track x:Name='PART_Track' IsDirectionReversed='True'>
                            <Track.Thumb>
                                <Thumb Style='{StaticResource DarkScrollThumb}'/>
                            </Track.Thumb>
                        </Track>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property='Orientation' Value='Horizontal'>
                <Setter Property='Width' Value='Auto'/>
                <Setter Property='Height' Value='10'/>
                <Setter Property='Template'>
                    <Setter.Value>
                        <ControlTemplate TargetType='{x:Type ScrollBar}'>
                            <Grid Background='#0B1120'>
                                <Track x:Name='PART_Track' IsDirectionReversed='False'>
                                    <Track.Thumb>
                                        <Thumb Style='{StaticResource DarkScrollThumb}'/>
                                    </Track.Thumb>
                                </Track>
                            </Grid>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Trigger>
        </Style.Triggers>
    </Style>

</ResourceDictionary>";

            var dict = (ResourceDictionary)XamlReader.Parse(xaml);
            dict[SystemColors.HighlightBrushKey] = BrushFromHex("#1D4ED8");
            dict[SystemColors.HighlightTextBrushKey] = BrushFromHex("#FFFFFF");
            dict[SystemColors.InactiveSelectionHighlightBrushKey] = BrushFromHex("#1E3A8A");
            dict[SystemColors.InactiveSelectionHighlightTextBrushKey] = BrushFromHex("#F8FAFC");
            dict[SystemColors.ControlBrushKey] = BrushFromHex("#1E293B");
            dict[SystemColors.ControlTextBrushKey] = BrushFromHex("#F8FAFC");
            dict[SystemColors.WindowBrushKey] = BrushFromHex("#0F172A");
            dict[SystemColors.WindowTextBrushKey] = BrushFromHex("#F8FAFC");
            return dict;
        }
    }
}
