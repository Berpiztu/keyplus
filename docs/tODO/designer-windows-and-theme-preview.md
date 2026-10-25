# Designer on Windows, and Tema with a preview (both platforms)

Agreed with the owner on 2026-10-23; the next piece of work after release
1.0.44 and the troubleshooting page (commit 63f98bc on `main`).

## Why

On Windows there is no designer: the standard keyboards cannot be
designed, nor saved as keyboards of their own. On Android the designer
works on the keyboard itself (⊞ › Diseñar). The owner wants the Android
designer on Windows, and, on both, Tema in the settings to show the chosen
keyboard as a preview and to design it there too.

## The plan, in this order

1. **The Android designer on Windows.** A "Diseñar" button on the Windows
   keyboard's toolbar enters the same design mode as Android's
   (`StandardKeyboard.cs`'s `Design`, `Designing`, `ResizeMarked`,
   `RemoveMarked`, `AddKey`, `AddRow` above, `DesignTouch`, `RowAt`,
   `MoveKey`, `DrawDesigning`; `KeyPanel.cs`'s `FillDesignBar`,
   `ShowExtraKeys`, `StartStandardDesign`, `EndStandardDesign`):
   - a click marks a key in blue; − and + make it narrower or wider (the
     keyboard keeps its width); a drag moves it, in its row or another;
   - **+ Tecla** (the extras: `StandardKeys.Extras`/`Extra`), **+ Fila**
     (the new row goes above), **Quitar**, **Cancelar**, **Hecho**;
   - Hecho saves a built-in keyboard as a new one, and a saved one in its
     place; designing again opens it as saved (the fix of 2026-10-23 in
     `KeyPanel.EndStandardDesign`: the panel keeps the saved record).
   - The design's format is the Android one, shared, not a second one:
     `StandardDesign.cs` (rows of `DesignedKey("b:r:c" | "x:id", width)`,
     JSON) compiled by the Windows app as `StandardKeys.cs` is (Android
     names in `windows/AndroidNames.cs` where needed).
