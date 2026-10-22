# KeyPlus

The keys a phone's keyboard lacks (Esc, Tab, Ctrl, Alt, Shift, the arrows,
Home, End, Page up and Page down), sent to the field being typed in: a
terminal, an SSH app, or a web editor such as vscode.dev or code-server in
the browser. Two products, two apps built from the same code, which can be
on one phone together:

- **KeyPlus** (the popup, `ai.berpiztu.keyplus`): the keys in a strip right
  above whatever keyboard is open. It works beside the keyboard you already
  use; it does not replace it.
- **Berpiztu AI Keyboard** (`ai.berpiztu.keyboard`): the same keys and
  title bar as a keyboard of its own, for any Android, and the one for a
  headset (Galaxy XR).

## How it works

Android allows one keyboard at a time, so the popup is not a keyboard but an
**accessibility service**. Since Android 13 such a service can get its own
input connection to the focused editor (`flagInputMethodEditor`) and send key
events through it; a browser turns them into the page's `keydown` events.
The strip is an accessibility overlay that never takes the focus, so the
keyboard stays open while you press its keys. The service watches where the
keyboard's window is, on whichever display (Samsung DeX is a display of its
own), and sits right above it, as wide as the keyboard, so it follows a
floating keyboard too; with no keyboard open it is out of sight.

On a headset (Galaxy XR, Android XR) the popup cannot follow the keyboard:
the headset keeps an accessibility overlay before the user's eyes, and
places the keyboard where the user moves it without Android knowing where.
There the **keyboard** is KeyPlus: the headset places it and lets the user
move it. The headset gives every keyboard the same box (1166 × 656 pixels on
the Galaxy XR): the keyboard fills it, the keys across its width and, under
the title bar, a row of what was copied and the rows of keys, all as tall;
there the whole keyboard has no column at its right, so its keys are wider
(Fn and the arrows are Inicio, Fin, RePág and AvPág), and no grips. On the
keyboard the title bar does not move it nor ⤓ dock it, for Android places
it; in ✕'s place it has **⚙**, its settings, whose first is **Cambiar de
teclado**, Android's keyboard picker.

On a phone the keyboard's title bar is Gboard's toolbar, one height and one
size of words whatever the keys' size: **⚙** at its left, **ABC** (or
**Keys**) at its right, and between them the chip of what was copied last,
for five minutes after it was copied, else **📋**; nothing is drawn over
anything else. A tap on the chip pastes it, a long press opens its menu
(Pegar, Fijar, Editar, Eliminar). Editar shows its card above the keyboard,
over the app, as Gboard does: one line that scrolls across, so neither the
card nor the keys change size with the text, and the keys type into it
until Cancelar or Guardar. A long press on the bar's buttons, its free room
or between the keys moves the keyboard; it has no corner grips. ⚙ turns the
bar into **✕** and **📋** and opens, in the keys' place and as Gboard's
rounded tiles, **Letra más pequeña**, **Letra más grande**, **Tamaño
normal**, **Dos filas** (in Keys), **Minimizar**, **Ajustes de la app**,
**Mover**, **Redimensionar**, **Diseñar**, **Modo normal**, **Modo
ventana** and **Cambiar de teclado** ([KeyboardFrame.cs](KeyboardFrame.cs)). Mover and Redimensionar lay a frame
over the keyboard until **Hecho**: moving, it follows a finger held anywhere
on it; resizing, the top and bottom bars size the keys and the side ones
their width, and **Restablecer** takes it back to its usual size. Modo
normal docks it at the bottom on a ground as wide as the screen, which the
app gives way to as to any keyboard: the keys as wide as it, or narrower,
for a finger to reach them, where Mover left them, across and up; Modo ventana makes it a window of its own over the app, which
keeps the whole screen and its touches outside the keyboard. Each screen
and mode keeps its own place and size.

