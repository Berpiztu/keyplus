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
keyboard's window is and sits right above it; with no keyboard open it is
out of sight.

| File | What it holds |
| --- | --- |
| [PanelKeys.cs](PanelKeys.cs) | The layouts (Termux's two rows of seven, and the letters and digits) and the meta state of held modifiers |
| [KeyPanel.cs](KeyPanel.cs) | The strip's views: holding Ctrl, Alt and Shift, repeat, folding into a tab |
| [KeyPlusService.cs](KeyPlusService.cs) | The service: placing the strip above the keyboard, sending the keys |
| [MainActivity.cs](MainActivity.cs) | The app's one screen: what it does, whether it is on, the way to Settings |

## Using it

- **Ctrl and Alt** hold for the next key: pressed once they stay filled, and
  are let go after the next key; pressed again they let go at once. While one
  is held, letters and digits appear above the strip: Ctrl+C, Ctrl+S or
  Alt+F are pressed there, because the phone's keyboard types text, not keys,
  and a modifier held here cannot reach a letter typed there.
- **Shift** stays on until pressed again, so Shift and the arrows select.
- **The arrows and pages** repeat while held.
- **▾** folds the strip into a small tab at the right; the tab unfolds it.

## Build and install

Needs the .NET 10 SDK with the `android` workload
(`dotnet workload install android`) and a phone with Android 13 or later.

```powershell
dotnet build                 # Debug build
.\install.ps1                # Release build, installed on the phone over adb
```

Then on the phone:

1. Open **KeyPlus** and choose **Open accessibility settings**.
2. Turn on **KeyPlus keys**.
3. If Android says the setting is restricted (Android 13 and later do so for
   apps not installed from a store), choose **Open app info** in KeyPlus,
   then **⋮ → Allow restricted settings**, and turn it on again.

## Limits

- Android 13 or later: before it, only a keyboard or root can send keys to
  another app.
- An app that reads only text, not key events, ignores Esc and the arrows.
  Browsers, terminals and SSH apps read them.
- The browser may take a few shortcuts (Ctrl+T, Ctrl+W) before the page does.
- Google Play reviews accessibility services closely; installed from the
  APK, nothing of that applies.
