# Keys: two rows (compact)

The owner's request, 17 October 2026: in Keys mode the window has a title bar
and three rows of keys, too much on a tablet (a Fold open), which has room
across but not to spare up and down. A toggle turns it into two rows, as
Termux lays out its extra keys. (First asked as one row; two, for the keys
do not fit one row well.)

## Today

```
┌────────────────────────────────────────────────────────────┐
│ ◤   A−  A+  A↺  ABC  ⤓  –  ✕                             ◥ │  title bar
├───────┬───────┬───────┬───────┬───────┬───────┐            │
│Ctrl+A │Ctrl+C │Ctrl+V │Ctrl+Z │Ctrl+Y │  Del  │            │  row 1
├────┬──┴─┬─────┼────┬──┴─┬─────┼────┬──┴──┬────┤            │
│Esc │ /  │Shift│Home│ ↑  │ End │PgUp│ ⌫   │                 │  row 2
├────┼────┼─────┼────┼────┼─────┼────┼─────┤                 │
│Tab │Ctrl│ Alt │ ←  │ ↓  │  →  │PgDn│ ⏎   │                 │  row 3
└────┴────┴─────┴────┴────┴─────┴────┴─────┘
```

## What is asked

- **A toggle**, an arrow: **▲** when the window is full, **▼** when it is
  two rows.
- **Full** (as today): the title bar and the three rows.
- **Two rows**: no title bar and no row 1 (the Ctrl+A … Ctrl+Y, Del
  shortcuts); rows 2 and 3, which are Termux's two rows of extra keys.
- Pressing the toggle goes from one to the other.

```
two rows:
┌────┬────┬─────┬────┬────┬─────┬────┬─────┬───┐
│Esc │Shit│Home │ ↑  │End │PU│ ⌫/sup │
├────┼────┼─────┼────┼────┼──┼──┼─────┤   │
│Tab │Ctrl│  ←  │ ↓  │  → │PD│At│  ⏎  │ 
└────┴────┴─────┴────┴────┴─────┴─── ─┴─
```

### The owner's drawing, as read (to confirm)

```
┌────┬─────┬──────┬────┬─────┬──┬────────┐
│Esc │Shift│ Home │ ↑  │ End │PU│ ⌫/Supr │
├────┼─────┼──────┼────┼─────┼──┼──┬─────┤
│Tab │Ctrl │  ←   │ ↓  │  →  │PD│Al│  ⏎  │
└────┴─────┴──────┴────┴─────┴──┴──┴─────┘
```

- **No toggle key.** A long press on **PgUp** opens the full Keys window
  (title bar and three rows); a tap is PgUp, as ever, but it no longer
  repeats while held. (First Esc, moved to PgUp, 17 October 2026: Esc is in
  the corner, where the grip that resizes the window is.)
- **Back to two rows**: a button in the full window's title bar.
- **The / is gone.** The arrows stay a cross: Home over ←, ↑ over ↓, End
  over →.
- **PgUp over PgDn**, both narrow (half a key).
- **Alt** moves to the bottom row, narrow, beside PgDn.
- **⌫ and Supr share a key**, wider than ⌫ is today: it takes the room the
  narrow PgUp leaves, as wide as Alt and ⏎ under it. ⌫ (Backspace) as it
  is, **Supr** (delete forward) with Shift held.
- **⏎** is wider, as wide as ← is.

## Proposed details (to confirm)

1. **Where the toggle stands.** A column of its own at the right of the two
   rows (above), so no key moves; when full, in the title bar's right corner.
2. **Which screens.** Available on every screen (phone, tablet, DeX), or on
   the tablet alone.
3. **Kept.** Whether the window is two rows or full is kept for each screen
   and mode, as its place and size are (`doc/diseño.md`).
4. **Moving and resizing with no title bar.** The window moves by holding the
   toggle still a moment, or the ground between the keys, as today; the
   corner grips stay at its corners.
5. **A−, A+, A↺, ABC, ⤓, –, ✕** are in the title bar only, so not reachable
   in two rows: to reach them, back to full.
