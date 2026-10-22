using Android.Content;
using Android.Media;
using Android.OS;
using Android.Security.Keystore;
using Android.Speech;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

[assembly: UsesPermission(Android.Manifest.Permission.RecordAudio)]

namespace Berpiztu.KeyPlus;

/// <summary>
/// 🎤's settings on Android, Escribir por voz's, the Windows keyboard's
/// (windows\VoiceSettings.cs) with Android's own: whether it stops by itself
/// once the user stops speaking and after how long a silence (off, it listens
/// until 🎤 is tapped again), how much the volume is raised, and which
/// service reads it: Local, Android's own recognizer on the device; an
/// ia-voice on the local network, by its IP; or a remote one, at an address,
/// with the user and password its proxy asks for,
/// the password sealed by a key of Android's keystore that never leaves the
/// device. Kept in the app's own preferences, never backed up.
/// </summary>
public static class VoicePreferences
{
    public const string AutoStopKey = "voiceAutoStop";
    public const string SilenceKey = "voiceSilence";
    public const string GainKey = "voiceGain";
    public const string ServiceKey = "voiceService";
    public const string LanAddressKey = "voiceLanAddress";

    /// <summary>Kept before the local network was a choice: whether the device's recognizer, else the address.</summary>
    private const string LocalKey = "voiceLocal";
    public const string AddressKey = "voiceAddress";
    public const string UserKey = "voiceUser";
    public const string PasswordKey = "voicePassword";

    public const float SilenceDefault = 2;

    /// <summary>The silences and gains offered, the Windows settings' steps.</summary>
    public static readonly float[] Silences = [0.5f, 1, 1.5f, 2, 3, 4, 5, 8, 10, 15, 20, 30];
    public static readonly float[] Gains = [1, 1.5f, 2, 2.5f, 3, 3.5f, 4];

    private const string KeyStoreName = "AndroidKeyStore";
    private const string SealAlias = "voicePassword";
    private const int TagBits = 128;

    public static bool AutoStop(Context context) => KeyPlusPreferences.Global(context).GetBoolean(AutoStopKey, false);

    public static float Silence(Context context) => KeyPlusPreferences.Global(context).GetFloat(SilenceKey, SilenceDefault);

    public static float Gain(Context context) => KeyPlusPreferences.Global(context).GetFloat(GainKey, 1);

    /// <summary>The service chosen (<see cref="VoiceTyping.OnDevice"/>, <see cref="VoiceTyping.OnLan"/>, <see cref="VoiceTyping.Remote"/>), as kept.</summary>
    public static string Choice(Context context)
    {
        var kept = KeyPlusPreferences.Global(context);
        return kept.GetString(ServiceKey, null) ?? (kept.GetBoolean(LocalKey, true) ? VoiceTyping.OnDevice : VoiceTyping.Remote);
    }

    /// <summary>Whether Android's own recognizer reads the dictation: chosen, or the choice has no address to go to.</summary>
    public static bool Local(Context context) => Choice(context) switch
    {
        VoiceTyping.OnLan => LanAddress(context).Length == 0,
        VoiceTyping.Remote => Address(context).Length == 0,
        _ => true,
    };

    public static string LanAddress(Context context) => KeyPlusPreferences.Global(context).GetString(LanAddressKey, "") ?? "";

    public static string Address(Context context) => KeyPlusPreferences.Global(context).GetString(AddressKey, "") ?? "";

    public static string User(Context context) => KeyPlusPreferences.Global(context).GetString(UserKey, "") ?? "";

    public static string Password(Context context)
    {
        var kept = KeyPlusPreferences.Global(context).GetString(PasswordKey, "") ?? "";
        try
        {
            return Unsealed(kept);
        }
        catch (Exception unreadable) when (unreadable is GeneralSecurityException or FormatException)
        {
            Android.Util.Log.Warn("KeyPlus", $"The voice password could not be read: {unreadable.Message}");
            return "";
        }
    }

    public static void SetPassword(Context context, string password) =>
        KeyPlusPreferences.Global(context).Edit()!.PutString(PasswordKey, Sealed(password))!.Apply();

