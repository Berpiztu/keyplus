# To test: turning on over Wireless debugging

Since 1.0.54 the pairing (`AdbGrant.cs`) runs the commands that turn on
what was asked there and then, and grants the app nothing to keep (no
WRITE_SECURE_SETTINGS); it has been built, not yet seen working on a device.
To test on the phone, the headset and INAIR's pod:

- Berpiztu AI Keyboard: **Turn it on with Wireless debugging** turns it on
  and makes it the keyboard in use (`ime enable`, `ime set`).
- Its menu watch: **Let it see the menus** › Wireless debugging turns it on,
  the other accessibility services kept.
- KeyPlus: **Turn it on with Wireless debugging** turns KeyPlus keys on, the
  other accessibility services kept.
- INAIR's pod: whether it has Wireless debugging at all; without it, a
  computer's `enable.ps1` stays the way.
