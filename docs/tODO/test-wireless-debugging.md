# To test, whole: turning on over Wireless debugging (ADB)

Since 1.0.54 the pairing (`AdbGrant.cs`) runs the commands that turn on
what was asked there and then, and grants the app nothing to keep (no
WRITE_SECURE_SETTINGS); it has been built, not yet seen working on a device.
The whole test, on the phone, the headset and INAIR's pod, each from a
fresh install (uninstalled first), then again over an install:

1. Wireless debugging, turned on by the user: the app opens Developer
   options at it; without Developer options it says how to turn them on.
2. Berpiztu AI Keyboard, **Turn it on with Wireless debugging**: the code
   typed in the notification, without leaving Settings; the keyboard turned
   on and made the one in use (`ime enable`, `ime set`); the notification
   says it is on, or what failed.
3. Its menu watch, **Let it see the menus**: the window says what it sees
   and why; Wireless debugging turns it on, the other accessibility
   services kept; Android's switch turns it on too, Allow restricted
   settings first where Android asks.
4. KeyPlus, **Turn on KeyPlus keys** and **Turn it on with Wireless
   debugging**: the same, for KeyPlus keys.
5. Android forgets (a reboot, an update): turned on again by pairing again.
6. INAIR's pod: whether it has Wireless debugging at all; without it, a
   computer's `enable.ps1` turns both on.
7. A new version: the app's question, and its yes opening the release's
   page on GitHub.
