using System.Globalization;
using System.Text;

namespace Berpiztu.KeyPlus;

/// <summary>What a spoken command asks of the keyboard beyond the words it writes.</summary>
public enum VoiceAction
{
    /// <summary>Enter, as the keyboard's key.</summary>
    Send,

    /// <summary>The microphone turned off.</summary>
    Stop,

    /// <summary>The list of commands shown.</summary>
    Help,

    /// <summary>The field's last word deleted: the dictation had none of its own to delete.</summary>
    DeleteWord,

    /// <summary>The field's last sentence deleted: the dictation had none of its own to delete.</summary>
    DeleteSentence,

    /// <summary>All the field's text selected.</summary>
    SelectAll,

    /// <summary>The paragraph the caret is in selected.</summary>
    SelectParagraph,

    /// <summary>What is selected in the field deleted; nothing where nothing is.</summary>
    DeleteSelection,

    /// <summary>What is selected copied.</summary>
    Copy,

    /// <summary>What is selected cut.</summary>
    Cut,

    /// <summary>What was copied pasted at the caret, or over what is selected.</summary>
    Paste,

    /// <summary>The caret at the text's end, nothing selected.</summary>
    GoEnd,

    /// <summary>The caret at the text's start, nothing selected.</summary>
    GoStart,

    /// <summary>What is selected, or all the text where nothing is, read by the language's Spelling models and written again as they correct it.</summary>
    Spelling,

    /// <summary>The field's own undo, as Ctrl and Z: the dictation had no command of its own to take back.</summary>
    Undo,

    /// <summary>What is selected, or all the text where nothing is, asked of Android's spell checker for its grammar, and what it marks left marked in the field.</summary>
    Grammar,

    /// <summary>Capitals or Small letters said: where text is selected, its case turned to the next, as the keyboard's Shift tapped twice does; with none selected, nothing, the next word being written so.</summary>
    SelectionCase,
}

/// <summary>A part of the commands' list as Help shows it: its name and its commands, one a line, how each is said after the keyword and what it writes or does.</summary>
public sealed record VoiceHelpSection(string Title, IReadOnlyList<string> Lines);

/// <summary>How a dictation's words are written: as heard, or all in capitals while Caps lock is on.</summary>
public enum VoiceCase
{
    AsHeard,
    Capitals,
}

/// <summary>A dictation as its commands leave it: the text to write, what they ask of the keyboard besides, in the order said, and how its words are being written at its end, for the dictation to go on so when it starts afresh.</summary>
public sealed record Spoken(string Text, IReadOnlyList<VoiceAction> Actions, VoiceCase Case = VoiceCase.AsHeard)
{
    /// <summary>
    /// What was heard after each keyword, in the order said, as the keyboard
    /// says it in its toolbar (the owner, 2026-11-14, to see why a command
    /// is not done): the keyword and the command it was taken for, or, for
    /// none, the words heard between « » with a question mark.
    /// </summary>
    public IReadOnlyList<string> Understood { get; init; } = [];
}

/// <summary>
/// The commands a dictation may hold, the owner's of 2026-11-14: the word
/// "comando" ("command") and then what is wanted, "comando coma", "command
/// new line". A dictation's words are read again whole each time they come,
/// so its commands are applied to the whole text each time, the same way:
/// those that write (a mark, a capital, a new line, a word deleted) change
/// the text, the others (send, stop, help, and a deletion with nothing of
/// the dictation's to delete) are told to the keyboard, which does each
/// once. Undo takes back the dictation's own last command that writes; with
/// none left, it is the field's own undo (Ctrl and Z), done only once the
/// dictation's words are written for good and it has started afresh: undone
/// while they were still being heard, the field stepped back through them,
/// the commands' own among them, and left them written. Either language's commands are heard after either word,
/// as they are said mixed ("comando help"); the language only decides the
/// marks (Spanish opens a question with ¿) and the list Help shows.
/// </summary>
public static class VoiceCommands
{
    /// <summary>
    /// The word a dictation puts between two utterances (what was said
    /// between two pauses), where it knows them, as Android's recognizer
    /// does: never written, it lets a command be known by itself. The
    /// recognizer loses or mishears an utterance's first word at times
    /// ("seleccionar todo", "cobalto seleccionar todo" for "comando
    /// seleccionar todo", the owner's headset, 2026-11-14), so an utterance
    /// that is nothing but a command of several words, with at most one word
    /// before it, is that command.
    /// </summary>
    public const string UtteranceBreak = "\u241E";

