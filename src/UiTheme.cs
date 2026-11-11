using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace DeepUninstaller
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
        /// Icône de l'application (ressource intégrée « DeepUninstaller.app.ico »), ou null si absente.
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
                        using (var stream = asm.GetManifestResourceStream("DeepUninstaller.app.ico"))
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
    <!-- STYLE DATAGRID, EN-TÊTES, LIGNES ET CELLULES                        -->
    <!-- =================================================================== -->
    <Style TargetType='{x:Type DataGridColumnHeader}'>
        <Setter Property='Background' Value='#0B1120'/>
        <Setter Property='Foreground' Value='#94A3B8'/>
        <Setter Property='FontWeight' Value='SemiBold'/>
        <Setter Property='FontSize' Value='12'/>
        <Setter Property='Padding' Value='12,9'/>
        <Setter Property='BorderBrush' Value='#26354D'/>
        <Setter Property='BorderThickness' Value='0,0,1,1'/>
        <Setter Property='Cursor' Value='Hand'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='{x:Type DataGridColumnHeader}'>
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
