# ADB in a utility of its own

Turning the apps on over the device's own Wireless debugging, no computer
needed, is out of both APKs: Play Protect's server calls an app that carries
an ADB client harmful (`POTENTIALLY_UNWANTED`, `generic_malware`). 1.0.55,
without it, installed with no warning; 1.0.56, the same with it, was called
harmful again. The code is kept, off: `KeyPlusAdb` in
`Berpiztu.KeyPlus.csproj` (`AdbGrant.cs`, `AdbShell.java`, libadb-android,
spake2-android, Bouncy Castle, Conscrypt).

To do: a utility of its own, apart from the keyboard and the popup, that
pairs with Wireless debugging (the code typed in its notification) and runs
what turns them on, there and then:

- Berpiztu AI Keyboard: `ime enable` and `ime set`.
- Its menu watch, or the popup's KeyPlus keys: the service added to
  `enabled_accessibility_services`, and `accessibility_enabled 1`.

Until then a computer's `enable.ps1` does it, where Android hides the
switches (a headset, INAIR's pod).
