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

## Where things stand

- `main` has everything (release 1.0.44 published with both APKs and the
  Windows MSI, then commit 63f98bc). `futo-swipe` is behind `main`: bring
  it up to `main` first (`git switch futo-swipe; git merge --ff-only main`)
  and work there, merging to `main` once the owner has tried it.
- Windows: `windows/KeyboardSurface.cs` draws and types the standard keys
  as `StandardKeyboard.cs` does; `windows/SettingsWindow.cs` has the pages
  (Idiomas, Preferencias, Tema, Escritura deslizando el dedo, Escribir por
  voz, Solución de problemas, Información); `windows/AGENTS.md` maps it.
- Before installing on the owner's devices or committing:
  `.\backup-designs.ps1` (designs are never lost).
