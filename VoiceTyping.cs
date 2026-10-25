using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Berpiztu.KeyPlus;

/// <summary>How a dictation listens and where it is read: whether it stops by itself after speaking and after how long a silence, how much the volume is raised, the service with the user and password its proxy asks for, and the language by its locale ("es-ES", "en-US").</summary>
public sealed record VoiceOptions(bool AutoStop, double SilenceSeconds, double Gain, string Service, string User, string Password, string Language);

/// <summary>A microphone that hears as the speech model wants it: 16 kHz, mono, 16 bits, in pieces on a thread of its own.</summary>
public interface IMicrophone : IDisposable
{
    /// <summary>Listening, each piece heard told with its length in bytes.</summary>
    void Start(Action<byte[], int> heard);

    /// <summary>Stopped, every piece heard told.</summary>
    Task Stop();
}

/// <summary>A dictation: listening, its level and its words so far as they come, the end of speaking where it is asked to stop by itself, and all its words once stopped; its events on the thread that started it.</summary>
public interface IDictation : IDisposable
{
    bool Recording { get; }

    /// <summary>The level heard, 0 to 1.</summary>
    event Action<double>? Level;

    /// <summary>All the words heard so far, read again while listening.</summary>
    event Action<string>? Partial;

    /// <summary>The user stopped speaking, where the options ask to stop then: time to stop.</summary>
    event Action? Silence;

    void Start(VoiceOptions options);

    /// <summary>Stopped, all its words; none where nothing was said.</summary>
    Task<string> Stop();
}

/// <summary>
/// 🎤 on both keyboards, Android's and Windows': what a microphone hears,
/// raised by the gain, read by ia-voice, the owner's speech service
/// (NVIDIA's Nemotron 3.5 ASR on the GPU, the model VS Code's own dictation
/// uses), as OpenAI's transcriptions are asked for (POST
/// /v1/audio/transcriptions, the audio and the language), signing in first
/// where its proxy asks for a user and password (wslc's publish proxy: a
/// login that leaves a cookie). While it listens, its level as it comes, and
/// every second what was heard so far read again, so the words show as they
/// are spoken; after speaking, where the options ask for it, their silence
/// tells that it is time to stop: heard as a level under speech's, or, where
/// the microphone's own noise is louder than that (Remote Desktop's), read as
/// no new word for as long. Its events on the thread that started it.
/// </summary>
public sealed class VoiceTyping(IMicrophone microphone, Action<string> log) : IDictation
{
    /// <summary>ia-voice on this computer, as its run.ps1 publishes it: Windows' local service.</summary>
    public const string LocalService = "http://127.0.0.1:8060";

    /// <summary>The services a dictation is read by: on the device itself, an ia-voice on the local network by its IP, or one published, with a user and password.</summary>
    public const string OnDevice = "device";
    public const string OnLan = "lan";
    public const string Remote = "remote";

    /// <summary>ia-voice's port, run.ps1's, for an address on the local network written without one.</summary>
    public const int Port = 8060;

    /// <summary>An ia-voice on the local network by its IP or name as written ("192.168.1.20"), as the address it answers at: http, and ia-voice's port unless one is written; none where nothing is.</summary>
    public static string LanService(string written)
    {
        var address = written.Trim().TrimEnd('/');
        if (address.Length == 0)
        {
            return "";
        }

        if (!address.Contains("://", StringComparison.Ordinal))
        {
            address = "http://" + address;
        }

        var host = address[(address.IndexOf("://", StringComparison.Ordinal) + 3)..];
        return host.Contains(':') ? address : $"{address}:{Port}";
    }

    /// <summary>How long a service is waited for before a dictation is read locally instead.</summary>
    private static readonly TimeSpan AnswerWithin = TimeSpan.FromSeconds(1.5);

