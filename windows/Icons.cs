using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The keys' signs, the Android keyboard's own vector drawables
/// (Resources\drawable, copied beside the app), each by its id's name: each path of their 960 box,
/// filled or stroked as there, drawn in the colour asked and the size asked.
/// </summary>
public static class Icons
{
    private static readonly XNamespace AndroidXml = "http://schemas.android.com/apk/res/android";
    private static readonly Dictionary<int, (double Box, List<(Geometry Shape, double Stroke)> Paths)> Read = [];

    /// <summary>A sign drawn in a square of a size, centred at a point.</summary>
    public static void Draw(DrawingContext context, int id, Point centre, double size, Color color)
    {
        if (Load(id) is not { } icon)
        {
            return;
        }

        var scale = size / icon.Box;
        context.PushTransform(new TranslateTransform(centre.X - (size / 2), centre.Y - (size / 2)));
        context.PushTransform(new ScaleTransform(scale, scale));
        var brush = new SolidColorBrush(color);
        foreach (var (shape, stroke) in icon.Paths)
        {
            if (stroke > 0)
            {
                context.DrawGeometry(null, new Pen(brush, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, shape);
            }
            else
            {
                context.DrawGeometry(brush, null, shape);
            }
        }

        context.Pop();
        context.Pop();
    }

    private static (double Box, List<(Geometry Shape, double Stroke)> Paths)? Load(int id)
    {
        if (Read.TryGetValue(id, out var known))
        {
            return known;
        }

        var file = Path.Combine(AppContext.BaseDirectory, "drawable", Resource.NameOf(typeof(Resource.Drawable), id) + ".xml");
        var vector = XDocument.Load(file).Root!;
        var box = double.Parse((string?)vector.Attribute(AndroidXml + "viewportWidth") ?? "960", System.Globalization.CultureInfo.InvariantCulture);
        List<(Geometry, double)> paths = [];
        foreach (var path in vector.Descendants("path"))
        {
            // Android fills by the non-zero rule: F1.
            var shape = Geometry.Parse("F1 " + (string)path.Attribute(AndroidXml + "pathData")!);
            shape.Freeze();
            var stroke = (string?)path.Attribute(AndroidXml + "strokeWidth") is { } width ? double.Parse(width, System.Globalization.CultureInfo.InvariantCulture) : 0;
            paths.Add((shape, stroke));
        }

        Read[id] = (box, paths);
        return Read[id];
    }
}
