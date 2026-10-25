using System.Collections.Concurrent;
using System.Globalization;
using System.IO;

namespace Berpiztu.KeyPlus;

/// <summary>
/// What the keyboard knows of words as they are typed, the same on Android
/// and Windows: each language's word list (<see cref="SwipeData"/>), its
/// spelling dictionary (<see cref="SpellingDictionary"/>), the common
/// misspellings (<see cref="CommonErrors"/>) and the accent model
/// (<see cref="AccentModel"/>), each read away from the keyboard's thread
/// once downloaded and again once downloaded again (<see cref="Loaded{T}"/>);
/// the words offered for a word being typed, whether a word is one, and what
/// a word may have meant, by the language's Spelling as it applies now.
/// </summary>
public sealed class SpellingEngine
{
    private const int FixesKept = 200;

    private readonly PersonalWords personal;
    private readonly Func<string, LanguageSpelling> applied;
    private readonly Action<string> log;
    private readonly Dictionary<StandardLanguage, Loaded<WordSuggestions>> wordLists = [];
    private readonly Dictionary<string, Loaded<SpellingDictionary>> dictionaries = [];
    private readonly Loaded<CommonErrors> commonErrors;

    /// <summary>What each word typed may have meant, found as it was typed, for its end; begun again past <see cref="FixesKept"/> words and as Spelling's choices change.</summary>
    private readonly ConcurrentDictionary<string, WordFixes> fixes = new();

    /// <param name="personal">The user's dictionary.</param>
    /// <param name="applied">A language's Spelling as it applies now, by its code (nothing with Spelling off).</param>
    /// <param name="wikipedia">Whether Spanish's Spelling uses the Wikipedia's common misspellings.</param>
    /// <param name="ownList">The keyboard's own common misspellings (<see cref="CommonErrors.OwnList"/>), opened as the app carries it.</param>
    /// <param name="log">Where a failure is written.</param>
    public SpellingEngine(PersonalWords personal, Func<string, LanguageSpelling> applied, Func<bool> wikipedia, Func<Stream> ownList, Action<string> log)
    {
        this.personal = personal;
        this.applied = applied;
        this.log = log;
        commonErrors = new(() => (wikipedia(), wikipedia() && CommonErrors.Downloaded ? CommonErrors.Stamp : default), () => CommonErrors.Load(wikipedia(), ownList));
    }

    /// <summary>The words a misspelled word being typed may have meant, found away from the keyboard's thread: told on the thread that typed it, for the toolbar to show them while that word is still being typed.</summary>
    public event Action<StandardLanguage, string>? FixesFound;

    /// <summary>Spelling's accent model, loaded the first time a sentence asks for it; the one loaded before let go once the new one is, a sentence may still be reading it.</summary>
    public Loaded<AccentModel> Accents { get; } = new(() => AccentModel.Downloaded ? AccentModel.Stamp : null, AccentModel.Load, old => old.Dispose());

    /// <summary>What words were found to mean forgotten: Spelling's choices changed.</summary>
    public void Forget() => fixes.Clear();

    /// <summary>What a language's Spelling turned on loaded at once, away from the keyboard's thread.</summary>
    public void Prepare(StandardLanguage language)
    {
        var spelling = applied(language.Code);
        if (spelling.Accents)
        {
            _ = Accents.Async();
        }

        if (spelling.Words)
        {
            _ = DictionaryFor(language)?.Async();
            _ = commonErrors.Async();
        }
    }

    /// <summary>A language's word list, read away from the keyboard's thread the first time it is asked for and again once it is downloaded again; none while it is not on this device.</summary>
    public Loaded<WordSuggestions> WordListOf(StandardLanguage language)
    {
        if (!wordLists.TryGetValue(language, out var list))
        {
            wordLists[language] = list = new(() => SwipeData.HasWordList(language) ? SwipeData.WordListStamp(language) : null, () =>
            {
                if (!SwipeData.HasWordList(language))
                {
                    return null;
                }

                try
                {
                    using var wordList = File.OpenRead(SwipeData.WordListPath(language));
                    return WordSuggestions.Read(wordList, language);
                }
                catch (Exception failure) when (failure is IOException or InvalidDataException)
                {
                    log($"The {language.Code} word list could not be read: {failure.Message}");
                    return null;
                }
            });
        }

        return list;
    }

