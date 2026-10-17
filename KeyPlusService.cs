using Android.AccessibilityServices;
using Android.App;
using Android.Graphics;
using Android.Hardware.Display;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Views.Accessibility;

namespace Berpiztu.KeyPlus;

/// <summary>
/// Shows the <see cref="KeyPanel"/> right above the keyboard while one is open,
/// and sends its keys to the focused editor. An accessibility service, not a
/// keyboard: Android allows one keyboard at a time, and this one has to stand
/// beside the user's. Since Android 13 such a service gets its own input
/// connection to the focused editor (flagInputMethodEditor), whose key events
/// a browser turns into the page's keydown events, so the keys reach web
/// editors as well as terminals. The panel's window never takes the focus,
/// or the keyboard would close at every key.
/// The keyboard may be on any display: Samsung DeX, a desktop on a second
/// screen, is a display of its own. Docked, the panel opens on the keyboard's
/// display, as wide as the keyboard and right above it, so it follows a
/// floating keyboard as well as one across the bottom, and is out of sight
/// while no keyboard is open. Floating (<see cref="PanelPlacement"/>), it
/// stands where the user dragged it, at the width they gave it, all the time.
/// </summary>
[Service(Name = "com.berpiztu.keyplus.KeyPlusService", Label = "@string/service_label",
    Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true)]
[IntentFilter(["android.accessibilityservice.AccessibilityService"])]
[MetaData("android.accessibilityservice", Resource = "@xml/keyplus_accessibility")]
public sealed class KeyPlusService : AccessibilityService
{
    private const string LogTag = "KeyPlus";

    /// <summary>The panel in its window on one display.</summary>
    private sealed record Overlay(int DisplayId, IWindowManager WindowManager, KeyPanel Panel, WindowManagerLayoutParams Placement);

    /// <summary>A keyboard's window and the display it is on.</summary>
    private readonly record struct Keyboard(int DisplayId, Rect Bounds);

    private Overlay? overlay;
    private PanelPlacement? placed;

