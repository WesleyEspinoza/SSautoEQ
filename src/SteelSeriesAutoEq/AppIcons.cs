using System.Drawing;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Resources;

namespace SteelSeriesAutoEq;

/// <summary>
/// Single place for the app icon pack URI and loaders (tray WinForms Icon + WPF ImageSource).
/// </summary>
internal static class AppIcons
{
    public const string PackUriString = "pack://application:,,,/Assets/icon.ico";
    public static readonly Uri PackUri = new(PackUriString);

    public static BitmapImage CreateImageSource()
    {
        var image = new BitmapImage(PackUri);
        image.Freeze();
        return image;
    }

    public static Icon CreateIcon()
    {
        StreamResourceInfo? resource = null;
        try
        {
            resource = Application.GetResourceStream(PackUri);
        }
        catch
        {
            // Fall through to the on-disk copy if the pack URI isn't available yet.
        }

        if (resource?.Stream is not null)
        {
            using (resource.Stream)
            using (var loaded = new Icon(resource.Stream))
            {
                return (Icon)loaded.Clone();
            }
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(path))
        {
            return new Icon(path);
        }

        throw new FileNotFoundException("Could not load Assets/icon.ico.", path);
    }
}
