using Android.Content;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Berpiztu AI Keyboard's choices by language on Android, as Windows' (its
/// KeyboardPreferences): each language's Spelling (<see cref="LanguageSpelling"/>)
/// and Glide typing, kept among the keyboard's settings for every screen;
/// Spelling on and off at once (the toolbar's spellcheck tool, the settings'
/// Correct as I type); and what a language has downloaded to work with. A
/// language not chosen yet takes what the app had for every language before:
/// its opening marks Spanish's, Glide typing the old switch's.
/// </summary>
public static class LanguageChoices
{
    /// <summary>Spelling on and off at once: on unless turned off.</summary>
    public const string SpellingOnKey = "spellingOn";

    private static string Key(string part, string language) => $"{part}.{language}";

    /// <summary>A language's Spelling as chosen on its page, every choice as it was left (<see cref="Applied"/> says what the keyboard does).</summary>
    public static LanguageSpelling SpellingIn(Context context, string language)
    {
        var kept = KeyPlusPreferences.Global(context);
        var spanish = LanguageSpelling.Offers(language);
        return new LanguageSpelling(
            kept.GetBoolean(Key("spellingWords", language), false),
            kept.GetBoolean(Key("spellingAutocorrect", language), true),
            spanish && kept.GetBoolean(Key("spellingAccents", language), false),
            spanish && kept.GetBoolean(Key("spellingWikipedia", language), false),
            spanish && kept.GetBoolean(Key("spellingOpenMark", language), kept.GetBoolean(KeyPlusPreferences.SpellingOpenMarkKey, false)),
            PunctuationModel.For(language) is not null && kept.GetBoolean(Key("spellingPunctuation", language), false),
            kept.GetBoolean(Key("spellingContext", language), false) ? ContextModelIn(context, language) : null,
            kept.GetBoolean(Key("spellingSuggestions", language), true),
            SystemSpellingIn(context, language));
    }

    /// <summary>A language's context model chosen in its list, kept while the model is off too, so it is the one turned on again; none until one is chosen.</summary>
    public static string? ContextModelIn(Context context, string language) =>
        KeyPlusPreferences.Global(context).GetString(Key("spellingContextModel", language), "") is { Length: > 0 } name ? name : null;

    /// <summary>What corrects a language, one of three (the owner, 2026-11-14): Android's own spell checker, the first and the one until another is chosen; its context model; or the keyboard's own words (Correct words, with its accents and marks).</summary>
    public enum Corrector
    {
        System,
        Context,
        Words,
    }

    /// <summary>
    /// What corrects a language, as chosen on its page; until one is chosen
    /// there, what its Spelling had on before the choice was made (its
    /// context model, else its Correct words), and with neither, Android's
    /// own spell checker.
    /// </summary>
    public static Corrector CorrectsWith(Context context, string language)
    {
        var kept = KeyPlusPreferences.Global(context);
        return kept.GetString(Key("spellingBy", language), null) is { } chosen && Enum.TryParse<Corrector>(chosen, out var by) ? by
            : kept.GetBoolean(Key("spellingSystem", language), false) ? Corrector.System
            : kept.GetBoolean(Key("spellingContext", language), false) ? Corrector.Context
            : kept.GetBoolean(Key("spellingWords", language), false) ? Corrector.Words
            : Corrector.System;
    }

    /// <summary>What corrects a language kept as chosen: the context model's switch goes with it, on for the model (where one is chosen in its list) and off for the others, and Correct words on for the words.</summary>
    public static void SetCorrectsWith(Context context, string language, Corrector by)
    {
        var edit = KeyPlusPreferences.Global(context).Edit()!.PutString(Key("spellingBy", language), by.ToString())!
            .PutBoolean(Key("spellingContext", language), by == Corrector.Context && ContextModelIn(context, language) is not null)!;
        if (by == Corrector.Words)
        {
            edit.PutBoolean(Key("spellingWords", language), true);
        }

        edit.Apply();
    }

    /// <summary>Whether Android's own spell checker corrects a language.</summary>
    public static bool SystemSpellingIn(Context context, string language) => CorrectsWith(context, language) == Corrector.System;

    /// <summary>Whether a language's context model gives the words offered as a word is typed too (Use it for suggestions too); on until turned off, and turned on with the model.</summary>
    public static bool ContextSuggestsIn(Context context, string language) =>
        KeyPlusPreferences.Global(context).GetBoolean(Key("spellingContextSuggests", language), true);

    /// <summary>A language's Percentage for bold; 70 until one is written (the owner, 2026-11-01); left empty, 0, none by percentage.</summary>
    public static int BoldPercentIn(Context context, string language) => KeyPlusPreferences.Global(context).GetInt(Key("spellingBoldPercent", language), WordSuggestions.BoldPercentUsual);

    /// <summary>A language's Percentage for bold, kept as written.</summary>
    public static void SetBoldPercent(Context context, string language, int percent) => KeyPlusPreferences.Global(context).Edit()!.PutInt(Key("spellingBoldPercent", language), percent)!.Apply();

    /// <summary>Troubleshooting's Search beams, for every language; 5 until one is written.</summary>
    public static int ModelBeams(Context context) => KeyPlusPreferences.Global(context).GetInt("modelBeams", ContextCorrector.BeamsUsual);

    /// <summary>Troubleshooting's Search beams, kept as written.</summary>
    public static void SetModelBeams(Context context, int beams) => KeyPlusPreferences.Global(context).Edit()!.PutInt("modelBeams", beams)!.Apply();

    /// <summary>Whether the debug window shows what the context model gave (the toolbar's Debug), for every language; off until turned on.</summary>
    public static bool ModelWindow(Context context) => KeyPlusPreferences.Global(context).GetBoolean("modelWindow", false);

    /// <summary>The toolbar's Debug, kept as turned.</summary>
    public static void SetModelWindow(Context context, bool on) => KeyPlusPreferences.Global(context).Edit()!.PutBoolean("modelWindow", on)!.Apply();

    /// <summary>A language's Use it for suggestions too, kept as turned.</summary>
    public static void SetContextSuggests(Context context, string language, bool on) =>
        KeyPlusPreferences.Global(context).Edit()!.PutBoolean(Key("spellingContextSuggests", language), on)!.Apply();

    /// <summary>A language's context model chosen in its list, the model on or off.</summary>
    public static void SetContextModel(Context context, string language, string name) =>
        KeyPlusPreferences.Global(context).Edit()!.PutString(Key("spellingContextModel", language), name)!.Apply();

    /// <summary>A language's Spelling kept as changed; its context model's name too, where it is on.</summary>
    public static void SetSpelling(Context context, string language, LanguageSpelling spelling)
    {
        if (spelling.Context is { } model)
        {
            SetContextModel(context, language, model);
        }

        // The context model, or Correct words, turned on from its own switch: it is what corrects from now on.
        var before = SpellingIn(context, language);
        if (spelling.Context is not null && before.Context is null)
        {
            KeyPlusPreferences.Global(context).Edit()!.PutString(Key("spellingBy", language), nameof(Corrector.Context))!.Apply();
        }
        else if (spelling.Words && !before.Words && CorrectsWith(context, language) != Corrector.Context)
        {
            KeyPlusPreferences.Global(context).Edit()!.PutString(Key("spellingBy", language), nameof(Corrector.Words))!.Apply();
        }

        KeyPlusPreferences.Global(context).Edit()!
            .PutBoolean(Key("spellingWords", language), spelling.Words)!
            .PutBoolean(Key("spellingAutocorrect", language), spelling.Autocorrect)!
            .PutBoolean(Key("spellingAccents", language), spelling.Accents)!
            .PutBoolean(Key("spellingWikipedia", language), spelling.Wikipedia)!
            .PutBoolean(Key("spellingOpenMark", language), spelling.OpenMark)!
            .PutBoolean(Key("spellingPunctuation", language), spelling.Punctuation)!
            .PutBoolean(Key("spellingContext", language), spelling.Context is not null)!
            .PutBoolean(Key("spellingSuggestions", language), spelling.Suggestions)!
            .Apply();
    }

    /// <summary>Whether Spelling is on at all.</summary>
    public static bool SpellingOn(Context context) => KeyPlusPreferences.Global(context).GetBoolean(SpellingOnKey, true);

    public static void SetSpellingOn(Context context, bool on) => KeyPlusPreferences.Global(context).Edit()!.PutBoolean(SpellingOnKey, on)!.Apply();

    /// <summary>A language's Spelling as the keyboard applies it: nothing with Spelling off; what corrects it alone, Android's spell checker or its context model (<see cref="LanguageSpelling.Alone"/>).</summary>
    public static LanguageSpelling Applied(Context context, string language) => SpellingOn(context) ? SpellingIn(context, language).Alone() : LanguageSpelling.Off;

    /// <summary>Whether a language has something Spelling corrects with: its dictionary downloaded with Correct words on, the accent model with its accents on, its punctuation model with punctuation on, a context model on, or ¿ and ¡ on, which need nothing.</summary>
    public static bool CanCorrect(Context context, string language)
    {
        var spelling = SpellingIn(context, language);
        return (spelling.Words && SpellingDictionary.DownloadedFor(language)) || (spelling.Accents && AccentModel.Downloaded)
            || (spelling.Punctuation && PunctuationModel.For(language)?.Downloaded == true) || spelling.Context is not null || spelling.OpenMark || spelling.System;
    }

    /// <summary>Glide typing on and off for every language at once, the user's: the model is one for all.</summary>
    public const string GlideOnKey = "glideOn";

    /// <summary>
    /// Whether Glide typing is on, for every language, as its page or the
    /// toolbar's tool has it; until it is turned, on where any language had it
    /// on by itself, or the oldest switch, for all, had.
    /// </summary>
    public static bool GlideOn(Context context)
    {
        var kept = KeyPlusPreferences.Global(context);
        return kept.Contains(GlideOnKey) ? kept.GetBoolean(GlideOnKey, false)
            : kept.GetBoolean(KeyPlusPreferences.SwipeTypingKey, false) || StandardKeys.Languages.Any(language => kept.GetBoolean(Key("glide", language.Code), false));
    }

    public static void SetGlideOn(Context context, bool on) =>
        KeyPlusPreferences.Global(context).Edit()!.PutBoolean(GlideOnKey, on)!.Apply();

    /// <summary>Whether a language is swiped in now: Glide typing on, and the model and the language's word list downloaded.</summary>
    public static bool Glides(Context context, StandardLanguage language) => GlideOn(context) && SwipeData.ReadyFor(language);
}
