using Android.AccessibilityServices;
using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
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
/// </summary>
[Service(Name = "com.berpiztu.keyplus.KeyPlusService", Label = "@string/service_label",
    Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true)]
[IntentFilter(["android.accessibilityservice.AccessibilityService"])]
[MetaData("android.accessibilityservice", Resource = "@xml/keyplus_accessibility")]
public sealed class KeyPlusService : AccessibilityService
{
    private IWindowManager? windowManager;
    private KeyPanel? panel;
    private WindowManagerLayoutParams? placement;

    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        windowManager = GetSystemService(WindowService).JavaCast<IWindowManager>();
        panel = new KeyPanel(this, SendKey);
        panel.FoldedChanged += Place;
        placement = new WindowManagerLayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent,
            WindowManagerTypes.AccessibilityOverlay,
            WindowManagerFlags.NotFocusable | WindowManagerFlags.LayoutInScreen | WindowManagerFlags.LayoutNoLimits,
            Format.Translucent)
        {
            // Measured from the bottom of the whole display, which is where the
            // keyboard's bounds are measured from too.
            FitInsetsTypes = 0,
            LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.Always,
        };
        panel.Root.Visibility = ViewStates.Gone;
        windowManager!.AddView(panel.Root, placement);
        Place();
    }

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
        if (panel is not null)
        {
            panel.StopRepeat();
            windowManager?.RemoveView(panel.Root);
            panel = null;
        }

        base.OnDestroy();
    }

    /// <summary>Puts the panel right above the keyboard, across its width or as a tab at its right, or out of sight while no keyboard is open.</summary>
    private void Place()
    {
        if (panel is null || placement is null || windowManager is null)
        {
            return;
        }

        var keyboard = Windows?.FirstOrDefault(window => window.Type == AccessibilityWindowType.InputMethod);
        if (keyboard is null)
        {
            panel.StopRepeat();
            panel.Root.Visibility = ViewStates.Gone;
            return;
        }

        var bounds = new Rect();
        keyboard.GetBoundsInScreen(bounds);
        var display = windowManager.MaximumWindowMetrics.Bounds;
        var y = display.Bottom - bounds.Top;
        var width = panel.Folded ? ViewGroup.LayoutParams.WrapContent : ViewGroup.LayoutParams.MatchParent;
        var gravity = GravityFlags.Bottom | (panel.Folded ? GravityFlags.End : GravityFlags.Start);

        // The panel's own window changing is a windows change too: placing it
        // again where it already is would only call this once more.
        if (panel.Root.Visibility == ViewStates.Visible && placement.Y == y && placement.Width == width && placement.Gravity == gravity)
        {
            return;
        }

        placement.Y = y;
        placement.Width = width;
        placement.Gravity = gravity;
        panel.Root.Visibility = ViewStates.Visible;
        windowManager.UpdateViewLayout(panel.Root, placement);
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
