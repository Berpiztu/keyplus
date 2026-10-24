using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
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

    private readonly WinEventProc seen;
    private readonly IntPtr[] hooks;
    private readonly System.Windows.Threading.DispatcherTimer focusSettling = new() { Interval = CaretAfterFocus };
    private readonly System.Windows.Threading.DispatcherTimer menuSettling = new() { Interval = MenuSettling };
    private bool inMenu;

    public FocusWatch()
    {
        seen = Seen;
        hooks = [Hook(ForegroundChanged, ForegroundChanged), Hook(MenuPopupStart, MenuPopupEnd), Hook(ObjectShow, ObjectShow), Hook(ObjectFocus, ObjectFocus), Hook(ObjectLocationChange, ObjectLocationChange)];

        // The window in front already, asked as those that come to the front later are.
        Awaken(GetForegroundWindow());
        focusSettling.Tick += (_, _) =>
        {
            focusSettling.Stop();
            if (CaretInForeground())
            {
                Log.Write("Focus: no caret came, but the window still shows one: still writing");
                return;
            }

            Log.Write("Focus: left the text");
            TextLeft?.Invoke();
        };
        menuSettling.Tick += (_, _) =>
        {
            menuSettling.Stop();
            if (inMenu)
            {
                inMenu = false;
                Log.Write("Menu: closed");
                MenuClosed?.Invoke();
            }
        };
    }

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

    public void Dispose()
    {
        focusSettling.Stop();
        menuSettling.Stop();
        foreach (var hook in hooks)
        {
            UnhookWinEvent(hook);
        }
    }

    private IntPtr Hook(uint first, uint last) => SetWinEventHook(first, last, IntPtr.Zero, seen, 0, 0, OutOfContext);

    private void Seen(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (kind == ForegroundChanged)
        {
            Awaken(window);
            return;
        }

        if (kind is MenuPopupStart or MenuPopupEnd)
        {
            Log.Write($"Menu: {(kind == MenuPopupStart ? "a popup started" : "a popup ended")} in {ProcessOf(window)}, object {objectId}");
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
                Log.Write($"Focus: {(kind == ObjectFocus ? "a focus" : "a popup")} in {ProcessOf(window)} could not be read");
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
                    Log.Write($"Focus: a caret showed in {ProcessOf(window)}: writing");
                    App = AppOf(window);
                    TextFocused?.Invoke();
                }

                if (Place(accessible, child) is { Height: > 0 } at)
                {
                    CaretMoved?.Invoke(at);
                }

                return;
            }

            var role = Role(accessible, child);
            // The menu itself, its bar or an item of it: VS Code focuses its popup menu as well as the items.
            var menu = kind == MenuPopupStart || role is MenuItemRole or MenuPopupRole or MenuBarRole;
            if (kind == ObjectFocus)
            {
                focusSettling.Stop();
                if (!menu)
                {
                    Field = Place(accessible, child) is { Width: > 0, Height: > 0 } place ? place : null;
                }

                var editable = role == TextRole && (accessible.get_accState(child) is not int state || (state & ReadOnlyState) == 0);
                Log.Write($"Focus: {RoleName(role)} \"{Named(accessible, child)}\" in {ProcessOf(window)}{(editable ? ": writing" : menu ? ": a menu" : "")}");
                if (editable)
                {
                    App = AppOf(window);
                    TextFocused?.Invoke();
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
                        Log.Write($"Menu: open in {ProcessOf(window)} at {shown}");
                    }

                    menuSettling.Stop();
                    inMenu = true;
                    MenuOpened?.Invoke(shown);
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

    /// <summary>A menu ended, or the focus left it: closed unless another menu comes at once.</summary>
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
            Log.Write($"Focus: {ProcessOf(window)} asked for its accessibility");
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

    private static string Named(IAccessible accessible, object child)
    {
        try
        {
            var name = accessible.get_accName(child) ?? "";
            return name.Length > 40 ? name[..40] + "…" : name;
        }
        catch (Exception gone) when (gone is COMException or ArgumentException)
        {
            return "";
        }
    }

    /// <summary>The application in front, before any focus is told: where a keyboard starting is about to type.</summary>
    public static string? FrontApp() => GetForegroundWindow() is var front && front != IntPtr.Zero ? AppOf(front) : null;

    /// <summary>A window's application by its program's name; none for the keyboard's own windows (the keyboard, its settings).</summary>
    private static string? AppOf(IntPtr window) => ProcessOf(window) is var name && name != Process.GetCurrentProcess().ProcessName ? name : null;

    private static string ProcessOf(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var id);
        try
        {
            using var process = Process.GetProcessById((int)id);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
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
