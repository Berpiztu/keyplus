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
/// It is the KeyPlus popup; Berpiztu AI Keyboard (KeyPlusKeyboard) is the
/// other product. On a headset (Galaxy XR) the keys are not in an overlay,
/// which the headset hangs on a layer that moves with the user's head, but in
/// an app's window of their own (<see cref="KeyWindowActivity"/>), a panel the
/// user places beside the keyboard; the service still sends them.
/// </summary>
[Service(Name = "ai.berpiztu.keyplus.KeyPlusService", Label = "@string/service_label",
    Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true)]
[IntentFilter(["android.accessibilityservice.AccessibilityService"])]
[MetaData("android.accessibilityservice", Resource = "@xml/keyplus_accessibility")]
public sealed class KeyPlusService : AccessibilityService, Android.Content.ISharedPreferencesOnSharedPreferenceChangeListener
{
    private const string LogTag = "KeyPlus";

    /// <summary>The panel in its window on one display, and the screen whose layout it has.</summary>
    private sealed record Overlay(int DisplayId, KeyPlusPreferences.Screen Screen, IWindowManager WindowManager, KeyPanel Panel, WindowManagerLayoutParams Placement);

    /// <summary>A keyboard's window and the display it is on.</summary>
    private readonly record struct Keyboard(int DisplayId, Rect Bounds);

    private Overlay? overlay;
    private PanelPlacement? placed;

    /// <summary>The system feature of Android XR's spatial windows: a headset.</summary>
    private const string XrSpatialFeature = "android.software.xr.api.spatial";

    /// <summary>Whether this is a headset, where the keys have a window of their own (<see cref="KeyWindowActivity"/>).</summary>
    public bool OnHeadset => PackageManager?.HasSystemFeature(XrSpatialFeature) == true;

    /// <summary>The service turned on, which the keys' own window sends through; none while it is off.</summary>
    public static KeyPlusService? Current { get; private set; }

    /// <summary>The panel in the keys' own window, while it is open (a headset).</summary>
    public KeyPanel? WindowPanel { get; set; }

    /// <summary>On a headset: whether the keyboard was open at the last windows change.</summary>
    private bool keyboardOpen;

    /// <summary>On a headset: until when the keyboard opening again is the field tapped back, not the user's, so the window is not brought again.</summary>
    private long quietUntil;

    /// <summary>
    /// On a headset, docking (⤓ filled): the keyboard opens, and the keys'
    /// window, minimized or closed by the user, is brought back to the front
    /// or opened again, the app that had the focus keeping it. Which window had the focus is noted first, with
    /// its field, which is tapped once the keys' window is brought, to give
    /// the focus back. The window is never minimized here, the user does
    /// that.
    /// </summary>
    private void BringKeysWithKeyboard(bool open)
    {
        var opened = open && !keyboardOpen;
        keyboardOpen = open;
        var now = SystemClock.UptimeMillis();
        // Docked (⤓ filled), a window the user minimized is brought back, and
        // one closed opened again; one in sight is left as it is, for
        // bringing it would make it the headset's first.
        if (!opened || !KeyWindowActivity.Docked(this) || KeyWindowActivity.InSight || now < quietUntil)
        {
            return;
        }

        quietUntil = now + 2000;
        var origin = FocusedWindow();
        var field = FindFocus(NodeFocus.Input);
        OpenKeyWindow();
        Report($"Keyboard open: keys' window to the front; focus in {Describe(origin)}");
        // Always given back: on a headset the app may not take keys again
        // until touched, though Android tells it still has the focus.
        new Handler(Looper.MainLooper!).PostDelayed(() =>
        {
            var given = field?.PerformAction(Android.Views.Accessibility.Action.Focus) == true
                | field?.PerformAction(Android.Views.Accessibility.Action.Click) == true;
            Report($"Focus {(given ? "given back to" : "could not be given back to")} {Describe(origin)}; Android says it is in {Describe(FocusedWindow())}");
        }, 500);
    }

