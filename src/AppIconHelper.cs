using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClearVestige
{
    /// <summary>
    /// Extrait de façon asynchrone, sûre et mise en cache les vraies icônes Windows (.exe, .ico, index DisplayIcon, AppX PNG)
    /// sans fuite GDI (DestroyIcon / DeleteObject systématiques).
    /// </summary>
    public static class AppIconHelper
    {
        private static readonly ConcurrentDictionary<string, ImageSource> _cache =
            new ConcurrentDictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        private static ImageSource _defaultAppIcon;

        public static ImageSource DefaultAppIcon
        {
            get
            {
                if (_defaultAppIcon == null)
                {
                    try
                    {
                        using (var sysIcon = SystemIcons.Application)
                        {
                            _defaultAppIcon = ConvertIconToFrozenImageSource(sysIcon.Handle);
                        }
                    }
                    catch { }
                }
                return _defaultAppIcon;
            }
        }

        public static ImageSource ResolveIconForApp(InstalledApp app)
        {
            if (app == null) return DefaultAppIcon;

            string cacheKey = (app.DisplayIcon ?? "") + "|" + (app.InstallLocation ?? "");
            ImageSource cached;
            if (_cache.TryGetValue(cacheKey, out cached))
                return cached ?? DefaultAppIcon;

            ImageSource resolved = null;

            // 1. Essayer DisplayIcon (ex: "C:\Path\app.exe,0" ou "C:\Path\icon.ico")
            if (!string.IsNullOrWhiteSpace(app.DisplayIcon))
            {
                resolved = TryLoadFromDisplayIconString(app.DisplayIcon);
            }

            // 2. Pour une appli Windows Store / AppX, chercher un logo PNG dans son dossier
            if (resolved == null && app.Scope == AppScope.WindowsStore && !string.IsNullOrWhiteSpace(app.InstallLocation))
            {
                resolved = TryLoadStoreAppLogo(app.InstallLocation);
            }

            // 3. Sinon, prendre l'exécutable dans UninstallString ou le premier .exe dans InstallLocation
            if (resolved == null && !string.IsNullOrWhiteSpace(app.InstallLocation) &&
                Directory.Exists(app.InstallLocation) &&
                !AppScanner.IsProtectedSystemDirectory(app.InstallLocation))
            {
                try
                {
                    var exeFile = Directory.GetFiles(app.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault(f =>
                        {
                            string fn = Path.GetFileName(f);
                            return fn.IndexOf("unins", StringComparison.OrdinalIgnoreCase) < 0 &&
                                   fn.IndexOf("crash", StringComparison.OrdinalIgnoreCase) < 0 &&
                                   fn.IndexOf("update", StringComparison.OrdinalIgnoreCase) < 0;
                        })
                        ?? Directory.GetFiles(app.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(exeFile))
                    {
                        resolved = TryExtractFromFile(exeFile, 0);
                    }
                }
                catch { }
            }

            if (resolved == null && !string.IsNullOrWhiteSpace(app.UninstallString))
            {
                string exe = AppScanner.ExtractExecutablePath(app.UninstallString);
                if (!string.IsNullOrWhiteSpace(exe) &&
                    File.Exists(exe) &&
                    exe.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) < 0 &&
                    exe.IndexOf("powershell", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    resolved = TryExtractFromFile(exe, 0);
                }
            }

            var finalIcon = resolved ?? DefaultAppIcon;
            _cache[cacheKey] = finalIcon;
            return finalIcon;
        }

        private static ImageSource TryLoadFromDisplayIconString(string raw)
        {
            try
            {
                string trimmed = Environment.ExpandEnvironmentVariables(raw.Trim());
                int iconIndex = 0;
                string filePath = trimmed;

                // Séparer l'index éventuel ",0" ou ",-101"
                int lastComma = trimmed.LastIndexOf(',');
                if (lastComma > 2)
                {
                    string afterComma = trimmed.Substring(lastComma + 1).Trim().Trim('"');
                    int parsedIdx;
                    if (int.TryParse(afterComma, out parsedIdx))
                    {
                        iconIndex = parsedIdx;
                        filePath = trimmed.Substring(0, lastComma).Trim();
                    }
                }

                filePath = filePath.Trim().Trim('"');
                if (!File.Exists(filePath)) return null;

                if (filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                {
                    return LoadFrozenBitmap(filePath);
                }

                return TryExtractFromFile(filePath, iconIndex);
            }
            catch
            {
                return null;
            }
        }

        private static ImageSource TryLoadStoreAppLogo(string installDir)
        {
            try
            {
                if (!Directory.Exists(installDir)) return null;
                string assetsDir = Path.Combine(installDir, "Assets");
                string searchRoot = Directory.Exists(assetsDir) ? assetsDir : installDir;

                var pngs = Directory.GetFiles(searchRoot, "*.png", SearchOption.TopDirectoryOnly);
                if (pngs.Length == 0) return null;

                string pick =
                    pngs.FirstOrDefault(p => Path.GetFileName(p).IndexOf("Square44x44Logo", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                             Path.GetFileName(p).IndexOf("targetsize-48", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? pngs.FirstOrDefault(p => Path.GetFileName(p).IndexOf("Square44x44Logo", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? pngs.FirstOrDefault(p => Path.GetFileName(p).IndexOf("StoreLogo", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? pngs.FirstOrDefault(p => Path.GetFileName(p).IndexOf("Logo", StringComparison.OrdinalIgnoreCase) >= 0);

                if (!string.IsNullOrWhiteSpace(pick))
                    return LoadFrozenBitmap(pick);
            }
            catch { }
            return null;
        }

        private static ImageSource LoadFrozenBitmap(string imagePath)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(imagePath, UriKind.Absolute);
                bi.DecodePixelWidth = 32;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                if (bi.CanFreeze) bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        private static ImageSource TryExtractFromFile(string filePath, int iconIndex)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            // 1. Si index != 0 ou extraction directe via ExtractIconEx
            IntPtr[] smallIcons = new IntPtr[1];
            IntPtr[] largeIcons = new IntPtr[1];
            try
            {
                uint count = ExtractIconEx(filePath, iconIndex, largeIcons, smallIcons, 1);
                if (count > 0)
                {
                    IntPtr hIcon = smallIcons[0] != IntPtr.Zero ? smallIcons[0] : largeIcons[0];
                    if (hIcon != IntPtr.Zero)
                    {
                        var img = ConvertIconToFrozenImageSource(hIcon);
                        if (img != null) return img;
                    }
                }
            }
            catch { }
            finally
            {
                if (smallIcons[0] != IntPtr.Zero) try { DestroyIcon(smallIcons[0]); } catch { }
                if (largeIcons[0] != IntPtr.Zero) try { DestroyIcon(largeIcons[0]); } catch { }
            }

            // 2. Fallback System.Drawing.Icon.ExtractAssociatedIcon
            try
            {
                using (var ico = Icon.ExtractAssociatedIcon(filePath))
                {
                    if (ico != null)
                        return ConvertIconToFrozenImageSource(ico.Handle);
                }
            }
            catch { }

            return null;
        }

        private static ImageSource ConvertIconToFrozenImageSource(IntPtr hIcon)
        {
            if (hIcon == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(24, 24));
                if (src != null && src.CanFreeze) src.Freeze();
                return src;
            }
            catch
            {
                return null;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}
