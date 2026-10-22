using System.Globalization;
using System.IO.Compression;
using System.Text;
using Android.Content;
using Android.Runtime;
using Android.Util;

namespace Berpiztu.KeyPlus;

/// <summary>
/// A word swiped over Berpiztu AI Keyboard's letters, read by FUTO Swipe
/// (SwipeDecoder.java, native/futo-swipe): its encoder, which reads a swipe
/// over any layout, and a trie of AOSP's Spanish word list, both loaded once
/// and away from the main thread. The trie knows a word by its letters alone,
/// so a word with an accent ("también") is kept there as "tambien" and written
/// back as the word list spells it, the commonest spelling first ("está",
/// then "esta").
/// </summary>
public sealed class SwipeTyping
{
    /// <summary>The Java side (SwipeDecoder.java), reached through JNI: it is not bound to C#.</summary>
    private static readonly IntPtr Decoder = JNIEnv.FindClass("org/futo/ml/inference/SwipeDecoder");

    private const string Tag = "KeyPlusSwipe";

    /// <summary>How many words a swipe offers: the one typed and one on each side of it in the toolbar, as Gboard's.</summary>
    private const int Offered = 3;

    private readonly Task<bool> loaded;

    /// <summary>The native engine is used by one thread at a time.</summary>
    private readonly Lock engineLock = new();

    private long engine;
    private long dictionary;

    /// <summary>Each word by its letters alone, as the trie knows it: its spellings in the word list, the commonest first.</summary>
    private Dictionary<string, string[]> spellings = [];

    /// <summary>The letters' centres last handed to the engine, so they are handed again only when the design changed.</summary>
    private string? layout;

    public SwipeTyping(Context context) => loaded = Task.Run(() => Load(context));

    /// <summary>The words a swipe reads as, the likeliest first; none where FUTO Swipe could not be loaded or read nothing.</summary>
    public async Task<IReadOnlyList<string>> Words(KeyPanel.SwipePath path)
    {
        if (!await loaded)
        {
            return [];
        }

        return await Task.Run(() =>
        {
            lock (engineLock)
            {
                var centres = $"{string.Join(',', path.KeyX)}|{string.Join(',', path.KeyY)}";
                if (centres != layout)
                {
                    if (!SetLayout(path.KeyX, path.KeyY))
                    {
                        Log.Warn(Tag, "FUTO Swipe did not take the letters' places");
                        return (IReadOnlyList<string>)[];
                    }

                    layout = centres;
                }

                var read = Recognize(path.X, path.Y, path.T);
                Log.Debug(Tag, $"{path.X.Length} points read as: {string.Join(", ", read)}");
                return Spelled(read);
            }
        });
    }

    /// <summary>
    /// The encoder copied out of the APK, where the native library reads it
    /// from a path, with its metadata beside it; the word list made a trie;
    /// the engine made.
    /// </summary>
    private bool Load(Context context)
    {
        try
        {
            var folder = Path.Combine(context.FilesDir!.AbsolutePath, "futo-swipe");
            Directory.CreateDirectory(folder);
            var encoder = Path.Combine(folder, "model_fp32.pte");
            CopyAsset(context, "futo-swipe/honorable_sturgeon/model_fp32.pte", encoder);
            CopyAsset(context, "futo-swipe/honorable_sturgeon/metadata.json", Path.Combine(folder, "metadata.json"));
            var words = Path.Combine(folder, "es.letters.combined");
            spellings = WriteLetterForms(context, words);
            engine = CallLong("create", "(Ljava/lang/String;)J", encoder);
            dictionary = CallLong("loadDictionary", "(Ljava/lang/String;Ljava/lang/String;)J", KeyPanel.SwipeLetters, words);
            Log.Info(Tag, $"FUTO Swipe loaded: engine {engine != 0}, {spellings.Count} words");
            return engine != 0 && dictionary != 0;
        }
        catch (Exception failure)
        {
            Log.Warn(Tag, $"FUTO Swipe could not be loaded: {failure}");
            return false;
        }
    }

    private static void CopyAsset(Context context, string asset, string path)
    {
        using var from = context.Assets!.Open(asset);
        using var to = File.Create(path);
        from.CopyTo(to);
    }

