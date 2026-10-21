# Berpiztu AI Keyboard: how it opens the first time

A review, 21 October 2026, before anything is changed: what the keyboard
looks like the first time it opens on a phone, why, and what is proposed.
Nothing here is decided until the owner says so; points are fixed together
afterwards.

Seen on the Pixel 7 emulator (Android 15, API 35, 1080 × 2400, 420 dpi),
Berpiztu AI Keyboard as first installed (its data cleared), docked, opened
in its own app's "Try the keys here" box.

## What it shows

```
┌──────────────────────────────────────────────────────┐
│ ⤡     A−  A+  A↺  ▭  📋  ABC  –  ⚙               ⤢ │  title bar
├────────┬────────┬────────┬────────┬────────┬─────────┤
│ Ctrl+  │ Ctrl+  │ Ctrl+  │ Ctrl+  │ Ctrl+  │   Del   │
│   A    │   C    │   V    │   Z    │   Y    │         │
├──────┬─┴────┬───┴──┬─────┴┬──────┬┴─────┬──┴───┬─────┤
│ Esc  │  /   │Shift │ Home │  ↑   │ End  │ PgUp │  ⌫  │
├──────┼──────┼──────┼──────┼──────┼──────┼──────┼─────┤
│ Tab  │ Ctrl │ Alt  │  ←   │  ↓   │  →   │ PgDn │  ⏎  │
├──────┴──────┴──────┴──────┴──────┴──────┴──────┴─────┤
│ ⤢                                                 ⤡ │  bottom bar
└──────────────────────────────────────────────────────┘
```

## Findings

### 1. It opens without letters

- **Seen:** the first thing it shows is the strip of shortcuts (Keys): no
  letter can be typed until ABC is pressed. A keyboard opens on its letters,
  as Gboard does.
- **Cause:** `KeyPanel.cs:453` reads `wholeKeyboard` with `false` as its
  default, so a screen with nothing kept opens in Keys
  (`PanelPlacement.KeysMode`).
- **Proposed:** the whole keyboard (ABC) as the default the first time, on
  every screen.

### 2. The four resize grips are always in sight

- **Seen:** ⤡ and ⤢ at both ends of the title bar and at both ends of a
  bar of their own under the keys, a row that holds nothing else.
- **Cause:** on a phone `KeyPlusKeyboard.cs:185` sets `panel.LargeGrips =
  true`: each grip 40 dp wide (`LargeGripDp`) and the bottom bar 28 dp tall
  (`LargeBottomBarDp`), always.
- **Proposed:** no grips while typing; sizing only from ⚙ → Redimensionar
  (KeyboardFrame), as Gboard. The bottom bar goes with them.

### 3. The title bar is full of tools

- **Seen:** A−, A+, A↺, ▭, 📋, ABC, – and ⚙ side by side
  (`KeyPanel.cs:295-312`).
- **Proposed:** in the bar only what is used while typing, as Gboard: 📋,
  ABC/Keys and ⚙. A−, A+ and A↺ (label sizes, set once) inside ⚙.
  ▭ and – to be decided.

### 4. The labels do not match each other

- **Seen:** "Ctrl+A" … "Ctrl+Y" in two large lines; "Home" smaller than
  "End"; "PgUp" and "PgDn" smaller still; ⏎ grey, as if it were off.
- **Cause, so far:** a word takes at most three quarters of its key
  (`WordShare`, `KeyPanel.cs:89`), and a word that does not fit at the
  words' one size is made smaller alone, down to 0.7 of the legends'
  (`WordFloor`): Home and PgUp, longer in keys as narrow as End's, shrink
  alone. Why Ctrl+A breaks into two lines and why ⏎ is grey: to measure.
- **Proposed:** to decide with the owner (one size for every word, or
  shorter words).

### 5. ← and → stand lower than ↑ and ↓

- **Seen:** the bottom row's horizontal arrows sit below the middle of their
  keys; the vertical ones are centred.
- **Cause, so far:** the arrows are drawn 1.75 times larger than the other
  labels (`SmallGlyphs`, `PanelKeys.cs:281`); where each glyph's ink sits in
  its font box: to measure.
- **Proposed:** every arrow centred on its key by its ink, measured.

## The owner's points

To be added as the review goes on.