    /// <summary>The word a command starts with, in Spanish and in English.</summary>
    private static readonly string[] Keywords = ["comando", "command"];

    private enum Kind
    {
        /// <summary>A mark written against the word before it, a space after: , . ; :</summary>
        Close,

        /// <summary>A mark written with no space either side: - @ /</summary>
        Join,
        Question,
        Exclamation,
        Quotes,
        Parenthesis,
        CapsLock,
        Capitals,
        Small,
        Cancel,
        Space,
        Line,
        Paragraph,
        DeleteWord,
        DeleteSentence,
        SelectAll,
        SelectParagraph,
        DeleteSelection,
        Copy,
        Cut,
        Paste,
        GoEnd,
        GoStart,
        Spelling,
        Grammar,
        Undo,
        Send,
        Stop,
        Help,
    }

    /// <summary>A command: how it is said in Spanish and in English, the first of each the one Help shows, the mark it writes, if any, and what Help says it does where it writes none.</summary>
    private sealed record Command(Kind Kind, string[] Spanish, string[] English, string Writes = "", string DoesSpanish = "", string DoesEnglish = "");

    private static readonly Command[] Commands =
    [
        new(Kind.Close, ["coma"], ["comma"], ","),
        new(Kind.Close, ["punto"], ["period", "full stop", "dot"], "."),
        new(Kind.Close, ["punto y coma"], ["semicolon"], ";"),
        new(Kind.Close, ["dos puntos"], ["colon"], ":"),
        new(Kind.Question, ["interrogación"], ["question", "question mark"], "?", "¿ … ?", "?"),
        new(Kind.Exclamation, ["exclamación"], ["exclamation", "exclamation mark", "exclamation point"], "!", "¡ … !", "!"),
        new(Kind.Quotes, ["comillas"], ["quotes", "quote"], "\""),
        new(Kind.Parenthesis, ["paréntesis"], ["parenthesis"], "(", "( … )", "( … )"),
        new(Kind.Join, ["guion"], ["dash", "hyphen"], "-"),
        new(Kind.Join, ["arroba"], ["at"], "@"),
        new(Kind.Join, ["barra"], ["slash"], "/"),
        new(Kind.CapsLock, ["bloqueo mayúsculas", "bloqueo mayúscula", "bloquear mayúsculas", "todo mayúsculas", "todo mayúscula"], ["caps lock", "all caps"], DoesSpanish: "TODO ASÍ; otra vez, lo quita", DoesEnglish: "ALL SO; again, ends it"),
        new(Kind.Capitals, ["mayúsculas", "mayúscula"], ["capitals", "capital"], DoesSpanish: "siguiente: Así, o gira la selección", DoesEnglish: "next: So, or turns the selection"),
        new(Kind.Small, ["minúsculas", "minúscula"], ["small letters", "lowercase", "lower case"], DoesSpanish: "siguiente: así, o gira la selección", DoesEnglish: "next: so, or turns the selection"),
        new(Kind.Cancel, ["cancelar", "cancela"], ["cancel"], DoesSpanish: "quita el bloqueo", DoesEnglish: "ends the lock"),
        new(Kind.Space, ["espacio"], ["space"], DoesSpanish: "un espacio", DoesEnglish: "a space"),
        new(Kind.Line, ["nueva línea"], ["new line"], DoesSpanish: "salto de línea", DoesEnglish: "a new line"),
        new(Kind.Paragraph, ["nuevo párrafo"], ["new paragraph"], DoesSpanish: "dos saltos", DoesEnglish: "two new lines"),
        new(Kind.DeleteWord, ["borra palabra", "borrar palabra"], ["delete word"], DoesSpanish: "última palabra", DoesEnglish: "the last word"),
        new(Kind.DeleteSentence, ["borra frase", "borrar frase"], ["delete sentence"], DoesSpanish: "última frase", DoesEnglish: "the last sentence"),
        new(Kind.SelectAll, ["seleccionar todo", "selecciona todo"], ["select all"], DoesSpanish: "todo el texto", DoesEnglish: "all the text"),
        new(Kind.SelectParagraph, ["seleccionar párrafo", "selecciona párrafo"], ["select paragraph"], DoesSpanish: "el párrafo", DoesEnglish: "the paragraph"),
        new(Kind.DeleteSelection, ["borrar", "borra"], ["delete"], DoesSpanish: "lo seleccionado", DoesEnglish: "what is selected"),
        new(Kind.Copy, ["copiar", "copia"], ["copy"], DoesSpanish: "lo seleccionado", DoesEnglish: "what is selected"),
        new(Kind.Cut, ["cortar", "corta"], ["cut"], DoesSpanish: "lo seleccionado", DoesEnglish: "what is selected"),
        new(Kind.Paste, ["pegar", "pega"], ["paste"], DoesSpanish: "lo copiado", DoesEnglish: "what was copied"),
        new(Kind.GoEnd, ["fin", "final"], ["end"], DoesSpanish: "al final del texto", DoesEnglish: "to the text's end"),
        new(Kind.GoStart, ["inicio", "principio"], ["start", "home"], DoesSpanish: "al inicio del texto", DoesEnglish: "to the text's start"),
        new(Kind.Spelling, ["ortografía"], ["spelling"], DoesSpanish: "corrige lo seleccionado", DoesEnglish: "corrects what is selected"),
        new(Kind.Grammar, ["gramática"], ["grammar"], DoesSpanish: "marca la gramática", DoesEnglish: "marks the grammar"),
        new(Kind.Undo, ["deshacer"], ["undo"], DoesSpanish: "lo último", DoesEnglish: "the last thing"),
        new(Kind.Send, ["enviar"], ["send"], DoesSpanish: "Intro", DoesEnglish: "Enter"),
        new(Kind.Stop, ["parar"], ["stop"], DoesSpanish: "apaga el micro", DoesEnglish: "microphone off"),
        new(Kind.Help, ["ayuda"], ["help"], DoesSpanish: "esta lista", DoesEnglish: "this list"),
    ];

