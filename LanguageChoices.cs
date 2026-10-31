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
            kept.GetBoolean(Key("spellingSuggestions", language), true));
    }

    /// <summary>A language's context model chosen in its list, kept while the model is off too, so it is the one turned on again; none until one is chosen.</summary>
    public static string? ContextModelIn(Context context, string language) =>
        KeyPlusPreferences.Global(context).GetString(Key("spellingContextModel", language), "") is { Length: > 0 } name ? name : null;

    /// <summary>Whether a language's context model gives the words offered as a word is typed too (Use it for suggestions too); on until turned off, and turned on with the model.</summary>
    public static bool ContextSuggestsIn(Context context, string language) =>
        KeyPlusPreferences.Global(context).GetBoolean(Key("spellingContextSuggests", language), true);

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

    /// <summary>A language's Spelling as the keyboard applies it: nothing with Spelling off; with its context model on, that alone (<see cref="LanguageSpelling.ContextAlone"/>).</summary>
    public static LanguageSpelling Applied(Context context, string language) => SpellingOn(context) ? SpellingIn(context, language).ContextAlone() : LanguageSpelling.Off;

    /// <summary>Whether a language has something Spelling corrects with: its dictionary downloaded with Correct words on, the accent model with its accents on, its punctuation model with punctuation on, a context model on, or ¿ and ¡ on, which need nothing.</summary>
    public static bool CanCorrect(Context context, string language)
    {
        var spelling = SpellingIn(context, language);
        return (spelling.Words && SpellingDictionary.DownloadedFor(language)) || (spelling.Accents && AccentModel.Downloaded)
            || (spelling.Punctuation && PunctuationModel.For(language)?.Downloaded == true) || spelling.Context is not null || spelling.OpenMark;
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
