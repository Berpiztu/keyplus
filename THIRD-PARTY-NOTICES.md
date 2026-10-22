# Third-party notices

KeyPlus is MIT-licensed ([LICENSE](LICENSE)). Both of its APKs carry the
libraries below, which the app uses to turn itself on over the device's own
Wireless debugging ([AdbShell.java](AdbShell.java)). Each keeps its own
licence; their versions are the ones in
[Berpiztu.KeyPlus.csproj](Berpiztu.KeyPlus.csproj).

| Library | Version | Licence | Source |
| --- | --- | --- | --- |
| libadb-android | 3.1.1 | Apache-2.0 (offered as GPL-3.0-or-later OR Apache-2.0; KeyPlus takes Apache-2.0) | https://github.com/MuntashirAkon/libadb-android |
| spake2-android (spake2-java) | 2.2.1 | LGPL-3.0 | https://github.com/MuntashirAkon/spake2-java |
| Bouncy Castle (bcprov-jdk15to18) | 1.86 | MIT | https://github.com/bcgit/bc-java |
| Conscrypt (conscrypt-android) | 2.7.0 | Apache-2.0 | https://github.com/google/conscrypt |

spake2-android is LGPL-3.0: its source is at the address above, and anyone
may build KeyPlus with a changed copy of it, since KeyPlus's own source is
public and the library is a separate artifact in the build
(`AndroidMavenLibrary` in the csproj).

## Swipe typing (Berpiztu AI Keyboard)

| Part | Licence | Source |
| --- | --- | --- |
| FUTO swipe-library, built as `native/futo-swipe/*/libswipe_jni.so` by `native/futo-swipe/build.sh` and, for Windows, as `native/futo-swipe/win-x64/keyplus_swipe.dll` (with ExecuTorch and XNNPACK in it) by `native/futo-swipe/windows/build.ps1`, with KeyPlus's trie functions (`keyplus_trie_jni.cpp`); `SwipeDecoder.java` adapted from its Kotlin interface | GPL-3.0 | https://gitlab.futo.org/keyboard/swipe-library |
| FUTO Swipe encoder model (`Assets/futo-swipe/honorable_sturgeon`) | FUTO Model Weights License 1.0 (`Assets/futo-swipe/LICENSE.md`): the app says "powered by FUTO Swipe" | https://huggingface.co/futo-org/futo-swipe |
| ExecuTorch, inside libswipe_jni.so | BSD | https://github.com/pytorch/executorch |
| libc++_shared.so, from the Android NDK r27d | Apache-2.0 with LLVM exceptions | https://developer.android.com/ndk |
| Vortice.Direct2D1 3.8.3 (Direct2D, DirectWrite and WIC for .NET), with Vortice.DXGI and SharpGen.Runtime, in the Windows app: emoji drawn in colour | MIT | https://github.com/amerkoleci/Vortice.Windows |
| Unicode's emoji list, emoji-test.txt 15.1, its fully-qualified emoji by group (`Assets/emoji/emoji-15.1.txt`) | Unicode License v3 (terms of use) | https://unicode.org/Public/emoji/15.1/ |
| AOSP LatinIME's Spanish and English (US) word lists (`Assets/dictionaries/es_wordlist.combined.gz`, `en_US_wordlist.combined.gz`) | AOSP LatinIME (to confirm) | https://android.googlesource.com/platform/packages/inputmethods/LatinIME |

## Icons

| Icon | Licence | Source |
| --- | --- | --- |
| The clipboard (`Resources/drawable/ic_clipboard.xml`), Material Icons' content_paste | Apache-2.0 | https://github.com/google/material-design-icons |
| The standard keyboard's keys and toolbar (`Resources/drawable/ic_key_*.xml`, `ic_tool_*.xml`, `ic_grid_*.xml`, `ic_settings_*.xml`, `ic_emoji_*.xml`), Material Symbols (outlined); `ic_grid_pc.xml`, `ic_grid_floating*.xml` its computer with "PC", keys or a slash drawn on | Apache-2.0 | https://github.com/google/material-design-icons |