    /// <summary>Every way a command is said, as its words without accents or capitals, the longest first, so "punto y coma" is heard before "punto".</summary>
    private static readonly (string[] Words, Command Command)[] Phrases =
    [
        .. Commands
            .SelectMany(command => command.Spanish.Concat(command.English).Select(said => (Words: Plain(said).Split(' '), Command: command)))
            .OrderByDescending(phrase => phrase.Words.Length),
    ];

    private static bool SpanishOf(string locale) => locale.StartsWith("es", StringComparison.OrdinalIgnoreCase);

    /// <summary>The parts Help lists the commands in, in its order, each with its name in Spanish and in English and the kinds of command it holds.</summary>
    private static readonly (string Spanish, string English, Kind[] Kinds)[] Sections =
    [
        ("Puntuación", "Punctuation", [Kind.Close, Kind.Question, Kind.Exclamation, Kind.Quotes, Kind.Parenthesis]),
        ("Símbolos", "Symbols", [Kind.Join]),
        ("Mayúsculas", "Capitals", [Kind.CapsLock, Kind.Capitals, Kind.Small, Kind.Cancel]),
        ("Espacios y líneas", "Spaces and lines", [Kind.Space, Kind.Line, Kind.Paragraph]),
        ("Borrar", "Delete", [Kind.DeleteWord, Kind.DeleteSentence, Kind.DeleteSelection, Kind.Undo]),
        ("Seleccionar y portapapeles", "Select and clipboard", [Kind.SelectAll, Kind.SelectParagraph, Kind.Copy, Kind.Cut, Kind.Paste, Kind.Spelling, Kind.Grammar]),
        ("Moverse", "Move", [Kind.GoEnd, Kind.GoStart]),
        ("Acciones", "Actions", [Kind.Send, Kind.Stop, Kind.Help]),
    ];

    /// <summary>What Help's window says on top, in a language by its locale: the keyword every command starts with.</summary>
    public static string HelpTitle(string locale) => SpanishOf(locale) ? $"Di «{Keywords[0]}» y…" : $"Say “{Keywords[1]}” and…";

    /// <summary>The commands as Help lists them in a language by its locale, part by part: how each is said after the keyword and what it writes or does; the keyword said twice, the word itself, with the last part's.</summary>
    public static IReadOnlyList<VoiceHelpSection> Help(string locale)
    {
        var spanish = SpanishOf(locale);
        var keyword = Keywords[spanish ? 0 : 1];
        return
        [
            .. Sections.Select((section, at) => new VoiceHelpSection(
                spanish ? section.Spanish : section.English,
                [
                    .. Commands.Where(command => section.Kinds.Contains(command.Kind)).Select(command =>
                    {
                        var does = spanish ? command.DoesSpanish : command.DoesEnglish;
                        return $"{(spanish ? command.Spanish : command.English)[0]}  →  {(does.Length > 0 ? does : command.Writes)}";
                    }),
                    .. at == Sections.Length - 1 ? new[] { $"{keyword}  →  {keyword}" } : [],
                ])),
        ];
    }

