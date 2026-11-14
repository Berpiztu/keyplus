using Android.Content;
using Android.Views.TextService;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Android's own spell checker, the one chosen in the system's settings
/// (Gboard's on the owner's devices): asked for a text's misspelled words
/// and, where it marks them, its grammar errors, each with the words it
/// suggests in their place. The Spelling command's where the language has it
/// chosen, and the Grammar command's.
/// </summary>
public sealed class SystemSpelling(Context context, Action<string> log)
{
    /// <summary>A place the checker marked: where it starts in the text asked about and how long it is, the words suggested for it, the likeliest first, and whether it is a misspelling, a grammar error, or both.</summary>
    public sealed record Marked(int At, int Length, IReadOnlyList<string> Suggestions, bool Misspelled, bool Grammar);

    /// <summary>SuggestionsInfo's attributes, as Android numbers them: a word that looks misspelled, and one that looks like a grammar error (Android 12).</summary>
    private const int LooksMisspelled = 0x0002;
    private const int LooksLikeGrammar = 0x0008;

    /// <summary>How many words are asked for each place, and how long the checker is waited for.</summary>
    private const int MostSuggestions = 3;
    private static readonly TimeSpan AnswerWithin = TimeSpan.FromSeconds(10);

    /// <summary>
    /// What the checker marks in a text of a language by its locale; none
    /// where Android has no checker to ask, it gives no session for the
    /// language, or it does not answer in time, each said in the log.
    /// </summary>
    public async Task<IReadOnlyList<Marked>?> Check(string text, string locale)
    {
        if (context.GetSystemService(Context.TextServicesManagerService) is not TextServicesManager services)
        {
            log("Spelling: Android has no text services to ask");
            return null;
        }

        var answered = new TaskCompletionSource<IReadOnlyList<Marked>>();
        using var listener = new Listener(answered);
        var asked = new SpellCheckerSession.SpellCheckerSessionParams.Builder()
            .SetLocale(Java.Util.Locale.ForLanguageTag(locale))
            .SetShouldReferToSpellCheckerLanguageSettings(false)
            .SetSupportedAttributes(LooksMisspelled | LooksLikeGrammar)
            .Build();
        var session = services.NewSpellCheckerSession(asked, context.MainExecutor!, listener);
        if (session is null)
        {
            log($"Spelling: Android's spell checker gave no session for {locale} (none chosen in the system's settings, or it is turned off there)");
            return null;
        }

        try
        {
            session.GetSentenceSuggestions([new TextInfo(text)], MostSuggestions);
            if (await Task.WhenAny(answered.Task, Task.Delay(AnswerWithin)) != answered.Task)
            {
                log($"Spelling: Android's spell checker did not answer within {AnswerWithin.TotalSeconds} s");
                return null;
            }

            return await answered.Task;
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>A text with each misspelling the checker marked written as its first suggestion, and how many were; the grammar's are left, being only marked.</summary>
    public static (string Text, int Changed) Corrected(string text, IReadOnlyList<Marked> marked)
    {
        var changed = 0;
        foreach (var place in marked.Where(place => place.Misspelled && place.Suggestions.Count > 0 && place.At + place.Length <= text.Length).OrderByDescending(place => place.At))
        {
            text = text[..place.At] + place.Suggestions[0] + text[(place.At + place.Length)..];
            changed++;
        }

        return (text, changed);
    }

    private sealed class Listener(TaskCompletionSource<IReadOnlyList<Marked>> answered) : Java.Lang.Object, SpellCheckerSession.ISpellCheckerSessionListener
    {
        public void OnGetSuggestions(SuggestionsInfo[]? results)
        {
        }

        public void OnGetSentenceSuggestions(SentenceSuggestionsInfo[]? results)
        {
            List<Marked> marked = [];
            foreach (var sentence in results ?? [])
            {
                for (var at = 0; at < sentence.SuggestionsCount; at++)
                {
                    if (sentence.GetSuggestionsInfoAt(at) is not { } info)
                    {
                        continue;
                    }

                    var attributes = (int)info.SuggestionsAttributes;
                    var misspelled = (attributes & LooksMisspelled) != 0;
                    var grammar = (attributes & LooksLikeGrammar) != 0;
                    if (!misspelled && !grammar)
                    {
                        continue;
                    }

                    List<string> suggestions = [];
                    for (var word = 0; word < info.SuggestionsCount; word++)
                    {
                        if (info.GetSuggestionAt(word) is { Length: > 0 } suggested)
                        {
                            suggestions.Add(suggested);
                        }
                    }

                    marked.Add(new(sentence.GetOffsetAt(at), sentence.GetLengthAt(at), suggestions, misspelled, grammar));
                }
            }

            answered.TrySetResult(marked);
        }
    }
}
