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