The popup on a headset puts its keys in an app's window of its own
([KeyWindowActivity.cs](KeyWindowActivity.cs)), a panel the user places
beside the keyboard, which never takes the focus and stays where it is left.
Docked (**⤓** filled), once the user minimized or closed it, the service
brings it back, or opens it again, when the keyboard opens, and taps the
field typed in, so its app keeps the focus; ⤓ not filled, it leaves the
window alone. What was measured on the Galaxy XR: an accessibility overlay
moves with the head; an application overlay stays still but cannot be moved;
an app's window stays where the user leaves it but is never smaller than
220 × 220 dp, and a minimized window, any app's, comes back where the
headset chooses, not where it was; none of them can follow the keyboard,
whose place in space no app can read. Two trials stay in that window: **⌨**
hides the keyboard, brings it back and shows what Android tells of its
window; **⚙** attaches keys into the keyboard's or the focused app's window
(Android 14 lets an accessibility service do that), where they show and
move but take the focus when touched, for the public way to draw them makes
a window that does.

| File | What it holds |
| --- | --- |
| [PanelKeys.cs](PanelKeys.cs) | The layouts (the editing shortcuts over Termux's two rows; the whole Spanish keyboard), the dead keys' accents, the meta state of held modifiers |
| [KeyPanel.cs](KeyPanel.cs) | The window's views: title bar, keys, corner grips and icon; holding Ctrl, Alt and Shift, repeat; the whole keyboard as the screen's design has it, and designing it |
| [KeyboardDesign.cs](KeyboardDesign.cs) | A design of the whole keyboard: its rows, holes, keys aside and widths, kept for each screen |
| [PanelPlacement.cs](PanelPlacement.cs) | Where the strip stands, docked or floating, and its size, kept across restarts |
| [KeyPlusService.cs](KeyPlusService.cs) | The service: placing the strip above the keyboard, sending the keys, turning a letter typed under Ctrl or Alt into the key |
| [KeyPlusKeyboard.cs](KeyPlusKeyboard.cs) | The keyboard product: the same panel as a keyboard of its own, docked or in a window on a phone |
| [KeyboardFrame.cs](KeyboardFrame.cs) | The frame ⚙'s Mover and Redimensionar lay over the keyboard |
| [KeySender.cs](KeySender.cs) | Sending a key with its scan code and its modifiers pressed around it, for both products |
| [ClipboardHistory.cs](ClipboardHistory.cs) | What was copied lately, texts and images, kept to paste again |
| [ClipboardImages.cs](ClipboardImages.cs) | The keyboard's images handed to the app they are pasted into |
| [ScreenshotWatcher.cs](ScreenshotWatcher.cs) | Each new screenshot into the keyboard's copied list |
| [ScanCodes.cs](ScanCodes.cs) | The scan code a hardware keyboard sends with each key, which remote desktop apps go by |
| [SelectionInputMethod.cs](SelectionInputMethod.cs) | The service's input method, which says when the editor's caret moves |
| [MainActivity.cs](MainActivity.cs) | The app's one screen: what it does, whether it is on, the way to Settings |
| [AdbGrant.cs](AdbGrant.cs) | The app turned on over the device's own Wireless debugging: the pairing code taken in a notification, the ADB commands run, no computer |
| [AdbShell.java](AdbShell.java) | The ADB client it runs them with (libadb-android): the pairing port found, paired, connected, the commands run as the shell user |
| [StandardKeys.cs](StandardKeys.cs) | The standard keyboard's pages as Gboard lays out Spanish: letters with the number row and the corners' symbols, ?123, =\< |
| [StandardKeyboard.cs](StandardKeyboard.cs) | The standard keyboard, Gboard's copied: drawn in the device's own colours, typed, held, swiped |
| [SwipeTyping.cs](SwipeTyping.cs) | The keyboard's swipe typing: FUTO Swipe's encoder and the Spanish word list loaded once, a swipe read as words, accents put back |
| [SwipeDecoder.java](SwipeDecoder.java) | FUTO Swipe's Java side over its native library ([native/futo-swipe](native/futo-swipe)), GPL-3.0 |
| [SwipeTrail.cs](SwipeTrail.cs) | The line a swiping finger draws over the keys, thinning behind it |

## Using it

- **What was copied** is kept to paste again, the last 20. **📋** in the
  title bar shows it in the keys' place; a tap pastes it, **✕** takes it out
  of the list. Berpiztu AI Keyboard keeps whatever is copied in any app,
  images too (Chrome's "Copy image"), and, once its app is let read the
  photos (**Allow screenshots**), each screenshot taken from then on, for
  Android puts none on the clipboard; an image pastes into a field that
  takes images (Chrome, Telegram), and a field that does not says so. The
  popup keeps what its own Ctrl+C and Ctrl+X copy, texts alone: Android
  keeps the clipboard from an accessibility service. On a headset the
  keyboard shows what was copied in a row of its own above the keys, every
  row as tall, the keyboard as tall as the headset's box.
- **Ctrl+A, Ctrl+C, Ctrl+V, Ctrl+Z and Ctrl+Y** have keys of their own, in
  the top row, beside **Del** (delete forward); **⌫** (Backspace) and **⏎**
  (Enter) end the terminal's two rows.
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
- **Two rows.** **▭** in the title bar makes the keys small: no title bar
  and no shortcuts' row, the terminal's two rows as Termux has them, the
  arrows a cross, PgUp and PgDn narrow, Alt beside PgDn, and ⌫ wider, which
  is Supr with Shift held. Hold **PgUp** a moment to open the full window
  again (PgUp does not repeat there). Whether it is two rows is kept for each screen, with its own place
  and size ([doc/compact-keys.md](doc/compact-keys.md)).
- **Keys repeat while held**, as a keyboard's do: every key acts on the
  touch, and one that types or sends a key goes on doing it while held. Not
  Ctrl, Alt, Shift, Alt Gr, Fn or ⊞, nor Ctrl+A, Ctrl+C, Ctrl+V and ImpPnt,
  which should happen once, nor the accents, which wait for a letter.
- **A window, docked or floating.** The keys are a small window with a title
  bar. It starts docked: right above the keyboard, as wide as it, while a
  keyboard is open, and as the **⌨** icon at the bottom right while none is.
  Hold the title bar still for a moment, anywhere on it, its buttons too: it
  buzzes, and the window floats where you leave it, in sight all the time,
  keyboard or not; a tap on a button does what the button is for. The
  ground between the keys moves it the same way. Drag a corner (◤ ◥ ◣ ◢) to
  resize it: across for its width, up or down for the size of its keys; the
  opposite corner stays put. With a mouse or a controller's pointer (INAIR's
  pod), the pointer turns into the diagonal arrows near any corner, and a
  press there resizes the window, whatever key is under it, as a desktop
  window's corner does. **⤓** in the title bar docks it again.
- **The labels.** Each key is a table of three by three cells: its shifted
  character in the top left, its plain one in the middle and its Alt Gr one
  in the bottom right, so none covers another however narrow the key. The
  one the key sends is white, the others grey; with Shift or Alt Gr held a
  key shows only what it types then, in the middle. Every label is one
  size, the largest at which every key in sight could hold three, so the
  labels grow and shrink with the keys. A point, a comma, a dash or an
  accent, which draw little ink, are drawn half as large again where their
  key has room, and on a key of two of them (; over , and : over .) each
  takes two rows. Words (Esc, Ctrl+A, Home…) share one size too and take at
  most three quarters of their key; a long word in a narrow key is made
  smaller alone. Nothing is ever cut.
- **A−** and **A+** in the title bar make every label smaller or larger, a
  tenth at a press. **A↺** brings the labels, and the keys a corner made
  larger or smaller, back to their usual size; the window keeps its place
  and width.
- **Each screen its own.** The phone held upright, the phone on its side and
  Samsung DeX each keep their own window: Keys or ABC, minimized or not, and
  the icon's place; and within each, Keys and ABC each keep their own place,
  width, keys' size, labels' size and whether they float. Turn the phone, or
  switch between Keys and ABC, and the window is as you last left it there.
- **Designing the whole keyboard.** On Berpiztu AI Keyboard, ⚙ →
  **Diseñar**: drag a key to another place; nothing else moves, it leaves a
  hole and takes a hole it is dropped on. Dragged above the keys it waits
  there, to be dragged back. A tap marks a key, and **−** and **+** make it
  narrower or wider. **+ Fila** adds an empty row at the top, **Repartir**
  closes the holes and sizes every key alike, **Restablecer** brings the
  keyboard back as made. Each screen keeps its own design
  ([docs/tODO/designs.md](docs/tODO/designs.md)).
- **–** minimizes the window into the **⌨** icon: a tap opens it again, a
  drag moves it. The icon is always in sight, and always where you last left
  it, apart from the window's own place.
- **✕** closes KeyPlus, once you say yes to "¿Quieres cerrar KeyPlus?" in the
  title bar: nothing of it is in sight. Opening the KeyPlus app brings it
  back. The service stays on meanwhile, since only Settings can turn an
  accessibility service on again; turn it off there to stop it altogether.

## Build and install

The rules for working on KeyPlus are in [rules.md](rules.md). Each
Versions are kept as wslc-ai-agent keeps its own: the csproj holds the
release's (1.0.40, versionCode 100040000), and each `build-apk.ps1` counts
itself as a fourth part (1.0.40.3, versionCode 100040003) in
`private\version.props`, which git ignores, so every APK installs over the
one before and building changes nothing tracked.

Needs the .NET 10 SDK with the `android` workload
(`dotnet workload install android`) and a phone with Android 13 or later.

```powershell
dotnet build                 # Debug build
dotnet build -p:KeyPlusProduct=Keyboard   # the keyboard's (Popup by default)
.\debug.ps1 -Phone           # Debug build installed on the phone (cable or Wi-Fi), turned on and opened
.\debug.ps1 -Emulator        # the same on an emulator, started if none runs
.\deploy.ps1                 # Release APK built and installed on the phone, turned on, app opened
.\build-apk.ps1              # both Release APKs, dist\KeyPlus-Popup.apk and dist\Berpiztu-AI-Keyboard.apk
.\enable.ps1                 # KeyPlus turned on by adb: the popup's service, or the keyboard
```

debug.ps1, deploy.ps1 and enable.ps1 take `-Product Popup` or
`-Product Keyboard`; without it a headset gets the keyboard and any other
device asks. Each APK carries everything it runs, so it installs with no
computer: copy it to the device and open it there.

Then on the phone, for the popup:

1. Copy `dist\KeyPlus-Popup.apk` to it and open it to install
   it (Android asks to allow installing from the app that opens it).
2. Open **KeyPlus** and choose **Turn on KeyPlus keys**, which opens
   Android's switch for it; turn it on.
3. If Android says the setting is restricted (Android 13 and later do so for
   apps not installed from a store), choose **Open app info** in KeyPlus,
   then **⋮ → Allow restricted settings**, and turn it on again.

Where Settings has no such menu (INAIR's pod), or to have KeyPlus turn
itself on, choose **Turn it on with Wireless debugging**, once after each
new install (installing over the app keeps it), with Wi-Fi on and Developer
options turned on: it opens Wireless debugging; turn it on, tap **Pair
device with pairing code**, then pull down the shade and type the code in
KeyPlus's notification, without leaving Settings, which would close the
code. KeyPlus pairs with the device's own adb, as an ADB app on it would,
runs these two commands and turns itself on:

```
appops set ai.berpiztu.keyplus ACCESS_RESTRICTED_SETTINGS allow
pm grant ai.berpiztu.keyplus android.permission.WRITE_SECURE_SETTINGS
```

From then on **Turn on KeyPlus keys** turns the service on by itself,
keeping the others that are on. The key it pairs with is made for that one
time and never kept. A computer's `adb shell` runs the same two commands,
and `deploy.ps1` and `debug.ps1` run them too. Berpiztu AI Keyboard's
screen has a box at its top to try the keys in.

For the keyboard, on a phone or a headset:

1. Copy `dist\Berpiztu-AI-Keyboard.apk` to it and open it to
   install it.
2. Open **Berpiztu AI Keyboard** and choose **Open keyboard settings**.
3. Turn on **Berpiztu AI Keyboard**, then go back and choose **Choose the
   keyboard**.

Where Android's choice will not take it (INAIR's pod), **Turn it on with
Wireless debugging** does it over the device's own Wireless debugging, as
for the popup above: it runs `ime enable`, `ime set` and grants the secure
settings; then **Make it the keyboard in use** does it by itself.
`deploy.ps1` and `debug.ps1` grant that too. The
same screen sets where the keyboard stands, on every screen: **Pop-up** puts
it in a window of its own over the app, or docks it again, at once; **Reset
the keyboard**, after asking, brings it back as first installed, docked at
the bottom as wide as the screen at its usual size, for one lost in a window
too small for it.

### Releasing

`.\deploy-release.ps1`, as wslc-ai-agent's, raises the release's version
to the next patch (or `-Version`), commits it, tags `v<version>` and pushes;
[the Release workflow](.github/workflows/release.yml) builds each APK in a
job of its own, with caches of its own, at that version, and publishes both
on the repository's **Releases** page, to download on a phone or a headset
and install there. `-Local` builds them here instead, `-Local -Publish`
uploads them from here too.

Every release has its notes, written before it is made, in
`docs/release-notes/<version>.md` (`docs/release-notes/1.0.42.md` for the tag
`v1.0.42`): Markdown, for the person about to install it, whole and in one
voice, a patch's holding everything its minor version brought with what it
adds. The release's page opens with them, above the list GitHub writes
itself. `deploy-release.ps1` looks for the file before anything else and
stops when the version has none, before committing, merging or pushing
anything; once `main` is up to date it stops again if the file is not in
the repository yet.

Every APK, built here or there, is signed with the same key, or Android
refuses to install it over the one before: `KEYPLUS_KEYSTORE` and
`KEYPLUS_KEYSTORE_PASS`, else `private\android.keystore` and its
`android.keystore.pass` (git ignores `private\`), made by the first
`build-apk.ps1` that finds none ([packaging\Packaging.ps1](packaging/Packaging.ps1)). GitHub has it as two repository secrets:

```powershell
gh secret set ANDROID_KEYSTORE_BASE64 --body ([Convert]::ToBase64String([IO.File]::ReadAllBytes("private\android.keystore")))
gh secret set ANDROID_KEYSTORE_PASS --body (Get-Content private\android.keystore.pass -Raw).Trim()
```

Back the two files up: without them no later APK updates the installed apps.

### Security: security-watch

`build-apk.ps1` asks [Berpiztu/security-watch](https://github.com/Berpiztu/security-watch)
first: a library it uses with a known vulnerability, moderate or worse, the
.NET runtime the APKs carry included, stops the build, naming it and the
version that fixes it; a lower one is a warning. When security-watch cannot
be reached it asks whether to go on. Once built, it sends the list of
libraries of `main` when it changed, as an issue opened with your `gh`
sign-in, and waits for the answer. `deploy-release.ps1` releases only a
list security-watch has checked: build first, then release; it sends each
release's list when it tags it. No
token: `keyplus` is in security-watch's `projects.txt`, and the account `gh`
is signed in as in its `submitters.txt`. GitHub Actions only asks;
`debug.ps1` and `deploy.ps1` do neither.

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

## For AI agents: DOX

This repository is documented for AI agents with **DOX**, from
[Iosub-ai/dox](https://github.com/Iosub-ai/dox), our fork of
[agent0ai/dox](https://github.com/agent0ai/dox): a hierarchy of `AGENTS.md`
files, one at the root with the project's rules and index, and one in each
folder with a contract of its own ([doc/](doc/AGENTS.md)). An agent reads
them from the root down before it edits, and keeps them current after.

DOX is by [Agent Zero](https://www.agent-zero.ai/), under the
[MIT License](https://github.com/Iosub-ai/dox?tab=License-1-ov-file):
Copyright (c) 2026 Agent Zero.