    /// <summary>
    /// A text as its sentences and what is between them, in turn, the
    /// sentences first (the even ones): cut after . ? ! or … and the space
    /// that follows, and at new lines. The Spelling command's: a model reads a
    /// sentence at a time, and what is between two stays as it is.
    /// </summary>
    public static string[] SentencesOf(string text) => System.Text.RegularExpressions.Regex.Split(text, @"((?<=[.?!…])[ \t]+|[ \t]*[\r\n]+[ \t]*)");

    /// <summary>
    /// A sentence with each of its words as Correct words writes it: the word
    /// a misspelled one meant, told by <paramref name="meant"/> (none for a
    /// word left as it is), with the capital the word had. The Spelling
    /// command's, where the language's context model is off.
    /// </summary>
    public static string WordsCorrected(string sentence, Func<string, string?> meant) =>
        System.Text.RegularExpressions.Regex.Replace(sentence, @"[\p{L}\p{M}]{2,}", word =>
            meant(word.Value) is not { Length: > 0 } written ? word.Value
            : char.IsUpper(word.Value[0]) && char.IsLower(written[0]) ? char.ToUpperInvariant(written[0]) + written[1..]
            : written);

    /// <summary>
    /// Help's parts laid in so many columns, in their order, each column
    /// about as long as the others: a part goes whole in one, and the next
    /// column begins once one holds its share of the lines.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<VoiceHelpSection>> InColumns(IReadOnlyList<VoiceHelpSection> sections, int columns)
    {
        // A part's lines as it is drawn: its name, its commands and the empty line after it.
        static int LinesOf(VoiceHelpSection section) => section.Lines.Count + 2;

        var share = (double)sections.Sum(LinesOf) / columns;
        List<List<VoiceHelpSection>> laid = [[]];
        var held = 0;
        foreach (var section in sections)
        {
            if (laid.Count < columns && held > 0 && held + (LinesOf(section) / 2.0) > share)
            {
                laid.Add([]);
                held = 0;
            }

            laid[^1].Add(section);
            held += LinesOf(section);
        }

        return laid;
    }

    /// <summary>Whether a dictation's text starts with something written against what is before it (a mark, a new line, a space of its own), so no space goes before it.</summary>
    public static bool StartsAttached(string text) => text.Length > 0 && ",.;:?!)-@/\n ".Contains(text[0]);

    private enum Glue
    {
        Word,
        Close,
        Open,
        Join,

        /// <summary>A space or new lines of a command's own: nothing is added either side.</summary>
        Raw,
    }

    private sealed record Piece(string Text, Glue Glue, bool EndsSentence = false);

    /// <summary>A word heard, or a command with the words that said it.</summary>
    private sealed record Item(string? Word, Command? Command);

