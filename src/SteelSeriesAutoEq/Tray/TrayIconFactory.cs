using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SteelSeriesAutoEq.Tray;

/// <summary>
/// Draws the tray icon in code so the app has no external image dependency. It is a dark
/// disc with an orange ring and three little equalizer bars.
/// </summary>
public static class TrayIconFactory
{
    public static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using (var bg = new SolidBrush(Color.FromArgb(255, 20, 20, 24)))
            {
                g.FillEllipse(bg, 1, 1, 30, 30);
            }

            using (var ring = new Pen(Color.FromArgb(255, 255, 90, 30), 2.5f))
            {
                g.DrawEllipse(ring, 3, 3, 26, 26);
            }

            // Simple EQ bars
            using var bar = new SolidBrush(Color.FromArgb(255, 255, 120, 40));
            g.FillRectangle(bar, 9, 18, 3, 6);
            g.FillRectangle(bar, 14, 12, 3, 12);
            g.FillRectangle(bar, 19, 8, 3, 16);
        }

        var hIcon = bitmap.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);
}
