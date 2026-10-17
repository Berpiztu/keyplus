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
/// screen, is a display of its own. The panel opens on the keyboard's display,
/// as wide as the keyboard and right above it, so it follows a floating
/// keyboard as well as one across the bottom.
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

    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
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

    /// <summary>Puts the panel right above the keyboard, as wide as it or as a tab at its right, or out of sight while no keyboard is open.</summary>
    private void Place()
    {
        if (FindKeyboard() is not { } keyboard)
        {
            Hide();
            return;
        }

        if (overlay?.DisplayId != keyboard.DisplayId)
        {
            Close();
            overlay = Open(keyboard.DisplayId);
            if (overlay is null)
            {
                return;
            }
        }

        var (_, windowManager, panel, placement) = overlay;
        var display = windowManager.MaximumWindowMetrics.Bounds;
        var bounds = keyboard.Bounds;
        var y = display.Bottom - bounds.Top;
        var x = panel.Folded ? display.Right - bounds.Right : bounds.Left - display.Left;
        var width = panel.Folded ? ViewGroup.LayoutParams.WrapContent : bounds.Width();
        var gravity = GravityFlags.Bottom | (panel.Folded ? GravityFlags.Right : GravityFlags.Left);

        // The panel's own window changing is a windows change too: placing it
        // again where it already is would only call this once more.
        if (panel.Root.Visibility == ViewStates.Visible
            && placement.X == x && placement.Y == y && placement.Width == width && placement.Gravity == gravity)
        {
            return;
        }

        Log.Info(LogTag, $"Keyboard on display {keyboard.DisplayId} at {bounds.ToShortString()}, display {display.ToShortString()}");
        placement.X = x;
        placement.Y = y;
        placement.Width = width;
        placement.Gravity = gravity;
        panel.Root.Visibility = ViewStates.Visible;
        windowManager.UpdateViewLayout(panel.Root, placement);
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
        var panel = new KeyPanel(context, SendKey);
        panel.FoldedChanged += Place;
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

    private void SendKey(Keycode code, MetaKeyStates meta)
    {
        var connection = InputMethod?.CurrentInputConnection;
        if (connection is null)
        {
            return;
        }

        var down = SystemClock.UptimeMillis();
        connection.SendKeyEvent(new KeyEvent(down, down, KeyEventActions.Down, code, 0, meta));
        connection.SendKeyEvent(new KeyEvent(down, SystemClock.UptimeMillis(), KeyEventActions.Up, code, 0, meta));
    }
}