    /// <summary>A language's spelling dictionary, read the first time it is asked for; none for a language Spelling has none for.</summary>
    public Loaded<SpellingDictionary>? DictionaryFor(StandardLanguage language)
    {
        var code = language.Code;
        if (!SpellingDictionary.Has(code))
        {
            return null;
        }

        if (!dictionaries.TryGetValue(code, out var loaded))
        {
            dictionaries[code] = loaded = new(() => SpellingDictionary.DownloadedFor(code) ? SpellingDictionary.StampFor(code) : null, () => SpellingDictionary.Load(code));
        }

        return loaded;
    }

    /// <summary>
    /// The words offered for a word being typed: the word itself first where
    /// it is a word (what a space writes, in the centre, whatever is commoner:
    /// "hola" before "holandés"); then the user's words starting so, which
    /// keep their place as what the word may have meant comes in after them,
    /// once found; the word list's starting so only where room is left.
    /// </summary>
    public IReadOnlyList<string> Suggest(StandardLanguage language, string typed)
    {
        IReadOnlyList<string> listed = WordListOf(language).InUse() is { } ready ? ready.For(typed) : [];
        if (!fixes.TryGetValue(typed, out var found))
        {
            FindFixes(language, typed);
            fixes.TryGetValue(typed, out found);
        }

        IReadOnlyList<string> itself = IsWord(language, typed) ? [typed] : [];
        return [.. itself.Concat(personal.Starting(language, typed)).Concat(found?.Words ?? []).Concat(listed).Distinct().Take(WordSuggestions.Offered)];
    }

    /// <summary>Whether a word is a word as it is written, its accents too: the language's word list's, the user's dictionary's or its spelling dictionary's (Hunspell's).</summary>
    public bool IsWord(StandardLanguage language, string word) =>
        WordListOf(language).InUse()?.HasSpelled(word) == true
        || personal.Words(language.Code).Contains(word, StringComparer.OrdinalIgnoreCase)
        || DictionaryFor(language)?.InUse()?.Knows(word) == true;

    /// <summary>Whether the language's word list has the word, accents aside; while the list is being read, taken as known, so nothing is offered by mistake; without one downloaded, not known by it.</summary>
    public bool Knows(StandardLanguage language, string word) =>
        WordListOf(language).InUse() is { } ready ? ready.Knows(word) : SwipeData.HasWordList(language);

    /// <summary>Whether no word list, no dictionary of the user's and no Hunspell of the language has the word; while the list is read, known, so nothing is offered to learn by mistake.</summary>
    public bool Unknown(StandardLanguage language, string word) =>
        !Knows(language, word) && !personal.Words(language.Code).Contains(word, StringComparer.OrdinalIgnoreCase) && DictionaryFor(language)?.InUse()?.Knows(word) != true;

    /// <summary>
    /// The words a word typed may have meant, at most as many as the toolbar
    /// shows, and whether it is misspelled; none for a word left as it is.
    /// </summary>
    public WordFixes? Fix(StandardLanguage language, string word)
    {
        if (Capital(language, word) is { } capital)
        {
            return new WordFixes([capital], Misspelled: true);
        }

        if (Checking(language, word) is not { } checking)
        {
            return null;
        }

        // Found already as it was typed, else looked for now: Hunspell's own limits keep the key waiting little.
        if (!fixes.TryGetValue(word, out var found))
        {
            fixes[word] = found = Corrections(word, checking);
        }

        return found.Words.Count == 0 ? null : found with { Words = [.. found.Words.Take(WordSuggestions.Offered)] };
    }

