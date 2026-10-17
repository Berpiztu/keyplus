# KeyPlus

The keys a phone's keyboard lacks (Esc, Tab, Ctrl, Alt, Shift, the arrows,
Home, End, Page up and Page down) in a strip right above whatever keyboard is
open, sent to the field being typed in: a terminal, an SSH app, or a web
editor such as vscode.dev or code-server in the browser. It works beside the
keyboard you already use; it does not replace it.

## How it works

Android allows one keyboard at a time, so KeyPlus is not a keyboard but an
**accessibility service**. Since Android 13 such a service can get its own
input connection to the focused editor (`flagInputMethodEditor`) and send key
events through it; a browser turns them into the page's `keydown` events.
The strip is an accessibility overlay that never takes the focus, so the
keyboard stays open while you press its keys. The service watches where the
keyboard's window is, on whichever display (Samsung DeX is a display of its
own), and sits right above it, as wide as the keyboard, so it follows a
floating keyboard too; with no keyboard open it is out of sight.

| File | What it holds |
| --- | --- |
| [PanelKeys.cs](PanelKeys.cs) | The layout (the editing shortcuts, then Termux's two rows of seven) and the meta state of held modifiers |
| [KeyPanel.cs](KeyPanel.cs) | The strip's views: holding Ctrl, Alt and Shift, repeat, its controls (move, resize, fold) |
| [PanelPlacement.cs](PanelPlacement.cs) | Where the strip stands, docked or floating, and its size, kept across restarts |
| [KeyPlusService.cs](KeyPlusService.cs) | The service: placing the strip above the keyboard, sending the keys, turning a letter typed under Ctrl or Alt into the key |
| [SelectionInputMethod.cs](SelectionInputMethod.cs) | The service's input method, which says when the editor's caret moves |
| [MainActivity.cs](MainActivity.cs) | The app's one screen: what it does, whether it is on, the way to Settings |

## Using it

- **Ctrl+C, Ctrl+V, Ctrl+Z and Ctrl+Y** have keys of their own, in the top
  row, beside **Del** (delete forward); Ctrl+Z, Ctrl+Y and Del repeat while
  held.
- **Ctrl and Alt** hold for the next key: pressed once they stay filled, and
  are let go after the next key; pressed again they let go at once. The next
  key can be one of the strip's or a character typed on your own keyboard:
  Ctrl then "s" is Ctrl+S, Ctrl then "P" is Ctrl+Shift+P. Your keyboard types
  text, not keys, so KeyPlus sees the character arrive in the editor, takes
  it back with Backspace and sends the key with Ctrl or Alt instead.
- **Shift** stays on until pressed again, so Shift and the arrows select.
  Every modifier goes out as a hardware keyboard sends it, pressed before the
  key and let go after it, since some editors select only when Shift itself
  went down.
- **The arrows and pages** repeat while held.
- **Docked or floating.** The strip starts docked: right above the keyboard,
  as wide as it, in sight only while a keyboard is open. Drag **✥** and it
  floats where you leave it, in sight all the time, keyboard or not, for the
  places that lack only the arrows. Drag **⇲** to resize it: across for its
  width, down for the size of its keys. A tap on **✥** docks it again. Where
  it stands and its size are kept.
- **▾** folds the strip into a small tab; the tab unfolds it.

## Build and install

The rules for working on KeyPlus are in [rules.md](rules.md). Each
`build-apk.ps1` raises the build number in [version.props](version.props),
so every APK installs over the one before; commit it with the build.

Needs the .NET 10 SDK with the `android` workload
(`dotnet workload install android`) and a phone with Android 13 or later.

```powershell
dotnet build                 # Debug build
.\build-apk.ps1              # Release APK, copied to dist\KeyPlus-1.0.0.<build>.apk
```

Then on the phone:

1. Copy `dist\KeyPlus-1.0.0.<build>.apk` to it and open it to install it
   (Android asks to allow installing from the app that opens it).
2. Open **KeyPlus** and choose **Open accessibility settings**.
3. Turn on **KeyPlus keys**.
4. If Android says the setting is restricted (Android 13 and later do so for
   apps not installed from a store), choose **Open app info** in KeyPlus,
   then **⋮ → Allow restricted settings**, and turn it on again.

## Limits

- Android 13 or later: before it, only a keyboard or root can send keys to
  another app.
- An app that reads only text, not key events, ignores Esc and the arrows.
  Browsers, terminals and SSH apps read them.
- A character typed under Ctrl or Alt reaches the editor before KeyPlus takes
  it back: in a terminal the program sees it and then a Backspace. And an
  editor that does not report its caret (Termux's terminal, for one) keeps
  the character as typed; use the strip's keys there, or Termux's own.
  With text selected, the character typed replaces the selection before it
  is taken back: that is why copying has a key of its own.
- The browser may take a few shortcuts (Ctrl+T, Ctrl+W) before the page does.
- Google Play reviews accessibility services closely; installed from the
  APK, nothing of that applies.
