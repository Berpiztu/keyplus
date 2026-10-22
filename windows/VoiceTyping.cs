using System.IO;
using System.Net.Http;
using System.Text.Json;
using NAudio.Wave;

namespace Berpiztu.KeyPlus;

/// <summary>
/// 🎤 on Windows: what the microphone hears, recorded as the speech model
/// wants it (16 kHz, mono, 16 bits), sent once it stops to ia-voice, the
/// owner's speech service (NVIDIA's Nemotron 3.5 ASR on the GPU, the model
/// VS Code's own dictation uses), as OpenAI's transcriptions are asked
/// for (POST /v1/audio/transcriptions, the audio and the language), its
/// text back.
/// </summary>
public sealed class VoiceTyping : IDisposable
{
    /// <summary>ia-voice on this computer, as its run.ps1 publishes it.</summary>
    public const string DefaultService = "http://127.0.0.1:8060";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    private readonly string service;
    private WaveInEvent? microphone;
    private MemoryStream? audio;
    private WaveFileWriter? writer;

    public VoiceTyping(string service = DefaultService)
    {
        this.service = service.TrimEnd('/');
    }

    public bool Recording => microphone is not null;

    /// <summary>The microphone listening, into a WAV kept in memory.</summary>
    public void Start()
    {
        if (Recording)
        {
            return;
        }

        audio = new MemoryStream();
        microphone = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 50 };
        writer = new WaveFileWriter(new IgnoreDispose(audio), microphone.WaveFormat);
        microphone.DataAvailable += (_, data) => writer?.Write(data.Buffer, 0, data.BytesRecorded);
        microphone.StartRecording();
    }

    /// <summary>The microphone stopped and what it heard read by the service in a language, by its locale ("es-ES", "en-US"); none where nothing was heard.</summary>
    public async Task<string> Stop(string language)
    {
        if (microphone is null || audio is null || writer is null)
        {
            return "";
        }

        var stopped = new TaskCompletionSource();
        microphone.RecordingStopped += (_, _) => stopped.TrySetResult();
        microphone.StopRecording();
        await stopped.Task;
        microphone.Dispose();
        microphone = null;
        writer.Dispose();
        writer = null;
        var wav = audio.ToArray();
        audio = null;
        if (wav.Length <= 44)
        {
            return "";
        }

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(wav), "file", "voice.wav" },
            { new StringContent(language), "language" },
        };
        using var answer = await Http.PostAsync($"{service}/v1/audio/transcriptions", form);
        answer.EnsureSuccessStatusCode();
        using var read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());
        return read.RootElement.TryGetProperty("text", out var text) ? text.GetString()?.Trim() ?? "" : "";
    }

    public void Dispose()
    {
        microphone?.Dispose();
        writer?.Dispose();
    }

    /// <summary>The memory the WAV is written to, left open when its writer closes, so it can be read after.</summary>
    private sealed class IgnoreDispose(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            // The memory stays open: its owner reads it once the writer is done.
        }
    }
}
