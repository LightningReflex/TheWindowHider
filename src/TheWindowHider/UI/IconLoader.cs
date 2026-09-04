using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace TheWindowHider.UI;

/// <summary>Extracts and caches executable icons as WPF ImageSources, keyed by exe path.</summary>
public static class IconLoader
{
    private static readonly ConcurrentDictionary<string, BitmapSource?> Cache = new();

    public static BitmapSource? ForExecutable(string exePath)
    {
        if (string.IsNullOrEmpty(exePath))
            return null;

        return Cache.GetOrAdd(exePath.ToLowerInvariant(), _ =>
        {
            try
            {
                if (!File.Exists(exePath)) return null;
                using Icon? icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon == null) return null;

                BitmapSource src = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(32, 32));
                src.Freeze(); // make it usable across threads
                return src;
            }
            catch
            {
                return null;
            }
        });
    }
}
