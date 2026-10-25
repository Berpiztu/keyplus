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
| NAudio.WinMM 2.2.1, with NAudio.Core, in the Windows app: the microphone for 🎤 | MIT | https://github.com/naudio/NAudio |
| Vortice.Direct2D1 3.8.3 (Direct2D, DirectWrite and WIC for .NET), with Vortice.DXGI and SharpGen.Runtime, in the Windows app: emoji drawn in colour | MIT | https://github.com/amerkoleci/Vortice.Windows |
| Unicode's emoji list, emoji-test.txt 15.1, its fully-qualified emoji by group (`Assets/emoji/emoji-15.1.txt`) | Unicode License v3 (terms of use) | https://unicode.org/Public/emoji/15.1/ |
| AOSP LatinIME's Spanish and English (US) word lists (`Assets/dictionaries/es_wordlist.combined.gz`, `en_US_wordlist.combined.gz`) | AOSP LatinIME (to confirm) | https://android.googlesource.com/platform/packages/inputmethods/LatinIME |

## Spelling (Berpiztu AI Keyboard)

| Part | Licence | Source |
| --- | --- | --- |
| diactag-1.0, olaverse's diacritic restoration model (`diactag.int8.onnx`, `labels.json`, `calibration.json`), not carried by the app: downloaded at revision `101cef2c97c656b0392429275dc17fe5f357d82d`, each file checked by its SHA-256, when Spelling's Correct accents with the model is turned on; its decoding in `windows/AccentModel.cs` follows olaverse's `nlp/_diactag/infer.py` | Apache-2.0 | https://huggingface.co/olaverse/diactag-1.0 |
| ONNX Runtime 1.31.0 (Microsoft.ML.OnnxRuntime), in the Windows app: runs the accent model | MIT | https://github.com/microsoft/onnxruntime |
| WeCantSpell.Hunspell 7.0.1, Hunspell ported to .NET, unchanged, in the Windows app: checks and corrects words (`windows/SpellingDictionary.cs`) | MPL-1.1 (of MPL 1.1, GPL 2.0 and LGPL 2.1) | https://github.com/aarondandy/WeCantSpell.Hunspell |
| LibreOffice's Spanish (Spain) spelling dictionary, RLA-ES (`es_ES.aff`, `es_ES.dic`), not carried by the app: downloaded from LibreOffice/dictionaries at commit `762abe74008b94b2ff06db6f4024b59a8254c467`, each file checked by its SHA-256, when Spelling's Correct words is turned on | MPL-1.1 (of GPL-3.0, LGPL-3.0 and MPL-1.1) | https://github.com/LibreOffice/dictionaries/tree/master/es |
| LibreOffice's English (US) spelling dictionary, from SCOWL (`en_US.aff`, `en_US.dic`), not carried by the app: downloaded from LibreOffice/dictionaries at the same commit with the Spanish one, each file checked by its SHA-256 | SCOWL's (permissive: use, copy, modify and distribute with its notice; the affix file under Ispell's BSD) | https://github.com/LibreOffice/dictionaries/tree/master/en |
| The Spanish Wikipedia's list of common misspellings, Wikipedia:Corrector ortográfico/Listado, by its contributors (`listado.txt`), not carried by the app: downloaded unchanged at revision 175669831 (2026-10-05), checked by its SHA-256, when Spelling's Use the Wikipedia's common misspellings is turned on | CC BY-SA 4.0 | https://es.wikipedia.org/wiki/Wikipedia:Corrector_ortogr%C3%A1fico/Listado |

## Icons

| Icon | Licence | Source |
| --- | --- | --- |
| The clipboard (`Resources/drawable/ic_clipboard.xml`), Material Icons' content_paste | Apache-2.0 | https://github.com/google/material-design-icons |
| The standard keyboard's keys and toolbar (`Resources/drawable/ic_key_*.xml`, `ic_tool_*.xml`, `ic_grid_*.xml`, `ic_settings_*.xml`, `ic_emoji_*.xml`, `ic_menu_*.xml`), Material Symbols (outlined); `ic_grid_pc.xml`, `ic_grid_floating*.xml` its computer with "PC", keys or a slash drawn on | Apache-2.0 | https://github.com/google/material-design-icons |