    /// <summary>Where the panel's window and size stood when a drag on ✥ or ⇲ started.</summary>
    private (int X, int Y, int Width, int Height, float Scale) dragStart;

    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        placed = new PanelPlacement(GetSharedPreferences("keyplus", Android.Content.FileCreationMode.Private)!);
        Place();
    }

    public override Android.AccessibilityServices.InputMethod OnCreateInputMethod() => new SelectionInputMethod(this, OnCaretMoved);

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        if (e?.EventType == EventTypes.WindowsChanged)
        {
            Place();
        }
    }

    public override void OnInterrupt()
    {
    }

    public override void OnDestroy()
    {
        Close();
        base.OnDestroy();
    }

    /// <summary>
    /// Puts the panel where it stands: docked, right above the keyboard, as wide
    /// as it or as a tab at its right, or out of sight while no keyboard is
    /// open; floating, where the user left it, on the keyboard's display, or on
    /// the one it is on while no keyboard is open.
    /// </summary>
    private void Place()
    {
        if (placed is null)
        {
            return;
        }

        var keyboard = FindKeyboard();
        if (keyboard is null && !placed.Floating)
        {
            Hide();
            return;
        }

        var displayId = keyboard?.DisplayId ?? overlay?.DisplayId ?? FocusedDisplayId() ?? Display.DefaultDisplay;
        if (overlay?.DisplayId != displayId)
        {
            Close();
            overlay = Open(displayId);
            if (overlay is null)
            {
                return;
            }
        }

        var (_, windowManager, panel, placement) = overlay;
        var display = windowManager.MaximumWindowMetrics.Bounds;
        int x, y, width;
        GravityFlags gravity;
        if (placed.Floating)
        {
            width = panel.Folded ? ViewGroup.LayoutParams.WrapContent : Math.Min(placed.Width, display.Width());
            x = Math.Clamp(placed.X, 0, Math.Max(0, display.Width() - (panel.Folded ? panel.Root.Width : width)));
            y = Math.Clamp(placed.Y, 0, Math.Max(0, display.Height() - panel.Root.Height));
            gravity = GravityFlags.Top | GravityFlags.Left;
        }
        else
        {
            var bounds = keyboard!.Value.Bounds;
            y = display.Bottom - bounds.Top;
            x = panel.Folded ? display.Right - bounds.Right : bounds.Left - display.Left;
            width = panel.Folded ? ViewGroup.LayoutParams.WrapContent : bounds.Width();
            gravity = GravityFlags.Bottom | (panel.Folded ? GravityFlags.Right : GravityFlags.Left);
        }

        // The panel's own window changing is a windows change too: placing it
        // again where it already is would only call this once more.
        if (panel.Root.Visibility == ViewStates.Visible
            && placement.X == x && placement.Y == y && placement.Width == width && placement.Gravity == gravity)
        {
            return;
        }

        Log.Info(LogTag, $"Panel on display {displayId} at {x},{y} width {width}, {(placed.Floating ? "floating" : $"above the keyboard at {keyboard!.Value.Bounds.ToShortString()}")}");
        placement.X = x;
        placement.Y = y;
        placement.Width = width;
        placement.Gravity = gravity;
        panel.Root.Visibility = ViewStates.Visible;
        windowManager.UpdateViewLayout(panel.Root, placement);
    }

    /// <summary>✥ dragged: the panel floats, and follows the finger.</summary>
    private void OnMoving(KeyPanel.Drag drag, float dx, float dy)
    {
        if (StartFloating(drag) is not { } floating)
        {
            return;
        }

        floating.X = dragStart.X + (int)dx;
        floating.Y = dragStart.Y + (int)dy;
        Dragged(drag, floating);
    }

    /// <summary>⇲ dragged: the panel floats; across it gets wider or narrower, down its keys bigger or smaller.</summary>
    private void OnResizing(KeyPanel.Drag drag, float dx, float dy)
    {
        if (StartFloating(drag) is not { } floating || overlay is null)
        {
            return;
        }

        var minWidth = (int)(160 * Resources!.DisplayMetrics!.Density);
        floating.Width = Math.Max(minWidth, dragStart.Width + (int)dx);
        floating.Scale = Math.Clamp(
            dragStart.Scale * (dragStart.Height + dy) / Math.Max(1, dragStart.Height),
            PanelPlacement.MinScale,
            PanelPlacement.MaxScale);
        overlay.Panel.Scale = floating.Scale;
        Dragged(drag, floating);
    }

    /// <summary>✥ tapped: the panel docks above the keyboard again.</summary>
    private void OnDockRequested()
    {
        if (placed is { Floating: true })
        {
            placed.Floating = false;
            placed.Save();
            Place();
        }
    }

    /// <summary>
    /// At a drag's start, floats the panel where it stands now and notes where
    /// that is; the placement to change, or none without a panel in sight.
    /// </summary>
    private PanelPlacement? StartFloating(KeyPanel.Drag drag)
    {
        if (placed is null || overlay is null)
        {
            return null;
        }

        if (drag == KeyPanel.Drag.Start)
        {
            var root = overlay.Panel.Root;
            var location = new int[2];
            root.GetLocationOnScreen(location);
            var display = overlay.WindowManager.MaximumWindowMetrics.Bounds;
            dragStart = (location[0] - display.Left, location[1] - display.Top, root.Width, root.Height, overlay.Panel.Scale);
            placed.Floating = true;
            placed.X = dragStart.X;
            placed.Y = dragStart.Y;
            placed.Width = dragStart.Width;
        }

        return placed;
    }

    /// <summary>Places the panel as dragged so far, and keeps where it stands once the drag ends.</summary>
    private void Dragged(KeyPanel.Drag drag, PanelPlacement floating)
    {
        Place();
        if (drag == KeyPanel.Drag.End)
        {
            floating.Save();
        }
    }

    /// <summary>
    /// The keyboard open now, on whichever display: the one on a display whose
    /// window has the focus when there are several, or none.
    /// </summary>
    private Keyboard? FindKeyboard()
    {
        Keyboard? found = null;
        var displays = WindowsOnAllDisplays;
        for (var i = 0; i < (displays?.Size() ?? 0); i++)
        {
            var displayId = displays!.KeyAt(i);
            IList<AccessibilityWindowInfo> windows = JavaList<AccessibilityWindowInfo>.FromJniHandle(displays.ValueAt(i)!.Handle, JniHandleOwnership.DoNotTransfer) ?? [];
            var keyboard = windows.FirstOrDefault(window => window.Type == AccessibilityWindowType.InputMethod);
            if (keyboard is null)
            {
                continue;
            }

            var bounds = new Rect();
            keyboard.GetBoundsInScreen(bounds);
            found ??= new Keyboard(displayId, bounds);
            if (windows.Any(window => window.IsFocused))
            {
                return new Keyboard(displayId, bounds);
            }
        }

        return found;
    }

    /// <summary>The display whose window has the focus, where a floating panel opens while no keyboard is.</summary>
    private int? FocusedDisplayId()
    {
        var displays = WindowsOnAllDisplays;
        for (var i = 0; i < (displays?.Size() ?? 0); i++)
        {
            IList<AccessibilityWindowInfo> windows = JavaList<AccessibilityWindowInfo>.FromJniHandle(displays!.ValueAt(i)!.Handle, JniHandleOwnership.DoNotTransfer) ?? [];
            if (windows.Any(window => window.IsFocused))
            {
                return displays.KeyAt(i);
            }
        }

        return null;
    }

    /// <summary>Opens the panel's window on a display, out of sight until placed.</summary>
    private Overlay? Open(int displayId)
    {
        var display = GetSystemService(DisplayService).JavaCast<DisplayManager>()?.GetDisplay(displayId);
        if (display is null)
        {
            return null;
        }

        var context = CreateWindowContext(display, (int)WindowManagerTypes.AccessibilityOverlay, null);
        var windowManager = context.GetSystemService(WindowService).JavaCast<IWindowManager>()!;
        var panel = new KeyPanel(context, SendKey) { Scale = placed?.Scale ?? 1f };
        panel.FoldedChanged += Place;
        panel.Moving += OnMoving;
        panel.Resizing += OnResizing;
        panel.DockRequested += OnDockRequested;
        var placement = new WindowManagerLayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent,
            WindowManagerTypes.AccessibilityOverlay,
            WindowManagerFlags.NotFocusable | WindowManagerFlags.LayoutInScreen | WindowManagerFlags.LayoutNoLimits,
            Format.Translucent)
        {
            // Measured from the edges of the whole display, which is where the
            // keyboard's bounds are measured from too.
            FitInsetsTypes = 0,
            LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.Always,
        };
        panel.Root.Visibility = ViewStates.Gone;
        windowManager.AddView(panel.Root, placement);
        return new Overlay(displayId, windowManager, panel, placement);
    }

    private void Hide()
    {
        if (overlay is not null)
        {
            overlay.Panel.StopRepeat();
            overlay.Panel.Root.Visibility = ViewStates.Gone;
        }
    }

    private void Close()
    {
        if (overlay is not null)
        {
            overlay.Panel.StopRepeat();
            overlay.WindowManager.RemoveView(overlay.Panel.Root);
            overlay = null;
        }
    }

    /// <summary>
    /// While Ctrl or Alt is held, a character typed on the phone's keyboard
    /// becomes that key with them: the keyboard commits the character as text,
    /// so it is taken back (Backspace) and sent again as a key event with the
    /// modifiers held, Ctrl+C for a "c", Ctrl+Shift+P for a "P". The caret
    /// moving on by one after the selection's start is how it shows.
    /// </summary>
    private void OnCaretMoved(int oldStart, int newStart)
    {
        if (overlay?.Panel is not { HoldsCtrlOrAlt: true } panel || newStart != oldStart + 1)
        {
            return;
        }

        var connection = InputMethod?.CurrentInputConnection;
        var before = connection?.GetSurroundingText(1, 0, 0);
        var text = before?.Text?.ToString();
        if (connection is null || before is null || text is null || before.SelectionStart < 1 || before.SelectionStart > text.Length)
        {
            return;
        }

        if (KeyOf(text[before.SelectionStart - 1]) is not { } typed)
        {
            return;
        }

        var meta = panel.TakeHeldForTyped() | typed.Meta;
        SendKey(Keycode.Del, MetaKeyStates.None);
        SendKey(typed.Code, meta);
    }

    /// <summary>The key that types a character on a virtual keyboard, with the Shift it takes; none for a character no key types.</summary>
    private static (Keycode Code, MetaKeyStates Meta)? KeyOf(char character)
    {
        var events = KeyCharacterMap.Load((int)KeyboardType.VirtualKeyboard)?.GetEvents([character]);
        var down = events?.FirstOrDefault(e => e.Action == KeyEventActions.Down && !KeyEvent.IsModifierKey(e.KeyCode));
        return down is null ? null : (down.KeyCode, down.MetaState & MetaKeyStates.ShiftMask);
    }

    /// <summary>
    /// Sends a key as a hardware keyboard would: each modifier held is pressed
    /// before it and let go after it. The meta state alone is not enough for
    /// every editor: Android's own text fields, for one, select with Shift and
    /// the arrows only when Shift itself went down.
    /// </summary>
    private void SendKey(Keycode code, MetaKeyStates meta)
    {
        var connection = InputMethod?.CurrentInputConnection;
        if (connection is null)
        {
            return;
        }

        var down = SystemClock.UptimeMillis();
        void Send(KeyEventActions action, Keycode key, MetaKeyStates state) =>
            connection.SendKeyEvent(new KeyEvent(down, SystemClock.UptimeMillis(), action, key, 0, state));

        var pressed = Modifiers.Where(modifier => meta.HasFlag(modifier.On)).ToList();
        var state = MetaKeyStates.None;
        foreach (var modifier in pressed)
        {
            state |= modifier.Meta;
            Send(KeyEventActions.Down, modifier.Key, state);
        }

        Send(KeyEventActions.Down, code, meta);
        Send(KeyEventActions.Up, code, meta);
        foreach (var modifier in Enumerable.Reverse(pressed))
        {
            state &= ~modifier.Meta;
            Send(KeyEventActions.Up, modifier.Key, state);
        }
    }

    /// <summary>The modifiers a key may be sent with: the meta flag that says so, the key pressed for it, and the meta state it adds.</summary>
    private static readonly (MetaKeyStates On, Keycode Key, MetaKeyStates Meta)[] Modifiers =
    [
        (MetaKeyStates.CtrlOn, Keycode.CtrlLeft, MetaKeyStates.CtrlOn | MetaKeyStates.CtrlLeftOn),
        (MetaKeyStates.AltOn, Keycode.AltLeft, MetaKeyStates.AltOn | MetaKeyStates.AltLeftOn),
        (MetaKeyStates.ShiftOn, Keycode.ShiftLeft, MetaKeyStates.ShiftOn | MetaKeyStates.ShiftLeftOn),
    ];
}
