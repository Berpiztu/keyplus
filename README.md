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
| [PanelKeys.cs](PanelKeys.cs) | The layouts (the editing shortcuts over Termux's two rows; the whole Spanish keyboard), the dead keys' accents, the meta state of held modifiers |
| [KeyPanel.cs](KeyPanel.cs) | The window's views: title bar, keys, corner grips and icon; holding Ctrl, Alt and Shift, repeat |
| [PanelPlacement.cs](PanelPlacement.cs) | Where the strip stands, docked or floating, and its size, kept across restarts |
| [KeyPlusService.cs](KeyPlusService.cs) | The service: placing the strip above the keyboard, sending the keys, turning a letter typed under Ctrl or Alt into the key |
| [ScanCodes.cs](ScanCodes.cs) | The scan code a hardware keyboard sends with each key, which remote desktop apps go by |
| [SelectionInputMethod.cs](SelectionInputMethod.cs) | The service's input method, which says when the editor's caret moves |
| [MainActivity.cs](MainActivity.cs) | The app's one screen: what it does, whether it is on, the way to Settings |

## Using it

- **Ctrl+A, Ctrl+C, Ctrl+V, Ctrl+Z and Ctrl+Y** have keys of their own, in
  the top row, beside **Del** (delete forward); **⌫** (Backspace) and **⏎**
  (Enter) end the terminal's two rows. Ctrl+Z, Ctrl+Y, Del and ⌫ repeat
  while held.
- **ABC** in the title bar turns the window into a whole keyboard laid out
  as Windows' touch keyboard lays out Spain's (ISO, es-ES), each key labelled
  as there: ñ, ç, ¡ ¿ º ª, Alt Gr for \ | @ # ~ € ¬ [ ] { }, Bloq May, ´ ` ^ ¨
  as dead keys (´ then a is á, ¨ then u is ü), Entrar as tall as two rows,
  the arrows as an inverted T at the bottom right, and ImpPnt
  (Windows+Shift+S, the snipping tool, since Print Screen does not reach a
  remote desktop), Inicio, Fin, RePág and AvPág in a column at the right,
  keys as wide as the others, ImpPnt beside ⌫. Rows keep their height whatever their keys read. **Fn** stays on until
  pressed again: it makes 1 to ¡ F1 to F12 and the arrows Inicio, Fin, RePág
  and AvPág. **⊞** (Windows) holds for the next key, as Ctrl does (⊞ then E
  is Windows+E); pressed twice it is Windows alone, the Start menu. The keyboard types its characters as text, so it types what it
  shows whatever layout the editor expects; with Ctrl or Alt held its keys
  go out as keys (Ctrl+S). **Keys** in the title bar turns it back into the
  strip.
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
- **Long press to move or resize.** Hold a key, or the ground between the
  keys, still for a moment: it buzzes, and the window follows your finger;
  started near one of its corners, it resizes from there instead. Keys type
  on release, so a held key types nothing. The keys that repeat while held
  (the arrows, ⌫, Del, the pages, Ctrl+Z, Ctrl+Y) act on the touch and
  repeat instead.
- **A window, docked or floating.** The keys are a small window with a title
  bar. It starts docked: right above the keyboard, as wide as it, while a
  keyboard is open, and as the **⌨** icon at the bottom right while none is.
  Drag the title bar and it floats where you
  leave it, in sight all the time, keyboard or not, for the places that lack
  only the arrows. Drag a corner (◤ ◥ ◣ ◢) to resize it: across for its
  width, up or down for the size of its keys; the opposite corner stays put.
  **⤓** in the title bar docks it again. Where it stands and its size are
  kept.
- **A−** and **A+** in the title bar make the labels of the keys that type
  characters (letters, digits, symbols) smaller or larger, a tenth at a
  press, and the size is kept; the keys keep theirs. Ctrl, Alt, Enter and
  the like stay as they are. **A↺** brings them, and the keys a corner made larger or smaller,
  back to their usual size; the window keeps its place and width. Each key shows its
  shifted character above its plain one, both in the middle, and its Alt Gr
  one small in its bottom right corner, as Windows' touch keyboard does; a
  key with all three has them all closer to its left, by half the room
  left of the two.
  Characters that are small glyphs are drawn larger, on lines that keep
  their height: " · = ? ¿ ' ` ^ + * ´ ¨ ; , . : - _ \ | ~ ¬ twice as large,
  [ ] { } and the arrows half as large again, @ a quarter. A label is never larger than its key: past
  that, a corner makes the keys, and so the labels, larger.
- **Phone and DeX apart.** The window's size, place, labels' size, Keys or
  ABC, docked or floating and minimized are kept apart for the phone's own
  screen and for Samsung DeX, so each keeps what you made for it.
- **–** minimizes the window into the **⌨** icon: a tap opens it again, a
  drag moves it. The icon is always in sight, and always where you last left
  it, apart from the window's own place.
- **✕** closes KeyPlus, once you say yes to "¿Quieres cerrar KeyPlus?" in the
  title bar: nothing of it is in sight. Opening the KeyPlus app brings it
  back. The service stays on meanwhile, since only Settings can turn an
  accessibility service on again; turn it off there to stop it altogether.

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
- Keys go out as a Spanish hardware keyboard sends them: each with its scan
  code, and each character key at its place on the keyboard (ç where a US
  keyboard has the backslash). A remote desktop app presses that same key on
  the remote machine, so Ctrl+ç there is Ctrl+ç when the remote machine's
  layout is Spanish; with another layout it is whatever that key is there.
- Google Play reviews accessibility services closely; installed from the
  APK, nothing of that applies.
