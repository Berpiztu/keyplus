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
    private KeyboardFrame? frame;

    /// <summary>Where the keyboard stood and how large it was when a drag on the frame or the title bar started.</summary>
    private (int X, int Y, int Width, int Lift, float Scale, int Height) adjustStart;

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
    }

    /// <summary>The keyboard opening: the screenshots watched, if the user has let it read the photos since.</summary>
    public override void OnStartInputView(Android.Views.InputMethods.EditorInfo? info, bool restarting)
    {
        base.OnStartInputView(info, restarting);
        panel?.FitLabels();
        screenshots?.Start();
    }

    public override void OnDestroy()
    {
        if (clipboardManager is not null)
        {
            clipboardManager.PrimaryClipChanged -= OnCopied;
        }

        screenshots?.Stop();
        base.OnDestroy();
    }

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
        var screen = KeyPlusPreferences.ScreenOf(this, Display?.DisplayId ?? Android.Views.Display.DefaultDisplay);
        var preferences = KeyPlusPreferences.ForScreen(this, screen);
        placed = new PanelPlacement(preferences);
        panel = new KeyPanel(this, preferences, clipboard!, SendKey, TypeText)
        {
            Dockable = false,
            PasteImage = PasteImage,
            Settings = [("Cambiar de teclado", () => ((InputMethodManager)GetSystemService(InputMethodService)!).ShowInputMethodPicker())],
        };
        placed.Mode = panel.Mode;
        panel.Scale = placed.Scale;
        panel.Resizing += OnResizing;
        if (OnHeadset)
        {
            // The keys sized to the headset's box, whatever its mode: no grips to size them.
            panel.Resizable = false;
            panel.SideKeys = false;
            panel.Root.LayoutChange += (_, _) => FillBox();
        }
        else
        {
            // Sized and moved from ⚙: no grips.
            panel.Resizable = false;
            panel.Settings = PhoneSettings();
            panel.Moving += (drag, dx, dy) =>
            {
                if (placed?.Floating == true)
                {
                    Adjust(KeyboardFrame.Part.Whole, drag, dx, dy);
                }
            };
            frame = new KeyboardFrame(this);
            frame.Dragged += Adjust;
            frame.ResetRequested += ResetSize;
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
            container.AddView(box);

            // The keys' height changes with their size and mode: in a window, kept inside the room.
            box.LayoutChange += (_, _) =>
            {
                if (placed?.Floating == true)
                {
                    box.Post(Place);
                }
            };
            Place();
            KeepCopiedRow();
        }

        panel.UsualSizeRequested += OnUsualSizeRequested;
        panel.ModeChanged += OnModeChanged;
        return container ?? panel.Root;
    }

    /// <summary>⚙ on a phone: Mover, Redimensionar, the two modes, the one in use ticked, and the keyboard picker.</summary>
    private IReadOnlyList<(string Label, Action Chosen)> PhoneSettings()
    {
        var windowed = placed?.Floating == true;
        return
        [
            ("✥ Mover", () => frame?.Show(resizing: false, sides: false)),
            ("⤡ Redimensionar", () => frame?.Show(resizing: true, sides: placed?.Floating == true)),
            ((windowed ? "" : "✓ ") + "Modo normal", () => SetWindowed(false)),
            ((windowed ? "✓ " : "") + "Modo ventana", () => SetWindowed(true)),
            ("Cambiar de teclado", () => ((InputMethodManager)GetSystemService(InputMethodService)!).ShowInputMethodPicker()),
        ];
    }

    /// <summary>
    /// Modo ventana or Modo normal chosen. The first time in a window, it
    /// opens three quarters of the screen wide, in the middle, at the bottom.
    /// </summary>
    private void SetWindowed(bool windowed)
    {
        if (panel is null || placed is null)
        {
            return;
        }

        if (windowed && placed.Width == 0)
        {
            var room = Room();
            placed.Width = room.Width * 3 / 4;
            placed.X = (room.Width - placed.Width) / 2;
            placed.Y = room.Height;
        }

        placed.Floating = windowed;
        placed.Save();
        panel.Settings = PhoneSettings();
        Place();
    }

    /// <summary>
    /// The room the keyboard stands in: the screen's width, and its height
    /// less the status and navigation bars; and the navigation bar's height
    /// (Below), which the keyboard's window reaches under
    /// and the keys keep clear of.
    /// </summary>
    private (int Width, int Height, int Below) Room()
    {
        var metrics = GetSystemService(WindowService).JavaCast<IWindowManager>()!.CurrentWindowMetrics;
        var bars = metrics.WindowInsets.GetInsetsIgnoringVisibility(WindowInsets.Type.SystemBars());
        return (metrics.Bounds.Width(), metrics.Bounds.Height() - bars.Top - bars.Bottom, bars.Bottom);
    }

    /// <summary>
    /// The keyboard where it stands: docked, at the bottom as wide as the
    /// screen, raised as far as Mover left it; in a window, where the user
    /// left it and as wide, inside the room. Only what changed is laid out
    /// again.
    /// </summary>
    private void Place()
    {
        if (container is null || box is null || placed is null)
        {
            return;
        }

        FrameLayout.LayoutParams layout;
        var room = Room();
        container.SetPadding(0, 0, 0, room.Below);
        if (placed.Floating)
        {
            var width = Math.Clamp(placed.Width, Dp(PanelPlacement.MinWidthDp), room.Width);
            layout = new FrameLayout.LayoutParams(width, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.Left)
            {
                LeftMargin = Math.Clamp(placed.X, 0, room.Width - width),
                TopMargin = Math.Clamp(placed.Y, 0, Math.Max(0, room.Height - box.Height)),
            };
            container.SetMinimumHeight(room.Height + room.Below);
            container.Background = null;
        }
        else
        {
            layout = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom)
            {
                BottomMargin = placed.Lift,
            };
            container.SetMinimumHeight(0);
            container.SetBackgroundColor(KeyPanel.PanelColor);
        }

        if (box.LayoutParameters is FrameLayout.LayoutParams now
            && now.Width == layout.Width && now.Gravity == layout.Gravity
            && now.LeftMargin == layout.LeftMargin && now.TopMargin == layout.TopMargin && now.BottomMargin == layout.BottomMargin)
        {
            return;
        }

        box.LayoutParameters = layout;
    }

    /// <summary>
    /// The keyboard in a window: the app behind keeps the whole screen, and
    /// only the keyboard takes touches, the rest of its window passing them
    /// to the app. Docked, as any keyboard.
    /// </summary>
    public override void OnComputeInsets(Insets? outInsets)
    {
        base.OnComputeInsets(outInsets);
        if (outInsets is null || container is null || box is null || placed?.Floating != true || box.Height == 0)
        {
            return;
        }

        var at = new int[2];
        container.GetLocationInWindow(at);
        var below = at[1] + container.Height;
        outInsets.ContentTopInsets = below;
        outInsets.VisibleTopInsets = below;
        outInsets.TouchableInsets = TouchableInsets.Region;
        outInsets.TouchableRegion!.Set(at[0] + box.Left, at[1] + box.Top, at[0] + box.Right, at[1] + box.Bottom);
    }

    /// <summary>
    /// A finger on the frame, or on the title bar of a keyboard in a window:
    /// the whole moves it, in a window anywhere, docked up and down; the top
    /// and bottom edges make the keys larger or smaller, the side ones the
    /// window wider or narrower. Kept once the finger is lifted.
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
            adjustStart = (layout.LeftMargin, layout.TopMargin, box.Width, placed.Lift, panel.Scale, Math.Max(1, box.Height));
        }

        var start = adjustStart;
        switch (part)
        {
            case KeyboardFrame.Part.Whole when placed.Floating:
                (placed.X, placed.Y) = (start.X + (int)dx, start.Y + (int)dy);
                break;
            case KeyboardFrame.Part.Whole:
                placed.Lift = Math.Clamp(start.Lift - (int)dy, 0, Room().Height / 2);
                break;
            case KeyboardFrame.Part.Top:
                Resize(start.Height - dy);
                placed.Y = start.Y + (int)dy;
                break;
            case KeyboardFrame.Part.Bottom:
                Resize(start.Height + dy);
                break;
            case KeyboardFrame.Part.Left:
                placed.Width = start.Width - (int)dx;
                placed.X = start.X + (int)dx;
                break;
            case KeyboardFrame.Part.Right:
                placed.Width = start.Width + (int)dx;
                break;
        }

        Place();
        panel.NoteDrag(drag, before: false);
        if (drag == KeyPanel.Drag.End)
        {
            placed.Save();
            KeepCopiedRow();
        }

        void Resize(float height)
        {
            placed.Scale = Math.Clamp(start.Scale * height / start.Height, PanelPlacement.MinScale, PanelPlacement.MaxScale);
            panel.Scale = placed.Scale;
        }
    }

    /// <summary>Restablecer: the keys at their usual size and, in a window, three quarters of the screen wide; docked, on the bottom again.</summary>
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
            placed.Width = Room().Width * 3 / 4;
        }
        else
        {
            placed.Lift = 0;
        }

        placed.Save();
        Place();
        KeepCopiedRow();
    }

    private int Dp(float value) => (int)((value * Resources!.DisplayMetrics!.Density) + 0.5f);

    /// <summary>The system feature of Android XR's spatial windows: a headset.</summary>
    private const string XrSpatialFeature = "android.software.xr.api.spatial";

    /// <summary>
    /// Whether this is a headset (Galaxy XR), which gives every keyboard a
    /// box of its own to stand in (1166 × 656 pixels on the Galaxy XR) that
    /// the user moves: the keys fill it.
    /// </summary>
    private bool OnHeadset => PackageManager?.HasSystemFeature(XrSpatialFeature) == true;

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
    /// Off a headset, what was copied in a row above the keys all the time,
    /// as tall as a row of keys, or as a text's two lines need where that is
    /// more, so a thing copied is in sight at once: a tap pastes it, ✏ edits
    /// it there. Not in ▭'s two small rows, which keep only the keys. Again
    /// when the keys change size, once the drag ends.
    /// </summary>
    private void KeepCopiedRow()
    {
        if (panel is not null && !OnHeadset)
        {
            panel.ClipboardAbove = panel.Mode == PanelPlacement.CompactMode ? 0 : Math.Max(panel.RowHeight, panel.CopiedRowHeight);
        }
    }

    /// <summary>Never the whole screen with an editor of its own, as a keyboard is by default with the phone on its side.</summary>
    public override bool OnEvaluateFullscreenMode() => false;

    public override void OnFinishInputView(bool finishingInput)
    {
        panel?.StopRepeat();
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
            KeepCopiedRow();
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
        KeepCopiedRow();
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
        Place();
        KeepCopiedRow();
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
