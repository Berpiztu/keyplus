namespace Berpiztu.KeyPlus;

/// <summary>
/// A language's Spelling, chosen on its page under Languages: its words
/// corrected (its dictionary), what is corrected written at once
/// (Autocorrect), and, where the language has them
/// (<see cref="Offers"/>), its accents by the model, the Wikipedia's common
/// misspellings and the opening marks.
/// </summary>
public sealed record LanguageSpelling(bool Words = false, bool Autocorrect = true, bool Accents = false, bool Wikipedia = false, bool OpenMark = false)
{
    /// <summary>Nothing corrected: Spelling off from the toolbar.</summary>
    public static LanguageSpelling Off { get; } = new(Words: false, Autocorrect: false);

    /// <summary>What Spelling has for a language beyond its words and Autocorrect: Spanish its accent model, the Wikipedia's list and ¿ ¡; another, nothing more.</summary>
    public static bool Offers(string language) => language == StandardKeys.Spanish.Code;
}

/// <summary>The words a word typed may have meant, the likeliest first, and whether it is misspelled, so the first may be written in its place; a word that is a word too ("as": has, haz) only has them offered.</summary>
public sealed record WordFixes(IReadOnlyList<string> Words, bool Misspelled);
