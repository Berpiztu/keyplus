using Android.App;
using Android.Content;
using Android.InputMethodServices;
using Android.Widget;
using Android.Runtime;
using Android.Views;
using Android.Views.InputMethods;
using Keycode = Android.Views.Keycode;

[assembly: UsesPermission(Berpiztu.KeyPlus.ScreenshotWatcher.Permission)]

namespace Berpiztu.KeyPlus;

/// <summary>
/// Berpiztu AI Keyboard: the <see cref="KeyPanel"/> as a keyboard of its
/// own, chosen in Android's keyboard list, on any Android. A headset
/// (Android XR) keeps the popup's window in front of the user's head, but
/// lets the user place a keyboard. The panel types straight into the editor; ABC is the whole
/// keyboard and Keys the strip's. Android sets where it stands and its width,
/// so ⤓ does not dock it; A− and A+ size the labels, ▭ makes it two rows,
/// – folds it into the ⌨ icon, and ⚙, in ✕'s place, shows its settings in
/// the keys' place, as Gboard does: on a phone Mover and Redimensionar (a
/// frame over the keyboard, <see cref="KeyboardFrame"/>), Modo normal, docked
/// at the bottom as wide as the screen, or Modo ventana, a window of its own
/// over the app, and the keyboard picker. Place and size are kept per screen
/// and mode, as the popup keeps them. It is a product of its own
/// (-p:KeyPlusProduct=Keyboard); the popup is the other.
/// </summary>
[Service(Name = "ai.berpiztu.keyboard.KeyPlusKeyboard", Label = "@string/keyboard_label",
    Permission = "android.permission.BIND_INPUT_METHOD", Exported = true)]
[IntentFilter(["android.view.InputMethod"])]
[MetaData("android.view.im", Resource = "@xml/keyplus_keyboard")]
public sealed class KeyPlusKeyboard : InputMethodService
{
    private KeyPanel? panel;
    private PanelPlacement? placed;

    /// <summary>The keys' size and the keyboard's height when a grip's drag started.</summary>
    private (float Scale, int Height) resizeStart;

    /// <summary>
    /// Off a headset, what the keyboard's window holds: the keyboard
    /// (<see cref="box"/>), at the bottom, or, in Modo ventana, anywhere in a
    /// room as tall as the screen, the app's touches passing through it
    /// (<see cref="OnComputeInsets"/>).
    /// </summary>
    private FrameLayout? container;

    /// <summary>The keys, and over them, while Mover or Redimensionar is on, the <see cref="frame"/>.</summary>
    private FrameLayout? box;

    /// <summary>Docked, the keyboard's ground, as wide as the screen, behind the keys and under them: beside keys made narrower, as far as Mover raised them, and the navigation bar's room.</summary>
    private View? ground;
    private KeyboardFrame? frame;

    /// <summary>Where the keyboard stood and how large it was when a drag on the frame or the title bar started.</summary>
    private (int X, int Y, int Width, int Lift, float Scale, int Height) adjustStart;

    /// <summary>The keyboard's place and size as Mover or Redimensionar found it, for Cancelar.</summary>
    private (int X, int Y, int Width, float Scale, int Lift, int DockedX, int DockedWidth) frameStart;

    /// <summary>The texts copied lately, which 📋 lists.</summary>
    private ClipboardHistory? clipboard;
    private ClipboardManager? clipboardManager;

    /// <summary>Brings each new screenshot into the copied list, once the user lets the keyboard read the photos.</summary>
    private ScreenshotWatcher? screenshots;

    public override void OnCreate()
    {
        base.OnCreate();
        KeyPlusPreferences.SplitLayoutsOnce(this);
        clipboard = new ClipboardHistory(this);
        clipboardManager = (ClipboardManager?)GetSystemService(ClipboardService);
        if (clipboardManager is not null)
        {
            clipboardManager.PrimaryClipChanged += OnCopied;
        }

        screenshots = new ScreenshotWatcher(this, clipboard);
        screenshots.Start();

        // The PC designs each screen kept before the list of keyboards, made keyboards of it once.
        Keyboards.MoveDesignsOnce(this);

        // FUTO Swipe and its word list, loaded once, away from the main thread.
        swipeTyping = new SwipeTyping(this);

        MenuWatchService.MenuOpened += AvoidMenu;
        MenuWatchService.MenuClosed += MenuGone;
    }

    /// <summary>Words swiped over the letters, on a phone.</summary>
    private SwipeTyping? swipeTyping;

    /// <summary>How far back the field is read for the word being typed.</summary>
    private const int WordReach = 48;

    /// <summary>Each language's words to offer as a word is typed, read from its word list the first time it is asked for, away from the main thread.</summary>
    private readonly Dictionary<StandardLanguage, Task<WordSuggestions?>> suggestions = [];

    /// <summary>The words offered for a word being typed; none until the language's word list is read.</summary>
    private IReadOnlyList<string> Suggest(StandardLanguage language, string typed)
    {
        if (!suggestions.TryGetValue(language, out var reading))
        {
            suggestions[language] = reading = Task.Run(() =>
            {
                try
                {
                    using var wordList = Assets!.Open(language.WordList);
                    return WordSuggestions.Read(wordList, language);
                }
                catch (Exception failure)
                {
                    Android.Util.Log.Warn("KeyPlus", $"The {language.Code} words to offer could not be read: {failure.Message}");
                    return null;
                }
            });
        }

        return reading.IsCompletedSuccessfully && reading.Result is { } ready ? ready.For(typed) : [];
    }

    /// <summary>Swipes read as words, where the app's settings have it on (Escritura deslizando el dedo); none, the letters only tapped.</summary>
    private Func<KeyPanel.SwipePath, Task<IReadOnlyList<string>>>? SwipeOn() =>
        KeyPlusPreferences.Global(this).GetBoolean(KeyPlusPreferences.SwipeTypingKey, true) && swipeTyping is { } swiping ? swiping.Words : null;

    /// <summary>
    /// Whether the keyboard shows for the field: as Android decides (not
    /// while a hardware keyboard is in), and never while the app's settings
    /// have it off, for another keyboard in use; still the keyboard in use,
    /// what a hardware keyboard types goes through it.
    /// </summary>
    public override bool OnEvaluateInputViewShown()
    {
        var kept = KeyPlusPreferences.Global(this);
        return base.OnEvaluateInputViewShown()
            && (kept.GetBoolean(KeyPlusPreferences.ShowKeyboardKey, true) || kept.GetBoolean(KeyPlusPreferences.KeyboardIconKey, true));
    }

    /// <summary>
    /// Its field let it go (Enter, the focus elsewhere): hidden, made its ⌨
    /// icon, or kept on screen, as the app's settings say (Al perder el foco);
    /// hidden all the same when the user asks, with ∨ or Back.
    /// </summary>
    public override void HideWindow()
    {
        var asked = hideAsked;
        hideAsked = false;
        var choice = KeyPlusPreferences.Global(this).GetString(KeyPlusPreferences.FocusLostKey, KeyPlusPreferences.FocusLostHide);
        if (!asked && choice != KeyPlusPreferences.FocusLostHide)
        {
            Android.Util.Log.Info("KeyPlus", $"Its field let it go: kept on screen ({choice})");
            if (choice == KeyPlusPreferences.FocusLostIcon)
            {
                panel?.ShowAsIcon(true);
                iconAsFieldLeft = true;
            }

            return;
        }

        base.HideWindow();
    }