    /// <summary>
    /// AOSP's Spanish word list ("word=…,f=…" lines) written again by each
    /// word's letters alone, the accents taken off, at the commonest
    /// frequency among its spellings, for the trie; the words with a letter
    /// the keyboard has no key for left out. Its spellings by letters, back.
    /// </summary>
    private static Dictionary<string, string[]> WriteLetterForms(Context context, string path)
    {
        var forms = new Dictionary<string, List<(string Word, int Frequency)>>();
        using (var reader = new StreamReader(new GZipStream(context.Assets!.Open("dictionaries/es_wordlist.combined.gz"), CompressionMode.Decompress), Encoding.UTF8))
        {
            while (reader.ReadLine() is { } line)
            {
                if (Entry(line) is not { } entry || Letters(entry.Word) is not { } letters)
                {
                    continue;
                }

                if (!forms.TryGetValue(letters, out var spelled))
                {
                    forms[letters] = spelled = [];
                }

                spelled.Add(entry);
            }
        }

        using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
        {
            foreach (var (letters, spelled) in forms)
            {
                writer.Write(" word=");
                writer.Write(letters);
                writer.Write(",f=");
                writer.Write(spelled.Max(form => form.Frequency).ToString(CultureInfo.InvariantCulture));
                writer.Write('\n');
            }
        }

        return forms.ToDictionary(
            form => form.Key,
            form => form.Value.OrderByDescending(spelled => spelled.Frequency).Select(spelled => spelled.Word).Distinct().ToArray());
    }

    /// <summary>A word list's line, " word=está,f=200,flags=,…": its word and frequency; none for any other line.</summary>
    private static (string Word, int Frequency)? Entry(string line)
    {
        var text = line.AsSpan().TrimStart();
        if (!text.StartsWith("word="))
        {
            return null;
        }

        text = text[5..];
        var comma = text.IndexOf(',');
        if (comma <= 0)
        {
            return null;
        }

        var word = text[..comma].ToString();
        var rest = text[comma..];
        var at = rest.IndexOf(",f=");
        if (at < 0)
        {
            return null;
        }

        var frequency = rest[(at + 3)..];
        var end = frequency.IndexOf(',');
        return int.TryParse(end < 0 ? frequency : frequency[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? (word, value)
            : null;
    }

    /// <summary>A word by its letters alone, lower case and its accents taken off ("Está" is "esta"); none where it has a letter the keyboard has no key for.</summary>
    private static string? Letters(string word)
    {
        var letters = new StringBuilder(word.Length);
        foreach (var character in word.ToLowerInvariant())
        {
            var plain = character switch
            {
                'á' => 'a',
                'é' => 'e',
                'í' => 'i',
                'ó' => 'o',
                'ú' or 'ü' => 'u',
                _ => character,
            };
            if (!KeyPanel.SwipeLetters.Contains(plain))
            {
                return null;
            }

            letters.Append(plain);
        }

        return letters.Length > 0 ? letters.ToString() : null;
    }

    /// <summary>The words read, by their letters, spelled as the word list has them, the commonest spelling first.</summary>
    private IReadOnlyList<string> Spelled(string[] read) =>
        read.SelectMany(letters => spellings.TryGetValue(letters, out var spelled) ? spelled : [letters])
            .Distinct()
            .Take(Offered)
            .ToList();

    private bool SetLayout(float[] keyX, float[] keyY)
    {
        var method = JNIEnv.GetStaticMethodID(Decoder, "setLayout", "(JLjava/lang/String;[F[FJ)Z");
        var letters = JNIEnv.NewString(KeyPanel.SwipeLetters);
        var cx = JNIEnv.NewArray(keyX);
        var cy = JNIEnv.NewArray(keyY);
        try
        {
            return JNIEnv.CallStaticBooleanMethod(Decoder, method,
                new JValue(engine), new JValue(letters), new JValue(cx), new JValue(cy), new JValue(dictionary));
        }
        finally
        {
            JNIEnv.DeleteLocalRef(letters);
            JNIEnv.DeleteLocalRef(cx);
            JNIEnv.DeleteLocalRef(cy);
        }
    }

    private string[] Recognize(float[] x, float[] y, float[] t)
    {
        var method = JNIEnv.GetStaticMethodID(Decoder, "recognize", "(J[F[F[FI)[Ljava/lang/String;");
        var javaX = JNIEnv.NewArray(x);
        var javaY = JNIEnv.NewArray(y);
        var javaT = JNIEnv.NewArray(t);
        try
        {
            var words = JNIEnv.CallStaticObjectMethod(Decoder, method,
                new JValue(engine), new JValue(javaX), new JValue(javaY), new JValue(javaT), new JValue(Offered));
            var read = JNIEnv.GetArray<string>(words) ?? [];
            JNIEnv.DeleteLocalRef(words);
            return read;
        }
        finally
        {
            JNIEnv.DeleteLocalRef(javaX);
            JNIEnv.DeleteLocalRef(javaY);
            JNIEnv.DeleteLocalRef(javaT);
        }
    }

    private static long CallLong(string name, string signature, params string[] arguments)
    {
        var method = JNIEnv.GetStaticMethodID(Decoder, name, signature);
        var strings = arguments.Select(JNIEnv.NewString).ToArray();
        try
        {
            return JNIEnv.CallStaticLongMethod(Decoder, method, strings.Select(text => new JValue(text)).ToArray());
        }
        finally
        {
            foreach (var text in strings)
            {
                JNIEnv.DeleteLocalRef(text);
            }
        }
    }
}
