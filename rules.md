# Rules

Rules for everyone working on KeyPlus, people and AI agents alike.

- **No co-authorship.** Commits and pull requests carry no Co-Authored-By
  line or any other attribution trailer.
- **Release builds and APKs are the owner's.** Agents check a change with a
  Debug `dotnet build` only; they never run `build-apk.ps1`, `dotnet publish`
  or a Release build.
- **English** for code, comments, docs and commit messages.
- **The version is 1.0.0.x**, x the build number 
  [version.props](version.props), which `build-apk.ps1` raises by one on every
  APK it builds. It is also the Android version code, so every APK installs
  over 