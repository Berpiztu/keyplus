using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The app's words, the Android keyboard's own (Resources/values/strings.xml
/// and values-es, copied beside the app), in Spanish where Windows is in
/// Spanish, else in English; a resource by its id, as Android's GetString.
/// </summary>
public static partial class Texts
{
    private static readonly Lazy<Dictionary<string, string>> Words = new(Load);

    /// <summary>A text by its id, its %1$s and on filled in order.</summary>
    public static string Get(int id, params object[] values)
    {
        var name = Resource.NameOf(typeof(Resource.String), id);
        var text = Words.Value.GetValueOrDefault(name) ?? name;
        return values.Length == 0 ? text : Placeholder().Replace(text, match => values[int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) - 1].ToString() ?? "");
    }

    private static Dictionary<string, string> Load()
    {
        var words = Read("values");
        if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es")
        {
            foreach (var (name, text) in Read("values-es"))
            {
                words[name] = text;
            }
        }

        return words;
    }

    private static Dictionary<string, string> Read(string folder)
    {
        var file = Path.Combine(AppContext.BaseDirectory, "strings", folder, "strings.xml");
        if (!File.Exists(file))
        {
            return [];
        }

        return XDocument.Load(file).Root!.Elements("string")
            .Where(text => text.Attribute("name") is not null)
            .GroupBy(text => (string)text.Attribute("name")!)
            .ToDictionary(named => named.Key, named => Unescaped(named.Last().Value));
    }

    /// <summary>Android's escapes in a text undone: \' \" \n.</summary>
    private static string Unescaped(string text) => text.Replace("\\'", "'").Replace("\\\"", "\"").Replace("\\n", "\n");

    [GeneratedRegex(@"%(\d+)\$[sd]")]
    private static partial Regex Placeholder();
}
