# Key labels: analysis and plan

How the keys' labels are laid out, why they were hard to read, and what was
changed. The rules behind it are in [rules.md](../rules.md): the labels must
be read well, with no room wasted on margins and empty gaps.


las teclas tienen repeticion
para mover haz la barra titulo mas alta y pulsacion laga mueve el teclado

NOTAS
Las teclas de cursor Hazlas un poquito más grande 
Mira cómo te mira como te he puesto la pantalla Date cuenta que los las teclas de puntuación Pero las leyendas las leyendas de puntuación Me punto la Coba todas estas casi no se ven y tiene espacio en la tecla Entonces en esos casos Como estamos con el 3 por 3 Pues igual en esos casos Tiene que ocupar Dos positivos De la tabla Para que así sea más grande La leyenda Me explico porque si no casi no se ve Jalvas que de cerca no veo bien

Y luego lo de mover Lo que pasa es que Se choca con lo de agrandar Creo que está muy cerca Y además si te vas si te fijas en la imagen también Como los a+ y a- también aumenta de tamaño no hay sitio para Para que con el dedo se pueda deslizar y mover 
La repetición de teclas no funcion

Mira las teclas En el emulador. A mano derecha Casi no se ven. Especialmente : . ,-´`y  quizas deberian Ocupar dos filas O sea debería de ser Ocupar la fila 0+1 y 1+2 Yo creo que eso es de puntuación Si tiene que ser el 50% más grande que el resto

Y cuando se bloquee Teléfono dile que desaparecer De pantalla Nuestro keyplus porque ahora se ve incluso Con el teléfono bloqueado

con shift y altgr  se quedan solo visibles las validas y se mueven a la posicion 1:1 
Ahora, por favor, guárdame. La configuración que yo haga en cada estado. Quiero decir, si estoy en modo key, el tamaño que yo deje en modo key es el que quiero ver la próxima vez que abra. Si estoy en modo teclado, el tamaño que yo deje es el tamaño y la posición que yo quiero ver la próxima vez que abra. El icono que se queda en pantalla se deja en el sitio donde yo lo haya dejado. En modo DEX tiene que diferenciar modo DEX de modo horizontal y vertical. O sea que hay tres modos. Horizontal, vertical y modo DEX. Y en cada uno de ellos se tiene que acordar de qué tamaño del teclado dejé yo qué tamaño de case y qué sitio lo he dejado

Cuando estoy en keys, y le doy al botoncito de acoplarse al teclado, eso significa que se aparecerá y desaparecerá el keyplus en función del teclado. Pero la posición que yo le deje en pantalla será la que tenga que aparecer. Y el tamaño. Y toda la, su configuración. ¿Vale? En el modo acoplado, tanto en apaisado, en horizontal, en vertical, como en dex, si lo tengo puesto en modo acoplado, significa que aparece y desaparece en función de cuando aparece o desaparece el teclado de Android. Donde yo lo dejo, como yo lo dejo, es como se tiene que abrir.

Y luego hay una tecla muy peligrosa, que es la de reset. ¿Vale? Por una confirmación en la de reset, por favor, porque es que sin darte cuenta, si le das a reset, pues todo para atrás.

y por favor pon el estilo de claro a oscuro en función del sistema. Porque si el sistema está en oscuro, pues que sea en oscuro. Si está en claro, que sea en claro. 


┌─┌─┬─┬─┬─┬─┐
├─┼─┼─┼─┼─┼─┤
├─┼─┼─┼─┼─┼─┤
├─┼─┼─┼─┼─┼─┤
├─┼─┼─┼─┼─┼─┤
├─┼─┼─┼─┼─┼─┤
└─┴─┴─┴─┴─┴─┘

┌─────┬─┬─┬─┐
│  ;  ├─┼─┼─┤
│  ;  ┼─┼─┼─┤
│  ;  ┼─┼─┼─┤
├─┬─┬─┼─┼─┼─┤
├─┼─┼─┤,    │
├─┼─┼─┤,    │
├─┼─┼─┤,    │
└─┴─┴─┴─────┘


┌─────┬─┬─┬─┐
│  ;  ├─┼─┼─┤
│  ;  ┼─┼─┼─┤
│  ;  ┼─┼─┼─┤
├─┬─┬─┼─┼─┼─┤
├     ,     │
├     ,     │
├     ,     │
└─┴─┴─┴─────┘



       └───┴───┴───┘

=======================================================

## Analysis (before the change)

Measured on the emulator (Pixel 7, 1080 × 2400 px, 420 dpi: 2.625 px per dp),
the whole Spanish keyboard (ABC), floating across the screen.

### Portrait: the keys are too narrow for what they hold

| What | Size |
| --- | --- |
| Window | 1080 px = 411 dp; 407 dp inside its 2 dp side padding |
| Share of one key | 407 dp / 16.5 (15.5 for the rows, 1 for the side column) = 24.7 dp |
| Key, after its 2 dp margin on each side | 20.7 dp wide, 52 dp tall |
| Room for the label of a character key, after its 6 dp padding left and right and 3 dp above and below | **8.7 dp wide**, 46 dp tall |

Almost 60 % of a key's width went to margins and padding, while most of its
height stayed empty. What it gave:

- **Words cut** by the key's edge: Esc → `Es`, ImpPnt → `Im`, Inicio → `Ini`,
  Fin → `Fir`, Bloq May → `Bloq`, Mayús → `May`, Supr → `Su`, RePág → `Re`,
  AvPág → `Av`, Insert → `Ins`, Ctrl → `Ctı`, Alt Gr → `Alt`, Entrar →
  `Entrai`; even `m` and `w` lost their sides.
- **Legends on top of each other** on the keys with three (`1 ! |`, `2 " @`,
  `3 · #`, `4 $ ~`, `5 % €`, `6 & ¬`, `º ª \`) and on `` ` ^ [ ``, `+ * ]`,
  `´ ¨ {`, `ç }`.

### Landscape: room to spare, labels small

At 2400 px wide the keys are wide enough, but the letters took about a third
of the key's height and the rest stayed empty; the keys with two legends
(`? '`, `* +`, `¨ ´`, `; ,`, `: .`, `_ -`) drew their small glyphs twice as
large and outweighed the letters.

### Causes, in the code

1. **Fixed sizes, not the key's room.** A label started at 18 sp (16 sp for
   Ctrl, Alt and the like) and the TextView's auto-size shrank it down to 8 sp;
   below 8 sp it was cut, not made smaller.
