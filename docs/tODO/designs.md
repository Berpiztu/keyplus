# Keyboard designs (ABC only)

Agreed with the owner, 21 October 2026.

## Done (⚙ → Diseñar, KeyboardDesign.cs)

- Each screen keeps its own design (preference `abcDesign`).
- Drag a key to another place, in its row or another; nothing else moves: it leaves a hole as wide as it, and takes a hole it is dropped on.
- Dragged off the keys, a key goes to the trunk above them, from where it is dragged back.
- Tap a key to mark it; − and + make it a quarter narrower or wider, the room given or taken as a hole after it.
- Export designs / Import designs (app screen): every screen's design as one file, sent anywhere and imported on another device; the designs replaced are kept on the device first.
- + Fila: an empty row at the top. Repartir: holes and empty rows gone, every key the same size, the longest row as wide as the keyboard. Restablecer: as made.

## To do

1. Designs: a library on the device, Design 1, 2, 3… each a pair, vertical and horizontal; an empty half uses the other (headset, pod: one is enough).
2. Turning the device picks the vertical or horizontal half of the screen's design.
3. Screens: phone (Fold closed) split into vertical and horizontal, as a normal phone; Fold open vertical and horizontal; DeX.
4. From ⚙: choose a design, new (a copy of the current one), rename, delete.
5. Keys mode (the strip): not designed; it stays as it is.

## The owner's design, 21 October 2026

Made on the Pixel 7 emulator (phone screen), copied to the Fold5 (closed):

```
13.5|60,-100,58,59,1,40,71,72;0,2,3,4,5,6,7,8,9,10,11,12,13;15,16,17,18,19,20,21,22,23,24,25,26,27;29,30,31,32,33,34,35,36,37,38,39,67,56;44,45,46,47,48,49,50,53,52,69,54,70,14;57,42,43,51,61,62,64,65,66,41|68,55,63,28|61:3.25,15:1.25
```
