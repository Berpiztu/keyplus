using System.Text;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Letters Spanish writes for one sound, or nearly, the same on Android and
/// Windows: what a word may be misspelled with (b for v, i for y, an h left
/// out), for Spelling to weigh a correction and for the user's own words to
/// be found as they are typed ("mis" finds "mysaasplace").
/// </summary>
public static class SpanishSounds
{
    /// <summary>The letters written for one another, by pairs; a letter in several (c) sounds as each of them.</summary>
    public static readonly string[] Alike = ["bv", "ck", "cs", "cz", "sz", "gj", "yi", "qk"];

    /// <summary>Whether one letter is written for the other, accents aside.</summary>
    public static bool AreAlike(char one, char other)
    {
        var (a, b) = (Bare(one), Bare(other));
        return Alike.Any(pair => pair.Contains(a) && pair.Contains(b));
    }

    /// <summary>
    /// A word as it sounds: lower case, accents off, the h left out, each
    /// letter as the first of the first pair it is in (v as b, i as y, k, q,
    /// s and z as c, j as g); two words that sound alike start alike.
    /// </summary>
    public static string Key(string word)
    {
        var key = new StringBuilder(word.Length);
        foreach (var character in word)
        {
            var letter = Bare(char.ToLowerInvariant(character));
            if (letter == 'h')
            {
                continue;
            }

            key.Append(Alike.FirstOrDefault(pair => pair.Contains(letter)) is { } pair ? pair[0] : letter);
        }

        return key.ToString();
    }

    /// <summary>A letter without its accent (á a, ü u, ñ n).</summary>
    public static char Bare(char letter) => letter.ToString().Normalize(NormalizationForm.FormD)[0];
}