    private static readonly HttpClient Asking = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = AnswerWithin };

    /// <summary>
    /// Whether a service answers at all, before a dictation is sent to it:
    /// any answer, a proxy's asking to sign in too, is one; none within
    /// <see cref="AnswerWithin"/> (no network, the server down) and the
    /// keyboard listens locally instead.
    /// </summary>
    public static async Task<bool> Answers(string service)
    {
        if (!Uri.TryCreate(service, UriKind.Absolute, out var address))
        {
            return false;
        }

        try
        {
            using var answer = await Asking.GetAsync(address, HttpCompletionOption.ResponseHeadersRead);
            return true;
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>The level (0 to 1) taken for speech, IA-BotMsgs' chat's.</summary>
    public const double SpeakingLevel = 0.12;

    public const int Rate = 16000;
    private const int BytesPerSecond = Rate * 2;

    /// <summary>The level's 1: half the most a sample holds, as the chat's meter reads it.</summary>
    private const double FullLevel = 16384;

    private static readonly TimeSpan PartialEvery = TimeSpan.FromSeconds(1);
    private static readonly HttpClient Http = new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(2),
    };

    private readonly object guard = new();
    private readonly MemoryStream pcm = new();
    private SynchronizationContext? caller;
    private VoiceOptions options = new(false, 2, 1, LocalService, "", "", "es-ES");
    private bool recording;

    /// <summary>Each listening's number: what an earlier one still sends after it ended is left unsaid.</summary>
    private int session;

    private bool spoke;
    private long quietBytes;

    public bool Recording => recording;

    /// <summary>The level heard, 0 to 1, as each piece comes.</summary>
    public event Action<double>? Level;

    /// <summary>What was heard so far, read again while listening.</summary>
    public event Action<string>? Partial;

    /// <summary>The options' silence after speaking: time to stop.</summary>
    public event Action? Silence;

    /// <summary>The microphone listening as the options say; its events on the caller's thread.</summary>
    public void Start(VoiceOptions options)
    {
        if (recording)
        {
            return;
        }

        this.options = options;
        caller = SynchronizationContext.Current;
        spoke = false;
        quietBytes = 0;
        lock (guard)
        {
            pcm.SetLength(0);
        }

        microphone.Start(Heard);
        recording = true;
        _ = Partials(++session);
    }

    /// <summary>The microphone stopped and all it heard read by the service; none where nothing was said.</summary>
    public async Task<string> Stop()
    {
        if (!recording)
        {
            return "";
        }

        session++;
        recording = false;
        await microphone.Stop();
        return Recorded() is { } wav ? await Transcribe(wav) : "";
    }

    public void Dispose()
    {
        session++;
        recording = false;
        microphone.Dispose();
    }

    /// <summary>A piece heard (on the microphone's thread): raised by the gain, kept, its level told, and the silence after speech counted.</summary>
    private void Heard(byte[] data, int length)
    {
        var buffer = data.AsSpan(0, length);
        var peak = 0;
        for (var at = 0; at + 1 < buffer.Length; at += 2)
        {
            var sample = (int)Math.Clamp(BinaryPrimitives.ReadInt16LittleEndian(buffer[at..]) * options.Gain, short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(buffer[at..], (short)sample);
            peak = Math.Max(peak, Math.Abs(sample));
        }

        lock (guard)
        {
            pcm.Write(buffer);
        }

        var level = Math.Min(1, peak / FullLevel);
        var silent = false;
        if (level >= SpeakingLevel)
        {
            spoke = true;
            quietBytes = 0;
        }
        else if (spoke)
        {
            quietBytes += buffer.Length;
            silent = options.AutoStop && quietBytes >= options.SilenceSeconds * BytesPerSecond;
        }

        var current = session;
        Tell(() =>
        {
            if (current != session)
            {
                return;
            }

            Level?.Invoke(level);
            if (silent)
            {
                Silence?.Invoke();
            }
        });
    }

    /// <summary>Every second while listening, all heard so far read again, one reading at a time.</summary>
    private async Task Partials(int current)
    {
        long read = 0;
        var mostWords = 0;
        long grewAt = 0;
        while (current == session)
        {
            await Task.Delay(PartialEvery);
            byte[]? wav;
            lock (guard)
            {
                wav = pcm.Length > read && current == session ? Recorded() : null;
                read = pcm.Length;
            }

            if (wav is null)
            {
                continue;
            }

            try
            {
                var text = await Transcribe(wav);
                if (current != session)
                {
                    break;
                }

                Partial?.Invoke(text);
                var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                if (words > mostWords)
                {
                    mostWords = words;
                    grewAt = wav.Length;
                }
                else if (mostWords > 0 && options.AutoStop && wav.Length - grewAt >= options.SilenceSeconds * BytesPerSecond)
                {
                    Silence?.Invoke();
                }
            }
            catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or JsonException or SignInRefused)
            {
                log($"ia-voice did not read the words so far: {failure.Message}");
            }
        }
    }

    /// <summary>What was heard as a WAV (16 kHz, mono, 16 bits); none when nothing was.</summary>
    private byte[]? Recorded()
    {
        lock (guard)
        {
            if (pcm.Length == 0)
            {
                return null;
            }

            var length = (int)pcm.Length;
            var wav = new byte[44 + length];
            var header = wav.AsSpan();
            "RIFF"u8.CopyTo(header);
            BinaryPrimitives.WriteInt32LittleEndian(header[4..], 36 + length);
            "WAVEfmt "u8.CopyTo(header[8..]);
            BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
            BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);
            BinaryPrimitives.WriteInt16LittleEndian(header[22..], 1);
            BinaryPrimitives.WriteInt32LittleEndian(header[24..], Rate);
            BinaryPrimitives.WriteInt32LittleEndian(header[28..], BytesPerSecond);
            BinaryPrimitives.WriteInt16LittleEndian(header[32..], 2);
            BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
            "data"u8.CopyTo(header[36..]);
            BinaryPrimitives.WriteInt32LittleEndian(header[40..], length);
            pcm.GetBuffer().AsSpan(0, length).CopyTo(header[44..]);
            return wav;
        }
    }

    /// <summary>The words in a WAV, as the service reads them; where its proxy asks to sign in, signed in with the options' user and asked again.</summary>
    private async Task<string> Transcribe(byte[] wav)
    {
        var answer = await Send(wav);
        if (SignInAsked(answer))
        {
            answer.Dispose();
            await SignIn();
            answer = await Send(wav);
        }

        using (answer)
        {
            if (SignInAsked(answer))
            {
                throw new SignInRefused();
            }

            answer.EnsureSuccessStatusCode();
            using var read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());
            return read.RootElement.TryGetProperty("text", out var text) ? text.GetString()?.Trim() ?? "" : "";
        }
    }

    private Task<HttpResponseMessage> Send(byte[] wav) =>
        Http.PostAsync($"{options.Service}/v1/audio/transcriptions", new MultipartFormDataContent
        {
            { new ByteArrayContent(wav), "file", "voice.wav" },
            { new StringContent(options.Language), "language" },
        });

    /// <summary>The proxy's answer to one not signed in: sent to its login page, or refused.</summary>
    private static bool SignInAsked(HttpResponseMessage answer) =>
        answer.StatusCode is HttpStatusCode.Unauthorized || ((int)answer.StatusCode >= 300 && (int)answer.StatusCode < 400);

    /// <summary>Signed in at the proxy's login with the options' user and password; its cookie then goes with every request.</summary>
    private async Task SignIn()
    {
        if (options.User.Length == 0)
        {
            throw new SignInRefused();
        }

        using var answer = await Http.PostAsync($"{options.Service}/__login/api/login", JsonContent.Create(new { username = options.User, password = options.Password, next = "/" }));
        if (!answer.IsSuccessStatusCode)
        {
            throw new SignInRefused();
        }
    }

    /// <summary>The service asks for a user and password, and those set, if any, are not taken.</summary>
    public sealed class SignInRefused() : Exception("The speech service did not take the user and password.");

    private void Tell(Action told)
    {
        if (caller is { } context)
        {
            context.Post(_ => told(), null);
        }
        else
        {
            told();
        }
    }
}
