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
/// until 🎤 is tapped again), the microphone ia-voice hears by and how much
/// its volume is raised, and which
/// service reads it: Local, Android's own recognizer on the device; an
/// ia-voice on the local network, by its IP; or a remote one, at an address,
/// with the user and password its proxy asks for,
/// the password sealed by a key of Android's keystore that never leaves the
/// device. Kept in the app's own preferences, never backed up.
/// </summary>
public static class VoicePreferences
{
    public const string AutoStopKey = "voiceAutoStop";
    public const string KeepAfterSendKey = "voiceKeepAfterSend";
    public const string SoundsKey = "voiceSounds";

    /// <summary>The sounds a dictation silenced, kept until it gives them back: the streams' numbers, so a keyboard that ended meanwhile gives them back as it starts.</summary>
    private const string SilencedKey = "voiceSilenced";
    public const string SilenceKey = "voiceSilence";
    public const string GainKey = "voiceGain";
    public const string ServiceKey = "voiceService";
    public const string LanAddressKey = "voiceLanAddress";
    public const string MicrophoneKey = "voiceMicrophone";

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

    /// <summary>Keep transcribing after sending: whether what was said but not written yet when the text is sent (Enter, a chat's send button) is still written after; off, it is dropped. The microphone stays on either way.</summary>
    public static bool KeepAfterSend(Context context) => KeyPlusPreferences.Global(context).GetBoolean(KeepAfterSendKey, false);

    /// <summary>Sound effects: whether Android's recognizer is left to beep as it starts and stops listening, which it does at every pause; off (the default), the device's sounds are silenced while it listens.</summary>
    public static bool Sounds(Context context) => KeyPlusPreferences.Global(context).GetBoolean(SoundsKey, false);

    /// <summary>The streams Android's recognizer may beep on, by device: the media's, the notifications' and the system's.</summary>
    private static readonly Android.Media.Stream[] Beeping = [Android.Media.Stream.Music, Android.Media.Stream.Notification, Android.Media.Stream.System];

    /// <summary>Each silencing's number: a giving back asked for later (after the recognizer's last beep) is left undone once another dictation has silenced them again.</summary>
    private static int silencing;

