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
    private const int ValueValueProperty = 30045;
    private const int LocalizedControlTypeProperty = 30004;
    private const int NameProperty = 30005;
    private const int IsPasswordProperty = 30019;
    private const int FrameworkIdProperty = 30024;
    private const int IsTextPatternAvailableProperty = 30040;
    private const int IsTextPattern2AvailableProperty = 30119;
    private const int AriaRoleProperty = 30101;
    private const int AriaPropertiesProperty = 30102;
    private const int TextPatternId = 10014;
    private const int TextPattern2Id = 10024;
    private const int StartEndpoint = 0;
    private const int EndEndpoint = 1;
    private const int CharacterUnit = 0;

    private static readonly Guid AutomationClass = new("ff48dba4-60ef-4201-aa87-54103eef594e");

    /// <summary>What has the focus: its process, whether text is typed in it or it is a menu, its kind for the log, its place in pixels, its UI Automation id and class, which tell a browser's address bar and a terminal (<see cref="FocusWatch.FieldOf"/>), and, with the detailed log on, what else it says it is, to learn which fields are documents. Document: text typed in it is a document's, which Autocorrect may correct by itself: a document, or a field of many lines (a message, a mail); a field of one line (an address, a search, a form's) or a combo box is not.</summary>
    public readonly record struct Focused(int Process, bool Editable, bool Menu, string Kind, Int32Rect? Place, string? Id, string? Class, bool Document, string Traits);

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

    /// <summary>The text before the caret in what has the focus, and how it was read; none where it could not be, with why.</summary>
    public readonly record struct FieldText(string? Before, string How);

    /// <summary>
    /// The text before the caret in what has the focus, at most
    /// <paramref name="most"/> characters: by the caret's range (TextPattern2),
    /// else by the selection's start (TextPattern); where neither is there,
    /// what the field offers instead, for the log. On a multithreaded
    /// apartment's thread, never the keyboard's: a slow application answers late.
    /// </summary>
    public FieldText ReadField(int most)
    {
        try
        {
            var element = automation.GetFocusedElement();
            if (element.GetCurrentPattern(TextPattern2Id) is IUIAutomationTextPattern2 withCaret)
            {
                return new FieldText(Before(withCaret.GetCaretRange(out _), most), "caret");
            }

            if (element.GetCurrentPattern(TextPatternId) is IUIAutomationTextPattern text)
            {
                var selection = text.GetSelection();
                return selection.Length() > 0
                    ? new FieldText(Before(selection.GetElement(0), most), "selection")
                    : new FieldText(null, "a text pattern with no selection");
            }

            return element.GetCurrentPropertyValue(ValueValueProperty) is string value
                ? new FieldText(null, $"no text pattern, only its value ({value.Length} characters), with no caret")
                : new FieldText(null, "no text pattern and no value");
        }
        catch (Exception failed) when (failed is COMException or InvalidCastException or ArgumentException)
        {
            return new FieldText(null, $"UI Automation failed: {failed.Message}");
        }
    }

    /// <summary>What comes before a range's start, at most <paramref name="most"/> characters.</summary>
    private static string Before(IUIAutomationTextRange range, int most)
    {
        var before = range.Clone();
        before.MoveEndpointByRange(EndEndpoint, before, StartEndpoint);
        before.MoveEndpointByUnit(StartEndpoint, CharacterUnit, -most);
        return before.GetText(-1) ?? "";
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
            element.GetCurrentPropertyValue(ClassNameProperty) as string,
            editable && (type == DocumentControl || (element.GetCurrentPropertyValue(AriaPropertiesProperty) as string)?.Contains("multiline=true", StringComparison.Ordinal) == true),
            editable && Log.Detailed ? Traits(element) : "");
    }

    /// <summary>What a field says it is, for the log: its type as named, its web role and properties (a search box, a single line), whether it is a password, its framework, class and name, and whether its text can be read.</summary>
    private static string Traits(IUIAutomationElement element)
    {
        string Text(int property) => element.GetCurrentPropertyValue(property) is string { Length: > 0 } text
            ? (text.Length > 60 ? text[..60] + "…" : text).ReplaceLineEndings(" ")
            : "-";
        return $"type {Text(LocalizedControlTypeProperty)}, role {Text(AriaRoleProperty)}, aria {Text(AriaPropertiesProperty)}, "
            + $"password {element.GetCurrentPropertyValue(IsPasswordProperty) is true}, framework {Text(FrameworkIdProperty)}, "
            + $"class {Text(ClassNameProperty)}, name {Text(NameProperty)}, text {element.GetCurrentPropertyValue(IsTextPatternAvailableProperty) is true}, "
            + $"caret {element.GetCurrentPropertyValue(IsTextPattern2AvailableProperty) is true}";
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

        IUIAutomationElement GetFocusedElement();

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

        void GetCurrentPropertyValueEx();
        void GetCachedPropertyValue();
        void GetCachedPropertyValueEx();
        void GetCurrentPatternAs();
        void GetCachedPatternAs();

        /// <summary>The pattern's object; none where the element has not that pattern.</summary>
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object? GetCurrentPattern(int patternId);
    }

    [ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextPattern
    {
        void RangeFromPoint();
        void RangeFromChild();

        IUIAutomationTextRangeArray GetSelection();
    }

    /// <summary>IUIAutomationTextPattern's methods first, as it derives from it.</summary>
    [ComImport, Guid("506a921a-fcc9-409f-b23b-37eb74106872"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextPattern2
    {
        void RangeFromPoint();
        void RangeFromChild();
        void GetSelection();
        void GetVisibleRanges();
        void GetDocumentRange();
        void GetSupportedTextSelection();
        void RangeFromAnnotation();

        IUIAutomationTextRange GetCaretRange([MarshalAs(UnmanagedType.Bool)] out bool isActive);
    }

    [ComImport, Guid("ce4ae76a-e717-4c98-81ea-47371d028eb6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRangeArray
    {
        int Length();

        IUIAutomationTextRange GetElement(int index);
    }

    [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRange
    {
        IUIAutomationTextRange Clone();

        void Compare();
        void CompareEndpoints();
        void ExpandToEnclosingUnit();
        void FindAttribute();
        void FindText();
        void GetAttributeValue();
        void GetBoundingRectangles();
        void GetEnclosingElement();

        [return: MarshalAs(UnmanagedType.BStr)]
        string? GetText(int maxLength);

        void Move();

        int MoveEndpointByUnit(int endpoint, int unit, int count);

        void MoveEndpointByRange(int endpoint, IUIAutomationTextRange range, int targetEndpoint);
    }

    [ComImport, Guid("c270f6b5-5c69-4290-9745-7a7f97169468"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUIAutomationFocusChangedEventHandler
    {
        void HandleFocusChangedEvent(IUIAutomationElement sender);
    }
}
