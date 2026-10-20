# DOX framework

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
  - `Keyboard`: Berpiztu AI Keyboard, `ai.berpiztu.keyboard`, an input method of its own, for phones and headsets (Galaxy XR)
- `README.md` is the user and build guide and holds the file-by-file map of the sources

### Ownership

- This file owns the sources, scripts, `Resources/`, `packaging/`, `.github/workflows/` and the root docs; none has an AGENTS.md of its own
- `doc/AGENTS.md` owns the design notes and the owner's reference captures
- Release builds, APKs, releases and installs on the owner's devices belong to the owner

### Local Contracts

- The product split lives in `Berpiztu.KeyPlus.csproj`:
  - Popup only: `KeyPlusService.cs`, `SelectionInputMethod.cs`, `KeyWindowActivity.cs`, `Resources/xml/keyplus_accessibility.xml`
  - Keyboard only: `KeyPlusKeyboard.cs`, `KeyboardFrame.cs`, `ClipboardImages.cs`, `ScreenshotWatcher.cs`, `Resources/xml/keyplus_keyboard.xml`; the Keyboard build defines `KEYBOARD`
  - Everything else is shared; a new product-specific file joins the csproj's lists
- Each product builds into `bin/<Product>/<Configuration>/` and `obj/<Product>/<Configuration>/`
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
  - `deploy-release.ps1`: raises the version, commits, tags `v<version>` and pushes
  - `packaging/Packaging.ps1`: version and signing-key helpers for `build-apk.ps1` and `deploy-release.ps1`
- `.github/workflows/release.yml` builds both APKs on a `v*` tag and publishes them as a GitHub release; it stops when the tag and the csproj version disagree or the signing secrets are missing; a tag marked "built locally" is skipped

### Work Guidance

- `rules.md` holds the owner's rules for people and agents and is binding; read it before any change
- A user-visible change updates `README.md` (its Using it section and file table) in the same change

### Verification

- `dotnet build` (Debug, Popup) and `dotnet build -p:KeyPlusProduct=Keyboard` (Debug, Keyboard); there is no test project

## User Preferences

When the user requests a durable behavior change, record it here or in the relevant child AGENTS.md

- Galaxy XR support stays as it is: phone changes go through the non-headset path; never remove or alter the headset path (`OnHeadset`, `KeyWindowActivity.cs`, `KeyPlusKeyboard.FillBox`, `KeyPlusService.AttachKeys`)
- Install on the owner's phone or headset only when the owner says "install" for that build

## Child DOX Index

| Path | Doc | Covers |
|------|-----|--------|
| `doc/` | [doc/AGENTS.md](doc/AGENTS.md) | Design notes (key labels, Keys in two rows) and the owner's reference captures |

Root-owned paths without their own AGENTS.md:

| Path | Covers |
|------|--------|
| `*.cs` | The app's sources, mapped file by file in `README.md` |
| `Berpiztu.KeyPlus.csproj`, `AndroidManifest.xml` | Build, products, versions, signing, shared manifest |
| `Resources/` | Icon (`drawable/`), strings (`values/`), the accessibility service and keyboard descriptors (`xml/`) |
| `*.ps1`, `packaging/` | Build, deploy, enable and release scripts |
| `.github/workflows/release.yml` | Release build and publishing |
| `README.md`, `rules.md`, `LICENSE` | User and build guide, owner's rules, license |
