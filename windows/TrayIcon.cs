using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keyboard by Windows' clock, while it runs, shown or hidden: the app's
/// icon; a click, and its menu's first line, restore the keyboard where it is
/// out of sight and put it away where it is in sight, as its ⌨ icon or hidden
/// as "When the field lets go" says (the owner, 2026-11-07); the menu also
/// opens the settings or quits, the only way the keyboard ends.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    /// <summary>The icon's size, Windows' small icon's, in pixels.</summary>
    private const int IconPixels = 32;

    private readonly Forms.NotifyIcon icon;
    private readonly System.Drawing.Icon drawn;

    public TrayIcon(Action show, Action putAway, Func<bool> inSight, Func<bool> asIcon, Action settings, Action quit)
    {
        void Toggle()
        {
            if (inSight())
            {
                putAway();
            }
            else
            {
                show();
            }
        }

        var menu = new Forms.ContextMenuStrip();
        var showing = menu.Items.Add("", null, (_, _) => Toggle());
        menu.Opening += (_, _) => showing.Text = Texts.Get(!inSight() ? Resource.String.restore_keyboard : asIcon() ? Resource.String.make_icon : Resource.String.hide_keyboard);
        menu.Items.Add(Texts.Get(Resource.String.keyboard_settings), null, (_, _) => settings());
        menu.Items.Add(Texts.Get(Resource.String.quit), null, (_, _) => quit());
        drawn = Drawn();
        icon = new Forms.NotifyIcon { Text = "Berpiztu AI Keyboard", Icon = drawn, ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                Toggle();
            }
        };
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        drawn.Dispose();
    }

    /// <summary>The app's icon (ic_keyplus) drawn in its own colours as a Windows icon.</summary>
    private static System.Drawing.Icon Drawn()
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            Icons.DrawOwn(context, Resource.Drawable.ic_keyplus, new Point(IconPixels / 2.0, IconPixels / 2.0), IconPixels);
        }

        var bitmap = new RenderTargetBitmap(IconPixels, IconPixels, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        png.Save(stream);
        using var drawing = new System.Drawing.Bitmap(stream);
        var handle = drawing.GetHicon();
        using var borrowed = System.Drawing.Icon.FromHandle(handle);
        var owned = (System.Drawing.Icon)borrowed.Clone();
        DestroyIcon(handle);
        return owned;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
