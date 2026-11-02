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

    private readonly Dictionary<string, Loaded<PunctuationModel>> punctuation = [];

    /// <summary>A language's punctuation model, loaded as the accent model is; none where the language has none.</summary>
    public Loaded<PunctuationModel>? PunctuationOf(string language)
    {
        if (PunctuationModel.For(language) is not { } files)
        {
            return null;
        }

        if (!punctuation.TryGetValue(language, out var model))
        {
            punctuation[language] = model = new(() => files.Downloaded ? files.Stamp : null, () => PunctuationModel.Load(files), old => old.Dispose());
        }

        return model;
    }

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

        if (spelling.Punctuation)
        {
            _ = PunctuationOf(language.Code)?.Async();
        }

        if (spelling.Words)
        {
            _ = DictionaryFor(language)?.Async();
            _ = commonErrors.Async();
        }

        // The words offered as you type, where its word list is downloaded (with Glide typing).
        _ = WordListOf(language).Async();
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
    /// "hola" before "holandés"); then the user's words starting so at most a
    /// letter longer than typed, which keep their place as what the word may
    /// have meant comes in after them, once found; then the word list's first,
    /// its centre (<see cref="WordSuggestions.For"/>); the user's longer ones,
    /// "hi" not writing "Iosu" with a space; the list's others where room is left.
    /// </summary>
    public IReadOnlyList<string> Suggest(StandardLanguage language, string typed)
    {
        IReadOnlyList<string> listed = WordListOf(language).InUse() is { } ready ? ready.For(typed) : [];
        if (!fixes.TryGetValue(typed, out var found))
        {
            FindFixes(language, typed);
            fixes.TryGetValue(typed, out found);
        }

        IReadOnlyList<string> itself = Listed(language, typed) ? [Kept(language, typed) ?? typed] : [];
        var own = personal.Starting(language, typed);
        return [.. itself.Concat(own.Where(word => word.Length <= typed.Length + 1)).Concat(found?.Words ?? []).Concat(listed.Take(1)).Concat(own).Concat(listed).Distinct().Take(WordSuggestions.Offered)];
    }

    /// <summary>Whether a word is a word as it is written, its accents too: the language's word list's, the user's dictionary's or its spelling dictionary's (Hunspell's).</summary>
    public bool IsWord(StandardLanguage language, string word) =>
        WordListOf(language).InUse()?.HasSpelled(word) == true
        || personal.Words(language.Code).Contains(word, StringComparer.OrdinalIgnoreCase)
        || DictionaryFor(language)?.InUse()?.Knows(word) == true;

    /// <summary>Whether a word is in the centre as typed: the word list's as it is written, the user's; with no list, Hunspell's. One only Hunspell knows ("qu", "man") goes at the left, the words it may have meant in the centre (the owner, 2026-11-01).</summary>
    private bool Listed(StandardLanguage language, string word) =>
        WordListOf(language).InUse() is { } ready
            ? ready.HasSpelled(word) || personal.Words(language.Code).Contains(word, StringComparer.OrdinalIgnoreCase)
            : IsWord(language, word);

    /// <summary>Whether only Hunspell knows the word, no list nor dictionary of the user's: shown as typed at the left, not to be learnt.</summary>
    public bool OnlyInHunspell(StandardLanguage language, string word) =>
        word.Length >= 2 && WordListOf(language).InUse() is not null && !Listed(language, word) && DictionaryFor(language)?.InUse()?.Knows(word) == true;

    /// <summary>
    /// The word it is sure the one typed meant, in bold in the toolbar's centre and written by a space (the owner, 2026-11-01,
    /// as Gboard): the only word of the user's dictionary starting so, where no word of the list does ("ios": iosu); else
    /// the only one the list says it can mean (<see cref="WordSuggestions.OnlyMeant"/>); else, where nothing starts so, the
    /// only one of those offered near it for Hunspell ("nosotos": nosotros). None where it is not clear.
    /// </summary>
    public string? Sure(StandardLanguage language, string typed, IReadOnlyList<string> offered)
    {
        if (typed.Length < 2 || WordListOf(language).InUse() is not { } list || personal.Words(language.Code).Contains(typed, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (list.NothingStarts(typed) && SwipeWords.Letters(typed, language.SwipeLetters) is { } start)
        {
            var own = personal.Starting(language, typed).Where(word => SwipeWords.Letters(word, language.SwipeLetters)?.StartsWith(start, StringComparison.Ordinal) == true).ToList();
            if (own.Count == 1)
            {
                return own[0];
            }
        }

        if (list.OnlyMeant(typed) is { } only)
        {
            return only;
        }

        if (!list.NothingStarts(typed) || IsWord(language, typed) || DictionaryFor(language)?.InUse() is not { } spelling)
        {
            return null;
        }

        var close = offered.Where(word => spelling.Close(typed, word)).ToList();
        return close.Count == 1 ? close[0] : null;
    }

    /// <summary>Whether the language's word list has the word, accents aside; while the list is being read, taken as known, so nothing is offered by mistake; without one downloaded, not known by it.</summary>
    public bool Knows(StandardLanguage language, string word) =>
        WordListOf(language).InUse() is { } ready ? ready.Knows(word) : SwipeData.HasWordList(language);

    /// <summary>
    /// Of the words a context model offered, the one a misspelling means, in bold and written by a space (the owner, 2026-11-01):
    /// what was typed no word of the language, and only one of them a word that sounds as it, in Spanish, an h aside, or one
    /// letter written for another ("jente": gente). None else: nothing else of the list decides the model's words.
    /// </summary>
    public string? SureOfModel(StandardLanguage language, string typed, IReadOnlyList<string> offered)
    {
        if (language.Code != StandardKeys.Spanish.Code || typed.Length < 2 || IsWord(language, typed) || WordListOf(language).InUse() is null)
        {
            return null;
        }

        var sound = SpanishSounds.Key(typed);
        var alike = offered.Where(word => !word.Equals(typed, StringComparison.OrdinalIgnoreCase) && SpanishSounds.Key(word) == sound && Listed(language, word)).ToList();
        return alike.Count == 1 ? alike[0] : null;
    }

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
        var found = Found(word, checking);
        return found.Words.Count == 0 ? null : found with { Words = [.. found.Words.Take(WordSuggestions.Offered)] };
    }

    /// <summary>
    /// What a word no list nor dictionary has may have meant, as <see cref="Fix"/> finds it, with the context model on
    /// too (which turns Correct words off): offered in the toolbar's centre where nothing else is ("henorme": enorme), as
    /// Gboard's. None for a word known, or where the language's dictionary is not downloaded.
    /// </summary>
    public IReadOnlyList<string> Meant(StandardLanguage language, string word)
    {
        if (Checking(language, word, whateverChosen: true) is not { Known: false } checking)
        {
            return [];
        }

        return [.. Found(word, checking).Words.Take(WordSuggestions.Offered)];
    }

    /// <summary>
    /// A word's corrections, kept once found: asked from the keys' thread (a space) and from another (what a word nothing
    /// starts as may have meant, which Hunspell took 6 s to find for "corrrecta"), each found by one alone.
    /// </summary>
    private WordFixes Found(string word, Check checking)
    {
        lock (fixes)
        {
            if (fixes.TryGetValue(word, out var kept))
            {
                return kept;
            }
        }

        var found = Corrections(word, checking);
        lock (fixes)
        {
            fixes[word] = found;
        }

        return found;
    }

    /// <summary>
    /// The user's dictionary's way of writing a word typed in small letters,
    /// where it keeps it with capitals ("iosu": Iosu), as Gboard's: offered
    /// in the toolbar's centre in its place, and, with Autocorrect, written so;
    /// none where it keeps it as typed, or not at all.
    /// </summary>
    private string? Kept(StandardLanguage language, string typed) =>
        typed.Any(char.IsUpper) ? null
        : personal.Words(language.Code).FirstOrDefault(word => word.Any(char.IsUpper) && string.Equals(word, typed, StringComparison.OrdinalIgnoreCase));

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

        if (Kept(language, word) is { } kept)
        {
            return kept;
        }

        var capital = char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..];
        return language.Capitals.Contains(word)
            || (DictionaryFor(language)?.InUse() is { } spelling && !spelling.Knows(word) && spelling.Knows(capital))
            ? capital
            : null;
    }

    /// <summary>What a word is checked with, read on the keyboard's thread: Correct words on, the language's dictionary loaded, the common misspellings in Spanish; and whether the word is one (a list's, the accents aside, those being the accent model's; the user's; Hunspell's). None, it is not checked.</summary>
    private sealed record Check(SpellingDictionary Spelling, CommonErrors? Common, WordSuggestions? Listed, IReadOnlyList<string> Personal, bool Known);

    private Check? Checking(StandardLanguage language, string word, bool whateverChosen = false)
    {
        if (!(whateverChosen || applied(language.Code).Words) || word.Length < 2 || DictionaryFor(language)?.InUse() is not { } spelling)
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

        // One search at a time, the latest word typed waiting for it: typed fast, each letter's would run at once, together.
        if (searching)
        {
            waiting = (language, word);
            return;
        }

        searching = true;
        Task.Run(() => fixes[word] = Corrections(word, checking))
            .ContinueWith(_ =>
            {
                searching = false;
                FixesFound?.Invoke(language, word);
                if (waiting is { } next)
                {
                    waiting = null;
                    FindFixes(next.Language, next.Word);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Whether a misspelled word's search is running away from the keyboard's thread, and the word typed since, to look for next.</summary>
    private bool searching;
    private (StandardLanguage Language, string Word)? waiting;
}