    /// <summary>
    /// A dictation's words with its commands applied, in a language by its
    /// locale, its words written from the start as <paramref name="written"/>
    /// says (a dictation started afresh goes on in the capitals
    /// locked it was in). Caps lock is the key's (the owner, 2026-11-14):
    /// every word after it in capitals until it is said again, or Cancel;
    /// Capitals and Small letters write the next word so, and ask the keyboard
    /// to turn the selection's case where there is one. While more words may come (<paramref name="open"/>), the word
    /// "comando" at the end, or with the start of a command of several
    /// words, is left unwritten, as its command is still being said; at the
    /// end of a dictation it is written as any word.
    /// </summary>
    public static Spoken Apply(string heard, string locale, bool open, VoiceCase written = VoiceCase.AsHeard)
    {
        var spanish = SpanishOf(locale);
        List<string> understood = [];
        var items = TakenBack(Items(heard.Split(' ', StringSplitOptions.RemoveEmptyEntries), open, (said, command) => understood.Add(
            command is null ? $"{Keywords[spanish ? 0 : 1]} «{said}» ?" : $"{Keywords[spanish ? 0 : 1]} {(spanish ? command.Spanish : command.English)[0]}")));
        List<Piece> pieces = [];
        List<VoiceAction> actions = [];
        var capital = false;
        var small = false;
        var questionOpen = false;
        var exclamationOpen = false;
        var quotesOpen = false;
        var parenthesisOpen = false;
        foreach (var item in items)
        {
            if (item.Word is { } word)
            {
                pieces.Add(new(
                    written == VoiceCase.Capitals ? word.ToUpperInvariant()
                    : small ? word.ToLowerInvariant()
                    : capital ? char.ToUpperInvariant(word[0]) + word[1..]
                    : word,
                    Glue.Word));
                capital = false;
                small = false;
                continue;
            }

            var command = item.Command!;
            switch (command.Kind)
            {
                case Kind.Close:
                    var ends = command.Writes == ".";
                    pieces.Add(new(command.Writes, Glue.Close, ends));
                    capital |= ends;
                    break;
                case Kind.Join:
                    pieces.Add(new(command.Writes, Glue.Join));
                    break;
                case Kind.Question:
                    Pair(ref questionOpen, "¿", "?");
                    break;
                case Kind.Exclamation:
                    Pair(ref exclamationOpen, "¡", "!");
                    break;
                case Kind.Quotes:
                    pieces.Add(new("\"", quotesOpen ? Glue.Close : Glue.Open));
                    quotesOpen = !quotesOpen;
                    break;
                case Kind.Parenthesis:
                    pieces.Add(parenthesisOpen ? new(")", Glue.Close) : new("(", Glue.Open));
                    parenthesisOpen = !parenthesisOpen;
                    break;
                case Kind.CapsLock:
                    // The Caps lock key's: said again, it ends it.
                    written = written == VoiceCase.Capitals ? VoiceCase.AsHeard : VoiceCase.Capitals;
                    break;
                case Kind.Capitals:
                    capital = true;
                    small = false;
                    actions.Add(VoiceAction.SelectionCase);
                    break;
                case Kind.Small:
                    small = true;
                    capital = false;
                    actions.Add(VoiceAction.SelectionCase);
                    break;
                case Kind.Cancel:
                    written = VoiceCase.AsHeard;
                    break;
                case Kind.Space:
                    pieces.Add(new(" ", Glue.Raw));
                    break;
                case Kind.Line:
                    pieces.Add(new("\n", Glue.Raw, EndsSentence: true));
                    capital = true;
                    break;
                case Kind.Paragraph:
                    pieces.Add(new("\n\n", Glue.Raw, EndsSentence: true));
                    capital = true;
                    break;
                case Kind.DeleteWord:
                    if (pieces.FindLastIndex(piece => piece.Glue == Glue.Word) is >= 0 and var last)
                    {
                        pieces.RemoveRange(last, pieces.Count - last);
                        capital = pieces.Count > 0 && pieces[^1].EndsSentence;
                    }
                    else
                    {
                        actions.Add(VoiceAction.DeleteWord);
                    }

                    break;
                case Kind.DeleteSentence:
                    if (pieces.Exists(piece => piece.Glue == Glue.Word))
                    {
                        // The sentence's own end first, then back to the end of the one before it, which stays.
                        var from = pieces.FindLastIndex(piece => piece.Glue == Glue.Word);
                        from = pieces.FindLastIndex(from, piece => piece.EndsSentence) + 1;
                        pieces.RemoveRange(from, pieces.Count - from);
                        capital = pieces.Count > 0;
                    }
                    else
                    {
                        actions.Add(VoiceAction.DeleteSentence);
                    }

                    break;
                case Kind.SelectAll:
                    actions.Add(VoiceAction.SelectAll);
                    break;
                case Kind.SelectParagraph:
                    actions.Add(VoiceAction.SelectParagraph);
                    break;
                case Kind.DeleteSelection:
                    actions.Add(VoiceAction.DeleteSelection);
                    break;
                case Kind.Copy:
                    actions.Add(VoiceAction.Copy);
                    break;
                case Kind.Cut:
                    actions.Add(VoiceAction.Cut);
                    break;
                case Kind.Paste:
                    actions.Add(VoiceAction.Paste);
                    break;
                case Kind.GoEnd:
                    actions.Add(VoiceAction.GoEnd);
                    break;
                case Kind.GoStart:
                    actions.Add(VoiceAction.GoStart);
                    break;
                case Kind.Spelling:
                    actions.Add(VoiceAction.Spelling);
                    break;
                case Kind.Grammar:
                    actions.Add(VoiceAction.Grammar);
                    break;
                case Kind.Undo:
                    // None of the dictation's commands left to take back: the field's own undo.
                    actions.Add(VoiceAction.Undo);
                    break;
                case Kind.Send:
                    actions.Add(VoiceAction.Send);
                    break;
                case Kind.Stop:
                    actions.Add(VoiceAction.Stop);
                    break;
                case Kind.Help:
                    actions.Add(VoiceAction.Help);
                    break;
            }
        }

        return new(Joined(pieces), actions, written) { Understood = understood };

        // A mark that opens and closes: in Spanish its opening one first, then its closing one; in English only the closing one.
        void Pair(ref bool isOpen, string opening, string closing)
        {
            if (spanish && !isOpen)
            {
                pieces.Add(new(opening, Glue.Open));
                isOpen = true;
                return;
            }

            pieces.Add(new(closing, Glue.Close, EndsSentence: true));
            capital = true;
            isOpen = false;
        }
    }

