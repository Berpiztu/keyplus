# windows/ — Berpiztu AI Keyboard for Windows

## Purpose

- A Windows app (.NET 10, WPF, `net10.0-windows10.0.19041.0`) with the standard keyboards, Estándar and Estándar PC, in a floating card that never takes the focus from the window typed in, its keys sent to that window

## Ownership

- This file owns `windows/`; the layouts and the keys' signs it uses belong to the root (`StandardKeys.cs`, `Resources/drawable/`)

## Local Contracts

- `Berpiztu.KeyPlus.Windows.csproj` compiles `..\StandardKeys.cs` as it is and copies the signs it draws (`..\Resources\drawable\ic_key_*.xml`, `ic_grid_pc`, `ic_grid_phone`, `ic_tool_close`) beside the app, under `drawable\`; the root csproj leaves `windows\**` out of the Android build
- `AndroidNames.cs` defines the Android names `StandardKeys.cs` uses (`Android.Views.Keycode` with Android's values, `Resource.Drawable` and `Resource.String` ids); a name added to `StandardKeys.cs` is added there too, and a sign there joins `Icons.cs`'s files and the csproj's copied ones
- `KeyboardSurface.cs` lays out, draws and types as the Android `StandardKeyboard.cs` does, with the same sizes (one Android dp a WPF unit); a change of behaviour there is made here too
- `KeySender.cs` sends with `SendInput`: text as Unicode characters, other keys by virtual key with Ctrl, Alt, ⊞ and Shift around them
- `KeyboardWindow.cs`: `WS_EX_NOACTIVATE` (never focused) and `WS_EX_APPWINDOW` (a taskbar button); its place, width, keyboard and language kept in `%APPDATA%\Berpiztu\Keyboard\window.json`
- `Palette.cs`: the colours from Windows' accent tones and the theme's background and foreground (`UISettings`), never written by hand
- `Program.cs`: one keyboard at a time (a mutex)
- Swipe typing: `SwipeEngine.cs` calls `keyplus_swipe.dll` (`native/futo-swipe/win-x64/`, built by `native/futo-swipe/windows/build.ps1` from FUTO's swipe-library and ExecuTorch with clang-cl, its C API in `keyplus_swipe.cpp`); the encoder and the word lists are the Android keyboard's `Assets/`, copied beside the app; the words are made with `..\SwipeWords.cs`, shared with Android; the letter forms are written to `%LOCALAPPDATA%\Berpiztu\Keyboard\futo-swipe`

## Work Guidance

- Not on Windows yet, saying so where asked: emoji, the grid, the designer, the clipboard

## Verification

- `dotnet build windows\Berpiztu.KeyPlus.Windows.csproj`; seeing it work needs a desktop in sight (a disconnected remote session draws nothing and takes no input)

## Child DOX Index

None.