2. **Margins and padding** of 2 dp around each key and 6 / 3 dp inside each
   character key, whatever the key's width.
3. **The Alt Gr character was drawn apart**, 5 dp from the key's bottom right
   corner, not knowing where the plain one had ended up: in a narrow key they
   met.
4. **Two lines of different sizes** (the shifted one at 70 %) set in one
   TextView, with small glyphs drawn twice as large over them.

## The design agreed

- **A table of three by three cells in every key** (row:column, 0:0 the top
  left). Each legend has its cell, on the diagonal, so none can cover another:

  ```
  three legends     two legends       one legend
  ┌───┬───┬───┐     ┌───┬───┬───┐     ┌───┬───┬───┐
  │ " │   │   │     │ = │   │   │     │   │   │   │
  ├───┼───┼───┤     ├───┼───┼───┤     ├───┼───┼───┤
  │   │ 2 │   │     │   │ 0 │   │     │   │ g │   │
  ├───┼───┼───┤     ├───┼───┼───┤     ├───┼───┼───┤
  │   │   │ @ │     │   │   │   │     │   │   │   │
  └───┴───┴───┘     └───┴───┴───┘     └───┴───┴───┘
  ```

  The shifted character at 0:0, the plain one at 1:1, the Alt Gr one at 2:2.
  A letter has one legend (its upper case is its own), plus € on the e.
- **Every legend one size**, except the small glyphs, drawn larger as before
  (`" · = ? ¿ ' ` ^ + * ´ ¨ ; , . : - _ \ | ~ ¬` twice, `[ ] { }` and the
  arrows half as large again, `@` a quarter).
- **The legend the key sends now is strong** (white), the others weak (grey):
  the plain one with nothing held, the shifted one with Shift, the Alt Gr one
  with Alt Gr. All three stay in sight.
- **Smaller, never cut**: a label that does not fit its key is drawn smaller,
  with no smallest size, and is read whole.
- **Margins to the least**: 1 dp around each key, 1 dp inside it.
- **The title bar taller**, 40 dp, and **a long press moves** the window from
  it, as from a key or the ground between the keys.

## What was changed

| File | Change |
| --- | --- |
| [KeyLegends.cs](../KeyLegends.cs) | New. Draws a character key's legends over it on the table of three by three, all at one size, the one sent strong; a legend wider than the key is drawn smaller; a small glyph is centred on what it draws. |
| [KeyPanel.cs](../KeyPanel.cs) | Character keys draw their legends with KeyLegends and read no text themselves. The one size comes from the key's height: a line of text (the font's top to its bottom) as tall as a third of the room inside the key. The other keys (Ctrl, Mayús, Entrar, the arrows…) take that same size as their largest, an arrow half as large again, and auto-size down to 1 px, so they shrink and are never cut. Margins and padding 1 dp. Title bar 40 dp; the title moves the window on a long press. A− and A+ still size the character keys' legends. |
| [PanelKeys.cs](../PanelKeys.cs) | `HasThreeLegends` removed: the table has a cell for every legend. |
| KeyLabel.cs, CornerLabel.cs, BigGlyphSpan.cs | Removed: the shifting of three legends to the left, the Alt Gr corner and the larger glyph inside a line are all KeyLegends' now. |

### The one size, worked out

`size = (row height − 2 × margin − 2 × padding) / 3 / line height of the font at 1 px`

At the usual keys' size a row is 56 dp, so the room is 52 dp and a cell
17.3 dp tall; with Roboto's line of about 1.17 times its size, every legend is
about 14.8 dp, in portrait and landscape alike. A corner that makes the keys
larger or smaller makes the legends so too.

## Status

- Debug build: succeeded, no warnings.
- Deployed to the emulator with `debug.ps1 -Emulator`.
- **To check**, on the emulator and then on the phone: portrait and landscape,
  every key's labels whole and none over another; the strong legend following
  Shift and Alt Gr; Fn's F1 to F12; the title bar moving the window on a long
  press.

## Still open

- The README's "Using it" and the app's own text (MainActivity) still say the
  title bar is dragged and describe the old legends: to be brought up to date
  once the change is approved.
- rules.md ends in the middle of a sentence ("so every APK installs over").
