# DOX framework

> This repository's AGENTS.md files follow **DOX**, used from
> [Iosub-ai/dox](https://github.com/Iosub-ai/dox), a fork of
> [agent0ai/dox](https://github.com/agent0ai/dox) by
> [Agent Zero](https://www.agent-zero.ai/). The DOX text below is theirs,
> under the [MIT License](https://github.com/Iosub-ai/dox?tab=License-1-ov-file):
> Copyright (c) 2026 Agent Zero.

- DOX is highly performant AGENTS.md hierarchy installed here
- Agent must follow DOX instructions across any edits

## Core Contract

- AGENTS.md files are binding work contracts for their subtrees
- Work products, source materials, instructions, records, assets, and durable docs must stay understandable from the nearest applicable AGENTS.md plus every parent AGENTS.md above it

## Read Before Editing

1. Read the root AGENTS.md
2. Identify every file or folder you expect to touch
3. Walk from the repository root to each target path
4. Read every AGENTS.md found along each route
5. If a parent AGENTS.md lists a child AGENTS.md whose scope contains the path, read that child and continue from there
6. Use the nearest AGENTS.md as the local contract and parent docs for repo-wide rules
7. If docs conflict, the closer doc controls local work details, but no child doc may weaken DOX

Do not rely on memory. Re-read the applicable DOX chain in the current session before editing.

## Update After Editing

Every meaningful change requires a DOX pass before the task is done.

Update the closest owning AGENTS.md when a change affects:

- purpose, scope, ownership, or responsibilities
- durable structure, contracts, workflows, or operating rules
- required inputs, outputs, permissions, constraints, side effects, or artifacts
- user preferences about behavior, communication, process, organization, or quality
- AGENTS.md creation, deletion, move, rename, or index contents

Update parent docs when parent-level structure, ownership, workflow, or child index changes. Update child docs when parent changes alter local rules. Remove stale or contradictory text immediately. Small edits that do not change behavior or contracts may leave docs unchanged, but the DOX pass still must happen.

## Hierarchy

- Root AGENTS.md is the DOX rail: project-wide instructions, global preferences, durable workflow rules, and the top-level Child DOX Index
- Child AGENTS.md files own domain-specific instructions and their own Child DOX Index
- Each parent explains what its direct children cover and what stays owned by the parent
- The closer a doc is to the work, the more specific and practical it must be

## Child Doc Shape

- Create a child AGENTS.md when a folder becomes a durable boundary with its own purpose, rules, responsibilities, workflow, materials, or quality standards
- Work Guidance must reflect the current standards of the project or user instructions; if there are no specific standards or instructions yet, leave it empty
- Verification must reflect an existing check; if no verification framework exists yet, leave it empty and update it when one exists

Default section order:
- Purpose
- Ownership
- Local Contracts
- Work Guidance
- Verification
- Child DOX Index

## Style

- Keep docs concise, current, and operational
- Document stable contracts, not diary entries
- Put broad rules in parent docs and concrete details in child docs
- Prefer direct bullets with explicit names
- Do not duplicate rules across many files unless each scope needs a local version
- Delete stale notes instead of explaining history
- Trim obvious statements, repeated rules, misplaced detail, and warnings for risks that no longer exist

## Closeout

1. Re-check changed paths against the DOX chain
2. Update nearest owning docs and any affected parents or children
3. Refresh every affected Child DOX Index
4. Remove stale or contradictory text
5. Run existing verification when relevant
6. Report any docs intentionally left unchanged and why

## Project: KeyPlus

### Purpose

- Android app (.NET 10, `net10.0-android`, API 33 and later, arm64 only) that sends the keys a phone's keyboard lacks (Esc, Tab, Ctrl, Alt, Shift, arrows, Home, End, pages) to the focused editor
- Two products from one code base, chosen by `-p:KeyPlusProduct`:
  - `Popup` (default): KeyPlus, `ai.berpiztu.keyplus`, an accessibility service whose key strip sits above the keyboard in use
  - `Keyboard`: Berpiztu AI Keyboard, `ai.berpiztu.keyboard`, an input method of its own, for phones and headsets (Galaxy XR), with an optional accessibility service of its own (`MenuWatchService.cs`) that only sees menus open and close, for Also move out of the way of menus, and the colour just inside the field typed in on a screenshot, for Theme's Per app (App's); the user turns it on, and a debug install may turn it off again
- `README.md` is the user and build guide and holds the file-by-file map of the sources
- `windows/`: Berpiztu AI Keyboard for Windows, the standard keyboards in a floating card, a project of its own; its installer is `build-windows-installer.ps1` with `packaging/windows-install` (WiX 5, per-user MSI), built by the Release workflow beside the APKs

### Ownership

- This file owns the sources, scripts, `Resources/`, `packaging/`, `.github/workflows/` and the root docs; none has an AGENTS.md of its own
- `doc/AGENTS.md` owns the design notes and the owner's reference captures
- `windows/AGENTS.md` owns the Windows app
- Release builds, APKs, releases and installs on the owner's devices belong to the owner

### Local Contracts

- The product split lives in `Berpiztu.KeyPlus.csproj`:
  - Popup only: `KeyPlusService.cs`, `SelectionInputMethod.cs`, `KeyWindowActivity.cs`, `Resources/xml/keyplus_accessibility.xml`
  - Keyboard only: `KeyPlusKeyboard.cs`, `KeyboardFrame.cs`, `ClipboardImages.cs`, `ScreenshotWatcher.cs`, `SwipeTyping.cs`, `SettingsScreen.cs`, `ThemeView.cs`, `ColorsRow.cs`, `MenuWatchService.cs`, `Resources/xml/keyplus_keyboard.xml`; the Keyboard build defines `KEYBOARD`
  - Everything else is shared; a new product-specific file joins the csproj's lists
- Each product builds into `bin/<Product>/<Configuration>/` and `obj/<Product>/<Configuration>/`
- `windows\**` is left out of the Android build; the Windows app compiles `StandardKeys.cs`, `StandardDesign.cs`, `KeyboardEntry.cs`, `KeyboardLook.cs`, `SwipeWords.cs`, `EmojiList.cs`, `VoiceTyping.cs`, `WordSuggestions.cs`, `Updates.cs` and `PersonalWords.cs` as they are, so an Android name added to them is added to `windows/AndroidNames.cs`
- Dictionary: `PersonalWords.cs` (shared), the personal dictionary and the words chosen for swipes, in `personal-words.json` (`FilesDir` on Android, `%APPDATA%\Berpiztu\Keyboard` on Windows), read from the file before every change so the keyboard and the settings share it; its words join the swipe's letter forms (`SwipeWords.WriteLetterForms`' personal words at `PersonalWords.Frequency`) and the tries are made again when they change (Android: `SwipeDecoder.freeDictionary`, `nativeDestroyTrie`, read again as a field opens; Windows: the file watched); the keyboard's `KeyPanel.OrderSwiped`, `SwipeChosen` and `WordTyped` on Android, `KeyboardSurface`'s on Windows; Settings › Dictionary on both, `SettingsRow.Edited` and `Removed` drawing ✏ and the red ✕ (`KeyboardLooks.DeleteRed`)
- Updates: `Updates.cs` (shared) asks GitHub's latest release of `Berpiztu/keyplus` (`releases/latest`), compares its tag with the version running and downloads the product's file by the release workflow's names (`Berpiztu-AI-Keyboard.apk`, `KeyPlus-Popup.apk`, `Berpiztu-AI-Keyboard-Windows.msi`), so renaming one there is renaming it here; Android's side is `AndroidUpdates.cs` (both products: the keyboard's `OnStartInputView` and the popup's `OnServiceConnected` check once a day while `updatesAutomatic` is on and post a notification; `MainActivity` asks, then a `PackageInstaller` session, `REQUEST_INSTALL_PACKAGES`); nothing installs without the user's yes
- Turning on without a computer: `AdbGrant.cs` (a short foreground service, both products) takes the Wireless debugging pairing code in a notification and runs the product's ADB commands through `AdbShell.java` (Java, `AndroidJavaSource` with `Bind="false"`, called from C# by JNI); the key it pairs with is made per run and never stored
- Java libraries come as `AndroidMavenLibrary` items with `Bind="false"`: libadb-android and spake2-android from JitPack, Bouncy Castle and Conscrypt from Maven Central; each version is checked for known vulnerabilities (OSV) before it is set, Bouncy Castle is held above the vulnerable one libadb asks for, and `THIRD-PARTY-NOTICES.md` lists them with their licences and is updated with them
- security-watch's inventory reads NuGet packages only, so it does not see the Java libraries
- The whole keyboard's design (`KeyboardDesign.cs`, drawn and edited in `KeyPanel.cs`) is kept per screen in the preference `abcDesign`: `width|rows|keys aside|id:width`, keys by their place in `KeyboardDesign.Catalog`, holes as negative widths in hundredths; a new key in `PanelKeys.Spanish` goes at the end of the catalog, or saved designs read wrong
- Nothing in the keyboard's layout may ask for a layout on every pass: `KeyPlusKeyboard.Place` runs on each layout of the keys' box, so a setter that always asks for one (`SetMinimumHeight`) loops about sixty times a second; set only what changed
- Each product's app label is `MainActivity.AppLabel`; `AndroidManifest.xml` holds only the shared application attributes
- Versions: the csproj's `ApplicationDisplayVersion` and `ApplicationVersion` (versionCode, 1.0.41 is 100041000) are the last release's, raised only by `deploy-release.ps1`; local builds count a fourth part in `private/version.props`
- Signing: one key for every APK, Debug included: `KEYPLUS_KEYSTORE` and `KEYPLUS_KEYSTORE_PASS`, else `private/android.keystore` and `private/android.keystore.pass`; on GitHub the secrets `ANDROID_KEYSTORE_BASE64` and `ANDROID_KEYSTORE_PASS`
- `private/` (keystore, version.props), `bin/`, `obj/` and `dist/` are never committed
- Scripts:
  - `adb.ps1`: helpers dot-sourced by the others (adb, device, service, package ids)
  - `debug.ps1`: Debug build installed with `dotnet build -t:Install` (never `adb install` of a Debug APK)
  - `deploy.ps1`: Release APK built and installed by adb
  - `build-apk.ps1`: Release APKs into `dist/KeyPlus-Popup.apk` and `dist/Berpiztu-AI-Keyboard.apk`
  - `enable.ps1`: turns the popup's service or the keyboard on by adb
  - `deploy-release.ps1`: raises the version, commits, tags `v<version>` and pushes; stops before anything when `docs/release-notes/<version>.md` is missing, and again before the tag when it is not committed; the release's page opens with it
  - `packaging/Packaging.ps1`: version and signing-key helpers for `build-apk.ps1` and `deploy-release.ps1`
  - `packaging/SecurityWatch.ps1`: a copy of Berpiztu/security-watch's `client/SecurityWatch.ps1`, never edited here (a change is made there and copied); `Packaging.ps1` loads it as project `keyplus`
- security-watch: `build-apk.ps1` asks it first (`Test-SecurityWatch`) and stops on a library with a vulnerability moderate or worse, the .NET runtime included, asking whether to go on when it cannot be reached; once built it sends the checkout's list of libraries when it changed, for `main` and release tags, as an issue opened by whoever `gh` is signed in as (`Send-SecurityWatchInventory`); `deploy-release.ps1` releases only the list security-watch checked (`Test-SecurityWatch -Release`: a list being checked is waited for, none stops it, so the build comes first) and sends the release's list when it tags it; GitHub Actions only asks; `debug.ps1` and `deploy.ps1` do neither
- `.github/workflows/release.yml` builds both APKs on a `v*` tag and publishes them as a GitHub release; it stops when the tag and the csproj version disagree or the signing secrets are missing; a tag marked "built locally" is skipped

### Work Guidance

- `rules.md` holds the owner's rules for people and agents and is binding; read it before any change
- A user-visible change updates `README.md` (its Using it section and file table) in the same change

### Verification

- `dotnet build` (Debug, Popup) and `dotnet build -p:KeyPlusProduct=Keyboard` (Debug, Keyboard); `dotnet build windows\Berpiztu.KeyPlus.Windows.csproj` (Windows); there is no test project

## User Preferences

When the user requests a durable behavior change, record it here or in the relevant child AGENTS.md

- Galaxy XR support stays as it is: phone changes go through the non-headset path; never remove or alter the headset path (`OnHeadset`, `KeyWindowActivity.cs`, `KeyPlusKeyboard.FillBox`, `KeyPlusService.AttachKeys`); on the headset Berpiztu AI Keyboard is the phone's keyboard by default, the headset's own (its box) where Preferences asks for it (`KeyPlusPreferences.HeadsetBoxKey`)
- Install on the owner's phone or headset only when the owner says "install" for that build
- The owner's keyboard designs are never lost: before any install, any change to an emulator (wipe, new image) and every commit, run `.\backup-designs.ps1`; it copies each design that changed since its last copy to `docs/tODO/designs/` as a new file, never over an old one, and those copies are committed
- The owner's rule number one: what a screen holds never moves what is on it; a choice made, a list longer or shorter, a language, a mode or a longer text changes no part's size or place (lists that vary are drop-downs or boxes of one size, a button out of use is hidden in its place, a preview is a box of one size)
- Space is never wasted: margins, padding and fixed widths are the least a finger and the text need, horizontal and vertical; a control is as wide as its content (a sign's button its sign and a finger's margin), and the room left goes to what holds text
- Every feature or fix is made for the Android and the Windows keyboard alike; the settings are an app of their own on both, independent of the keyboard
- Everything written is in English, release notes and docs included: a setting, a button or a key is named by its English label (`Resources/values/strings.xml`: Theme, Design, Standard PC, Check now), never the Spanish one; a label the code has only in Spanish is translated

## Child DOX Index

| Path | Doc | Covers |
|------|-----|--------|
| `doc/` | [doc/AGENTS.md](doc/AGENTS.md) | Design notes (key labels, Keys in two rows) and the owner's reference captures |
| `windows/` | [windows/AGENTS.md](windows/AGENTS.md) | Berpiztu AI Keyboard for Windows: the standard keyboards in a floating card that never takes the focus |

Root-owned paths without their own AGENTS.md:

| Path | Covers |
|------|--------|
| `*.cs` | The app's sources, mapped file by file in `README.md` |
| `Berpiztu.KeyPlus.csproj`, `AndroidManifest.xml` | Build, products, versions, signing, shared manifest |
| `Resources/` | Icons (`drawable/`: the app's, and the standard keyboard's and the settings' Material Symbols `ic_key_*`, `ic_tool_*`, `ic_grid_*`, `ic_settings_*`, and `ic_menu_*`, a thing copied's menu), strings in English and Spanish (`values/`, `values-es/`), the accessibility service and keyboard descriptors (`xml/`) |
| `*.ps1`, `packaging/` | Build, deploy, enable and release scripts |
| `.github/workflows/release.yml` | Release build and publishing |
| `docs/release-notes/` | Each release's notes, required by `deploy-release.ps1` |
| `docs/tODO/` | The owner's work lists, schematic: `layout.md` (findings with their photos and scale), `designs.md` (keyboard designs), `settings-languages.md` (Gboard copied: Standard and PC, the grid, the settings, languages) with `gboard-checklist.md` (what of it the owner marked to make now); `photos/` (`photos/gboard/`, Gboard as the owner's Fold and the emulator have it, the standard keyboard's model); `designs/`, every version of the owner's designs, one file each (`backup-designs.ps1`), and `designs/import/`, each as a file Import designs reads |
| `AdbShell.java` | The Java ADB client over Wireless debugging, on libadb-android |
| `SwipeDecoder.java`, `native/futo-swipe/`, `Assets/` | Swipe typing (keyboard only): FUTO Swipe's Java side, its native libraries per ABI with `build.sh` (built in the `futo-swipe-build` wslc container), the Windows DLL `win-x64/keyplus_swipe.dll` with `windows/` (its C API, CMakeLists and `build.ps1`, built on Windows with clang-cl outside the repository), its model and the word lists |
| `backup-designs.ps1` | Copies each attached device's changed keyboard designs to `docs/tODO/designs/` |
| `README.md`, `rules.md`, `LICENSE`, `THIRD-PARTY-NOTICES.md` | User and build guide, owner's rules, license, the bundled libraries' licences |
