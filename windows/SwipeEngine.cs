using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Berpiztu.KeyPlus;

/// <summary>
/// A swipe over the letters read as words by FUTO Swipe on Windows
/// (keyplus_swipe.dll, native/futo-swipe/windows), as the Android keyboard
/// reads it (SwipeTyping.cs): its encoder loaded once beside the app, a trie
/// of each language's AOSP word list made the first time a swipe is read in
/// it (<see cref="SwipeWords"/>), all away from the window's thread; the
/// letters read written back as the word list spells them.
/// </summary>
public sealed class SwipeEngine
{
    /// <summary>How many words a swipe offers: the one typed and one on each side of it, as Gboard's.</summary>
    private const int Offered = 3;

    private const int BeamWidth = 100;

    /// <summary>The letters' tries and the words built from the word lists, kept where Windows keeps an app's own files.</summary>
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Berpiztu", "Keyboard", "futo-swipe");

    private readonly Task<bool> loaded;
    private readonly Lock engineLock = new();
    private readonly Dictionary<StandardLanguage, (IntPtr Trie, Dictionary<string, string[]> Spellings)> languages = [];

    /// <summary>A language's personal dictionary (<see cref="PersonalWords"/>), read with its word list.</summary>
    private readonly Func<StandardLanguage, IReadOnlyList<string>> personal;
    private IntPtr engine;
    private string? layout;

    public SwipeEngine(Func<StandardLanguage, IReadOnlyList<string>> personal)
    {
        this.personal = personal;
        loaded = Task.Run(Load);
    }

    /// <summary>The personal dictionary changed: every language's trie let go, made again with it at its next swipe.</summary>
    public void Forget() => Task.Run(() =>
    {
        lock (engineLock)
        {
            foreach (var (trie, _) in languages.Values)
            {
                keyplus_swipe_destroy_trie(trie);
            }

            languages.Clear();
            layout = null;
        }
    });

    /// <summary>A swipe: its points and the letters' centres from 0 to 1 over the letters' room, t in milliseconds, and its language.</summary>
    public sealed record Swipe(float[] X, float[] Y, float[] T, float[] KeyX, float[] KeyY, StandardLanguage Language);

    /// <summary>The words a swipe reads as, the likeliest first; none where FUTO Swipe could not be loaded or read nothing.</summary>
    public async Task<IReadOnlyList<string>> Words(Swipe path)
    {
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
                    if (keyplus_swipe_set_layout(engine, path.Language.SwipeLetters, path.KeyX, path.KeyY, path.KeyX.Length, language.Trie) == 0)
                    {
                        Log.Write("FUTO Swipe did not take the letters' places");
                        return (IReadOnlyList<string>)[];
                    }

                    layout = centres;
                }

                var read = new byte[4096];
                var count = keyplus_swipe_recognize(engine, path.X, path.Y, path.T, path.X.Length, Offered, BeamWidth, read, read.Length);
                if (count <= 0)
                {
                    return (IReadOnlyList<string>)[];
                }

                var words = Encoding.UTF8.GetString(read, 0, Array.IndexOf(read, (byte)0)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                return SwipeWords.Spelled(words, language.Spellings, Offered);
            }
        });
    }

    /// <summary>The engine made with the encoder beside the app; Spanish's trie made at once.</summary>
    private bool Load()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var encoder = Path.Combine(AppContext.BaseDirectory, "futo-swipe", "honorable_sturgeon", "model_fp32.pte");
            engine = keyplus_swipe_create(encoder, 2, BeamWidth, 4);
            lock (engineLock)
            {
                return engine != IntPtr.Zero && Prepared(StandardKeys.Spanish) is not null;
            }
        }
        catch (Exception failure)
        {
            Log.Write($"FUTO Swipe could not be loaded: {failure}");
            return false;
        }
    }

    /// <summary>A language's trie and spellings, made from its word list the first time; none where it could not be made.</summary>
    private (IntPtr Trie, Dictionary<string, string[]> Spellings)? Prepared(StandardLanguage language)
    {
        if (languages.TryGetValue(language, out var prepared))
        {
            return prepared;
        }

        var words = Path.Combine(Folder, $"{language.Code}.letters.combined");
        Dictionary<string, string[]> spellings;
        using (var wordList = File.OpenRead(Path.Combine(AppContext.BaseDirectory, language.WordList)))
        {
            spellings = SwipeWords.WriteLetterForms(wordList, language, words, personal(language));
        }

        var trie = keyplus_swipe_load_trie(language.SwipeLetters, words, "f");
        if (trie == IntPtr.Zero)
        {
            return null;
        }

        languages[language] = prepared = (trie, spellings);
        return prepared;
    }

    [DllImport("keyplus_swipe.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr keyplus_swipe_create([MarshalAs(UnmanagedType.LPUTF8Str)] string encoderPath, int threads, int beamWidth, int topK);

    [DllImport("keyplus_swipe.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr keyplus_swipe_load_trie([MarshalAs(UnmanagedType.LPUTF8Str)] string letters, [MarshalAs(UnmanagedType.LPUTF8Str)] string vocabPath, [MarshalAs(UnmanagedType.LPUTF8Str)] string freqKey);

    [DllImport("keyplus_swipe.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void keyplus_swipe_destroy_trie(IntPtr trie);

    [DllImport("keyplus_swipe.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int keyplus_swipe_set_layout(IntPtr engine, [MarshalAs(UnmanagedType.LPUTF8Str)] string letters, float[] cx, float[] cy, int keys, IntPtr trie);

    [DllImport("keyplus_swipe.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int keyplus_swipe_recognize(IntPtr engine, float[] x, float[] y, float[] t, int points, int topK, int beamWidth, byte[] output, int outputSize);
}
