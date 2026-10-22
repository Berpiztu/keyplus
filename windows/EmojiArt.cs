using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.WIC;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Emoji in colour on Windows: WPF's text draws them black and white, so
/// each is drawn as Windows draws it, by DirectWrite's colour fonts through
/// Direct2D into a bitmap, once for each size, and kept. The list offered is
/// <see cref="EmojiList"/>'s, Flags left out (Windows draws no flags, only
/// their letters), and those typed lately are kept in the app's folder.
/// </summary>
public static class EmojiArt
{
    private static readonly string RecentFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Berpiztu", "Keyboard", "recent-emoji.txt");

    private static readonly Lazy<IReadOnlyList<EmojiGroup>> Read = new(() =>
    {
        using var reader = new StreamReader(Path.Combine(AppContext.BaseDirectory, EmojiList.Asset));
        var read = EmojiList.Read(reader, _ => true);
        return [.. EmojiList.Kinds.Where(kind => kind.Unicode != "Flags")
            .Select(kind => new EmojiGroup(Texts.Get(kind.Name), kind.Icon, read.GetValueOrDefault(kind.Unicode) ?? []))];
    });

    private static readonly Dictionary<(string, int), BitmapSource> Drawn = [];
    private static ID2D1Factory? direct2D;
    private static IDWriteFactory? directWrite;
    private static IWICImagingFactory? imaging;

    /// <summary>The groups, in Unicode's order, each with its emoji.</summary>
    public static IReadOnlyList<EmojiGroup> Groups => Read.Value;

    public static IReadOnlyList<string> Recent() =>
        EmojiList.Recent(File.Exists(RecentFile) ? File.ReadAllText(RecentFile) : null);

    /// <summary>An emoji typed: first among those typed lately.</summary>
    public static void Used(string emoji)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RecentFile)!);
        File.WriteAllText(RecentFile, EmojiList.Used(File.Exists(RecentFile) ? File.ReadAllText(RecentFile) : null, emoji));
    }

    /// <summary>An emoji drawn in colour in a square so many pixels wide, kept for the next time.</summary>
    public static BitmapSource Draw(string emoji, int pixels)
    {
        if (Drawn.TryGetValue((emoji, pixels), out var kept))
        {
            return kept;
        }

        direct2D ??= D2D1.D2D1CreateFactory<ID2D1Factory>();
        directWrite ??= DWrite.DWriteCreateFactory<IDWriteFactory>();
        imaging ??= new IWICImagingFactory();
        using var format = directWrite.CreateTextFormat("Segoe UI Emoji", FontWeight.Normal, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, pixels * 0.75f);
        format.TextAlignment = TextAlignment.Center;
        format.ParagraphAlignment = ParagraphAlignment.Center;
        using var bitmap = imaging.CreateBitmap((uint)pixels, (uint)pixels, Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapCreateCacheOption.CacheOnLoad);
        using var target = direct2D.CreateWicBitmapRenderTarget(bitmap, new RenderTargetProperties(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied)));
        using var brush = target.CreateSolidColorBrush(new Vortice.Mathematics.Color4(0, 0, 0, 1));
        target.BeginDraw();
        target.Clear(new Vortice.Mathematics.Color4(0, 0, 0, 0));
        target.DrawText(emoji, format, new Vortice.RawRectF(0, 0, pixels, pixels), brush, DrawTextOptions.EnableColorFont);
        target.EndDraw();
        var bytes = new byte[pixels * pixels * 4];
        bitmap.CopyPixels((uint)(pixels * 4), bytes);
        var image = BitmapSource.Create(pixels, pixels, 96, 96, PixelFormats.Pbgra32, null, bytes, pixels * 4);
        image.Freeze();
        Drawn[(emoji, pixels)] = image;
        return image;
    }
}