    /// <summary>The keyboard became its icon as its field let it go: the next field opens it whole again.</summary>
    private bool iconAsFieldLeft;

    /// <summary>The user asked the keyboard to hide (∨, Back), which it does even while it stays on screen as its field lets it go.</summary>
    private bool hideAsked;

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (keyCode == Keycode.Back && IsInputViewShown)
        {
            hideAsked = true;
        }

        return base.OnKeyDown(keyCode, e);
    }

    /// <summary>The keyboard opening: the screenshots watched, if the user has let it read the photos since.</summary>
    public override void OnStartInputView(Android.Views.InputMethods.EditorInfo? info, bool restarting)
    {
        base.OnStartInputView(info, restarting);
        InAppColors(info?.PackageName);
        panel?.FitLabels();
        if (panel is not null && !OnHeadset)
        {
            panel.ReadSettings();
            panel.Swiping = SwipeOn();
            panel.ForgetTyping();

            // Not shown, for another keyboard in use: its ⌨ icon in its place, which a tap opens. Made its
            // icon as the last field let it go, whole again for this one.
            if (!KeyPlusPreferences.Global(this).GetBoolean(KeyPlusPreferences.ShowKeyboardKey, true))
            {
                panel.ShowAsIcon(true);
            }
            else if (iconAsFieldLeft)
            {
                panel.ShowAsIcon(false);
            }

            iconAsFieldLeft = false;

            // The caret's place on the screen, now and as it moves, for a keyboard in a window to keep clear of it.
            var followed = CurrentInputConnection?.RequestCursorUpdates((int)(CursorUpdate.Immediate | CursorUpdate.Monitor)) == true;
            caretSkipLogged = null;
            Android.Util.Log.Info("KeyPlus", $"Field of {info?.PackageName}: its caret {(followed ? "followed" : "not told by the app")}");
        }

        screenshots?.Start();
    }

    public override void OnDestroy()
    {
        if (clipboardManager is not null)
        {
            clipboardManager.PrimaryClipChanged -= OnCopied;
        }

        screenshots?.Stop();
        dictation?.Dispose();
        MenuWatchService.MenuOpened -= AvoidMenu;
        MenuWatchService.MenuClosed -= MenuGone;
        base.OnDestroy();
    }

    /// <summary>🎤's dictation going on: by Android's own recognizer (Local) or ia-voice at an address, as Escribir por voz says.</summary>
    private IDictation? dictation;
    private bool transcribing;

    /// <summary>The space a dictation's words need before them: none after a space or nothing.</summary>
    private string spokenLead = "";

    /// <summary>
    /// 🎤 tapped: a dictation started, its words written in the field as they
    /// are heard (Android's composing text, underlined as Gboard's), or the
    /// one going on stopped and its words written for good. Without the
    /// microphone's permission, the app asks for it.
    /// </summary>
    private void Dictate()
    {
        if (transcribing)
        {
            return;
        }

        if (dictation is { Recording: true })
        {
            EndDictation();
            return;
        }

        if (CheckSelfPermission(Android.Manifest.Permission.RecordAudio) != Android.Content.PM.Permission.Granted)
        {
            Toast.MakeText(this, Resource.String.voice_permission_asked, ToastLength.Long)!.Show();
            StartActivity(new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask).PutExtra(MainActivity.AskMicrophoneExtra, true));
            return;
        }

        var locale = panel?.DictationLocale ?? StandardKeys.Spanish.Locale;
        var local = VoicePreferences.Local(this);
        dictation?.Dispose();
        IDictation made;
        VoiceOptions options;
        if (local)
        {
            var speech = new AndroidSpeech(this, LogVoice);
            if (!speech.Available)
            {
                Toast.MakeText(this, Resource.String.voice_unavailable, ToastLength.Short)!.Show();
                return;
            }

            made = speech;
            options = new VoiceOptions(VoicePreferences.AutoStop(this), VoicePreferences.Silence(this), 1, "", "", "", locale);
        }
        else
        {
            made = new VoiceTyping(new AndroidMicrophone(), LogVoice);
            options = VoicePreferences.Options(this, locale);
        }

        made.Level += level =>
        {
            if (dictation == made && made.Recording)
            {
                panel?.ShowListening(true, level);
            }
        };
        made.Partial += text => CurrentInputConnection?.SetComposingText(text.Length == 0 ? "" : spokenLead + text, 1);
        made.Silence += EndDictation;
        var before = CurrentInputConnection?.GetTextBeforeCursor(1, 0)?.ToString();
        spokenLead = string.IsNullOrEmpty(before) || char.IsWhiteSpace(before[^1]) ? "" : " ";
        try
        {
            made.Start(options);
        }
        catch (Exception none) when (none is InvalidOperationException or Java.Lang.Exception)
        {
            LogVoice($"The microphone could not listen: {none.Message}");
            Toast.MakeText(this, Resource.String.voice_no_microphone, ToastLength.Short)!.Show();
            made.Dispose();
            return;
        }

        dictation = made;
        panel?.ShowListening(true, 0);
    }

    /// <summary>The dictation stopped: all its words written for good in its composing text's place.</summary>
    private async void EndDictation()
    {
        if (dictation is not { Recording: true } going || transcribing)
        {
            return;
        }

        transcribing = true;
        panel?.ShowListening(false, 0);
        try
        {
            var text = await going.Stop();
            if (text.Length > 0)
            {
                CurrentInputConnection?.CommitText(spokenLead + text, 1);
            }
            else
            {
                CurrentInputConnection?.FinishComposingText();
                Toast.MakeText(this, Resource.String.voice_nothing_heard, ToastLength.Short)!.Show();
            }
        }
        catch (VoiceTyping.SignInRefused)
        {
            LogVoice("ia-voice's proxy did not take the user and password.");
            CurrentInputConnection?.FinishComposingText();
            Toast.MakeText(this, Resource.String.voice_sign_in_refused, ToastLength.Long)!.Show();
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            LogVoice($"ia-voice did not answer: {failure.Message}");
            CurrentInputConnection?.FinishComposingText();
            Toast.MakeText(this, Resource.String.voice_unavailable, ToastLength.Short)!.Show();
        }
        finally
        {
            transcribing = false;
        }
    }

    /// <summary>The field let the keyboard go while dictating: the dictation dropped, the words so far left as written.</summary>
    private void DropDictation()
    {
        if (dictation is not { Recording: true } going)
        {
            return;
        }

        going.Dispose();
        dictation = null;
        CurrentInputConnection?.FinishComposingText();
        panel?.ShowListening(false, 0);
    }

    private static void LogVoice(string line) => Android.Util.Log.Info("KeyPlus", line);

    /// <summary>
    /// Something copied, in any app, kept in the copied list: an image, read
    /// into a file of its own while Android lets the keyboard read it, or a
    /// text. The keyboard in use is the one app Android lets read the
    /// clipboard whenever it changes.
    /// </summary>
    private void OnCopied(object? sender, EventArgs e)
    {
        if (clipboard is null || clipboardManager?.PrimaryClip is not { ItemCount: > 0 } clip)
        {
            return;
        }

        var item = clip.GetItemAt(0)!;
        var mimeType = item.Uri is { } uri ? ContentResolver?.GetType(uri) : null;
        if (mimeType?.StartsWith("image/", StringComparison.Ordinal) == true)
        {
            try
            {
                using var image = ContentResolver!.OpenInputStream(item.Uri!)!;
                clipboard.AddImage(image, mimeType);
            }
            catch (Exception unreadable) when (unreadable is Java.Lang.SecurityException or Java.IO.IOException or IOException)
            {
                Android.Util.Log.Warn("KeyPlus", $"The image copied could not be read: {unreadable.Message}");
            }

            return;
        }

        clipboard.Add(item.CoerceToText(this)?.ToString());
    }

    /// <summary>
    /// An image copied pasted into the field, as Android hands a keyboard's
    /// content to an app (commitContent), with the right to read it; an app
    /// whose field takes no images is told so, and nothing is pasted.
    /// </summary>
    private void PasteImage(ClipboardHistory.Entry image)
    {
        var accepted = CurrentInputEditorInfo?.ContentMimeTypes ?? [];
        if (!accepted.Any(type => ClipDescription.CompareMimeTypes(image.MimeType, type)))
        {
            Android.Widget.Toast.MakeText(this, "This field takes no images", Android.Widget.ToastLength.Short)!.Show();
            return;
        }

        var content = new InputContentInfo(ClipboardImages.UriOf(image.Image!), new ClipDescription("Image", [image.MimeType!]));
        CurrentInputConnection?.CommitContent(content, InputContentFlags.GrantReadUriPermission, null);
    }

    /// <summary>
    /// The panel, with the layout of the screen the keyboard is on. Made again
    /// at each configuration change (the phone turned, a Fold opened), so
    /// each screen opens as it was left.
    /// </summary>
    public override View? OnCreateInputView()
    {
        panel?.StopRepeat();
        shownColors = StandardColors.Chosen(this, StandardColors.App);
        var screen = KeyPlusPreferences.ScreenOf(this, Display?.DisplayId ?? Android.Views.Display.DefaultDisplay);
        var preferences = KeyPlusPreferences.ForScreen(this, screen);
        Watch(preferences);
        placed = new PanelPlacement(preferences);
        // It opens on its letters the first time, as a keyboard does, on a phone and on a headset;
        // on a phone, on the standard keyboard, Gboard's.
        panel = new KeyPanel(this, preferences, clipboard!, SendKey, TypeText, lettersFirst: true, keyboardScreen: OnHeadset ? null : screen, floating: placed.Floating)
        {
            Enter = PerformEnter,
            Dockable = false,
            PasteImage = PasteImage,
            Settings = [new("Cambiar de teclado", ShowKeyboardPicker)],
        };
        placed.Mode = panel.Mode;
        panel.Scale = placed.Scale;
        if (OnHeadset)
        {
            panel.Resizing += OnResizing;
            // The keys sized to the headset's box, whatever its mode: no grips to size them.
            panel.Resizable = false;
            panel.SideKeys = false;
            panel.Root.LayoutChange += (_, _) => FillBox();
        }
        else
        {
            // Moved and sized from ⚙, or a corner's grip dragged, a large one.
            // What was copied in the title bar, as Gboard has it.
            panel.Settings = PhoneSettings();
            panel.GridTiles = GridTiles();
            panel.CopiedInTitleBar = true;

            // Words swiped over the letters, as Gboard's (FUTO Swipe).
            panel.Swiping = SwipeOn();
            panel.VoiceTapped += Dictate;
            panel.Suggesting = Suggest;
            panel.WordBeforeCaret = () =>
            {
                var before = CurrentInputConnection?.GetTextBeforeCursor(WordReach, 0)?.ToString() ?? "";
                var start = before.Length;
                while (start > 0 && char.IsLetter(before[start - 1]))
                {
                    start--;
                }

                return before[start..];
            };
            panel.TextBeforeCaret = () => CurrentInputConnection?.GetTextBeforeCursorFormatted(1, 0)?.ToString();
            panel.ReplaceBeforeCaret = (length, text) =>
            {
                if (CurrentInputConnection is not { } field)
                {
                    return;
                }

                field.BeginBatchEdit();
                field.DeleteSurroundingText(length, 0);
                field.CommitText(text, 1);
                field.EndBatchEdit();
            };
            panel.LargeGrips = true;
            // A long press, on the bar's buttons, its free room or between the
            // keys, moves the keyboard (docked, up and down); a corner's grip
            // dragged, or a long press near a corner, sizes it from there, as
            // Redimensionar does.
            panel.Moving += (drag, dx, dy) => Adjust(KeyboardFrame.Part.Whole, drag, dx, dy);
            panel.Resizing += (corner, drag, dx, dy) => Adjust(
                ((corner & KeyPanel.Corner.Top) != 0 ? KeyboardFrame.Part.Top : KeyboardFrame.Part.Bottom)
                | ((corner & KeyPanel.Corner.Left) != 0 ? KeyboardFrame.Part.Left : KeyboardFrame.Part.Right),
                drag,
                dx,
                dy);
            frame = new KeyboardFrame(this);
            panel.ResizeRequested += () => ShowFrame(resizing: true);
            panel.HideRequested += () =>
            {
                hideAsked = true;
                RequestHideSelf(0);
            };
            panel.ResetSizeRequested += ResetSize;
            frame.Dragged += Adjust;
            frame.ResetRequested += ResetSize;
            frame.CancelRequested += CancelFrame;
            frame.DoneRequested += frame.Hide;
            box = new FrameLayout(this);
            box.AddView(panel.Root, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            box.AddView(frame, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0));

            // The frame as tall as the keys, not as the room it would stretch the box to.
            panel.Root.LayoutChange += (_, change) =>
            {
                var height = change.Bottom - change.Top;
                if (frame.LayoutParameters!.Height != height)
                {
                    frame.LayoutParameters.Height = height;
                    frame.Post(frame.RequestLayout);
                }
            };
            container = new FrameLayout(this);
            ground = new View(this);
            ground.SetBackgroundColor(panel.GroundColor);
            container.AddView(ground);
            container.AddView(box);
            if (KeyPlusPreferences.IsHeadset(this))
            {
                AsHeadsetCard(box, ground);
            }

            // Editar's card above the keys, over the app, as Gboard's.
            panel.EditorOutside = true;
            container.AddView(panel.EditorCard);
            panel.EditingChanged += () => container.Post(Place);

            // The keys' height changes with their size and mode: in a window
            // kept inside the room, and the editor's card kept above them.
            box.LayoutChange += (_, _) => box.Post(Place);
            Place();
        }

        panel.UsualSizeRequested += OnUsualSizeRequested;
        panel.ModeChanged += OnModeChanged;
        return container ?? panel.Root;
    }

    /// <summary>⚙ on a phone, as Gboard's tiles: Mover, Redimensionar, the two modes, the one in use marked, and the keyboard picker.</summary>
    private IReadOnlyList<KeyPanel.Setting> PhoneSettings()
    {
        var windowed = placed?.Floating == true;
        return
        [
            new("Mover", () => ShowFrame(resizing: false), "✥"),
            new("Redimensionar", () => ShowFrame(resizing: true), "⇲"),
            new("Diseñar", () => panel?.Design(), "✎"),
            new("Modo normal", () => SetWindowed(false), "⬓", InUse: !windowed),
            new("Modo ventana", () => SetWindowed(true), "❐", InUse: windowed),
            new("Cambiar de teclado", ShowKeyboardPicker, "🌐"),
        ];
    }

    /// <summary>
    /// The standard keyboard's grid (⊞), Gboard's buttons in Gboard's order
    /// as the owner's Fold has them, then the keyboard's own: Flotante is
    /// Modo ventana, Deshacer Ctrl+Z; the rest not made yet say so.
    /// </summary>
    private IReadOnlyList<GridTile> GridTiles()
    {
        void NotMade(int name) => panel?.NotMadeYet(GetString(name));
        return
        [
            // The owner's sign, a screen with keys; crossed while the keyboard floats, as Gboard's; a tap, the other state.
            placed?.Floating == true
                ? new("floating", GetString(Resource.String.floating), Resource.Drawable.ic_grid_floating_off, () => SetWindowed(false))
                : new("floating", GetString(Resource.String.floating), Resource.Drawable.ic_grid_floating, () => SetWindowed(true)),
            new("gif", "GIF", Resource.Drawable.ic_grid_gif, () => panel?.NotMadeYet("GIF"), More: true),
            new("split", GetString(Resource.String.split), Resource.Drawable.ic_grid_split, () => NotMade(Resource.String.split)),
            new("translate", GetString(Resource.String.translate), Resource.Drawable.ic_grid_translate, () => NotMade(Resource.String.translate)),
            new("theme", GetString(Resource.String.theme), Resource.Drawable.ic_grid_theme, () => NotMade(Resource.String.theme), More: true),
            new("share", GetString(Resource.String.share), Resource.Drawable.ic_grid_share, () => NotMade(Resource.String.share)),
            new("emojis", "Emojis", Resource.Drawable.ic_key_emoji, () => panel?.ShowEmojis(), More: true),
            new("undo", GetString(Resource.String.undo), Resource.Drawable.ic_grid_undo, () => SendKey(Keycode.Z, MetaKeyStates.CtrlOn | MetaKeyStates.CtrlLeftOn)),
            new("nextLanguage", GetString(Resource.String.next_language), Resource.Drawable.ic_key_language, () => NotMade(Resource.String.next_language)),
            new("textEditing", GetString(Resource.String.text_editing), Resource.Drawable.ic_grid_text_editing, () => NotMade(Resource.String.text_editing), More: true),
            new("move", GetString(Resource.String.move), Resource.Drawable.ic_grid_move, () => ShowFrame(resizing: false)),
            new("switchKeyboard", GetString(Resource.String.switch_keyboard), Resource.Drawable.ic_grid_keyboard, ShowKeyboardPicker),
        ];
    }

    /// <summary>Android's keyboard picker, to change for another keyboard.</summary>
    private void ShowKeyboardPicker() => ((InputMethodManager)GetSystemService(InputMethodService)!).ShowInputMethodPicker();

    /// <summary>
    /// A drag of the whole keyboard ended, docking and undocking by dragging
    /// being on: floating, its bottom within <see cref="DockReachDp"/> of the
    /// room's, it docks; docked, raised more than half its height, it floats
    /// where it was let go, its lift as before the drag.
    /// </summary>
    private void DockOrFloat(int lift, int liftBefore)
    {
        if (placed is null || box is null)
        {
            return;
        }

        var room = Room();
        if (placed.Floating)
        {
            if (placed.Y + box.Height >= room.Height - Dp(DockReachDp))
            {
                SetWindowed(false);
            }

            return;
        }

        if (lift - liftBefore > box.Height / 2)
        {
            (placed.Width, placed.X) = (box.Width, box.Left);
            placed.Y = room.Height - room.Below - lift - box.Height;
            placed.Lift = liftBefore;
            SetWindowed(true);
        }
    }

    /// <summary>
    /// The caret moved, or the field: a keyboard in a window that covers the
    /// text (<see cref="Avoided"/>: the field, or the caret's line in a tall
    /// one), keeping clear of it being on, moves above it where there is room,
    /// else below it, <see cref="CaretGapDp"/> away, else to the side with more
    /// room. Where it stood is kept, as after a drag: it stays clear of the
    /// text from then on. Each choice is written to the log.
    /// </summary>
    public override void OnUpdateCursorAnchorInfo(CursorAnchorInfo? cursorAnchorInfo)
    {
        base.OnUpdateCursorAnchorInfo(cursorAnchorInfo);
        var skipped = cursorAnchorInfo is null ? "no caret told"
            : placed is not { Floating: true } ? "docked"
            : box is null || container is null || box.Height == 0 ? "not laid out"
            : OnHeadset ? "on a headset"
            : !KeyPlusPreferences.Global(this).GetBoolean(KeyPlusPreferences.AvoidCaretKey, true) ? "turned off"
            : float.IsNaN(cursorAnchorInfo.InsertionMarkerTop) && cursorAnchorInfo.EditorBoundsInfo?.EditorBounds is null ? "the app tells neither its caret nor its field"
            : null;
        if (skipped is not null || cursorAnchorInfo is null || placed is null || box is null || container is null)
        {
            if (skipped != caretSkipLogged)
            {
                caretSkipLogged = skipped;
                Android.Util.Log.Info("KeyPlus", $"Caret: not avoided, {skipped}");
            }

            return;
        }

        caretSkipLogged = null;

        // The caret and the field, from the field's own place to the screen's.
        var matrix = cursorAnchorInfo.Matrix;
        float[] caret = [cursorAnchorInfo.InsertionMarkerHorizontal, cursorAnchorInfo.InsertionMarkerTop, cursorAnchorInfo.InsertionMarkerHorizontal, cursorAnchorInfo.InsertionMarkerBottom];
        matrix?.MapPoints(caret);
        Android.Graphics.RectF? field = null;
        if (cursorAnchorInfo.EditorBoundsInfo?.EditorBounds is { } bounds)
        {
            field = new Android.Graphics.RectF(bounds);
            matrix?.MapRect(field);
        }

        // All on the room's terms, from the keyboard's window's top.
        var origin = new int[2];
        container!.GetLocationOnScreen(origin);
        var at = new int[2];
        box.GetLocationOnScreen(at);
        var room = Room();
        var gap = Dp(CaretGapDp);
        var avoided = Avoided(field, caret, origin, gap, room.Height);
        var top = at[1] - origin[1];
        var covered = avoided.Bottom > top && avoided.Top < top + box.Height && avoided.Right > at[0] && avoided.Left < at[0] + box.Width;
        if (!covered)
        {
            if (caretSkipLogged != "clear")
            {
                caretSkipLogged = "clear";
                Android.Util.Log.Info("KeyPlus", $"Text at {avoided.ToShortString()}, clear of the keyboard at {at[0]},{at[1]} ({box.Width}x{box.Height}), window from {origin[1]}");
            }

            return;
        }

        caretSkipLogged = null;
        Android.Util.Log.Info("KeyPlus", $"Text at {avoided.ToShortString()} under the keyboard at {at[0]},{at[1]} ({box.Width}x{box.Height}): moved");
        var above = (int)avoided.Top - gap - box.Height;
        var below = (int)avoided.Bottom + gap;
        placed.Y = above >= 0 ? above
            : below + box.Height <= room.Height ? below
            : avoided.Top >= room.Height - avoided.Bottom ? 0 : room.Height - box.Height;
        placed.Save();
        Place();
    }

    /// <summary>
    /// What the keyboard keeps clear of, on the room's terms: the whole field
    /// where the keyboard fits above or below it, so a field it covers in part
    /// is cleared too; else, a field too tall for that (an editor), the line
    /// the caret is on, across the field; with no field told, the caret alone.
    /// </summary>
    private Android.Graphics.RectF Avoided(Android.Graphics.RectF? field, float[] caret, int[] origin, int gap, int roomHeight)
    {
        var hasCaret = !float.IsNaN(caret[1]);
        if (field is { } whole)
        {
            var shifted = new Android.Graphics.RectF(whole.Left - origin[0], whole.Top - origin[1], whole.Right - origin[0], whole.Bottom - origin[1]);
            var fits = shifted.Top - gap - (box?.Height ?? 0) >= 0 || shifted.Bottom + gap + (box?.Height ?? 0) <= roomHeight;
            if (fits || !hasCaret)
            {
                return new Android.Graphics.RectF(whole.Left, shifted.Top, whole.Right, shifted.Bottom);
            }

            return new Android.Graphics.RectF(whole.Left, caret[1] - origin[1], whole.Right, caret[3] - origin[1]);
        }

        return new Android.Graphics.RectF(caret[0], caret[1] - origin[1], caret[0] + 1, caret[3] - origin[1]);
    }

    /// <summary>Why the caret was last not avoided, written to the log once until it changes.</summary>
    private string? caretSkipLogged;

    /// <summary>Whether the open menu has moved the keyboard already: once a menu, so its keys can be reached.</summary>
    private bool movedForMenu;

    /// <summary>
    /// Apartarse también de los menús, as on Windows: a menu (seen by the
    /// menu watch, where the user turned it on) under a keyboard in a window,
    /// the keyboard moves above it where there is room, else below it, else
    /// to the side with more room, once a menu, and stays where it went.
    /// </summary>
    private void AvoidMenu(Android.Graphics.Rect menu)
    {
        if (movedForMenu || placed is not { Floating: true } || box is null || container is null || box.Height == 0 || OnHeadset
            || !KeyPlusPreferences.Global(this).GetBoolean(KeyPlusPreferences.AvoidMenusKey, true))
        {
            return;
        }

        var origin = new int[2];
        container.GetLocationOnScreen(origin);
        var at = new int[2];
        box.GetLocationOnScreen(at);
        var room = Room();
        var (menuTop, menuBottom) = (menu.Top - origin[1], menu.Bottom - origin[1]);
        var top = at[1] - origin[1];
        if (menuBottom <= top || menuTop >= top + box.Height || menu.Right <= at[0] || menu.Left >= at[0] + box.Width)
        {
            return;
        }

        var gap = Dp(CaretGapDp);
        var above = menuTop - gap - box.Height;
        var below = menuBottom + gap;
        placed.Y = above >= 0 ? above
            : below + box.Height <= room.Height ? below
            : menuTop >= room.Height - menuBottom ? 0 : room.Height - box.Height;
        movedForMenu = true;
        placed.Save();
        Place();
    }

    private void MenuGone() => movedForMenu = false;

    /// <summary>The room a keyboard in a window keeps between itself and the caret it moved away from, in dp.</summary>
    private const int CaretGapDp = 16;

    /// <summary>How near the room's bottom a floating keyboard is let go to dock, in dp.</summary>
    private const int DockReachDp = 24;

    /// <summary>Modo ventana or Modo normal chosen.</summary>
    private void SetWindowed(bool windowed)
    {
        if (panel is null || placed is null)
        {
            return;
        }

        placed.Floating = windowed;
        placed.Save();

        // The same keyboard in the other mode; designed in it, with that mode's own design: the keyboard made again with it.
        panel.ShowsFloating = windowed;
        if (panel.Keyboard is { } shown && shown.DesignFor(docked: windowed) != shown.DesignFor(docked: !windowed))
        {
            panel.Root.Post(() => SetInputView(OnCreateInputView()));
            return;
        }

        panel.Settings = PhoneSettings();
        panel.GridTiles = GridTiles();
        Place();
    }

    /// <summary>
    /// The room the keyboard stands in: the screen's width, and its height
    /// less the status and navigation bars; the navigation bar's inset
    /// (Bar), which a keyboard's window reaches under; and the height docked
    /// keys keep clear of under them (Below): the keyboard's own navigation
    /// bar, where Android draws ∨ and ⌨ (navigation_bar_frame_height, 48 dp),
    /// taller than the gesture handle's inset, as Gboard keeps clear of it. A
    /// window is held to the room and the bar alone: held taller, Android cut
    /// it and the keys were laid out again and again.
    /// </summary>
    private (int Width, int Height, int Below, int Bar) Room()
    {
        var metrics = GetSystemService(WindowService).JavaCast<IWindowManager>()!.CurrentWindowMetrics;
        var bars = metrics.WindowInsets.GetInsetsIgnoringVisibility(WindowInsets.Type.SystemBars());
        var frame = Resources!.GetIdentifier("navigation_bar_frame_height", "dimen", "android");
        var below = Math.Max(bars.Bottom, frame != 0 ? Resources.GetDimensionPixelSize(frame) : 0);
        return (metrics.Bounds.Width(), metrics.Bounds.Height() - bars.Top - bars.Bottom, below, bars.Bottom);
    }

    /// <summary>
    /// The keyboard where it stands: docked, at the bottom as wide as the
    /// screen, raised as far as Mover left it, its ground under it; in a
    /// window, where the user left it and as wide, inside the room. Editar's
    /// card, while it shows, above the keys and as wide, or under them where
    /// a window has no room above. Only what changed is laid out again.
    /// </summary>
    private void Place()
    {
        if (container is null || box is null || ground is null || panel is null || placed is null)
        {
            return;
        }

        var room = Room();
        var gap = Dp(8);
        FrameLayout.LayoutParams keys, card, under;
        if (placed.Floating)
        {
            // The first time in a window, from ⚙ or from the app's Pop-up: three
            // quarters of the screen wide, in the middle, at the bottom; the
            // standard keyboard as wide as Gboard's floating one, 52 px of 1080
            // (20 dp) clear of each side.
            if (placed.Width == 0)
            {
                placed.Width = FloatingWidth(room.Width);
                placed.X = (room.Width - placed.Width) / 2;
                placed.Y = room.Height;
            }

            var width = Math.Clamp(placed.Width, Dp(PanelPlacement.MinWidthDp), room.Width);
            var left = Math.Clamp(placed.X, 0, room.Width - width);
            var top = Math.Clamp(placed.Y, 0, Math.Max(0, room.Height - Tall(width)));
            keys = new FrameLayout.LayoutParams(width, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.Left)
            {
                LeftMargin = left,
                TopMargin = top,
            };
            card = top >= Dp(EditorRoomDp)
                ? new FrameLayout.LayoutParams(width, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom | GravityFlags.Left)
                {
                    LeftMargin = left,
                    BottomMargin = room.Height - top + gap,
                }
                : new FrameLayout.LayoutParams(width, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.Left)
                {
                    LeftMargin = left,
                    TopMargin = top + box.Height + gap,
                };
            under = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, GravityFlags.Bottom);
            Hold(room.Bar, room.Height + room.Bar);
            panel.InWindow = true;
        }
        else
        {
            // As wide as the screen, or narrower where the user made it so, a
            // finger to reach them: the ground stays as wide as the screen,
            // and the app gives way to all of it, as to any keyboard.
            var raised = placed.Lift + room.Below;
            var width = placed.DockedWidth > 0 ? Math.Clamp(placed.DockedWidth, Dp(PanelPlacement.MinWidthDp), room.Width) : room.Width;
            if (KeyPlusPreferences.IsHeadset(this))
            {
                FillHeadsetWindow(width, room.Height - raised);
            }
            keys = new FrameLayout.LayoutParams(width, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom | GravityFlags.Left)
            {
                LeftMargin = Math.Clamp(placed.DockedX, 0, room.Width - width),
                BottomMargin = raised,
            };
            card = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom)
            {
                LeftMargin = gap,
                RightMargin = gap,
                BottomMargin = raised + box.Height + gap,
            };
            under = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, raised + box.Height, GravityFlags.Bottom);
            Hold(0, 0);
            panel.InWindow = false;
        }

        // Its icon alone (not shown, for another keyboard in use): where the user dragged it, at first at
        // the bottom right; nothing under it.
        if (panel.ShowsIcon)
        {
            var (right, bottom) = IconPlace();
            keys = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom | GravityFlags.Right)
            {
                RightMargin = Math.Clamp(right, 0, Math.Max(0, room.Width - box.Width)),
                BottomMargin = room.Below + Math.Clamp(bottom, 0, Math.Max(0, room.Height - box.Height)),
            };
            under = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, GravityFlags.Bottom);
        }

        Lay(box, keys);
        Lay(panel.EditorCard, card);
        Lay(ground, under);
    }

    /// <summary>
    /// The keyboard's whole height at that width, as it wants it, measured
    /// with no room limit: as laid out it is no taller than the room left
    /// under where it stands, so a window kept by it never rose enough for
    /// its bottom (the floating bar) to show; and, not laid out yet, a height
    /// of 0 left it on the room's bottom edge, never seen.
    /// </summary>
    private int Tall(int width)
    {
        box!.Measure(
            View.MeasureSpec.MakeMeasureSpec(width, MeasureSpecMode.Exactly),
            View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));
        return box.MeasuredHeight;
    }

    /// <summary>A window's usual width: the standard keyboard's as Gboard's floating one, the others three quarters of the room.</summary>
    private int FloatingWidth(int room) =>
        panel?.Mode == PanelPlacement.StandardMode ? room - (2 * Dp(FloatingMarginDp)) : room * 3 / 4;

    /// <summary>The icon's room from the screen's right and from the keyboard's navigation bar the first time, in dp.</summary>
    private const int IconMarginDp = 16;

    /// <summary>Where the icon stands, kept for the screen: from the room's right and bottom, in pixels.</summary>
    private const string IconRightPreference = "iconRight";
    private const string IconBottomPreference = "iconBottom";

    /// <summary>Where the icon stood when a drag of it started.</summary>
    private (int Right, int Bottom) iconStart;

    private (int Right, int Bottom) IconPlace() =>
        (watched?.GetInt(IconRightPreference, Dp(IconMarginDp)) ?? Dp(IconMarginDp), watched?.GetInt(IconBottomPreference, Dp(IconMarginDp)) ?? Dp(IconMarginDp));

    private void KeepIconPlace(int right, int bottom)
    {
        var room = Room();
        var size = box?.Width ?? 0;
        watched?.Edit()!
            .PutInt(IconRightPreference, Math.Clamp(right, 0, Math.Max(0, room.Width - size)))!
            .PutInt(IconBottomPreference, Math.Clamp(bottom, 0, Math.Max(0, room.Height - size)))!
            .Apply();
    }

    /// <summary>The standard keyboard's room on each side, floating the first time, in dp: Gboard's.</summary>
    private const float FloatingMarginDp = 52 / 2.625f;

    /// <summary>The room above a keyboard in a window that Editar's card takes, in dp; with less, the card stands under it.</summary>
    private const int EditorRoomDp = 140;

    /// <summary>A view laid out anew only where its place changed, for laying out is what calls <see cref="Place"/> again.</summary>
    /// <summary>
    /// The container's room under the keys and its least height, set only
    /// when they change: setting the least height asks for a layout even when
    /// it is the same, the layout placed the keyboard again (Place), and that
    /// went round about sixty times a second, a keyboard open or not.
    /// </summary>
    private void Hold(int below, int least)
    {
        if (container is null)
        {
            return;
        }

        if (container.PaddingLeft != 0 || container.PaddingTop != 0 || container.PaddingRight != 0 || container.PaddingBottom != below)
        {
            container.SetPadding(0, 0, 0, below);
        }

        if (container.MinimumHeight != least)
        {
            container.SetMinimumHeight(least);
        }
    }

    private static void Lay(View view, FrameLayout.LayoutParams layout)
    {
        if (view.LayoutParameters is FrameLayout.LayoutParams now
            && now.Width == layout.Width && now.Height == layout.Height && now.Gravity == layout.Gravity
            && now.LeftMargin == layout.LeftMargin && now.TopMargin == layout.TopMargin
            && now.RightMargin == layout.RightMargin && now.BottomMargin == layout.BottomMargin)
        {
            return;
        }

        view.LayoutParameters = layout;
    }

    /// <summary>
    /// Only the keys, and Editar's card while it shows, take touches; the
    /// rest of the keyboard's window passes them to the app. Docked, the app
    /// gives way to the keys, not to the card, which stands over it, as
    /// Gboard's; in a window, the app keeps the whole screen.
    /// </summary>
    public override void OnComputeInsets(Insets? outInsets)
    {
        base.OnComputeInsets(outInsets);
        if (outInsets is null || container is null || box is null || panel is null || placed is null || box.Height == 0)
        {
            return;
        }

        var at = new int[2];
        container.GetLocationInWindow(at);
        // In a window, or its icon alone, the app keeps all of its room.
        var top = placed.Floating || panel.ShowsIcon ? at[1] + container.Height : at[1] + box.Top;
        outInsets.ContentTopInsets = top;
        outInsets.VisibleTopInsets = top;
        outInsets.TouchableInsets = TouchableInsets.Region;
        var region = outInsets.TouchableRegion!;
        region.Set(at[0] + box.Left, at[1] + box.Top, at[0] + box.Right, at[1] + box.Bottom);
        if (!placed.Floating && !panel.ShowsIcon && ground is not null)
        {
            // Docked, the ground beside and under the keys is the keyboard's too.
            region.InvokeOp(at[0] + ground.Left, at[1] + ground.Top, at[0] + ground.Right, at[1] + ground.Bottom, Android.Graphics.Region.Op.Union!);
        }
        var card = panel.EditorCard;
        if (card.Visibility == ViewStates.Visible)
        {
            region.InvokeOp(at[0] + card.Left, at[1] + card.Top, at[0] + card.Right, at[1] + card.Bottom, Android.Graphics.Region.Op.Union!);
        }
    }

    /// <summary>
    /// A finger on the frame, a corner's grip or a long press on the
    /// keyboard: the whole moves it, in a window anywhere, docked across and
    /// up and down; the top and bottom edges make the keys larger or smaller,
    /// the side ones the keys wider or narrower (docked, within the
    /// keyboard's ground, as wide as the screen), a corner both. Kept once
    /// the finger is lifted.
    /// </summary>
    private void Adjust(KeyboardFrame.Part part, KeyPanel.Drag drag, float dx, float dy)
    {
        if (panel is null || placed is null || box?.LayoutParameters is not FrameLayout.LayoutParams layout)
        {
            return;
        }

        panel.NoteDrag(drag, before: true);
        if (drag == KeyPanel.Drag.Start)
        {
            adjustStart = (box.Left, layout.TopMargin, box.Width, placed.Lift, panel.Scale, Math.Max(1, box.Height));
        }

        var start = adjustStart;
        if (part.HasFlag(KeyboardFrame.Part.Whole) && panel.ShowsIcon)
        {
            if (drag == KeyPanel.Drag.Start)
            {
                iconStart = IconPlace();
            }

            // From the right and the bottom: dragged right or down, nearer them.
            KeepIconPlace(iconStart.Right - (int)dx, iconStart.Bottom - (int)dy);
        }
        else if (part.HasFlag(KeyboardFrame.Part.Whole))
        {
            if (placed.Floating)
            {
                (placed.X, placed.Y) = (start.X + (int)dx, start.Y + (int)dy);
            }
            else
            {
                placed.Lift = Math.Clamp(start.Lift - (int)dy, 0, Room().Height / 2);
                placed.DockedX = start.X + (int)dx;
            }
        }

        if (part.HasFlag(KeyboardFrame.Part.Top))
        {
            Resize(start.Height - dy);
            placed.Y = start.Y + (int)dy;
        }

        if (part.HasFlag(KeyboardFrame.Part.Bottom))
        {
            Resize(start.Height + dy);
        }

        if (part.HasFlag(KeyboardFrame.Part.Left))
        {
            SetWidth(start.Width - (int)dx, start.X + (int)dx);
        }

        if (part.HasFlag(KeyboardFrame.Part.Right))
        {
            SetWidth(start.Width + (int)dx, start.X);
        }

        Place();
        panel.NoteDrag(drag, before: false);
        if (drag == KeyPanel.Drag.End)
        {
            placed.Save();
            if (part == KeyboardFrame.Part.Whole && !panel.ShowsIcon && KeyPlusPreferences.Global(this).GetBoolean(KeyPlusPreferences.DockByDragKey, false))
            {
                DockOrFloat(start.Lift - (int)dy, start.Lift);
            }
        }

        void Resize(float height)
        {
            placed.Scale = Math.Clamp(start.Scale * height / start.Height, PanelPlacement.MinScale, PanelPlacement.MaxScale);
            panel.Scale = placed.Scale;
        }

        // In a window, the window's width; docked, the keys' within the ground.
        void SetWidth(int width, int left)
        {
            if (placed.Floating)
            {
                (placed.Width, placed.X) = (width, left);
            }
            else
            {
                (placed.DockedWidth, placed.DockedX) = (width, left);
            }
        }
    }

    /// <summary>Mover (<paramref name="resizing"/> false) or Redimensionar: the frame over the keyboard, with the side bars to size it; the keyboard as it is kept for Cancelar.</summary>
    private void ShowFrame(bool resizing)
    {
        if (frame is null || placed is null)
        {
            return;
        }

        frameStart = (placed.X, placed.Y, placed.Width, placed.Scale, placed.Lift, placed.DockedX, placed.DockedWidth);
        frame.Show(resizing, sides: resizing);
    }

    /// <summary>Cancelar: the keyboard as the frame found it, Restablecer undone too, the frame gone.</summary>
    private void CancelFrame()
    {
        frame?.Hide();
        if (panel is null || placed is null)
        {
            return;
        }

        (placed.X, placed.Y, placed.Width, placed.Scale, placed.Lift, placed.DockedX, placed.DockedWidth) = frameStart;
        panel.Scale = placed.Scale;
        placed.Save();
        Place();
    }

    /// <summary>Restablecer: the keys at their usual size and, in a window, three quarters of the screen wide; docked, as wide as the screen and on the bottom again.</summary>
    private void ResetSize()
    {
        if (panel is null || placed is null)
        {
            return;
        }

        placed.Scale = 1f;
        panel.Scale = 1f;
        if (placed.Floating)
        {
            placed.Width = FloatingWidth(Room().Width);
            placed.X = (Room().Width - placed.Width) / 2;
        }
        else
        {
            (placed.Lift, placed.DockedX, placed.DockedWidth) = (0, 0, 0);
        }

        placed.Save();
        Place();
    }

    private int Dp(float value) => (int)((value * Resources!.DisplayMetrics!.Density) + 0.5f);

    /// <summary>The screen's preferences the keyboard open now was made from, watched for the app's Pop-up and Reset.</summary>
    private ISharedPreferences? watched;
    private PreferencesWatch? watch;

    private void Watch(ISharedPreferences preferences)
    {
        watch ??= new PreferencesWatch(this);
        watched?.UnregisterOnSharedPreferenceChangeListener(watch);
        watched = preferences;
        preferences.RegisterOnSharedPreferenceChangeListener(watch);
        // The language and the keyboards saved are kept for every screen; the phone's preferences are those, registered once all the same.
        KeyPlusPreferences.Global(this).RegisterOnSharedPreferenceChangeListener(watch);
    }

    /// <summary>
    /// The app's Pop-up turned on or off, its Reset or designs imported, a
    /// keyboard chosen for the screen, or the language or the keyboards
    /// saved changed so that the screen shows another keyboard, or the same
    /// one otherwise designed (Tema), or its colours (Tema): the keyboard made again from what is
    /// kept now, at once. Its own changes, already in sight, are left alone.
    /// </summary>
    private void OnPreferenceChanged(ISharedPreferences preferences, string? key)
    {
        var screens = watched is not null && preferences.Equals(watched);
        var reset = screens && (key is null || key == KeyboardDesign.ImportedPreference || Keyboards.IsSelection(key));
        var popupChanged = screens && key == PanelPlacement.FloatingPreference && placed is not null
            && new PanelPlacement(preferences).Floating != placed.Floating;
        var keyboardChanged = ((key == KeyPlusPreferences.LanguageKey || Keyboards.IsList(key)) && ShowsAnother())
            || (KeyPlusPreferences.IsColors(key) && StandardColors.Chosen(this, StandardColors.App) != shownColors);
        if (reset || popupChanged || keyboardChanged)
        {
            panel?.Root.Post(() => SetInputView(OnCreateInputView()));
        }
    }

    /// <summary>The colours the keyboard in sight was made in (<see cref="StandardColors.Chosen"/>).</summary>
    private (bool Dark, Rgb? Key, Rgb? Accent) shownColors;

    /// <summary>
    /// A field of an application to type in: the application noted (Tema's Por app lists it, with its theme's background),
    /// and the keyboard made again at once where its colours there are not those in sight.
    /// </summary>
    private void InAppColors(string? package)
    {
        var cameTo = package != StandardColors.App;
        StandardColors.App = package is not null && package != PackageName ? package : null;
        if (StandardColors.App is { } app)
        {
            AppThemes.Seen(this, app);

            // Its colour as it shows, seen once as the application is come to (each look a screenshot), the keyboard made
            // again in it if it differs, through the preference written.
            if (cameTo && AppThemes.For(this, app).Look == AppLook.Own && KeyPlusPreferences.Global(this).GetBoolean(KeyPlusPreferences.PerAppKey, false))
            {
                MenuWatchService.Running?.SeeFieldColor(seen => AppThemes.SeenOnScreen(this, app, seen));
            }
        }

        if (panel is not null && !OnHeadset && StandardColors.Chosen(this, StandardColors.App) != shownColors)
        {
            SetInputView(OnCreateInputView());
        }
    }

    /// <summary>Whether the screen shows another keyboard in the keyboard's language than the one in sight, or the same one otherwise designed.</summary>
    private bool ShowsAnother() => !OnHeadset && panel?.Keyboard is { } shown && watched is not null
        && Keyboards.Selected(this, watched, KeyPlusPreferences.ScreenOf(this, Display?.DisplayId ?? Android.Views.Display.DefaultDisplay), KeyPlusPreferences.Language(this).Code) != shown;

    /// <summary>Tells the keyboard of its screen's preferences changing (kept as a field: Android holds its listeners weakly).</summary>
    private sealed class PreferencesWatch(KeyPlusKeyboard keyboard) : Java.Lang.Object, ISharedPreferencesOnSharedPreferenceChangeListener
    {
        public void OnSharedPreferenceChanged(ISharedPreferences? sharedPreferences, string? key)
        {
            if (sharedPreferences is not null)
            {
                keyboard.OnPreferenceChanged(sharedPreferences, key);
            }
        }
    }

    /// <summary>
    /// Whether this is a headset (Galaxy XR), which gives every keyboard a
    /// box of its own to stand in (1166 × 656 pixels on the Galaxy XR) that
    /// the user moves, and the user asked for the headset's keyboard there:
    /// the keys fill it. Else, a headset too, the phone's keyboard.
    /// </summary>
    private bool OnHeadset => KeyPlusPreferences.HeadsetKeyboard(this);

    /// <summary>
    /// The keyboard as tall as the headset's box, which sets its width: under
    /// the title bar a row of the copied texts, then the keys, every row as
    /// tall, the copied texts' too. Again at each change of shape (mode,
    /// labels, the box), until it stands so.
    /// </summary>
    private void FillBox()
    {
        if (panel is null)
        {
            return;
        }

        var box = GetSystemService(WindowService).JavaCast<IWindowManager>()?.CurrentWindowMetrics.Bounds.Height() ?? 0;
        var height = panel.Root.Height;
        var keys = panel.KeysHeight;
        if (box <= 0 || keys <= 0)
        {
            return;
        }

        // What is neither keys nor copied texts: the title bar.
        var rest = height - keys - panel.ClipboardAbove;
        var rows = keys / (float)panel.RowHeight;
        var scale = Math.Clamp((box - rest) / (rows + 1) / panel.RowHeight * panel.Scale, PanelPlacement.MinScale, PanelPlacement.MaxScale);
        var keysThen = (int)Math.Round(keys * scale / panel.Scale);
        var above = Math.Max(0, box - rest - keysThen);
        if (Math.Abs(scale - panel.Scale) < 0.005f && Math.Abs(above - panel.ClipboardAbove) <= 1)
        {
            return;
        }

        Android.Util.Log.Info("KeyPlus", $"Box {box}px: {rows:0.#} rows of keys and one of copied texts, scale {panel.Scale:0.###} to {scale:0.###}, copied texts {above}px");

        // Not in the middle of the layout pass that told the sizes.
        panel.Root.Post(() =>
        {
            panel.Scale = scale;
            panel.ClipboardAbove = above;
        });
    }

    /// <summary>
    /// On a headset, not in its own box (<see cref="FillBox"/>), the keyboard's window is a box all the same (1166 × 656 px on
    /// the Galaxy XR), whose bottom Estándar XR left empty: its keys made as tall as fill it, as Gboard's there, the bar and
    /// the band as they are. Again at each layout, until it does.
    /// </summary>
    private void FillHeadsetWindow(int width, int room)
    {
        if (panel is not { ShowsXr: true } || room <= 0)
        {
            return;
        }

        var tall = Tall(width);
        var keys = panel.StandardHeight;
        if (keys <= 0 || Math.Abs(room - tall) <= 1)
        {
            return;
        }

        var scale = Math.Clamp(panel.Scale * (keys + room - tall) / keys, PanelPlacement.MinScale, PanelPlacement.MaxScale);
        if (Math.Abs(scale - panel.Scale) < 0.003f)
        {
            return;
        }

        Android.Util.Log.Info("KeyPlus", $"Headset's window {room}px: Estándar XR's keys {keys}px of {tall}px, scale {panel.Scale:0.###} to {scale:0.###}");

        // Not in the middle of the layout pass that told the sizes.
        panel.Root.Post(() => panel.Scale = scale);
    }

    /// <summary>
    /// On a headset, Gboard's card there (docs/tODO/photos/gboard/xr-letters.png): the keys' box rounded, 55 px on its band of
    /// 84 (our band the bar's height), and the room around it see-through, the window's ground and the keyboard's taken away.
    /// </summary>
    private void AsHeadsetCard(View keys, View under)
    {
        var radius = Dp(KeyPanel.ToolbarDp * 55f / 84);
        keys.OutlineProvider = new RoundCard(radius);
        keys.ClipToOutline = true;
        under.Visibility = ViewStates.Invisible;
        Window?.Window?.SetBackgroundDrawable(new Android.Graphics.Drawables.ColorDrawable(Android.Graphics.Color.Transparent));
    }

    /// <summary>A view's outline, a rectangle with round corners of a radius.</summary>
    private sealed class RoundCard(float radius) : ViewOutlineProvider
    {
        public override void GetOutline(View? view, Android.Graphics.Outline? outline) => outline?.SetRoundRect(0, 0, view!.Width, view.Height, radius);
    }

    /// <summary>Never the whole screen with an editor of its own, as a keyboard is by default with the phone on its side.</summary>
    public override bool OnEvaluateFullscreenMode() => false;

    public override void OnFinishInputView(bool finishingInput)
    {
        panel?.StopRepeat();
        DropDictation();
        base.OnFinishInputView(finishingInput);
    }

    /// <summary>A grip dragged up or down: the keys bigger or smaller; the width is the one Android gives the keyboard.</summary>
    private void OnResizing(KeyPanel.Corner corner, KeyPanel.Drag drag, float dx, float dy)
    {
        if (panel is null || placed is null)
        {
            return;
        }

        if (drag == KeyPanel.Drag.Start)
        {
            resizeStart = (panel.Scale, Math.Max(1, panel.Root.Height));
        }

        var fromTop = (corner & KeyPanel.Corner.Top) != 0;
        placed.Scale = Math.Clamp(
            resizeStart.Scale * (resizeStart.Height + (fromTop ? -dy : dy)) / resizeStart.Height,
            PanelPlacement.MinScale,
            PanelPlacement.MaxScale);
        panel.Scale = placed.Scale;
        if (drag == KeyPanel.Drag.End)
        {
            placed.Save();
        }
    }

    /// <summary>A↺ pressed: the keys back at their usual size.</summary>
    private void OnUsualSizeRequested()
    {
        if (panel is null || placed is null)
        {
            return;
        }

        placed.Scale = 1f;
        placed.Save();
        panel.Scale = 1f;
    }

    /// <summary>ABC, Keys or ▭ pressed: the keys take that mode's size.</summary>
    private void OnModeChanged()
    {
        if (panel is null || placed is null)
        {
            return;
        }

        placed.Mode = panel.Mode;
        panel.Scale = placed.Scale;
        ground?.SetBackgroundColor(panel.GroundColor);
        Place();
    }

    /// <summary>
    /// The standard keyboard's Enter as the field asks, as Gboard's: its
    /// action (Go, Search, Send, Next, Done) where it has one and wants it on
    /// Enter, else a new line.
    /// </summary>
    private void PerformEnter()
    {
        var options = CurrentInputEditorInfo?.ImeOptions ?? 0;
        var action = (ImeAction)((int)options & (int)ImeAction.ImeMaskAction);
        if (options.HasFlag(ImeFlags.NoEnterAction) || action is ImeAction.None or ImeAction.Unspecified)
        {
            SendKey(Keycode.Enter, MetaKeyStates.None);
            return;
        }

        CurrentInputConnection?.PerformEditorAction(action);
    }

    private void SendKey(Keycode code, MetaKeyStates meta)
    {
        if (CurrentInputConnection is { } connection)
        {
            KeySender.Send(key => connection.SendKeyEvent(key), code, meta);
        }
    }

    private void TypeText(string text) => CurrentInputConnection?.CommitText(text, 1);
}