    /// <summary>The settings as a dictation by ia-voice listens by them, in a language by its locale.</summary>
    public static VoiceOptions Options(Context context, string language) => Choice(context) == VoiceTyping.OnLan
        ? new(AutoStop(context), Silence(context), Gain(context), VoiceTyping.LanService(LanAddress(context)), "", "", language)
        : new(AutoStop(context), Silence(context), Gain(context), Address(context).Trim().TrimEnd('/'), User(context), Password(context), language);

    private static string Sealed(string password)
    {
        if (password.Length == 0)
        {
            return "";
        }

        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, SealKey());
        var sealedBytes = cipher.DoFinal(System.Text.Encoding.UTF8.GetBytes(password))!;
        return $"{Convert.ToBase64String(cipher.GetIV()!)}:{Convert.ToBase64String(sealedBytes)}";
    }

    private static string Unsealed(string kept)
    {
        if (kept.Split(':') is not [var iv, var sealedText])
        {
            return "";
        }

        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.DecryptMode, SealKey(), new GCMParameterSpec(TagBits, Convert.FromBase64String(iv)));
        return System.Text.Encoding.UTF8.GetString(cipher.DoFinal(Convert.FromBase64String(sealedText))!);
    }

    /// <summary>The keystore's key the password is sealed with, made the first time.</summary>
    private static IKey SealKey()
    {
        var store = KeyStore.GetInstance(KeyStoreName)!;
        store.Load(null);
        if (store.GetKey(SealAlias, null) is { } key)
        {
            return key;
        }

        var maker = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, KeyStoreName)!;
        maker.Init(new KeyGenParameterSpec.Builder(SealAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)!
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
            .Build());
        return maker.GenerateKey()!;
    }
}

/// <summary>The device's microphone for 🎤 by ia-voice: Android's voice-recognition input, 16 kHz, mono, 16 bits, read in pieces of 50 ms on a thread of its own.</summary>
public sealed class AndroidMicrophone : IMicrophone
{
    private const int PieceBytes = VoiceTyping.Rate * 2 / 20;

    private AudioRecord? record;
    private Thread? reader;
    private volatile bool reading;

    public void Start(Action<byte[], int> heard)
    {
        var least = AudioRecord.GetMinBufferSize(VoiceTyping.Rate, ChannelIn.Mono, Encoding.Pcm16bit);
        var listening = new AudioRecord(AudioSource.VoiceRecognition, VoiceTyping.Rate, ChannelIn.Mono, Encoding.Pcm16bit, Math.Max(least, PieceBytes) * 2);
        if (listening.State != State.Initialized)
        {
            listening.Release();
            throw new InvalidOperationException("The microphone did not open.");
        }

        record = listening;
        listening.StartRecording();
        reading = true;
        reader = new Thread(() =>
        {
            var piece = new byte[PieceBytes];
            while (reading)
            {
                var length = listening.Read(piece, 0, piece.Length);
                if (length > 0)
                {
                    heard(piece, length);
                }
            }
        }) { IsBackground = true };
        reader.Start();
    }

    public async Task Stop()
    {
        if (record is not { } stopping)
        {
            return;
        }

        reading = false;
        if (reader is { } waiting)
        {
            await Task.Run(waiting.Join);
        }

        record = null;
        reader = null;
        stopping.Stop();
        stopping.Release();
    }

    public void Dispose()
    {
        reading = false;
        record?.Release();
        record = null;
    }
}

/// <summary>
/// 🎤 by Android's own recognizer on the device (Local), as a dictation by
/// ia-voice behaves: its words so far as they come, its level, and, where the
/// options ask for it, the end of speaking taken as time to stop; else it
/// listens on, one utterance after another, until 🎤 is tapped again, all
/// heard joined.
/// </summary>
public sealed class AndroidSpeech(Context context, Action<string> log) : IDictation
{
    /// <summary>How long Stop waits for the recognizer's last words.</summary>
    private static readonly TimeSpan LastWords = TimeSpan.FromSeconds(3);

    private readonly Action<string> log = log;
    private readonly List<string> heard = [];
    private SpeechRecognizer? recognizer;
    private Listener? listener;
    private VoiceOptions options = new(false, VoicePreferences.SilenceDefault, 1, "", "", "", "es-ES");
    private string utterance = "";
    private TaskCompletionSource? lastWords;

