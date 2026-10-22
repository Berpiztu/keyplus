using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using NAudio.Wave;

namespace Berpiztu.KeyPlus;

/// <summary>
/// 🎤 on Windows: what the microphone hears, recorded as the speech model
/// wants it (16 kHz, mono, 16 bits) and raised by the settings' gain, read by
/// ia-voice, the owner's speech service (NVIDIA's Nemotron 3.5 ASR on the
/// GPU, the model VS Code's own dictation uses), as OpenAI's transcriptions
/// are asked for (POST /v1/audio/transcriptions, the audio and the
/// language), signing in first where its proxy asks for a user and password
/// (wslc's publish proxy: a login that leaves a cookie). While it listens,
/// its level as it comes, and every second what
/// was heard so far read again, so the words show as they are spoken; after
/// speaking, where the settings ask for it, their silence tells that it is time to stop: heard as a
/// level under speech's, or, where the microphone's own noise is louder than
/// that (Remote Desktop's), read as no new word for as long.
/// </summary>
public sealed class VoiceTyping : IDisposable
{
    /// <summary>ia-voice on this computer, as its run.ps1 publishes it.</summary>
    public const string DefaultService = "http://127.0.0.1:8060";

    /// <summary>The level (0 to 1) taken for speech, IA-BotMsgs' chat's.</summary>
    public const double SpeakingLevel = 0.12;

    private const int Rate = 16000;
    private const int BytesPerSecond = Rate * 2;

    /// <summary>The level's 1: half the most a sample holds, as the chat's meter reads it.</summary>
    private const double FullLevel = 16384;

    private static readonly WaveFormat Format = new(Rate, 16, 1);
    private static readonly TimeSpan PartialEvery = TimeSpan.FromSeconds(1);
    private static readonly HttpClient Http = new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(2),
    };

    private readonly object guard = new();
    private readonly MemoryStream pcm = new();
    private WaveInEvent? microphone;
    private SynchronizationContext? caller;
    private VoiceSettings settings = VoiceSettings.Default;
    private string language = "";

    /// <summary>Each listening's number: what an earlier one still sends after it ended is left unsaid.</summary>
    private int session;

    private bool spoke;
    private long quietBytes;

    public bool Recording => microphone is not null;

    /// <summary>The level heard, 0 to 1, every 50 ms.</summary>
    public event Action<double>? Level;

    /// <summary>What was heard so far, read again while listening.</summary>
    public event Action<string>? Partial;

    /// <summary>The settings' silence after speaking: time to stop.</summary>
    public event Action? Silence;

    /// <summary>The microphones Windows offers, by name.</summary>
    public static IReadOnlyList<string> Microphones() =>
        [.. Enumerable.Range(0, WaveInEvent.DeviceCount).Select(device => WaveInEvent.GetCapabilities(device).ProductName)];

    /// <summary>The microphone listening as the settings say, for a language by its locale ("es-ES", "en-US"); its events on the caller's thread.</summary>
    public void Start(VoiceSettings settings, string language)
    {
        if (Recording)
        {
            return;
        }

        this.settings = settings;
        this.language = language;
        caller = SynchronizationContext.Current;
        spoke = false;
        quietBytes = 0;
        lock (guard)
        {
            pcm.SetLength(0);
        }

        microphone = new WaveInEvent { DeviceNumber = DeviceOf(settings.Microphone), WaveFormat = Format, BufferMilliseconds = 50 };
        microphone.DataAvailable += Heard;
        try
        {
            microphone.StartRecording();
        }
        catch
        {
            microphone.Dispose();
            microphone = null;
            throw;
        }

        _ = Partials(++session);
    }

    /// <summary>The microphone stopped and all it heard read by the service; none where nothing was said.</summary>
    public async Task<string> Stop()
    {
        if (microphone is null)
        {
            return "";
        }

        session++;
        var stopped = new TaskCompletionSource();
        microphone.RecordingStopped += (_, _) => stopped.TrySetResult();
        microphone.StopRecording();
        await stopped.Task;
        microphone.Dispose();
        microphone = null;
        return Recorded() is { } wav ? await Transcribe(wav) : "";
    }

    public void Dispose()
    {
        session++;
        microphone?.Dispose();
        microphone = null;
    }

    /// <summary>The microphone's number by its name; the system's default (-1) when none is named or it is gone.</summary>
    private static int DeviceOf(string name)
    {
        var named = Microphones().ToList().IndexOf(name);
        return name.Length > 0 && named >= 0 ? named : -1;
    }

    /// <summary>50 ms heard (on the microphone's thread): raised by the gain, kept, its level told, and the silence after speech counted.</summary>
    private void Heard(object? sender, WaveInEventArgs data)
    {
        var buffer = data.Buffer.AsSpan(0, data.BytesRecorded);
        var peak = 0;
        for (var at = 0; at + 1 < buffer.Length; at += 2)
        {
            var sample = (int)Math.Clamp(BinaryPrimitives.ReadInt16LittleEndian(buffer[at..]) * settings.Gain, short.MinValue, short.MaxValue);
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
            silent = settings.AutoStop && quietBytes >= settings.SilenceSeconds * BytesPerSecond;
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

    /// <summary>Every second while listening and once something was said, all heard so far read again, one reading at a time.</summary>
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
                else if (mostWords > 0 && settings.AutoStop && wav.Length - grewAt >= settings.SilenceSeconds * BytesPerSecond)
                {
                    Silence?.Invoke();
                }
            }
            catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or JsonException or SignInRefused)
            {
                Log.Write($"ia-voice did not read the words so far: {failure.Message}");
            }
        }
    }

    /// <summary>What was heard as a WAV; none when nothing was.</summary>
    private byte[]? Recorded()
    {
        lock (guard)
        {
            if (pcm.Length == 0)
            {
                return null;
            }

            var wav = new MemoryStream();
            using (var writer = new WaveFileWriter(wav, Format))
            {
                writer.Write(pcm.GetBuffer(), 0, (int)pcm.Length);
            }

            return wav.ToArray();
        }
    }

    /// <summary>The words in a WAV, as the service reads them; where its proxy asks to sign in, signed in with the settings' user and asked again.</summary>
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
        Http.PostAsync($"{settings.Service}/v1/audio/transcriptions", new MultipartFormDataContent
        {
            { new ByteArrayContent(wav), "file", "voice.wav" },
            { new StringContent(language), "language" },
        });

    /// <summary>The proxy's answer to one not signed in: sent to its login page, or refused.</summary>
    private static bool SignInAsked(HttpResponseMessage answer) =>
        answer.StatusCode is HttpStatusCode.Unauthorized || ((int)answer.StatusCode >= 300 && (int)answer.StatusCode < 400);

    /// <summary>Signed in at the proxy's login with the settings' user and password; its cookie then goes with every request.</summary>
    private async Task SignIn()
    {
        if (settings.User.Length == 0)
        {
            throw new SignInRefused();
        }

        using var answer = await Http.PostAsync($"{settings.Service}/__login/api/login", JsonContent.Create(new { username = settings.User, password = settings.Password, next = "/" }));
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
