using System.Runtime.InteropServices;
using System.Windows;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The focus as UI Automation tells it, Narrator's way: WinUI apps (Task
/// Manager, Settings, the new Notepad) tell their focus inside the window
/// only so, never as the WinEvents <see cref="FocusWatch"/> hooks, which see
/// only their window's client. Windows' own UI Automation (COM), for .NET's
/// System.Windows.Automation sees nothing inside those windows. Its events
/// come on UI Automation's own threads, where what is focused is read at once
/// and told as plain values, no COM object leaving them.
/// </summary>
internal sealed class AutomationFocus : IDisposable
{
    // UIAutomationClient.h (Windows SDK 10.0.26100.0).
    private const int ProcessIdProperty = 30002;
    private const int AutomationIdProperty = 30011;
    private const int ClassNameProperty = 30012;
    private const int ControlTypeProperty = 30003;
    private const int BoundingRectangleProperty = 30001;
    private const int IsValuePatternAvailableProperty = 30043;
    private const int ValueIsReadOnlyProperty = 30046;
    private const int EditControl = 50004;
    private const int DocumentControl = 50030;
    private const int MenuControl = 50009;
    private const int MenuBarControl = 50010;
    private const int MenuItemControl = 50011;

    private static readonly Guid AutomationClass = new("ff48dba4-60ef-4201-aa87-54103eef594e");

    /// <summary>What has the focus: its process, whether text is typed in it or it is a menu, its kind for the log, its place in pixels, and its UI Automation id and class, which tell a browser's address bar and a terminal (<see cref="FocusWatch.FieldOf"/>).</summary>
    public readonly record struct Focused(int Process, bool Editable, bool Menu, string Kind, Int32Rect? Place, string? Id, string? Class);

    private readonly IUIAutomation automation;
    private readonly Handler handler;

    private AutomationFocus(IUIAutomation automation, Handler handler)
    {
        this.automation = automation;
        this.handler = handler;
    }

    /// <summary>The focus followed, each change told on UI Automation's thread; none where Windows' UI Automation cannot be had, said in the log.</summary>
    public static AutomationFocus? Start(Action<Focused> told)
    {
        try
        {
            var automation = (IUIAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(AutomationClass, throwOnError: true)!)!;
            var handler = new Handler(told);
            automation.AddFocusChangedEventHandler(IntPtr.Zero, handler);
            return new AutomationFocus(automation, handler);
        }
        catch (Exception refused) when (refused is COMException or InvalidCastException or TypeLoadException)
        {
            Log.Write($"Focus: UI Automation's focus could not be followed, so WinUI apps' fields are not seen: {refused.Message}");
            return null;
        }
    }

    /// <summary>The handler let go away from the caller: UI Automation waits for events being told to end.</summary>
    public void Dispose()
    {
        handler.Stopped = true;
        Task.Run(() =>
        {
            try
            {
                automation.RemoveFocusChangedEventHandler(handler);
            }
            catch (COMException)
            {
                // UI Automation is gone with the session: nothing is left to let go.
            }
        });
    }

    private sealed class Handler(Action<Focused> told) : IUIAutomationFocusChangedEventHandler
    {
        public volatile bool Stopped;

        public void HandleFocusChangedEvent(IUIAutomationElement sender)
        {
            if (Stopped)
            {
                return;
            }

            try
            {
                told(Read(sender));
            }
            catch (Exception gone) when (gone is COMException or InvalidCastException or ArgumentException)
            {
                // The element went with its window before it could be read.
            }
            catch (Exception failed)
            {
                // UI Automation's call: nothing thrown may go back to it.
                Log.Write($"Focus: a UI Automation focus could not be followed: {failed}");
            }
        }
    }

    /// <summary>
    /// What is focused: text is typed in an edit not read-only, and in a document only where it says it takes a value
    /// that is not read-only (a page in a browser, read, is a document too).
    /// </summary>
    private static Focused Read(IUIAutomationElement element)
    {
        var type = element.GetCurrentPropertyValue(ControlTypeProperty) is int control ? control : 0;
        var takesValue = element.GetCurrentPropertyValue(IsValuePatternAvailableProperty) is true;
        var readOnly = takesValue && element.GetCurrentPropertyValue(ValueIsReadOnlyProperty) is true;
        var editable = !readOnly && (type == EditControl || (type == DocumentControl && takesValue));
        var place = element.GetCurrentPropertyValue(BoundingRectangleProperty) is double[] { Length: 4 } box && box[2] > 0 && box[3] > 0
            ? new Int32Rect((int)box[0], (int)box[1], (int)box[2], (int)box[3])
            : (Int32Rect?)null;
        var kind = type switch
        {
            EditControl => "edit",
            DocumentControl => "document",
            _ => $"control {type}",
        };
        return new Focused(
            element.GetCurrentPropertyValue(ProcessIdProperty) is int process ? process : 0,
            editable,
            type is MenuControl or MenuBarControl or MenuItemControl,
            kind,
            place,
            element.GetCurrentPropertyValue(AutomationIdProperty) as string,
            element.GetCurrentPropertyValue(ClassNameProperty) as string);
    }

    // Windows' UI Automation interfaces, as UIAutomationClient.h orders their methods: those not called keep their
    // place in the table, as methods without parameters.
    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void CompareElements();
        void CompareRuntimeIds();
        void GetRootElement();
        void ElementFromHandle();
        void ElementFromPoint();
        void GetFocusedElement();
        void GetRootElementBuildCache();
        void ElementFromHandleBuildCache();
        void ElementFromPointBuildCache();
        void GetFocusedElementBuildCache();
        void CreateTreeWalker();
        void GetControlViewWalker();
        void GetContentViewWalker();
        void GetRawViewWalker();
        void GetRawViewCondition();
        void GetControlViewCondition();
        void GetContentViewCondition();
        void CreateCacheRequest();
        void CreateTrueCondition();
        void CreateFalseCondition();
        void CreatePropertyCondition();
        void CreatePropertyConditionEx();
        void CreateAndCondition();
        void CreateAndConditionFromArray();
        void CreateAndConditionFromNativeArray();
        void CreateOrCondition();
        void CreateOrConditionFromArray();
        void CreateOrConditionFromNativeArray();
        void CreateNotCondition();
        void AddAutomationEventHandler();
        void RemoveAutomationEventHandler();
        void AddPropertyChangedEventHandlerNativeArray();
        void AddPropertyChangedEventHandler();
        void RemovePropertyChangedEventHandler();
        void AddStructureChangedEventHandler();
        void RemoveStructureChangedEventHandler();

        void AddFocusChangedEventHandler(IntPtr cacheRequest, IUIAutomationFocusChangedEventHandler handler);

        void RemoveFocusChangedEventHandler(IUIAutomationFocusChangedEventHandler handler);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUIAutomationElement
    {
        void SetFocus();
        void GetRuntimeId();
        void FindFirst();
        void FindAll();
        void FindFirstBuildCache();
        void FindAllBuildCache();
        void BuildUpdatedCache();

        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCurrentPropertyValue(int propertyId);
    }

    [ComImport, Guid("c270f6b5-5c69-4290-9745-7a7f97169468"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUIAutomationFocusChangedEventHandler
    {
        void HandleFocusChangedEvent(IUIAutomationElement sender);
    }
}
