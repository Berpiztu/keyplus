using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Accessibility;

namespace Berpiztu.KeyPlus;

/// <summary>
/// What the user is at, in any window, as accessibility shows it and Windows'
/// Magnifier follows it: the caret where text is typed (OBJID_CARET, shown or
/// moved), and a menu while it is open (its popup started, or an item of it
/// focused, as Chromium's menus in VS Code and Edge are), until it ends or
/// the focus leaves it; and whether the focus went to a place text is typed
/// in (an editable text, or anything a caret shows in soon after) or away
/// from one: a menu is not away, nor a focus that leaves a caret still shown
/// in the window (VS Code's suggestions, focused while the editor keeps its
/// caret). Places on the screen, in pixels, told on the thread that made the
/// watch; each focus and what was made of it written to the log.
/// Watched and read on a thread of its own: reading another application's
/// accessibility waits for that application to answer, and one busy (a VS
/// Code window connecting by SSH) held the keyboard's keys still while it
/// did.
/// </summary>
public sealed class FocusWatch : IDisposable
{
    private const uint ForegroundChanged = 0x0003;
    private const uint MenuPopupStart = 0x0006;
    private const uint MenuPopupEnd = 0x0007;
    private const uint ObjectShow = 0x8002;
    private const uint ObjectFocus = 0x8005;
    private const uint ObjectLocationChange = 0x800B;
    private const uint OutOfContext = 0;
    private const int CaretObject = -8;
    private const int ClientObject = -4;
    private const int MenuBarRole = 2;
    private const int MenuPopupRole = 11;
    private const int MenuItemRole = 12;
    private const int TextRole = 42;
    private const int ReadOnlyState = 0x40;
    private const int InvisibleState = 0x8000;

    /// <summary>How long after the focus moved a caret still tells it went where text is typed: an app shows its caret just after.</summary>
    private static readonly TimeSpan CaretAfterFocus = TimeSpan.FromMilliseconds(400);

    private static readonly Guid AccessibleId = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    /// <summary>How long after a menu ended it is taken for closed: going from a submenu to another ends one and opens the next.</summary>
    private static readonly TimeSpan MenuSettling = TimeSpan.FromMilliseconds(400);

    /// <summary>How far up from a focused item its menu is looked for.</summary>
    private const int MostParents = 8;

    /// <summary>The thread that made the watch, where what is seen is told.</summary>
    private readonly Dispatcher keyboard = Dispatcher.CurrentDispatcher;

    /// <summary>The watch's own thread: its hooks, its timers and every read of another application's accessibility.</summary>
    private readonly Dispatcher watching;

    private readonly WinEventProc seen;
    private IntPtr[] hooks = [];
    private DispatcherTimer focusSettling = null!;
    private DispatcherTimer menuSettling = null!;
    private bool inMenu;

    /// <summary>Set on the keyboard's thread: nothing more is told once the watch is disposed.</summary>
    private bool disposed;

    /// <summary>The focus as UI Automation tells it, for what WinEvents do not (WinUI apps); none where it cannot be had.</summary>
    private readonly AutomationFocus? automationFocus;

    /// <summary>On the watch's thread: what UI Automation told focused last, and when a WinEvent last told a focus.</summary>
    private AutomationFocus.Focused? automationFocused;
    private long focusSeenAt;