    public bool Recording { get; private set; }

    public event Action<double>? Level;

    public event Action<string>? Partial;

    public event Action? Silence;

    /// <summary>Whether the device has a recognizer to listen with.</summary>
    public bool Available => SpeechRecognizer.IsRecognitionAvailable(context);

    public void Start(VoiceOptions options)
    {
        if (Recording)
        {
            return;
        }

        this.options = options;
        heard.Clear();
        utterance = "";
        lastWords = null;
        listener ??= new Listener(this);
        recognizer ??= SpeechRecognizer.CreateSpeechRecognizer(context)!;
        recognizer.SetRecognitionListener(listener);
        Recording = true;
        Listen();
    }

    public async Task<string> Stop()
    {
        if (!Recording)
        {
            return "";
        }

        Recording = false;
        lastWords = new TaskCompletionSource();
        recognizer?.StopListening();
        await Task.WhenAny(lastWords.Task, Task.Delay(LastWords));
        recognizer?.Cancel();
        return Joined();
    }

    public void Dispose()
    {
        Recording = false;
        recognizer?.Destroy();
        recognizer = null;
        listener?.Dispose();
        listener = null;
    }

    private void Listen()
    {
        var asked = new Intent(RecognizerIntent.ActionRecognizeSpeech);
        asked.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        asked.PutExtra(RecognizerIntent.ExtraLanguage, options.Language);
        asked.PutExtra(RecognizerIntent.ExtraPartialResults, true);
        asked.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
        if (options.AutoStop)
        {
            var silence = (long)(options.SilenceSeconds * 1000);
            asked.PutExtra(RecognizerIntent.ExtraSpeechInputCompleteSilenceLengthMillis, silence);
            asked.PutExtra(RecognizerIntent.ExtraSpeechInputPossiblyCompleteSilenceLengthMillis, silence);
        }

        recognizer?.StartListening(asked);
    }

    private string Joined() => string.Join(' ', heard.Append(utterance).Where(words => words.Length > 0));

    private static string? Words(Bundle? results) => results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault();

    /// <summary>An utterance ended with its words, or with none (silence, nothing understood): kept, then the dictation ends where it was asked to or the options stop it, else it listens on.</summary>
    private void Ended(string? words)
    {
        if (words is { Length: > 0 })
        {
            heard.Add(words);
        }

        utterance = "";
        Partial?.Invoke(Joined());
        if (lastWords is { } waiting)
        {
            waiting.TrySetResult();
        }
        else if (options.AutoStop && heard.Count > 0)
        {
            Silence?.Invoke();
        }
        else if (Recording)
        {
            Listen();
        }
    }

    private sealed class Listener(AndroidSpeech speech) : Java.Lang.Object, IRecognitionListener
    {
        public void OnBeginningOfSpeech()
        {
        }

        public void OnBufferReceived(byte[]? buffer)
        {
        }

        public void OnEndOfSpeech()
        {
        }

        public void OnError(SpeechRecognizerError error)
        {
            if (error is not (SpeechRecognizerError.NoMatch or SpeechRecognizerError.SpeechTimeout))
            {
                speech.log($"Android's recognizer stopped: {error}");
                if (speech.lastWords is null && speech.Recording)
                {
                    // Busy or failed: the dictation ends with what it heard.
                    speech.Silence?.Invoke();
                }
            }

            speech.Ended(null);
        }

        public void OnEvent(int eventType, Bundle? @params)
        {
        }

        public void OnPartialResults(Bundle? partialResults)
        {
            if (Words(partialResults) is { } words)
            {
                speech.utterance = words;
                speech.Partial?.Invoke(speech.Joined());
            }
        }

        public void OnReadyForSpeech(Bundle? @params)
        {
        }

        public void OnResults(Bundle? results) => speech.Ended(Words(results));

        /// <summary>Android's level, about -2 to 10 dB, as 0 to 1.</summary>
        public void OnRmsChanged(float rmsdB) => speech.Level?.Invoke(Math.Clamp((rmsdB + 2) / 12, 0, 1));
    }
}