2. **Saved keyboards on Windows.** As Android's `Keyboards.cs` (Estándar,
   Estándar PC and those designed, each renamed or deleted), kept in the
   app's folder (`%APPDATA%\Berpiztu\Keyboard`), the keyboard chosen in
   `preferences.json` (today `Pc`; it becomes the keyboard's id). The
   settings (a separate process) and the keyboard share it through the
   preferences' watch, as the language and Tema already do.
3. **Tema with a preview, on Windows.** The settings' Tema page lists the
   keyboards and draws the chosen one below, as it shows on screen (a
   `KeyboardSurface` in the settings window). **Diseñar** there opens the
   designer on the preview, roomier and easier with a mouse; saving changes
   the keyboard on screen at once.
4. **The same on Android** (the owner's rule: everything on both): Tema
   with the chosen keyboard's preview, and the designer from there too.

Start with 1 and 2 (what does not work on Windows today), then 3 and 4.

## Later: one key size for the whole keyboard

Asked by the owner on 2026-10-23, to do after this plan, on both
platforms. Today a row designed wider than the keyboard as made squeezes
its own keys alone (`Lay` in `StandardKeyboard.cs` and
`windows/KeyboardSurface.cs`), so two keys of the same designed width in
different rows (↑ and ↓) come out different and cannot be made equal.
One key unit for every row was tried on Windows and undone: it changed
the owner's whole keyboard (rows of different lengths, margins). What
stayed: a shaped Enter's top is sized after its lower part
(`KeyWidths`). Equal keys across rows (↑ and ↓) is still open.

## Colours (Tema › Colores)

Asked by the owner on 2026-10-23. On Windows, tried by the owner: Sistema,
Claro, Oscuro and Personalizado (the keyboard's colour and the accent's,
Windows' colour picker, ↶ one step back), and Personalizado's Por app (each
application its window's colour, De la app, or its own two, `apps.json`).
On Android, written and built, not tried: the same row (`ColorsRow.cs`, a
picker of hue, saturation and brightness, Android having none), Sistema
following the device's dark theme, Claro and Oscuro the palette's tones,
Personalizado's colours with the Windows app's arithmetic (`Rgb`, shared),
and Por app by the field's package, De la app taking the background the
application's theme declares (`AppThemes.cs`), made light or dark as the
device is (Chrome declares a light one and turns dark by itself), or, with
the keyboard's accessibility service on, the colour seen on the screen
beside the field (`MenuWatchService.SeeFieldColor`), as on Windows.

## The glasses: Estándar XR

Asked by the owner on 2026-10-23, for the glasses alone: Gboard's look on
the Galaxy XR, captured there (docs/tODO/photos/gboard/xr-letters.png,
xr-symbols.png) and measured: the ground #1D2024, the letters' keys and the
space bar #101418 (darker than the ground), the function keys #3F4753,
Enter a #8CB4E5 pill, a band of the keys' colour as tall as the bar over
it; a tablet's keys, 13 across, each 84 by 78 px, 26 px apart, its corners
20 px round, the card's 55. Tried by the owner, alike: Estándar XR
(`StandardPage.XrLetters`, `XrSymbols`, `Keyboards.StandardXrId`), listed and
first on a headset alone, made as tall as the headset's window
(`KeyPlusKeyboard.FillHeadsetWindow`; no band over the bar and no room for a
navigation bar, as the owner asked, so the keys take the whole window), the
round card on a see-through window (`AsHeadsetCard`), the dark keys and
Personalizado as Gboard's there (`StandardColors.Read`). Still to do: the
.com key Gboard adds in a web address's field, once captured where it goes.

## Later: more keyboards the keyboard comes with

Asked by the owner on 2026-10-23, once the designer is done: keyboards
made after the usual PC standards, built in beside Estándar and Estándar
PC: the owner's own design as one of them, and a full one with the
numeric pad at the right. The owner chose on 2026-10-23 the design in
`designs/2026-10-23_174700_Windows-IAMAX_keyboards.json` (ISO Enter,
Insert, Inicio, Fin, RePág, AvPág) to be Estándar PC itself.

## Where things stand

- Pushed on 2026-10-23 (b8808e1), the preview tried by the owner on the Galaxy XR: step 4 on Android
  (`ThemeView.cs` in Ajustes › Tema; keyboards by language, each screen
  its own one in each language, docked and floating alike, `Keyboards.cs`;
  the designer shared by the keyboard and Tema, `StandardDesigner.cs`), and
  Estándar PC in Spanish (and, later, English) as the owner's design on both
  (`StandardKeys.PcDesign`); the phone starts on Estándar, a tablet on its
  side and DeX on Estándar PC, as before.

- On `futo-swipe`, pushed to f31540c: 1 and 2 tried by the owner, with
  + Tecla, + Columna and the Enter's shapes on both platforms; 3 done on
  Windows (Tema with its preview, designed there one to one) with keyboards
  by language (each language its own keyboards and chosen one).
- Docked and floating, agreed on 2026-10-23: one keyboard per language;
  Windows starts floating, Android docked; Acoplar (Windows) or Flotante
  (Android) shows the same keyboard in the other mode; designing there and
  Hecho keeps that mode's own design on the same keyboard
  (`KeyboardEntry.DockedDesign`/`FloatingDesign`, `DesignFor`), Cancelar
  keeps nothing; a built-in one designed saves a new keyboard.
- Windows docking, written, not committed, being tried by the owner:
  `windows/DockedBar.cs` (an app bar, SHAppBarMessage, the bottom Windows
  gave kept, for asked again it counts its own room as taken);
  `KeyboardWindow.SetDocked`/`PlaceDocked` (the window held on its bar in
  WM_WINDOWPOSCHANGING, Windows otherwise pushing it inside the work area);
  docked keys never stretched: their own width centred, the bar's ground
  around (`dockedWidth`, `dockedOffset`, `dockedLift` in `window.json`);
  Redimensionar as Android's `KeyboardFrame.cs` (edges for height and
  width, ✥ to move, Restablecer, Cancelar, Hecho), floating and docked.
- The owner's findings on 2026-10-23, written, not tried yet: Cancelar
  (as the frame found it, Restablecer undone too) on Windows and Android
  (Mover's frame too); the height and the lift follow the drag as it goes;
  the lift that grew sideways and the enormous bar came from summing the
  `Thumb`'s change, which is the whole way from the start when the grip
  does not move: every drag is now measured on the screen from where it
  began, the window bounded by the screen and the lift by half of it.
- Windows: `windows/KeyboardSurface.cs` draws and types the standard keys
  as `StandardKeyboard.cs` does; `windows/SettingsWindow.cs` has the pages
  (Idiomas, Preferencias, Tema, Escritura deslizando el dedo, Escribir por
  voz, Solución de problemas, Información); `windows/AGENTS.md` maps it.
- Before installing on the owner's devices or committing:
  `.\backup-designs.ps1` (designs are never lost).
