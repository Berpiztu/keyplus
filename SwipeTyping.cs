using Android.Content;
using Android.Runtime;
using Android.Util;

namespace Berpiztu.KeyPlus;

/// <summary>
/// A word swiped over Berpiztu AI Keyboard's letters, read by FUTO Swipe
/// (SwipeDecoder.java, native/futo-swipe): its encoder, which reads a swipe
/// over any layout, loaded once, and a trie of each language's AOSP word
/// list, made the first time a swipe is read in it, all away from the main
/// thread. Neither is carried: a language's Glide typing downloads its word
/// list, and the encoder the first time (<see cref="SwipeData"/>); until
/// they are here a swipe reads nothing, and once they are, the next loads
/// them. The trie knows a word by its letters alone,
/// so a word with an accent ("también") is kept there as "tambien" and written
/// back as the word list spells it, the commonest spelling first ("está",
/// then "esta").
/// </summary>
public sealed class SwipeTyping
{
    /// <summary>The Java side (SwipeDecoder.java), reached through JNI: it is not bound to C#.</summary>
    private static readonly IntPtr Decoder = JNIEnv.FindClass("org/futo/ml/inference/SwipeDecoder");

    /// <summary>How many words a swipe offers: the one typed and one on each side of it in the toolbar, as Gboard's.</summary>
    private const int Offered = 3;

    private Task<bool> loaded;

    /// <summary>The native engine is used by one thread at a time.</summary>
    private readonly Lock engineLock = new();

    private readonly Context context;
    private long engine;

    /// <summary>
    /// Each language's trie, and each word by its letters alone, as the trie
    /// knows it: its spellings in the word list, the commonest first.
    /// </summary>
    private readonly Dictionary<StandardLanguage, (long Trie, Dictionary<string, string[]> Spellings)> languages = [];

    /// <summary>The language and letters' centres last handed to the engine, so they are handed again only when they changed.</summary>
    private string? layout;

    /// <summary>A language's personal dictionary (<see cref="PersonalWords"/>), read with its word list.</summary>
    private readonly Func<StandardLanguage, IReadOnlyList<string>> personal;

    public SwipeTyping(Context context, Func<StandardLanguage, IReadOnlyList<string>> personal)
    {
        this.context = context;
        this.personal = personal;
        loaded = Task.Run(Load);
    }

    /// <summary>The personal dictionary changed: every language's trie let go, made again with it at its next swipe.</summary>
    public void Forget() => Task.Run(() =>
    {
        lock (engineLock)
        {
            var free = JNIEnv.GetStaticMethodID(Decoder, "freeDictionary", "(J)V");
            foreach (var (trie, _) in languages.Values)
            {
                JNIEnv.CallStaticVoidMethod(Decoder, free, new JValue(trie));
            }

            languages.Clear();
            layout = null;
        }
    });

    /// <summary>The words a swipe reads as, the likeliest first; none where FUTO Swipe could not be loaded or read nothing.</summary>
    public async Task<IReadOnlyList<string>> Words(KeyPanel.SwipePath path)
    {
        // Not loaded for want of the encoder, and it is here now: loaded again.
        if (loaded.IsCompleted && !loaded.Result && SwipeData.ModelDownloaded)
        {
            loaded = Task.Run(Load);
        }

        if (!await loaded)
        {
            return [];
        }

        return await Task.Run(() =>
        {
            lock (engineLock)
            {
                if (Prepared(path.Language) is not { } language)
                {
                    return (IReadOnlyList<string>)[];
                }

                var centres = $"{path.Language.Code}|{string.Join(',', path.KeyX)}|{string.Join(',', path.KeyY)}";
                if (centres != layout)
                {
                    if (!SetLayout(path.Language.SwipeLetters, path.KeyX, path.KeyY, language.Trie))
                    {
                        Log.Write("FUTO Swipe did not take the letters' places");
                        return (IReadOnlyList<string>)[];
                    }

                    layout = centres;
                }

                var read = Recognize(path.X, path.Y, path.T);
                Log.Detail($"{path.X.Length} points read as: {string.Join(", ", read)}");
                return SwipeWords.Spelled(read, language.Spellings, Offered);
            }
        });
    }

    private string Folder => Path.Combine(context.FilesDir!.AbsolutePath, "futo-swipe");

    /// <summary>The engine made with the encoder downloaded, its metadata beside it, where the native library reads them from a path; false while it is not downloaded.</summary>
    private bool Load()
    {
        if (!SwipeData.ModelDownloaded)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Folder);
            engine = CallLong("create", "(Ljava/lang/String;)J", SwipeData.ModelPath);
            Log.Write($"FUTO Swipe loaded: engine {engine != 0}");
            return engine != 0;
        }
        catch (Exception failure)
        {
            Log.Write($"FUTO Swipe could not be loaded: {failure}");
            return false;
        }
    }

    /// <summary>A language's trie and spellings, made from its word list the first time; none where it is not downloaded or could not be made.</summary>
    private (long Trie, Dictionary<string, string[]> Spellings)? Prepared(StandardLanguage language)
    {
        if (languages.TryGetValue(language, out var prepared))
        {
            return prepared;
        }

        if (!SwipeData.HasWordList(language))
        {
            return null;
        }

        try
        {
            var words = Path.Combine(Folder, $"{language.Code}.letters.combined");
            Dictionary<string, string[]> spellings;
            using (var wordList = File.OpenRead(SwipeData.WordListPath(language)))
            {
                spellings = SwipeWords.WriteLetterForms(wordList, language, words, personal(language));
            }

            var trie = CallLong("loadDictionary", "(Ljava/lang/String;Ljava/lang/String;)J", language.SwipeLetters, words);
            Log.Write($"FUTO Swipe's {language.Code} words: {spellings.Count}");
            if (trie == 0)
            {
                return null;
            }

            languages[language] = prepared = (trie, spellings);
            return prepared;
        }
        catch (Exception failure)
        {
            Log.Write($"FUTO Swipe's {language.Code} words could not be made: {failure}");
            return null;
        }
    }

    private bool SetLayout(string keys, float[] keyX, float[] keyY, long dictionary)
    {
        var method = JNIEnv.GetStaticMethodID(Decoder, "setLayout", "(JLjava/lang/String;[F[FJ)Z");
        var letters = JNIEnv.NewString(keys);
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