    /// <summary>
    /// The words heard as words and commands: the keyword and a command's
    /// words after it are that command (told to <paramref name="heard"/>
    /// with the words taken for it, or with none for words no command is near
    /// to), as said or as near as the recognizer
    /// wrote them (<see cref="Nearest"/>). The keyword with no command known
    /// after it is not written, nor is what was said for one (the owner,
    /// 2026-11-14: a command not understood must leave nothing to delete by
    /// hand); said twice, it is the word itself.
    /// </summary>
    private static List<Item> Items(string[] words, bool open, Action<string, Command?> heard)
    {
        List<Item> items = [];
        for (var at = 0; at < words.Length; at++)
        {
            if (words[at] == UtteranceBreak)
            {
                continue;
            }

            // An utterance that is nothing but a command of several words, its keyword lost or heard as another word.
            if ((at == 0 || words[at - 1] == UtteranceBreak) && Alone(words, at) is { } alone)
            {
                items.Add(new(null, alone.Command));
                heard(string.Join(' ', words.Skip(at).Take(alone.Taken)), alone.Command);
                at += alone.Taken - 1;
                continue;
            }

            if (!IsKeyword(Plain(words[at])))
            {
                items.Add(new(words[at], null));
                continue;
            }

            // The recognizer writes a mark said after the keyword as the mark itself, against it ("comando punto" comes as
            // "Comando."): that mark's command.
            if (MarkOf(words[at]) is { } mark)
            {
                items.Add(new(null, mark));
                heard(words[at][^1].ToString(), mark);
                continue;
            }

            var after = words.Skip(at + 1).Select(Plain).ToArray();
            if (after.Length > 0 && IsKeyword(after[0]))
            {
                // The keyword said twice is the word itself, written once.
                items.Add(new(words[at], null));
                at++;
                continue;
            }

            var said = Phrases.FirstOrDefault(phrase => after.Length >= phrase.Words.Length && phrase.Words.SequenceEqual(after.Take(phrase.Words.Length)));
            var (command, taken) = said.Command is not null ? (said.Command, said.Words.Length) : Nearest(after);
            if (command is not null)
            {
                items.Add(new(null, command));
                heard(string.Join(' ', words.Skip(at + 1).Take(taken)), command);
                at += taken;
            }
            else if (open && Phrases.Any(phrase => StillSaid(phrase.Words, after)))
            {
                // Its command is still being said: nothing of it is written yet.
                break;
            }
            else if (after.Length > 0)
            {
                // No command known: nothing of it is written, neither the keyword nor the word after it, nor the next
                // where that one begins a command of several words ("comando borrar casa"), nor those after it that
                // are a command's own ("comando luego párrafo" wrote "párrafo").
                var gone = after.Length > 1 && BeginsSeveral(after[0]) ? 2 : 1;
                while (gone < after.Length && OwnWords.Contains(after[gone]))
                {
                    gone++;
                }

                // Where the utterances are told apart, all the rest of its own: after the keyword everything is the
                // command, the owner's rule, and the pause is where it ends ("comando se retirar todo" wrote "retirar todo").
                if (words[0] == UtteranceBreak)
                {
                    gone = Math.Max(gone, after.TakeWhile(word => word != UtteranceBreak).Count());
                }

                heard(string.Join(' ', words.Skip(at + 1).Take(gone)), null);
                at += gone;
            }
        }

        return items;
    }

    /// <summary>Whether a word, as commands are matched, is the keyword, or its plural, which the recognizer writes for it at times ("comandos seleccionar todo").</summary>
    private static bool IsKeyword(string plain) => Keywords.Contains(plain) || (plain.EndsWith('s') && Keywords.Contains(plain[..^1]));

    /// <summary>The words that are a command's own, of every command in both languages, the short ones aside ("y", "dos", "new"): after a command not understood they are part of it, not of the text.</summary>
    private static readonly HashSet<string> OwnWords = [.. Phrases.SelectMany(phrase => phrase.Words).Where(word => word.Length >= LeastOwnWord)];