    public FocusWatch()
    {
        seen = Seen;
        Dispatcher? made = null;
        using var started = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            made = Dispatcher.CurrentDispatcher;
            Watch();
            started.Set();

            // The window in front already, asked as those that come to the front later are.
            Seen(IntPtr.Zero, ForegroundChanged, GetForegroundWindow(), 0, 0, 0, 0);
            Dispatcher.Run();
            foreach (var hook in hooks)
            {
                UnhookWinEvent(hook);
            }
        })
        { IsBackground = true, Name = "Focus watch" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        started.Wait();
        watching = made!;

        // Off this thread and the watch's, which are single-threaded apartments: UI Automation's clients are best in a multithreaded one.
        automationFocus = Task.Run(() => AutomationFocus.Start(focused => watching.BeginInvoke(() => SeenByAutomation(focused)))).Result;
    }

    /// <summary>On the watch's thread: its timers, then its hooks, whose calls come to the thread that set them.</summary>
    private void Watch()
    {
        focusSettling = new DispatcherTimer { Interval = CaretAfterFocus };
        menuSettling = new DispatcherTimer { Interval = MenuSettling };
        focusSettling.Tick += (_, _) =>
        {
            focusSettling.Stop();
            // A caret still shown is text being typed in: VS Code's editor, come back to, tells only a pane
            // focused; told as text, so a keyboard that hid shows again and one in sight stays.
            if (CaretInForeground())
            {
                Log.Detail("Focus: no caret came, but the window still shows one: still writing");
                TellTextFocused(FrontApp());
                return;
            }

            // A WinUI app's field (Task Manager's filter): its window tells only its client focused, UI Automation the field.
            if (automationFocused is { Editable: true } field && field.Process == FrontProcess())
            {
                Log.Detail("Focus: UI Automation has a field focused there: writing");
                TellTextFocused(FrontApp());
                return;
            }

            Log.Detail("Focus: left the text");
            Tell(() => TextLeft?.Invoke());
        };
        menuSettling.Tick += (_, _) =>
        {
            menuSettling.Stop();
            if (inMenu)
            {
                inMenu = false;
                Log.Detail("Menu: closed");
                Tell(() => MenuClosed?.Invoke());
            }
        };

        hooks = [Hook(ForegroundChanged, ForegroundChanged), Hook(MenuPopupStart, MenuPopupEnd), Hook(ObjectShow, ObjectShow), Hook(ObjectFocus, ObjectFocus), Hook(ObjectLocationChange, ObjectLocationChange)];
    }

    /// <summary>What was seen told on the keyboard's thread, in the order it was seen; nothing once the watch is disposed.</summary>
    private void Tell(Action told) => keyboard.BeginInvoke(() =>
    {
        if (!disposed)
        {
            told();
        }
    });

    /// <summary>The focus went where text is typed.</summary>
    public event Action? TextFocused;

    /// <summary>The application the text focused is in, by its program's name; none before one, or in the keyboard's own windows.</summary>
    public string? App { get; private set; }

    /// <summary>The focus went somewhere no text is typed.</summary>
    public event Action? TextLeft;

    /// <summary>The caret shown or moved: its rectangle.</summary>
    public event Action<Int32Rect>? CaretMoved;

    /// <summary>What has the focus now, a field where text is typed: its place on the screen, in pixels; none where it gives none.</summary>
    public Int32Rect? Field { get; private set; }

    /// <summary>A menu open: its rectangle.</summary>
    public event Action<Int32Rect>? MenuOpened;

    /// <summary>The menus closed, or the focus gone from them, for a moment with no other menu.</summary>
    public event Action? MenuClosed;

    /// <summary>On the keyboard's thread, never waiting: the watch's thread lets go of its hooks once done with what it is asking.</summary>
    public void Dispose()
    {
        disposed = true;
        automationFocus?.Dispose();
        watching.BeginInvokeShutdown(DispatcherPriority.Send);
    }

    /// <summary>
    /// A focus UI Automation told, on the watch's thread: a field is text typed in at once; anything else, where no
    /// WinEvent told a focus just now (a WinUI app, whose window tells none inside it), waits for a caret as a
    /// WinEvent's focus does. Menus are left to the WinEvents, which place them; the keyboard's own windows to
    /// them too.
    /// </summary>
    private void SeenByAutomation(AutomationFocus.Focused focused)
    {
        try
        {
            var name = NameOf(focused.Process);
            if (name == Process.GetCurrentProcess().ProcessName)
            {
                return;
            }

            automationFocused = focused;
            if (focused.Menu)
            {
                return;
            }

            if (focused.Editable)
            {
                focusSettling.Stop();
                Log.Detail($"Focus: {focused.Kind} in {name}, by UI Automation: writing");
                Tell(() => Field = focused.Place);
                TellTextFocused(name, KindOf(focused.Id, focused.Class));
            }
            else if (Environment.TickCount64 - focusSeenAt > CaretAfterFocus.TotalMilliseconds)
            {
                Log.Detail($"Focus: {focused.Kind} in {name}, by UI Automation");
                focusSettling.Stop();
                focusSettling.Start();
            }
        }
        catch (Exception failed)
        {
            Log.Write($"Focus: a UI Automation focus could not be followed: {failed}");
        }
    }

    private IntPtr Hook(uint first, uint last) => SetWinEventHook(first, last, IntPtr.Zero, seen, 0, 0, OutOfContext);

    /// <summary>Windows' call: nothing thrown may go back to it, for that ends the keyboard; what failed is written to the log.</summary>
    private void Seen(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        try
        {
            See(kind, window, objectId, childId);
        }
        catch (Exception failed)
        {
            Log.Write($"Focus: event 0x{kind:X4} in window 0x{window:X} could not be followed: {failed}");
        }
    }

    private void See(uint kind, IntPtr window, int objectId, int childId)
    {
        if (kind == ForegroundChanged)
        {
            if (!OwnWindow(window))
            {
                Interlocked.Exchange(ref lastFront, window);
            }

            Awaken(window);
            return;
        }

        // The keyboard's own window focused (Windows activates it at times, a drag begun with the shell in front): not the
        // text left, for the keyboard gives the front back to the window typed in (LastFront).
        if (kind == ObjectFocus && OwnWindow(window))
        {
            Log.Detail("Focus: the keyboard's own window, the text not left");
            return;
        }

        if (kind is MenuPopupStart or MenuPopupEnd)
        {
            Log.Detail($"Menu: {(kind == MenuPopupStart ? "a popup started" : "a popup ended")} in {ProcessOf(window)}, object {objectId}");
        }

        if (kind == MenuPopupEnd)
        {
            EndMenu();
            return;
        }

        var caret = objectId == CaretObject;
        if (kind is ObjectShow or ObjectLocationChange && !caret)
        {
            return;
        }

        if (AccessibleObjectFromEvent(window, objectId, childId, out var accessible, out var child) != 0 || accessible is null)
        {
            // What the focus or a menu told but could not be read, for the log to say where it was lost.
            if (kind is ObjectFocus or MenuPopupStart)
            {
                Log.Detail($"Focus: {(kind == ObjectFocus ? "a focus" : "a popup")} in {ProcessOf(window)} could not be read");
            }

            return;
        }

        try
        {
            if (caret)
            {
                // A caret behind an open menu (the editor's, still blinking) is not the user going back to it.
                if (inMenu)
                {
                    return;
                }

                if (focusSettling.IsEnabled)
                {
                    focusSettling.Stop();
                    Log.Detail($"Focus: a caret showed in {ProcessOf(window)}: writing");
                    TellTextFocused(AppOf(window));
                }

                if (Place(accessible, child) is { Height: > 0 } at)
                {
                    Tell(() => CaretMoved?.Invoke(at));
                }

                return;
            }

            var role = Role(accessible, child);
            // The menu itself, its bar or an item of it: VS Code focuses its popup menu as well as the items.
            var menu = kind == MenuPopupStart || role is MenuItemRole or MenuPopupRole or MenuBarRole;
            if (kind == ObjectFocus)
            {
                focusSeenAt = Environment.TickCount64;
                focusSettling.Stop();
                if (!menu)
                {
                    Int32Rect? field = Place(accessible, child) is { Width: > 0, Height: > 0 } place ? place : null;
                    Tell(() => Field = field);
                }

                var editable = role == TextRole && (accessible.get_accState(child) is not int state || (state & ReadOnlyState) == 0);
                // The role only, never its name: the log names nothing on the screen (a window's title, an item's text).
                Log.Detail($"Focus: {RoleName(role)} in {ProcessOf(window)}{(editable ? ": writing" : menu ? ": a menu" : "")}");
                if (editable)
                {
                    TellTextFocused(AppOf(window));
                }
                else if (!menu)
                {
                    // Not text: wait a moment for a caret to show there.
                    focusSettling.Start();
                }
            }

            if (menu)
            {
                if ((kind == MenuPopupStart || role == MenuPopupRole ? Place(accessible, child) : MenuOf(accessible)) is { Height: > 0 } shown)
                {
                    if (!inMenu)
                    {
                        Log.Detail($"Menu: open in {ProcessOf(window)} at {shown}");
                    }

                    menuSettling.Stop();
                    inMenu = true;
                    Tell(() => MenuOpened?.Invoke(shown));
                }
            }
            else
            {
                EndMenu();
            }
        }
        catch (Exception gone) when (gone is COMException or ArgumentException or InvalidCastException)
        {
            // The object went with its window before it could be read.
        }
    }

    /// <summary>Text focused, told to the keyboard; its kind as given, else as UI Automation tells what has the focus now (<see cref="FocusedFieldKind"/>).</summary>
    private void TellTextFocused(string? app, StandardField? kind = null)
    {
        var told = kind ?? FocusedFieldKind();
        Tell(() =>
        {
            App = app;
            FieldKind = told;
            TextFocused?.Invoke();
        });
    }

    /// <summary>
    /// The kind of the text field focused (<see cref="StandardField"/>): the keyboard's own settings tell it of their
    /// boxes to try each kind's keys in, by their UI Automation id (<see cref="FieldIds"/>), and the browsers' address
    /// bars are known by theirs (<see cref="KindOf"/>); Windows tells it of no other field, which is text.
    /// </summary>
    public StandardField FieldKind { get; private set; }

    /// <summary>
    /// A field's kind by its UI Automation id and class: the settings' boxes (<see cref="FieldIds"/>), and a browser's
    /// address bar a web address's: Chrome's and Edge's (Chromium's OmniboxViewViews), Firefox's (urlbar-input).
    /// </summary>
    internal static StandardField KindOf(string? automationId, string? className) =>
        automationId is not null && FieldIds.TryGetValue(automationId, out var kind) ? kind
        : className == "OmniboxViewViews" || automationId == "urlbar-input" ? StandardField.Web
        : StandardField.Text;

    /// <summary>The settings' boxes' UI Automation ids, each a kind of field.</summary>
    public const string NumberFieldId = "Berpiztu.Field.Number";
    public const string EmailFieldId = "Berpiztu.Field.Email";
    public const string WebFieldId = "Berpiztu.Field.Web";

    public static readonly IReadOnlyDictionary<string, StandardField> FieldIds = new Dictionary<string, StandardField>
    {
        [NumberFieldId] = StandardField.Number,
        [EmailFieldId] = StandardField.Email,
        [WebFieldId] = StandardField.Web,
    };

    private static StandardField FocusedFieldKind()
    {
        try
        {
            var focused = System.Windows.Automation.AutomationElement.FocusedElement?.Current;
            return focused is { } now ? KindOf(now.AutomationId, now.ClassName) : StandardField.Text;
        }
        catch (Exception refused) when (refused is ArgumentException or InvalidOperationException or COMException or System.Windows.Automation.ElementNotAvailableException)
        {
            return StandardField.Text;
        }
    }

    /// <summary>Windows already asked for their accessibility, so it is asked once a window.</summary>
    private readonly HashSet<IntPtr> awakened = [];

    /// <summary>
    /// A window come to the front asked for its accessibility once, as a
    /// screen reader or the Magnifier asks: Chromium's apps (VS Code, Edge,
    /// Chrome) tell their focus, carets and menus only once something has
    /// asked, and their menus are what a keyboard moves out of the way of.
    /// </summary>
    private void Awaken(IntPtr window)
    {
        if (window == IntPtr.Zero || !awakened.Add(window))
        {
            return;
        }

        var asked = false;
        foreach (var part in ChromiumContent(window).Prepend(window))
        {
            var id = AccessibleId;
            asked |= AccessibleObjectFromWindow(part, ClientObject, ref id, out var accessible) == 0 && accessible is not null;
        }

        // UI Automation's ask as well, Narrator's: Electron (VS Code) turns its accessibility on for a UI Automation client.
        try
        {
            asked |= System.Windows.Automation.AutomationElement.FromHandle(window) is not null;
        }
        catch (Exception refused) when (refused is ArgumentException or InvalidOperationException or COMException or System.Windows.Automation.ElementNotAvailableException)
        {
            // The window went, or answers no UI Automation: the accessibility ask above stands.
        }

        if (asked)
        {
            Log.Detail($"Focus: {ProcessOf(window)} asked for its accessibility");
        }
    }

    /// <summary>The windows Chromium draws a page in, inside an app's window (VS Code's, Edge's): those a screen reader asks for the page's accessibility.</summary>
    private static List<IntPtr> ChromiumContent(IntPtr window)
    {
        var found = new List<IntPtr>();
        EnumChildWindows(window, (child, _) =>
        {
            var name = new StringBuilder(64);
            if (GetClassName(child, name, name.Capacity) > 0 && name.ToString() == "Chrome_RenderWidgetHostHWND")
            {
                found.Add(child);
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool ChildProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, ChildProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int size);

    /// <summary>A menu ended, or the focus left it: closed unless another menu comes at once.</summary>
    private void EndMenu()
    {
        if (inMenu && !menuSettling.IsEnabled)
        {
            menuSettling.Start();
        }
    }

    /// <summary>Whether the window in front still shows a caret: Windows' own (GetGUIThreadInfo) or the one it shows to accessibility, visible and with a height.</summary>
    private static bool CaretInForeground()
    {
        var front = GetForegroundWindow();
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        if (front == IntPtr.Zero || !GetGUIThreadInfo(GetWindowThreadProcessId(front, out _), ref info))
        {
            return false;
        }

        if (info.Caret != IntPtr.Zero && info.CaretBottom > info.CaretTop)
        {
            return true;
        }

        var id = AccessibleId;
        if (AccessibleObjectFromWindow(info.Focus != IntPtr.Zero ? info.Focus : front, CaretObject, ref id, out var caret) != 0 || caret is null)
        {
            return false;
        }

        try
        {
            caret.accLocation(out _, out _, out _, out var height, 0);
            return height > 0 && (caret.get_accState(0) is not int state || (state & InvisibleState) == 0);
        }
        catch (Exception gone) when (gone is COMException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The menu a focused item is in: the nearest popup menu above it; the item's own place where none is found.</summary>
    private static Int32Rect? MenuOf(IAccessible item)
    {
        var at = item;
        for (var up = 0; up < MostParents && at.accParent is IAccessible parent; up++, at = parent)
        {
            if (Role(parent, 0) == MenuPopupRole)
            {
                return Place(parent, 0);
            }
        }

        return Place(item, 0);
    }

    private static int Role(IAccessible accessible, object child) => accessible.get_accRole(child) is int role ? role : 0;

    private static string RoleName(int role)
    {
        var text = new StringBuilder(64);
        return GetRoleText((uint)role, text, (uint)text.Capacity) > 0 ? text.ToString() : $"role {role}";
    }

    /// <summary>The application in front, before any focus is told: where a keyboard starting is about to type.</summary>
    public static string? FrontApp() => GetForegroundWindow() is var front && front != IntPtr.Zero ? AppOf(front) : null;

    /// <summary>A window's application by its program's name; none for the keyboard's own windows (the keyboard, its settings).</summary>
    private static string? AppOf(IntPtr window) => ProcessOf(window) is var name && name != Process.GetCurrentProcess().ProcessName ? name : null;

    private static string ProcessOf(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var id);
        return NameOf((int)id);
    }

    /// <summary>The window in front last that is not the keyboard's own, where it gives the front back when Windows activates it; none yet.</summary>
    public IntPtr LastFront => Interlocked.CompareExchange(ref lastFront, IntPtr.Zero, IntPtr.Zero);

    private IntPtr lastFront;

    /// <summary>Whether a window is the keyboard's own process's (the keyboard, its icon), not the settings', which are a program apart.</summary>
    private static bool OwnWindow(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var id);
        return id == Environment.ProcessId;
    }

    /// <summary>The process of the window in front, by its id; 0, none.</summary>
    private static int FrontProcess()
    {
        var front = GetForegroundWindow();
        if (front == IntPtr.Zero)
        {
            return 0;
        }

        GetWindowThreadProcessId(front, out var id);
        return (int)id;
    }

    private static string NameOf(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return process.ProcessName;
        }
        catch (Exception gone) when (gone is ArgumentException or InvalidOperationException)
        {
            // No such process, or it ended as it was asked.
            return $"process {id}";
        }
    }

    private static Int32Rect? Place(IAccessible accessible, object child)
    {
        accessible.accLocation(out var left, out var top, out var width, out var height, child);
        return new Int32Rect(left, top, width, height);
    }

    private delegate void WinEventProc(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public int CaretLeft;
        public int CaretTop;
        public int CaretRight;
        public int CaretBottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromEvent(IntPtr window, int objectId, int childId, [MarshalAs(UnmanagedType.Interface)] out IAccessible? accessible, [MarshalAs(UnmanagedType.Struct)] out object child);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr window, int objectId, ref Guid id, [MarshalAs(UnmanagedType.Interface)] out IAccessible? accessible);

    [DllImport("oleacc.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetRoleText(uint role, StringBuilder text, uint size);
}