    /// <summary>
    /// A word the language writes only with a capital, as Gboard writes it,
    /// typed all in small letters: one of the language's own
    /// (<see cref="StandardLanguage.Capitals"/>: English's "i"), or one its
    /// Hunspell knows only so ("english": English, "i'm": I'm, "españa":
    /// España), a single letter too; none with Correct words off, nor for
    /// one the user's dictionary has as typed.
    /// </summary>
    private string? Capital(StandardLanguage language, string word)
    {
        if (!applied(language.Code).Words || word.Length == 0 || !char.IsLetter(word[0]) || word.Any(char.IsUpper)
            || personal.Words(language.Code).Contains(word, StringComparer.Ordinal))
        {
            return null;
        }

        var capital = char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..];
        return language.Capitals.Contains(word)
            || (DictionaryFor(language)?.InUse() is { } spelling && !spelling.Knows(word) && spelling.Knows(capital))
            ? capital
            : null;
    }

    /// <summary>What a word is checked with, read on the keyboard's thread: Correct words on, the language's dictionary loaded, the common misspellings in Spanish; and whether the word is one (a list's, the accents aside, those being the accent model's; the user's; Hunspell's). None, it is not checked.</summary>
    private sealed record Check(SpellingDictionary Spelling, CommonErrors? Common, WordSuggestions? Listed, IReadOnlyList<string> Personal, bool Known);

    private Check? Checking(StandardLanguage language, string word)
    {
        if (!applied(language.Code).Words || word.Length < 2 || DictionaryFor(language)?.InUse() is not { } spelling)
        {
            return null;
        }

        var listed = WordListOf(language).InUse();
        var own = personal.Words(language.Code);
        var known = Knows(language, word) || own.Contains(word, StringComparer.OrdinalIgnoreCase) || spelling.Knows(word);

        // The common misspellings are Spanish ones.
        return new Check(spelling, language.Code == StandardKeys.Spanish.Code ? commonErrors.InUse() : null, listed, own, known);
    }

    /// <summary>
    /// What the word may have meant: a common misspelling's words first, in
    /// their list's order; then, for a word that is none, Hunspell's and the
    /// nearest of the word list and of the user's dictionary ("bidominoi":
    /// bidominio), the user's as common as swiping has them
    /// (<see cref="PersonalWords.Frequency"/>). A word that is a word ("as") is only offered its
    /// common ones, never corrected alone. Nothing for a word Hunspell fails
    /// on, which is then not looked for again.
    /// </summary>
    private WordFixes Corrections(string word, Check checking)
    {
        IReadOnlyList<string> common = checking.Common?.For(word) ?? [];
        if (checking.Known)
        {
            return new(common, Misspelled: false);
        }

        try
        {
            var listed = checking.Listed;
            var own = checking.Personal.Where(mine => checking.Spelling.Close(word, mine)).ToList();
            var near = own.Concat(listed?.Near(word, other => checking.Spelling.Close(word, other)) ?? []);
            var suggested = checking.Spelling.Suggest(word, fix => own.Contains(fix) ? PersonalWords.Frequency : listed?.Frequency(fix) ?? 0, near);
            return new([.. common.Concat(suggested).Distinct()], Misspelled: true);
        }
        catch (Exception failure)
        {
            log($"Spelling: a word's corrections could not be found: {failure.Message}");
            return new(common, Misspelled: true);
        }
    }

    /// <summary>
    /// A word being typed: what it may have meant, a word's at once (a list's
    /// look-up), a misspelled one's away from the keyboard's thread
    /// (Hunspell's search) and told once found (<see cref="FixesFound"/>).
    /// </summary>
    private void FindFixes(StandardLanguage language, string word)
    {
        if (fixes.ContainsKey(word) || Checking(language, word) is not { } checking)
        {
            return;
        }

        if (fixes.Count > FixesKept)
        {
            fixes.Clear();
        }

        if (checking.Known)
        {
            fixes[word] = Corrections(word, checking);
            return;
        }

        Task.Run(() => fixes[word] = Corrections(word, checking))
            .ContinueWith(_ => FixesFound?.Invoke(language, word), TaskScheduler.FromCurrentSynchronizationContext());
    }
}