    /// <summary>
    /// The command an utterance starting at a word is by itself, with how
    /// many words it takes: all of the utterance a command of several words
    /// as said, or that with one word before it that is not the keyword (the
    /// keyword misheard). None for an utterance with anything else in it, or
    /// with the keyword, which is read as ever.
    /// </summary>
    private static (Command Command, int Taken)? Alone(string[] words, int from)
    {
        var said = words.Skip(from).TakeWhile(word => word != UtteranceBreak).Select(Plain).ToArray();
        if (said.Length < 2 || said.Any(IsKeyword))
        {
            return null;
        }

        foreach (var phrase in Phrases.Where(phrase => phrase.Words.Length > 1))
        {
            if (said.SequenceEqual(phrase.Words) || (said.Length == phrase.Words.Length + 1 && said.Skip(1).SequenceEqual(phrase.Words)))
            {
                return (phrase.Command, said.Length);
            }
        }

        return null;
    }

    /// <summary>The command of the mark a word ends with as the recognizer wrote it (. , ; : ? !); none where it ends with none.</summary>
    private static Command? MarkOf(string word) =>
        word.Length > 1 && ".,;:?!".Contains(word[^1])
            ? Commands.FirstOrDefault(command => command.Kind is Kind.Close or Kind.Question or Kind.Exclamation && command.Writes == word[^1].ToString())
            : null;

    /// <summary>Whether a word is, as written, the first of a command of several words ("borra", "nueva", "punto").</summary>
    private static bool BeginsSeveral(string word) => Phrases.Any(phrase => phrase.Words.Length > 1 && phrase.Words[0] == word);

    /// <summary>The shortest word that names a command of several words by itself ("frase", "línea"): a shorter one ("y", "dos") is no one's.</summary>
    private const int LeastOwnWord = 4;

    /// <summary>
    /// The command nearest to the words after the keyword, where they are
    /// none's exactly, with how many of them it takes (the owner, 2026-11-14:
    /// the recognizer writes "frases" or "frasis" for "frase", and the
    /// command meant is still that one): first one of several words, each
    /// near its own ("borrar frases"); then, unless the first word begins a
    /// command of several as written and its rest is to come or was not
    /// understood, after one of several with one word heard as another
    /// ("luego párrafo": nuevo párrafo) ("borra", which is no "barra"), the command of one word
    /// nearest to it ("para": parar), or the one of several that a word of
    /// its own names, no other command having one like it ("frase": borra
    /// frase; "nueva" names none, being new line's and new paragraph's).
    /// None where nothing is near.
    /// </summary>
    private static (Command? Command, int Taken) Nearest(string[] after)
    {
        if (after.Length == 0)
        {
            return (null, 0);
        }

        var several = Phrases
            .Where(phrase => phrase.Words.Length > 1 && phrase.Words.Length <= after.Length)
            .Select(phrase => (phrase.Command, phrase.Words.Length, Far: phrase.Words.Select((word, at) => Far(after[at], word)).ToArray()))
            .Where(near => near.Far.All(far => far is not null))
            .OrderByDescending(near => near.Length)
            .ThenBy(near => near.Far.Sum())
            .FirstOrDefault();
        if (several.Command is not null)
        {
            return (several.Command, several.Length);
        }

        // One of its words heard as another ("luego párrafo": nuevo párrafo): the others as said, one of them a word
        // of its own, and the one heard wrong no further from its own than half its letters; the nearest so.
        var oneWrong = Phrases
            .Where(phrase => phrase.Words.Length > 1 && phrase.Words.Length <= after.Length)
            .SelectMany(phrase => Enumerable.Range(0, phrase.Words.Length).Select(wrong => (phrase.Command, phrase.Words, Wrong: wrong)))
            .Where(tried => tried.Words.Select((word, at) => at == tried.Wrong || after[at] == word).All(same => same)
                && tried.Words.Where((_, at) => at != tried.Wrong).Any(word => word.Length >= LeastOwnWord)
                && Changes(after[tried.Wrong], tried.Words[tried.Wrong]) * 2 <= Math.Max(after[tried.Wrong].Length, tried.Words[tried.Wrong].Length))
            .OrderBy(tried => Changes(after[tried.Wrong], tried.Words[tried.Wrong]))
            .ThenByDescending(tried => tried.Words.Length)
            .FirstOrDefault();
        if (oneWrong.Command is not null)
        {
            return (oneWrong.Command, oneWrong.Words.Length);
        }

        if (BeginsSeveral(after[0]))
        {
            return (null, 0);
        }

        var one = Phrases
            .Where(phrase => phrase.Words.Length == 1 && Far(after[0], phrase.Words[0]) is not null)
            .OrderBy(phrase => Far(after[0], phrase.Words[0]))
            .FirstOrDefault();
        if (one.Command is not null)
        {
            return (one.Command, 1);
        }

        var named = Commands.Where(command => Phrases.Any(phrase => phrase.Command == command && phrase.Words.Any(word => Far(after[0], word) is not null))).ToArray();
        return after[0].Length >= LeastOwnWord && named.Length == 1 ? (named[0], 1) : (null, 0);
    }

