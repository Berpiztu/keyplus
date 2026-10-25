namespace Berpiztu.KeyPlus;

/// <summary>
/// Spanish's opening marks, the same on Android and Windows (Spelling › Put ¿
/// and ¡ at the start): a sentence closed with ? or ! that lacks its "¿" or
/// "¡" takes it at its start, "qué quieres?" written "¿qué quieres?". Its
/// accents are the accent model's (Correct accents with the model).
/// </summary>
public static class SpanishMarks
{
    /// <summary>
    /// The sentence a "?" or "!" is about to close, from the text before the
    /// caret, with its opening mark put at its start where it has none: how
    /// many characters before the caret to write again, and what with; none
    /// when it already has one, or there is no sentence.
    /// </summary>
    public static (int Replaced, string Text)? Opened(string before, char closing)
    {
        var sentence = before[(before.LastIndexOfAny(['.', '?', '!', '\n']) + 1)..].TrimStart();
        return sentence.Length == 0 || sentence[0] is '¿' or '¡'
            ? null
            : (sentence.Length, (closing == '?' ? "¿" : "¡") + sentence);
    }
}