    /// <summary>Test on a headset: keys attached into another app's window, and what draws them.</summary>
    private SurfaceControlViewHost? attachedHost;
    private SurfaceControl? attachedSurface;

    /// <summary>Test on a headset: where the attached keys stand in their window, and where a drag on their title bar started.</summary>
    private (float X, float Y) attachedAt;
    private (float X, float Y) attachedDragFrom;

    /// <summary>
    /// Test on a headset: the keys attached into another app's window, the
    /// keyboard's or the focused app's, half as wide as it, where they were
    /// left in it last, at first its bottom left: Android 14 lets an
    /// accessibility service hang an overlay of its own in any window it
    /// sees, which then moves with that window. Their title bar dragged moves
    /// them within that window, never out of it; where they are left is kept,
    /// for the keyboard's and for an app's apart.
    /// </summary>
    public void AttachKeys(bool toKeyboard)
    {
        DetachKeys();
        if (!OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            Report("Attaching keys to another app's window takes Android 14");
            return;
        }

        var target = toKeyboard ? WindowOfType(AccessibilityWindowType.InputMethod) : FocusedWindow();
        if (target is null)
        {
            Report(toKeyboard ? "No keyboard open to attach the keys to" : "No focused window to attach the keys to");
            return;
        }

        var bounds = new Rect();
        target.GetBoundsInScreen(bounds);
        var display = GetSystemService(DisplayService).JavaCast<DisplayManager>()!.GetDisplay(target.DisplayId)!;
        var context = CreateDisplayContext(display)!;
        // The field typed in, which the attached keys give the focus back to
        // before each key: touching them takes it.
        var field = FindFocus(NodeFocus.Input);
        var fieldWindow = FocusedWindow();
        void Refocused(System.Action send)
        {
            var before = FocusedWindow();
            if (field is null || fieldWindow is null || before?.Id == fieldWindow.Id)
            {
                send();
                return;
            }

            field.Refresh();
            var given = field.PerformAction(Android.Views.Accessibility.Action.Focus);
            new Handler(Looper.MainLooper!).PostDelayed(() =>
            {
                Report($"Focus was in {Describe(before)}; {(given ? "given back to" : "could not be given back to")} {Describe(fieldWindow)}; now in {Describe(FocusedWindow())}");
                send();
            }, 80);
        }

        var panel = new KeyPanel(
            context,
            KeyPlusPreferences.ForScreen(this, KeyPlusPreferences.ScreenOf(this, target.DisplayId)),
            Clipboard,
            (code, meta) => Refocused(() => SendKey(code, meta)),
            text => Refocused(() => TypeText(text)))
        {
            Dockable = false,
            Resizable = false,
        };
        var width = Math.Max(1, bounds.Width() / 2);
        panel.Root.Measure(
            View.MeasureSpec.MakeMeasureSpec(width, MeasureSpecMode.Exactly),
            View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));
        var height = Math.Max(1, panel.Root.MeasuredHeight);
        try
        {
            var host = new SurfaceControlViewHost(context, display, new Binder());
            var unfocusable = SetUnfocusableView(host, panel.Root, width, height);
            var surface = host.GetSurfacePackage()!.SurfaceControl!;
            AttachAccessibilityOverlayToWindow(target.Id, surface);

            // Within the window, never out of it.
            var room = (X: Math.Max(0, bounds.Width() - width), Y: Math.Max(0, bounds.Height() - height));
            (float X, float Y) Within(float x, float y) => (Math.Clamp(x, 0, room.X), Math.Clamp(y, 0, room.Y));
            var preferences = KeyPlusPreferences.Global(this);
            var key = toKeyboard ? "attached.keyboard" : "attached.app";
            attachedAt = Within(preferences.GetFloat($"{key}.x", 0), preferences.GetFloat($"{key}.y", room.Y));
            new SurfaceControl.Transaction()
                .SetVisibility(surface, true)!
                .SetPosition(surface, attachedAt.X, attachedAt.Y)!
                .SetLayer(surface, int.MaxValue)!
                .Apply();
            panel.Moving += (drag, dx, dy) =>
            {
                if (drag == KeyPanel.Drag.Start)
                {
                    attachedDragFrom = attachedAt;
                }

                attachedAt = Within(attachedDragFrom.X + dx, attachedDragFrom.Y + dy);
                new SurfaceControl.Transaction().SetPosition(surface, attachedAt.X, attachedAt.Y)!.Apply();
                if (drag == KeyPanel.Drag.End)
                {
                    preferences.Edit()!.PutFloat($"{key}.x", attachedAt.X)!.PutFloat($"{key}.y", attachedAt.Y)!.Apply();
                    Report($"Attached keys left at {attachedAt.X:0},{attachedAt.Y:0} in their window");
                }
            };
            attachedHost = host;
            attachedSurface = surface;
            Report($"Keys attached to {Describe(target)}, its bounds {bounds.ToShortString()}, keys {width}x{height} at {attachedAt.X:0},{attachedAt.Y:0}, {unfocusable}");
        }
        catch (Exception refused)
        {
            Report($"Keys not attached to {Describe(target)}: {refused.GetType().Name}: {refused.Message}");
        }
    }

    /// <summary>
    /// Test on a headset: the attached keys' view set in the window that
    /// draws them, a window that never takes the focus, so the field typed in
    /// keeps it and the keys reach it. The public setView makes a window that
    /// takes the focus when touched; the one taking the window's settings,
    /// where it can be told not to, is hidden in Android, so it is reached
    /// through JNI, and where Android refuses, the public one is used; which
    /// is said.
    /// </summary>
    private static string SetUnfocusableView(SurfaceControlViewHost host, View view, int width, int height)
    {
        var settings = new WindowManagerLayoutParams(width, height, WindowManagerTypes.Application, WindowManagerFlags.NotFocusable, Format.Transparent);
        try
        {
            var type = JNIEnv.FindClass("android/view/SurfaceControlViewHost");
            var setView = JNIEnv.GetMethodID(type, "setView", "(Landroid/view/View;Landroid/view/WindowManager$LayoutParams;)V");
            JNIEnv.CallVoidMethod(host.Handle, setView, new JValue(view), new JValue(settings));
            return "a window that never takes the focus";
        }
        catch (Exception refused)
        {
            host.SetView(view, width, height);
            return $"hidden setView refused ({refused.GetType().Name}: {refused.Message}): a window that takes the focus";
        }
    }

    /// <summary>Test on a headset: the keys attached into another app's window taken out.</summary>
    public void DetachKeys()
    {
        if (attachedSurface is not null)
        {
            new SurfaceControl.Transaction().Reparent(attachedSurface, null)!.Apply();
            Report("Attached keys taken out");
        }

        attachedHost?.Release();
        attachedHost = null;
        attachedSurface = null;
    }

    /// <summary>The first window of a type, on whichever display; none.</summary>
    private AccessibilityWindowInfo? WindowOfType(AccessibilityWindowType type)
    {
        var displays = WindowsOnAllDisplays;
        for (var i = 0; i < (displays?.Size() ?? 0); i++)
        {
            IList<AccessibilityWindowInfo> windows = JavaList<AccessibilityWindowInfo>.FromJniHandle(displays!.ValueAt(i)!.Handle, JniHandleOwnership.DoNotTransfer) ?? [];
            if (windows.FirstOrDefault(window => window.Type == type) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The window that has the focus, on whichever display; none.</summary>
    private AccessibilityWindowInfo? FocusedWindow()
    {
        var displays = WindowsOnAllDisplays;
        for (var i = 0; i < (displays?.Size() ?? 0); i++)
        {
            IList<AccessibilityWindowInfo> windows = JavaList<AccessibilityWindowInfo>.FromJniHandle(displays!.ValueAt(i)!.Handle, JniHandleOwnership.DoNotTransfer) ?? [];
            if (windows.FirstOrDefault(window => window.IsFocused) is { } focused)
            {
                return focused;
            }
        }

        return null;
    }

    /// <summary>A window as the line names it: its app and its title.</summary>
    private static string Describe(AccessibilityWindowInfo? window) =>
        window is null ? "no window" : $"{window.Root?.PackageName ?? "?"} '{window.Title}' (window {window.Id})";

    /// <summary>Each screen's layout: the phone upright, on its side, and DeX.</summary>
    private readonly Dictionary<KeyPlusPreferences.Screen, PanelPlacement> placements = [];

    /// <summary>Where the panel's window and size stood when a drag on its title bar, icon or a grip started.</summary>
    private (int X, int Y, int Width, int Height, float Scale) dragStart;

    /// <summary>Where the icon stood when a drag on it started.</summary>
    private (int X, int Y) iconDragStart;

    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        KeyPlusPreferences.SplitLayoutsOnce(this);
        KeyPlusPreferences.Global(this).RegisterOnSharedPreferenceChangeListener(this);
        lockReceiver = new LockReceiver(this);
        var locking = new Android.Content.IntentFilter(Android.Content.Intent.ActionScreenOff);
        locking.AddAction(Android.Content.Intent.ActionScreenOn);
        locking.AddAction(Android.Content.Intent.ActionUserPresent);
        RegisterReceiver(lockReceiver, locking, Android.Content.ReceiverFlags.NotExported);
        Current = this;
        if (OnHeadset)
        {
            OpenKeyWindow();
        }

        Place();
    }

    /// <summary>The keys' own window opened, on a headset, where the user places it beside the keyboard.</summary>
    public void OpenKeyWindow() =>
        StartActivity(new Android.Content.Intent(this, typeof(KeyWindowActivity)).AddFlags(Android.Content.ActivityFlags.NewTask));

    /// <summary>KeyPlus closed, or opened again by the app: the panel goes, or comes back.</summary>
    public void OnSharedPreferenceChanged(Android.Content.ISharedPreferences? preferences, string? key)
    {
        if (key == KeyPlusPreferences.HiddenKey)
        {
            Place();
        }
    }

    public override Android.AccessibilityServices.InputMethod OnCreateInputMethod() => new SelectionInputMethod(this, OnCaretMoved);

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        if (e?.EventType == EventTypes.WindowsChanged)
        {
            Place();
            ReportKeyboard();
        }
    }

    /// <summary>Test on a headset: whether ⌨ waits to tell where the keyboard came back.</summary>
    private bool reportPending;

    /// <summary>
    /// Test on a headset, ⌨ in the keys' window: hides the keyboard, and at
    /// once lets it show again and taps the field that has the focus, so the
    /// keyboard opens anew; once it is back, <see cref="ReportKeyboard"/> says
    /// what Android tells of where it stands.
    /// </summary>
    public void ToggleKeyboard()
    {
        var controller = GetSoftKeyboardController()!;
        controller.SetShowMode(AccessibilityServiceShowMode.Hidden);
        Report("Keyboard hidden");
        new Handler(Looper.MainLooper!).PostDelayed(() =>
        {
            controller.SetShowMode(AccessibilityServiceShowMode.Auto);
            reportPending = true;
            var field = FindFocus(NodeFocus.Input);
            var tapped = field?.PerformAction(Android.Views.Accessibility.Action.Click) == true;
            Report(tapped ? "Keyboard asked back: field tapped" : "Keyboard asked back: no focused field to tap");
        }, 500);
    }

    /// <summary>
    /// Once the keyboard is back after ⌨: everything Android tells an
    /// accessibility service of the keyboard's window, on the keys' window's
    /// line and in the log.
    /// </summary>
    private void ReportKeyboard()
    {
        if (!reportPending)
        {
            return;
        }

        var displays = WindowsOnAllDisplays;
        for (var i = 0; i < (displays?.Size() ?? 0); i++)
        {
            IList<AccessibilityWindowInfo> windows = JavaList<AccessibilityWindowInfo>.FromJniHandle(displays!.ValueAt(i)!.Handle, JniHandleOwnership.DoNotTransfer) ?? [];
            if (windows.FirstOrDefault(window => window.Type == AccessibilityWindowType.InputMethod) is not { } keyboard)
            {
                continue;
            }

            var bounds = new Rect();
            keyboard.GetBoundsInScreen(bounds);
            var region = new Region();
            keyboard.GetRegionInScreen(region);
            reportPending = false;
            Report($"Keyboard back: bounds {bounds.ToShortString()} {bounds.Width()}x{bounds.Height()}, region {region.Bounds.ToShortString()}, layer {keyboard.Layer}, display {displays.KeyAt(i)}, id {keyboard.Id}, title '{keyboard.Title}', active {keyboard.IsActive}, focused {keyboard.IsFocused}");
            return;
        }
    }

    /// <summary>Test on a headset: a line on the keys' window and in the log.</summary>
    private void Report(string text)
    {
        Log.Info(LogTag, text);
        if (WindowPanel is { } panel)
        {
            panel.InfoText = $"{DateTime.Now:HH:mm:ss} {text}";
        }
    }

    public override void OnInterrupt()
    {
    }

    /// <summary>Whether the phone is locked: KeyPlus is not shown on the lock screen.</summary>
    private bool Locked() => GetSystemService(KeyguardService).JavaCast<KeyguardManager>()?.IsKeyguardLocked == true;

    /// <summary>The screen going off or the phone unlocked: KeyPlus goes, or comes back.</summary>
    private sealed class LockReceiver(KeyPlusService service) : Android.Content.BroadcastReceiver
    {
        public override void OnReceive(Android.Content.Context? context, Android.Content.Intent? intent) => service.Place();
    }

    private LockReceiver? lockReceiver;

    /// <summary>The phone turned: the panel takes the layout kept for the way it is held now.</summary>
    public override void OnConfigurationChanged(Android.Content.Res.Configuration? newConfig)
    {
        base.OnConfigurationChanged(newConfig!);
        Place();
    }

    public override void OnDestroy()
    {
        Current = null;
        KeyPlusPreferences.Global(this).UnregisterOnSharedPreferenceChangeListener(this);
        if (lockReceiver is not null)
        {
            UnregisterReceiver(lockReceiver);
        }

        Close();
        base.OnDestroy();
    }

    /// <summary>
    /// Puts the panel where it stands: docked, right above the keyboard, as wide
    /// as it or as a tab at its right, or as the icon alone at the display's
    /// bottom right while no keyboard is open, for the icon is always in sight;
    /// floating, where the user left it, on the keyboard's display, or on the
    /// one it is on while no keyboard is open.
    /// </summary>
    private void Place()
    {
        if (KeyPlusPreferences.IsHidden(this))
        {
            Close();
            return;
        }

        // On a headset the keys are in a window of their own, not here.
        if (OnHeadset)
        {
            Close();
            BringKeysWithKeyboard(FindKeyboard() is not null);
            return;
        }

        var keyboard = FindKeyboard();
        var displayId = keyboard?.DisplayId ?? overlay?.DisplayId ?? FocusedDisplayId() ?? Display.DefaultDisplay;
        var screen = KeyPlusPreferences.ScreenOf(this, displayId);
        placed = PlacementFor(screen);
        if (overlay?.DisplayId != displayId || overlay.Screen != screen)
        {
            // Another display, or the phone turned: a window with that
            // screen's layout, Keys or ABC, labels' size and all.
            Close();
            overlay = Open(displayId, screen);
            if (overlay is null)
            {
                return;
            }

            placed.Mode = overlay.Panel.Mode;
            overlay.Panel.Scale = placed.Scale;
        }

        if (Locked())
        {
            // Nothing of KeyPlus on the lock screen; back once the phone is unlocked.
            overlay.Panel.StopRepeat();
            overlay.Panel.Root.Visibility = ViewStates.Gone;
            return;
        }

        var (_, _, windowManager, panel, placement) = overlay;
        panel.Floating = placed.Floating;
        panel.IconOnly = keyboard is null && !placed.Floating;
        var display = windowManager.MaximumWindowMetrics.Bounds;
        int x, y, width;
        GravityFlags gravity;
        if (panel.ShowsIcon && placed.HasIconPlace)
        {
            width = ViewGroup.LayoutParams.WrapContent;
            x = Math.Clamp(placed.IconX, 0, Math.Max(0, display.Width() - panel.Root.Width));
            y = Math.Clamp(placed.IconY, 0, Math.Max(0, display.Height() - panel.Root.Height));
            gravity = GravityFlags.Top | GravityFlags.Left;
        }
        else if (panel.IconOnly)
        {
            var margin = (int)(16 * Resources!.DisplayMetrics!.Density);
            (x, y, width) = (margin, 6 * margin, ViewGroup.LayoutParams.WrapContent);
            gravity = GravityFlags.Bottom | GravityFlags.Right;
        }
        else if (placed.Floating || placed.Width > 0)
        {
            // Floating, or docked where the user has placed it: there, as they
            // left it; docked, only while a keyboard is open.
            // No width yet when the icon was the first thing dragged: the
            // display's, until a grip gives it one.
            width = panel.ShowsIcon ? ViewGroup.LayoutParams.WrapContent
                : placed.Width > 0 ? Math.Min(placed.Width, display.Width())
                : display.Width();
            x = Math.Clamp(placed.X, 0, Math.Max(0, display.Width() - (panel.ShowsIcon ? panel.Root.Width : width)));
            y = Math.Clamp(placed.Y, 0, Math.Max(0, display.Height() - panel.Root.Height));
            gravity = GravityFlags.Top | GravityFlags.Left;
        }
        else
        {
            var bounds = keyboard!.Value.Bounds;
            y = display.Bottom - bounds.Top;
            x = panel.ShowsIcon ? display.Right - bounds.Right : bounds.Left - display.Left;
            width = panel.ShowsIcon ? ViewGroup.LayoutParams.WrapContent : bounds.Width();
            gravity = GravityFlags.Bottom | (panel.ShowsIcon ? GravityFlags.Right : GravityFlags.Left);
        }

        // The panel's own window changing is a windows change too: placing it
        // again where it already is would only call this once more.
        if (panel.Root.Visibility == ViewStates.Visible
            && placement.X == x && placement.Y == y && placement.Width == width && placement.Gravity == gravity)
        {
            return;
        }

        // Docked with no keyboard open, the panel is the icon alone: there is no keyboard to name.
        var where = placed.Floating ? "floating"
            : keyboard is null ? "as the icon, no keyboard open"
            : placed.Width > 0 ? "docked where it was left"
            : $"above the keyboard at {keyboard.Value.Bounds.ToShortString()}";
        Log.Info(LogTag, $"Panel on display {displayId} at {x},{y} width {width}, {where}");
        placement.X = x;
        placement.Y = y;
        placement.Width = width;
        placement.Gravity = gravity;
        panel.Root.Visibility = ViewStates.Visible;
        windowManager.UpdateViewLayout(panel.Root, placement);
    }

    /// <summary>The title bar dragged: the panel follows the finger, docked or floating as it was; the icon dragged moves the icon alone.</summary>
    private void OnMoving(KeyPanel.Drag drag, float dx, float dy)
    {
        if (overlay?.Panel.ShowsIcon == true)
        {
            MoveIcon(drag, dx, dy);
            return;
        }

        if (StartPlacing(drag) is not { } moved)
        {
            return;
        }

        moved.X = dragStart.X + (int)dx;
        moved.Y = dragStart.Y + (int)dy;
        Dragged(drag, moved);
    }

    /// <summary>The icon dragged: it alone follows the finger, and stays where it is left; the window keeps its own place.</summary>
    private void MoveIcon(KeyPanel.Drag drag, float dx, float dy)
    {
        if (placed is null || overlay is null)
        {
            return;
        }

        if (drag == KeyPanel.Drag.Start)
        {
            var location = new int[2];
            overlay.Panel.Root.GetLocationOnScreen(location);
            var display = overlay.WindowManager.MaximumWindowMetrics.Bounds;
            iconDragStart = (location[0] - display.Left, location[1] - display.Top);
        }

        placed.IconX = iconDragStart.X + (int)dx;
        placed.IconY = iconDragStart.Y + (int)dy;
        Dragged(drag, placed);
    }

    /// <summary>
    /// A corner's grip dragged: across the panel gets wider or narrower, up or
    /// down its keys bigger or smaller, and the opposite corner stays where it
    /// is; docked or floating as it was.
    /// </summary>
    private void OnResizing(KeyPanel.Corner corner, KeyPanel.Drag drag, float dx, float dy)
    {
        if (StartPlacing(drag) is not { } floating || overlay is null)
        {
            return;
        }

        var fromLeft = (corner & KeyPanel.Corner.Left) != 0;
        var fromTop = (corner & KeyPanel.Corner.Top) != 0;
        var minWidth = (int)(PanelPlacement.MinWidthDp * Resources!.DisplayMetrics!.Density);
        var height = Math.Max(1, dragStart.Height);
        floating.Width = Math.Max(minWidth, dragStart.Width + (int)(fromLeft ? -dx : dx));
        floating.Scale = Math.Clamp(
            dragStart.Scale * (height + (fromTop ? -dy : dy)) / height,
            PanelPlacement.MinScale,
            PanelPlacement.MaxScale);
        var grownHeight = (int)(height * floating.Scale / dragStart.Scale) - height;
        floating.X = fromLeft ? dragStart.X - (floating.Width - dragStart.Width) : dragStart.X;
        floating.Y = fromTop ? dragStart.Y - grownHeight : dragStart.Y;
        overlay.Panel.Scale = floating.Scale;
        Dragged(drag, floating);
    }

    /// <summary>A↺ pressed: the keys go back to their usual size; the window keeps its place and width.</summary>
    private void OnUsualSizeRequested()
    {
        if (placed is null || overlay is null)
        {
            return;
        }

        placed.Scale = 1f;
        placed.Save();
        overlay.Panel.Scale = 1f;
        Place();
    }

    /// <summary>
    /// ⤓ pressed: docked, the panel shows only while a keyboard is open, or,
    /// pressed again, floats, in sight all the time; where it stands and its
    /// size stay as they are either way.
    /// </summary>
    private void OnDockRequested()
    {
        if (placed is null)
        {
            return;
        }

        placed.Floating = !placed.Floating;
        placed.Save();
        Place();
    }

    /// <summary>The icon tapped with no keyboard to dock above: the window opens floating where the icon is.</summary>
    private void OnOpenRequested()
    {
        if (StartPlacing(KeyPanel.Drag.Start) is { } floating && overlay is not null)
        {
            floating.Floating = true;
            floating.Save();
            overlay.Panel.Unfold();
        }
    }

    /// <summary>
    /// The user said yes to closing KeyPlus: nothing of it is in sight until
    /// the app is opened again. The service stays on, for an app cannot turn
    /// an accessibility service back on; only Settings can.
    /// </summary>
    private void OnCloseRequested() => KeyPlusPreferences.SetHidden(this, true);

    /// <summary>The layout of a screen, each read once and kept.</summary>
    private PanelPlacement PlacementFor(KeyPlusPreferences.Screen screen)
    {
        if (!placements.TryGetValue(screen, out var placement))
        {
            placement = new PanelPlacement(KeyPlusPreferences.ForScreen(this, screen));
            placements[screen] = placement;
        }

        return placement;
    }

    /// <summary>ABC or Keys pressed: the window takes the other mode's place, width and keys' size.</summary>
    private void OnModeChanged()
    {
        if (placed is null || overlay is null)
        {
            return;
        }

        placed.Mode = overlay.Panel.Mode;
        overlay.Panel.Scale = placed.Scale;
        Place();
    }

    /// <summary>
    /// At a drag's start, notes where the panel stands now, as its place from
    /// then on, docked or floating as it is; the placement to change, or none
    /// without a panel in sight. The width is the open window's: dragging the
    /// icon keeps the one it had.
    /// </summary>
    private PanelPlacement? StartPlacing(KeyPanel.Drag drag)
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
            placed.X = dragStart.X;
            placed.Y = dragStart.Y;
            if (!overlay.Panel.ShowsIcon)
            {
                placed.Width = dragStart.Width;
            }
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

    /// <summary>Opens the panel's window on a display, with a screen's layout, out of sight until placed.</summary>
    private Overlay? Open(int displayId, KeyPlusPreferences.Screen screen)
    {
        var display = GetSystemService(DisplayService).JavaCast<DisplayManager>()?.GetDisplay(displayId);
        if (display is null)
        {
            return null;
        }

        var context = CreateWindowContext(display, (int)WindowManagerTypes.AccessibilityOverlay, null);
        var windowManager = context.GetSystemService(WindowService).JavaCast<IWindowManager>()!;
        var panel = new KeyPanel(context, KeyPlusPreferences.ForScreen(this, screen), Clipboard, SendKey, TypeText);
        panel.FoldedChanged += Place;
        panel.Moving += OnMoving;
        panel.Resizing += OnResizing;
        panel.DockRequested += OnDockRequested;
        panel.UsualSizeRequested += OnUsualSizeRequested;
        panel.ModeChanged += OnModeChanged;
        panel.OpenRequested += OnOpenRequested;
        panel.CloseRequested += OnCloseRequested;
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
        try
        {
            windowManager.AddView(panel.Root, placement);
        }
        catch (WindowManagerBadTokenException refused)
        {
            // The service's connection was being taken down as it came up
            // (turned off and on at once): no window now; the next windows
            // change, on the connection that stays, opens it.
            Log.Warn(LogTag, $"No window on display {displayId}: {refused.Message}");
            return null;
        }

        return new Overlay(displayId, screen, windowManager, panel, placement);
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
        if ((overlay?.Panel ?? WindowPanel) is not { HoldsKeyModifier: true } panel || newStart != oldStart + 1)
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

    /// <summary>
    /// The key that types a character, with the Shift it takes: the Spanish
    /// keyboard's, so ç is the key where a US keyboard has the backslash, else
    /// a virtual keyboard's; none for a character no key types.
    /// </summary>
    private static (Keycode Code, MetaKeyStates Meta)? KeyOf(char character)
    {
        if (PanelKeys.SpanishKeyOf(character.ToString()) is { } spanish)
        {
            return spanish;
        }

        var events = KeyCharacterMap.Load((int)KeyboardType.VirtualKeyboard)?.GetEvents([character]);
        var down = events?.FirstOrDefault(e => e.Action == KeyEventActions.Down && !KeyEvent.IsModifierKey(e.KeyCode));
        return down is null ? null : (down.KeyCode, down.MetaState & MetaKeyStates.ShiftMask);
    }

    /// <summary>Types text into the focused editor, as the phone's keyboard does.</summary>
    public void TypeText(string text) => InputMethod?.CurrentInputConnection?.CommitText(text, 1, null);

    /// <summary>
    /// Sends a key, with the modifiers held, to the focused editor
    /// (<see cref="KeySender"/>); Ctrl+C and Ctrl+X keep the text selected in
    /// the copied texts first, for Android lets no accessibility service read
    /// the clipboard they copy it to.
    /// </summary>
    public void SendKey(Keycode code, MetaKeyStates meta)
    {
        if (InputMethod?.CurrentInputConnection is not { } connection)
        {
            return;
        }

        if (code is Keycode.C or Keycode.X && meta.HasFlag(MetaKeyStates.CtrlOn))
        {
            // No text around the selection asked for: the selection alone.
            Clipboard.Add(connection.GetSurroundingText(0, 0, 0)?.Text?.ToString());
        }

        KeySender.Send(connection.SendKeyEvent, code, meta);
    }

    /// <summary>The texts copied lately, which 📋 lists.</summary>
    public ClipboardHistory Clipboard => clipboard ??= new ClipboardHistory(this);

    private ClipboardHistory? clipboard;
}