    /// <summary>
    /// The device's sounds silenced while a dictation by Android's recognizer
    /// listens, where Sound effects is off: only the streams that were not
    /// silent already, noted so they are given back, by this keyboard or the
    /// next one started. One the device does not let be silenced (Do not
    /// disturb's) is left as it is.
    /// </summary>
    public static void SilenceSounds(Context context)
    {
        silencing++;
        var audio = (AudioManager)context.GetSystemService(Context.AudioService)!;
        var kept = KeyPlusPreferences.Global(context);
        var silenced = (kept.GetString(SilencedKey, "") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var stream in Beeping)
        {
            try
            {
                if (!audio.IsStreamMute(stream))
                {
                    audio.AdjustStreamVolume(stream, Adjust.Mute, 0);
                    silenced.Add(((int)stream).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            catch (Java.Lang.SecurityException refused)
            {
                Log.Warning($"Voice typing: the device does not let {stream} be silenced", refused);
            }
        }

        kept.Edit()!.PutString(SilencedKey, string.Join(',', silenced.Distinct()))!.Apply();
    }

    /// <summary>The sounds a dictation silenced given back, a moment later where asked, so the recognizer's last beep is not heard; nothing where another dictation silenced them again meanwhile.</summary>
    public static void GiveSoundsBack(Context context, int afterMs = 0)
    {
        var asked = silencing;
        if (afterMs > 0)
        {
            new Handler(Looper.MainLooper!).PostDelayed(
                () =>
                {
                    if (asked == silencing)
                    {
                        GiveSoundsBack(context);
                    }
                },
                afterMs);
            return;
        }

        var kept = KeyPlusPreferences.Global(context);
        var audio = (AudioManager)context.GetSystemService(Context.AudioService)!;
        foreach (var number in (kept.GetString(SilencedKey, "") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                audio.AdjustStreamVolume((Android.Media.Stream)int.Parse(number, System.Globalization.CultureInfo.InvariantCulture), Adjust.Unmute, 0);
            }
            catch (Exception refused) when (refused is Java.Lang.SecurityException or FormatException)
            {
                Log.Warning($"Voice typing: a sound silenced could not be given back", refused);
            }
        }

        kept.Edit()!.Remove(SilencedKey)!.Apply();
    }

    public static float Silence(Context context) => KeyPlusPreferences.Global(context).GetFloat(SilenceKey, SilenceDefault);

    public static float Gain(Context context) => KeyPlusPreferences.Global(context).GetFloat(GainKey, 1);

    /// <summary>The microphone chosen, by its name (<see cref="AndroidMicrophone.Names"/>), as Windows': empty, the system's default.</summary>
    public static string Microphone(Context context) => KeyPlusPreferences.Global(context).GetString(MicrophoneKey, "") ?? "";

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
            Log.Warning($"The voice password could not be read", unreadable);
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

/// <summary>
/// The microphone for 🎤 by ia-voice: Android's voice-recognition input, 16
/// kHz, mono, 16 bits, read in pieces of 50 ms on a thread of its own; the
/// one chosen (<see cref="VoicePreferences.Microphone"/>) where it is
/// connected, a Bluetooth one made the call's device while it listens, as
/// Android routes no other's sound from it; else the system's default.
/// </summary>
public sealed class AndroidMicrophone(Context context) : IMicrophone
{
    private const int PieceBytes = VoiceTyping.Rate * 2 / 20;

    /// <summary>The kinds offered besides the system's default, as Windows offers each microphone: a headset's, a USB or a Bluetooth one; the device's own are the default's.</summary>
    private static readonly AudioDeviceType[] Offered =
        [AudioDeviceType.WiredHeadset, AudioDeviceType.UsbDevice, AudioDeviceType.UsbHeadset, AudioDeviceType.UsbAccessory, AudioDeviceType.BluetoothSco, AudioDeviceType.BleHeadset];

    private AudioRecord? record;
    private Thread? reader;
    private volatile bool reading;

    /// <summary>A Bluetooth microphone made the call's device for this dictation, given back when it stops.</summary>
    private bool calling;

    /// <summary>The microphones connected now that can be chosen, by name, each once.</summary>
    public static IReadOnlyList<string> Names(Context context) => [.. Inputs(context).Select(NameOf).Distinct()];

    private static AudioManager Audio(Context context) => (AudioManager)context.GetSystemService(Context.AudioService)!;

    private static IEnumerable<AudioDeviceInfo> Inputs(Context context) =>
        Audio(context).GetDevices(GetDevicesTargets.Inputs)!.Where(device => Offered.Contains(device.Type));

    private static string NameOf(AudioDeviceInfo device) => device.ProductName?.ToString() is { Length: > 0 } name ? name : device.Type.ToString();

    public void Start(Action<byte[], int> heard)
    {
        var least = AudioRecord.GetMinBufferSize(VoiceTyping.Rate, ChannelIn.Mono, Encoding.Pcm16bit);
        var listening = new AudioRecord(AudioSource.VoiceRecognition, VoiceTyping.Rate, ChannelIn.Mono, Encoding.Pcm16bit, Math.Max(least, PieceBytes) * 2);
        if (listening.State != State.Initialized)
        {
            listening.Release();
            throw new InvalidOperationException("The microphone did not open.");
        }

        if (VoicePreferences.Microphone(context) is { Length: > 0 } wanted)
        {
            if (Inputs(context).FirstOrDefault(device => NameOf(device) == wanted) is { } chosen)
            {
                listening.SetPreferredDevice(chosen);
                if (chosen.Type is AudioDeviceType.BluetoothSco or AudioDeviceType.BleHeadset)
                {
                    calling = Audio(context).SetCommunicationDevice(chosen);
                }
            }
            else
            {
                Log.Write($"Voice typing: the microphone {wanted} is not connected, so the system's default listens");
            }
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
        EndCall();
    }

    public void Dispose()
    {
        reading = false;
        record?.Release();
        record = null;
        EndCall();
    }

    private void EndCall()
    {
        if (calling)
        {
            Audio(context).ClearCommunicationDevice();
            calling = false;
        }
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

    /// <summary>The first words of the utterance going on that were forgotten (<see cref="Forget"/>): the recognizer says them again with each result until it ends.</summary>
    private int forgotten;

    /// <summary>The utterance going on is left unsaid until it ends (<see cref="Forget"/> without keeping what was not read yet).</summary>
    private bool dropping;

    /// <summary>Whether the recognizer has heard speech begin in the utterance going on: silence waited in has nothing to leave unsaid.</summary>
    private bool speaking;

    /// <summary>Whether the recognizer is hearing an utterance, from asked to listen until it ends it.</summary>
    private bool listening;

    private TaskCompletionSource? lastWords;

    public bool Recording { get; private set; }

    /// <summary>A voice being heard now, as the recognizer tells it: from speech beginning to its end.</summary>
    public bool Speaking => Recording && voiced;

    private bool voiced;

    /// <summary>How long after it stops the device's sounds are given back: the recognizer's last beep comes as it is let go.</summary>
    private const int SoundsBackAfterMs = 700;

    /// <summary>Whether this dictation silenced the device's sounds (Sound effects off).</summary>
    private bool silenced;

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
        forgotten = 0;
        dropping = false;
        lastWords = null;
        listener ??= new Listener(this);
        recognizer ??= SpeechRecognizer.CreateSpeechRecognizer(context)!;
        recognizer.SetRecognitionListener(listener);
        Recording = true;
        if (!VoicePreferences.Sounds(context))
        {
            VoicePreferences.SilenceSounds(context);
            silenced = true;
        }

        Listen();
    }

    public async Task<string> Stop()
    {
        if (!Recording)
        {
            return "";
        }

        Recording = false;
        if (listening)
        {
            lastWords = new TaskCompletionSource();
            recognizer?.StopListening();
            await Task.WhenAny(lastWords.Task, Task.Delay(LastWords));
        }

        // Between two utterances (the silence that stops it by itself) there are no last words to wait for.
        listening = false;
        voiced = false;
        recognizer?.Cancel();
        SoundsBack();
        return Joined();
    }

    /// <summary>
    /// All heard so far forgotten, the recognizer left listening as it is
    /// (stopped and started again it may answer busy, which ends the
    /// dictation). Of the utterance going on, the words written so far are
    /// counted and left out of what it still says of it, so only those not
    /// read yet come; or, where they are not kept, all of it is left unsaid
    /// until it ends, and the next utterance is the first to come.
    /// </summary>
    public void Forget(bool keepUnread)
    {
        forgotten += WordsOf(utterance).Length;
        dropping = !keepUnread && speaking;
        heard.Clear();
        utterance = "";
    }

    public void Dispose()
    {
        Recording = false;

        // Cancelled before it is let go: let go while listening (a key typed stops the microphone at once), the next
        // dictations heard nothing for a minute on the owner's headset, 2026-11-14.
        voiced = false;
        recognizer?.Cancel();
        recognizer?.Destroy();
        SoundsBack();
        recognizer = null;
        listener?.Dispose();
        listener = null;
    }

    private void SoundsBack()
    {
        if (silenced)
        {
            silenced = false;
            VoicePreferences.GiveSoundsBack(context, SoundsBackAfterMs);
        }
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

        listening = true;
        speaking = false;
        voiced = false;
        recognizer?.StartListening(asked);
    }

    /// <summary>All heard, each utterance after the word that tells them apart (<see cref="VoiceCommands.UtteranceBreak"/>), the first too, so the commands know they are told apart; they read that word and nothing writes it.</summary>
    private string Joined() => string.Concat(heard.Append(utterance).Where(words => words.Length > 0).Select(words => $"{VoiceCommands.UtteranceBreak} {words} ")).TrimEnd();

    private static string? Words(Bundle? results) => results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault();

    private static string[] WordsOf(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>What the recognizer says of the utterance going on, without the words forgotten.</summary>
    private string Kept(string words) => forgotten == 0 ? words : string.Join(' ', WordsOf(words).Skip(forgotten));

    /// <summary>An utterance ended with its words, or with none (silence, nothing understood): kept, then the dictation ends where it was asked to or the options stop it, else it listens on.</summary>
    private void Ended(string? words)
    {
        if (!dropping && words is not null && Kept(words) is { Length: > 0 } kept)
        {
            heard.Add(kept);
        }

        dropping = false;
        speaking = false;
        voiced = false;
        forgotten = 0;
        utterance = "";
        listening = false;
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
            speech.speaking = true;
            speech.voiced = true;
        }

        public void OnBufferReceived(byte[]? buffer)
        {
        }

        public void OnEndOfSpeech() => speech.voiced = false;

        public void OnError(SpeechRecognizerError error)
        {
#if DEBUG
            if (error is SpeechRecognizerError.NoMatch or SpeechRecognizerError.SpeechTimeout)
            {
                // A Debug build alone: every time the recognizer hears nothing, to tell a microphone that gives it none.
                speech.log($"Android's recognizer heard nothing (Debug build only): {error}");
            }
#endif
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
            if (!speech.dropping && Words(partialResults) is { } words)
            {
                speech.utterance = speech.Kept(words);
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
