using NAudio.Wave;

namespace Berpiztu.KeyPlus;

/// <summary>Windows' microphone for 🎤 (NAudio, WinMM): the one named in the settings, else the system's default, heard in pieces of 50 ms.</summary>
public sealed class NAudioMicrophone : IMicrophone
{
    private static readonly WaveFormat Format = new(VoiceTyping.Rate, 16, 1);

    private WaveInEvent? recording;

    /// <summary>The microphone to listen with, by its name; none, the system's default.</summary>
    public string Name { get; set; } = "";

    /// <summary>The microphones Windows offers, by name.</summary>
    public static IReadOnlyList<string> Names() =>
        [.. Enumerable.Range(0, WaveInEvent.DeviceCount).Select(device => WaveInEvent.GetCapabilities(device).ProductName)];

    public void Start(Action<byte[], int> heard)
    {
        recording = new WaveInEvent { DeviceNumber = DeviceOf(Name), WaveFormat = Format, BufferMilliseconds = 50 };
        recording.DataAvailable += (_, data) => heard(data.Buffer, data.BytesRecorded);
        try
        {
            recording.StartRecording();
        }
        catch
        {
            recording.Dispose();
            recording = null;
            throw;
        }
    }

    public async Task Stop()
    {
        if (recording is not { } stopping)
        {
            return;
        }

        recording = null;
        var stopped = new TaskCompletionSource();
        stopping.RecordingStopped += (_, _) => stopped.TrySetResult();
        stopping.StopRecording();
        await stopped.Task;
        stopping.Dispose();
    }

    public void Dispose()
    {
        recording?.Dispose();
        recording = null;
    }

    /// <summary>The microphone's number by its name; the system's default (-1) when none is named or it is gone.</summary>
    private static int DeviceOf(string name)
    {
        var named = Names().ToList().IndexOf(name);
        return name.Length > 0 && named >= 0 ? named : -1;
    }
}
