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

    /// <summary>A language's Spelling as chosen on its page.</summary>
    public static LanguageSpelling SpellingIn(Context context, string language)
    {
        var kept = KeyPlusPreferences.Global(context);
        var spanish = LanguageSpelling.Offers(language);
        return new(
            kept.GetBoolean(Key("spellingWords", language), false),
            kept.GetBoolean(Key("spellingAutocorrect", language), true),
            spanish && kept.GetBoolean(Key("spellingAccents", language), false),
            spanish && kept.GetBoolean(Key("spellingWikipedia", language), false),
            spanish && kept.GetBoolean(Key("spellingOpenMark", language), kept.GetBoolean(KeyPlusPreferences.SpellingOpenMarkKey, false)),
            PunctuationModel.For(language) is not null && kept.GetBoolean(Key("spellingPunctuation", language), false));
    }

    /// <summary>A language's Spelling kept as changed.</summary>
    public static void SetSpelling(Context context, string language, LanguageSpelling spelling) =>
        KeyPlusPreferences.Global(context).Edit()!
            .PutBoolean(Key("spellingWords", language), spelling.Words)!
            .PutBoolean(Key("spellingAutocorrect", language), spelling.Autocorrect)!
            .PutBoolean(Key("spellingAccents", language), spelling.Accents)!
            .PutBoolean(Key("spellingWikipedia", language), spelling.Wikipedia)!
            .PutBoolean(Key("spellingOpenMark", language), spelling.OpenMark)!
            .PutBoolean(Key("spellingPunctuation", language), spelling.Punctuation)!
            .Apply();

    /// <summary>Whether Spelling is on at all.</summary>
    public static bool SpellingOn(Context context) => KeyPlusPreferences.Global(context).GetBoolean(SpellingOnKey, true);

    public static void SetSpellingOn(Context context, bool on) => KeyPlusPreferences.Global(context).Edit()!.PutBoolean(SpellingOnKey, on)!.Apply();

    /// <summary>A language's Spelling as the keyboard applies it: nothing with Spelling off.</summary>
    public static LanguageSpelling Applied(Context context, string language) => SpellingOn(context) ? SpellingIn(context, language) : LanguageSpelling.Off;

    /// <summary>Whether a language has something Spelling corrects with: its dictionary downloaded with Correct words on, the accent model with its accents on, its punctuation model with punctuation on, or ¿ and ¡ on, which need nothing.</summary>
    public static bool CanCorrect(Context context, string language)
    {
        var spelling = SpellingIn(context, language);
        return (spelling.Words && SpellingDictionary.DownloadedFor(language)) || (spelling.Accents && AccentModel.Downloaded)
            || (spelling.Punctuation && PunctuationModel.For(language)?.Downloaded == true) || spelling.OpenMark;
    }

    /// <summary>Whether a language is swiped in, as its page or the toolbar's tool has it; one not chosen yet, as the old switch had every language.</summary>
    public static bool GlidesIn(Context context, string language)
    {
        var kept = KeyPlusPreferences.Global(context);
        return kept.GetBoolean(Key("glide", language), kept.GetBoolean(KeyPlusPreferences.SwipeTypingKey, false));
    }

    public static void SetGlide(Context context, string language, bool on) =>
        KeyPlusPreferences.Global(context).Edit()!.PutBoolean(Key("glide", language), on)!.Apply();

    /// <summary>Whether a language is swiped in now: chosen, and its word list and the model downloaded.</summary>
    public static bool Glides(Context context, StandardLanguage language) => GlidesIn(context, language.Code) && SwipeData.ReadyFor(language);
}
