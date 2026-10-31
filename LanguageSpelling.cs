namespace Berpiztu.KeyPlus;

/// <summary>
/// A language's Spelling, chosen on its page under Languages: its words
/// corrected (its dictionary), what is corrected written at once
/// (Autocorrect), and, where the language has them
/// (<see cref="Offers"/>), its accents by the model, the Wikipedia's common
/// misspellings and the opening marks; its punctuation by the model where
/// it has one (<see cref="PunctuationModel.For"/>); on Android, the context
/// model that reads its sentences.
/// </summary>
/// <param name="Context">
/// The context model that reads each sentence closed, by its name (a
/// corrector put on the device, <c>ContextCorrector</c>); none while it is
/// off. On, it alone corrects the language (<see cref="ContextAlone"/>).
/// </param>
/// <param name="Suggestions">
/// Whether what corrects (the context model, or the others) offers in the
/// toolbar: the words starting as typed, a word's correction, a sentence's;
/// Autocorrect beside it says whether a correction is written. Both off,
/// nothing is offered nor written. Android's for now.
/// </param>
public sealed record LanguageSpelling(bool Words = false, bool Autocorrect = true, bool Accents = false, bool Wikipedia = false, bool OpenMark = false, bool Punctuation = false, string? Context = null, bool Suggestions = true)
{
    /// <summary>
    /// The rule of a context model, the user's: on, it alone corrects the
    /// language, its words too, so what changed is its doing (the words
    /// offered as one is typed still come from the word list, which corrects
    /// nothing); every other correction off as the keyboard applies them (the
    /// words, the Wikipedia's misspellings, the accents and the punctuation by
    /// their models, ¿ and ¡), kept as chosen for when it is turned off.
    /// Autocorrect and Suggestions stay: they say whether its corrections are
    /// written, offered, both or neither. The settings' choices that are these
    /// are marked (<c>SpellingChoice.Corrects</c>).
    /// </summary>
    public LanguageSpelling ContextAlone() => Context is null ? this
        : this with { Words = false, Wikipedia = false, Accents = false, Punctuation = false, OpenMark = false };

    /// <summary>Nothing corrected: Spelling off from the toolbar.</summary>
    public static LanguageSpelling Off { get; } = new(Words: false, Autocorrect: false);

    /// <summary>What Spelling has for a language beyond its words and Autocorrect: Spanish its accent model, the Wikipedia's list and ¿ ¡; another, nothing more.</summary>
    public static bool Offers(string language) => language == StandardKeys.Spanish.Code;
}

/// <summary>The words a word typed may have meant, the likeliest first, and whether it is misspelled, so the first may be written in its place; a word that is a word too ("as": has, haz) only has them offered.</summary>
public sealed record WordFixes(IReadOnlyList<string> Words, bool Misspelled);
