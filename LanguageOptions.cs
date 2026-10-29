using System.Globalization;

namespace Berpiztu.KeyPlus;

/// <summary>What a choice fetches, never carried by the app: how it is named in the log, whether it is here, how it is fetched, and the words its line says while it is, once it is and when it could not be.</summary>
public sealed record Fetched(string Named, Func<bool> Downloaded, Func<IProgress<(long Done, long Total)>, CancellationToken, Task> Download, int Downloading, int Done, int Failed);

/// <summary>A language's Spelling choice: its name and what it says, what it fetches if anything, and how it reads and changes the language's Spelling.</summary>
public sealed record SpellingChoice(int Label, string Summary, Fetched? Fetches, Func<LanguageSpelling, bool> Get, Func<LanguageSpelling, bool, LanguageSpelling> Set);

/// <summary>
/// What each language's page offers, the same in Android's settings and
/// Windows': its Spelling's choices and its Glide typing, and what each
/// fetches (<see cref="Fetched"/>), so a language added comes with its own.
/// Words are made by the app's own <c>text</c>: a string by its id, filled
/// with the values given.
/// </summary>
public static class LanguageOptions
{
    /// <summary>Correct words' files for a language: its dictionary, and its word list, which offers words as they are typed and tells how common a correction is, so it works without glide typing.</summary>
    public static Fetched DictionaryOf(StandardLanguage language) => new(
        $"the {language.Code} dictionary and word list",
        () => SpellingDictionary.DownloadedFor(language.Code) && SwipeData.HasWordList(language),
        async (progress, cancel) =>
        {
            await Downloads.FilesAsync([.. SpellingDictionary.FilesFor(language.Code), SwipeData.WordListFile(language)], progress, cancel);
            Log.Write($"Spelling: the {language.Code} dictionary and word list downloaded");
        },
        Resource.String.spelling_dictionary_downloading, Resource.String.spelling_dictionary_downloaded, Resource.String.spelling_dictionary_download_failed);

    /// <summary>The accent model's files.</summary>
    public static readonly Fetched Model = new(
        "the accent model", () => AccentModel.Downloaded, AccentModel.DownloadAsync,
        Resource.String.spelling_downloading, Resource.String.spelling_downloaded, Resource.String.spelling_download_failed);

    /// <summary>The punctuation model's files.</summary>
    public static readonly Fetched Punctuation = new(
        "the punctuation model", () => PunctuationModel.Downloaded, PunctuationModel.DownloadAsync,
        Resource.String.spelling_downloading, Resource.String.spelling_downloaded, Resource.String.spelling_download_failed);

    /// <summary>The Wikipedia's common misspellings.</summary>
    public static readonly Fetched WikipediaList = new(
        "the Wikipedia's common misspellings", () => CommonErrors.Downloaded, CommonErrors.DownloadAsync,
        Resource.String.spelling_wikipedia_downloading, Resource.String.spelling_wikipedia_downloaded, Resource.String.spelling_wikipedia_download_failed);

    /// <summary>A language's Glide typing's files: its word list, and FUTO Swipe's model the first time.</summary>
    public static Fetched GlideOf(StandardLanguage language) => new(
        $"glide typing for {language.Code}", () => SwipeData.ReadyFor(language), (progress, cancel) => SwipeData.DownloadFor(language, progress, cancel),
        Resource.String.glide_downloading, Resource.String.glide_downloaded, Resource.String.glide_download_failed);

    /// <summary>What a language's Glide typing says: its word list's size, and the model's, fetched the first time.</summary>
    public static string GlideSummary(StandardLanguage language, Func<int, object[], string> text) =>
        text(Resource.String.glide_summary_download, [Megabytes(SwipeData.WordListMegabytes(language)), Megabytes(SwipeData.ModelMegabytes)]);

    /// <summary>
    /// What Spelling has for a language, in its page's order: its words where
    /// it has a dictionary, named with its size; Spanish's common
    /// misspellings, its accents and its punctuation; Autocorrect; Spanish's ¿ and ¡.
    /// </summary>
    public static IEnumerable<SpellingChoice> SpellingChoices(string language, Func<int, object[], string> text)
    {
        var spanish = LanguageSpelling.Offers(language);
        if (SpellingDictionary.Has(language))
        {
            var named = text(language == StandardKeys.Spanish.Code ? Resource.String.spelling_dictionary_es : Resource.String.spelling_dictionary_en, []);
            var known = StandardKeys.Languages.First(standard => standard.Code == language);
            var size = Megabytes(SpellingDictionary.MegabytesFor(language) + SwipeData.WordListMegabytes(known));
            yield return new(Resource.String.spelling_words, text(Resource.String.spelling_words_summary, [named, size]), DictionaryOf(known),
                spelling => spelling.Words, (spelling, on) => spelling with { Words = on });
        }

        if (spanish)
        {
            yield return new(Resource.String.spelling_wikipedia, text(Resource.String.spelling_wikipedia_summary, []), WikipediaList,
                spelling => spelling.Wikipedia, (spelling, on) => spelling with { Wikipedia = on });
            yield return new(Resource.String.spelling_accent_closing, text(Resource.String.spelling_accent_closing_summary, []), Model,
                spelling => spelling.Accents, (spelling, on) => spelling with { Accents = on });
            yield return new(Resource.String.spelling_punctuation, text(Resource.String.spelling_punctuation_summary, []), Punctuation,
                spelling => spelling.Punctuation, (spelling, on) => spelling with { Punctuation = on });
        }

        yield return new(Resource.String.spelling_autocorrect, text(Resource.String.spelling_autocorrect_summary, []), null,
            spelling => spelling.Autocorrect, (spelling, on) => spelling with { Autocorrect = on });
        if (spanish)
        {
            yield return new(Resource.String.spelling_open_mark, text(Resource.String.spelling_open_mark_summary, []), null,
                spelling => spelling.OpenMark, (spelling, on) => spelling with { OpenMark = on });
        }
    }

    private static string Megabytes(double megabytes) => megabytes.ToString("0.0#", CultureInfo.CurrentCulture);
}