    /// <summary>
    /// How far a word heard is from a command's: 0 the same, the letters
    /// changed, added or left out where they are few for its length (one in
    /// a word of four letters or more, two in one of seven or more), or 1.5
    /// where both begin with the same four letters and differ by two letters
    /// in length at most ("frasis": frase, "palabras": palabra); none where
    /// it is another word.
    /// </summary>
    private static double? Far(string heard, string word)
    {
        if (heard == word)
        {
            return 0;
        }

        var allowed = word.Length >= 7 ? 2 : word.Length >= 4 ? 1 : 0;
        var changes = Changes(heard, word);
        if (changes <= allowed)
        {
            return changes;
        }

        const int Stem = 4;
        return heard.Length >= Stem && word.Length >= Stem && heard[..Stem] == word[..Stem] && Math.Abs(heard.Length - word.Length) <= 2 ? 1.5 : null;
    }

    /// <summary>The letters to change, add or leave out to make one word the other (Levenshtein's distance).</summary>
    private static int Changes(string one, string other)
    {
        var before = Enumerable.Range(0, other.Length + 1).ToArray();
        for (var row = 1; row <= one.Length; row++)
        {
            var now = new int[other.Length + 1];
            now[0] = row;
            for (var column = 1; column <= other.Length; column++)
            {
                var changed = before[column - 1] + (one[row - 1] == other[column - 1] ? 0 : 1);
                now[column] = Math.Min(changed, Math.Min(before[column], now[column - 1]) + 1);
            }

            before = now;
        }

        return before[other.Length];
    }

    /// <summary>Whether the words after the keyword, the last ones heard, may be a command still being said: its first words, the last of them heard only in part ("comando nue", "comando punto y co").</summary>
    private static bool StillSaid(string[] phrase, string[] after) =>
        after.Length == 0
        || (after.Length <= phrase.Length
            && phrase.Take(after.Length - 1).SequenceEqual(after.Take(after.Length - 1))
            && phrase[after.Length - 1].StartsWith(after[^1], StringComparison.Ordinal)
            && !(after.Length == phrase.Length && phrase[^1] == after[^1]));

    /// <summary>The items without the commands an Undo takes back, each with the Undo that did: the last one before it that writes (a mark, a line, the capitals); a deletion is not one of them, for with no words of the dictation's it was done in the field already, and taking it back here undid nothing that was seen. An Undo with none before it stays, for the field's own undo.</summary>
    private static List<Item> TakenBack(List<Item> items)
    {
        List<Item> kept = [];
        Stack<int> writing = [];
        foreach (var item in items)
        {
            if (item.Command?.Kind == Kind.Undo && writing.Count > 0)
            {
                kept[writing.Pop()] = new(null, null);
                continue;
            }

            if (item.Command is { Kind: not (Kind.Undo or Kind.Send or Kind.Stop or Kind.Help or Kind.SelectAll or Kind.SelectParagraph or Kind.DeleteSelection or Kind.Copy or Kind.Cut or Kind.Paste or Kind.GoEnd or Kind.GoStart or Kind.Spelling or Kind.Grammar or Kind.DeleteWord or Kind.DeleteSentence) })
            {
                writing.Push(kept.Count);
            }

            kept.Add(item);
        }

        kept.RemoveAll(item => item.Word is null && item.Command is null);
        return kept;
    }

    /// <summary>The pieces as text: a space between two unless one of them is written against the other.</summary>
    private static string Joined(List<Piece> pieces)
    {
        var text = new StringBuilder();
        for (var at = 0; at < pieces.Count; at++)
        {
            var piece = pieces[at];
            if (at > 0 && piece.Glue is Glue.Word or Glue.Open && pieces[at - 1].Glue is Glue.Word or Glue.Close)
            {
                text.Append(' ');
            }

            text.Append(piece.Text);
        }

        return text.ToString();
    }

    /// <summary>A word as commands are matched: small letters, no accents, no marks around it.</summary>
    private static string Plain(string word)
    {
        var plain = new StringBuilder();
        foreach (var letter in word.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(char.ToLowerInvariant(letter));
            }
        }

        return plain.ToString().Trim(',', '.', ';', ':', '?', '!', '¿', '¡');
    }
}
