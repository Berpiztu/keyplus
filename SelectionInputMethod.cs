using Android.AccessibilityServices;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The service's input method, which says when the focused editor's selection
/// moves: the only sign an accessibility service gets of a character typed on
/// the phone's keyboard, which commits text and sends no key events.
/// </summary>
public sealed class SelectionInputMethod(AccessibilityService service, Action<int, int> caretMoved)
    : Android.AccessibilityServices.InputMethod(service)
{
    public override void OnUpdateSelection(int oldSelStart, int oldSelEnd, int newSelStart, int newSelEnd, int candidatesStart, int candidatesEnd)
    {
        base.OnUpdateSelection(oldSelStart, oldSelEnd, newSelStart, newSelEnd, candidatesStart, candidatesEnd);
        if (newSelStart == newSelEnd)
        {
            caretMoved(Math.Min(oldSelStart, oldSelEnd), newSelStart);
        }
    }
}
