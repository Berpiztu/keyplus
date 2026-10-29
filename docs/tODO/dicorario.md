Te voy a poner aquí las preguntas. A ver, ¿me estás diciendo que esta solución pongo una tabla comparativa de utilizar este modelo en castellano con lo que nosotros tenemos ahora? O sea, corrige puntos y comas, corrige mayúsculas qué es lo que corrige este y qué es lo que el otro estaríamos.

## Respuesta: lo que corrige hoy el teclado y lo que añade el modelo de puntuación (castellano)

Modelo probado el 2026-10-28: `1-800-BAD-CODE/punctuation_fullstop_truecase_romance` (Apache-2.0, 144 MB, ONNX, revisión 1e8d395). Recibe la frase en minúsculas y sin puntuación; devuelve puntuación, mayúsculas y dónde acaba cada frase. Entre 3 y 70 ms por frase, sin GPU.

| Qué se corrige | Hoy en el teclado | El modelo nuevo |
|---|---|---|
| Palabras mal escritas ("kasa" → "casa") | ✅ Correct words (diccionario de LibreOffice) y errores comunes de la Wikipedia | ❌ No cambia palabras |
| Tildes ("como estas" → "cómo estás") | ✅ Modelo de acentos diactag (38 MB) | ❌ No pone tildes |
| Mayúscula al empezar frase | ⚠️ Regla con muchos `if`; falla al borrar o moverse | ✅ Leyendo la frase entera |
| Mayúscula de nombres ("españa" → "España", "iosu" → "Iosu") | ⚠️ Solo palabras que el diccionario tiene siempre con mayúscula | ✅ También nombres que no están (Iosu, Chrome, Windows); con fallos ("GithUb") |
| Puntos (dónde acaba una frase) | ❌ Nada | ✅ Con fallos (a veces corta donde no toca) |
| Comas | ❌ Nada | ✅ Bastante bien |
| ¿ al principio | ⚠️ Solo si se cierra con ? (Put ¿ and ¡ at the start) | ✅ Decide él si es pregunta; con fallos (a veces inventa una) |
| ? al final | ❌ Lo pone quien escribe | ✅ Con fallos (a veces ? sin ¿) |
| ¡ y ! | ⚠️ Solo el ¡ si se cierra con ! | ❌ No los conoce |

Resultados con frases reales (entrada en minúsculas y sin puntuación):

- ✓ "Hola, Iosu. ¿Qué tal estás? Vamos a probar el teclado de Windows en Vs Code y en Chrome."
- ✓ "Pues ahora hago prueba de Chrome, pero yo creo que Chrome y este es lo mismo, ¿no?"
- ✓ "Si ayer no fallaba, ¿con qué usuario estás, con qué usuario estás conectándote al GithUb?" (bien salvo "GithUb")
- ✗ "Ayer o desde ayer? Arreglaste que cuando estás viendo la fila de iconos, la fila de emojis no se podía mover con el ratón, el teclado y lo arreglaste. Pero, ¿qué ocurre?"
- ✗ "Mira en el proyecto de Klus. ¿Por qué puede pasar que cuando estoy con el transcriptor de voz a texto? Bueno, hay veces que…"
- ✗ "Qué bien que funciona? Por fin, estoy muy contento." (no conoce ¡!)

El modelo de 47 idiomas (233 MB) salió peor en castellano; en inglés lo hizo bien. Para inglés hay uno propio (`punct_cap_seg_en`, 209 MB), sin probar.

## Conclusión

- No se quita nada de lo que hay: Correct words, la Wikipedia y el modelo de tildes siguen; el modelo nuevo no hace nada de eso.
- Se añade el modelo de puntuación, como complemento: puntos, comas, ¿?, frases y mayúsculas.
- Sustituye solo la regla de la mayúscula al empezar frase (la de los `if`): la mayúscula la decide el modelo leyendo el campo.
- Necesita leer el campo (UI Automation): comprobado en VS Code y Chrome; falta el Bloc de notas.
- Se aplica solo con Autocorrect y solo en un documento (ver abajo); en otro campo se ofrece. Solo añade, nunca quita lo que se escribió.
- Se descarga por idioma al activarlo, como el resto.

## Estado al 2026-10-28 (Windows) y lo siguiente

Hecho en Windows (commits c38445b y el siguiente):

- Modelo de puntuación por idioma: castellano (`punctuation_fullstop_truecase_romance`, 138 MB en disco) e inglés (`punctuation_fullstop_truecase_english`, 201 MB); opción "Punctuate with the model" en cada idioma; va antes del modelo de tildes.
- Autocorrect (palabras, puntuación, tildes, mayúscula de frase, ¿¡) solo si el campo es seguro un documento: UI Automation dice documento o `multiline=true`. Barra de direcciones (una línea) y buscador de Google (combo box) no lo son. Las sugerencias salen siempre.
- Lectura del campo por UI Automation (TextPattern): funciona en VS Code (editor y chat de Claude) y Chrome. Falta probar el Bloc de notas y Word.
- Si el teclado pierde el hilo (dictado, clic, teclado físico, flechas), la frase se lee del campo al cerrarla y se corrige allí; en Enter espera a que el campo termine con la palabra recién escrita.
- Las flechas olvidan la palabra en curso (ya no se unen dos líneas).
- Dictado: solo reescribe las 3 últimas palabras; una tecla, un clic o Enter la congela.

Lo que ocupa hoy en disco (`%LOCALAPPDATA%\Berpiztu\Keyboard`): 390 MB (puntuación inglés 201, castellano 138, tildes 37, deslizar 8, listas 5, diccionarios 3).

Siguiente (por orden):

1. **Corrector con contexto**: sin contexto hay un techo ("you now" → "you know", "maña" → "mañana" no se pueden arreglar). Un modelo de lenguaje pequeño (1.000-1.500 M parámetros, 0,7-1 GB) valdría para los dos idiomas y para todo (palabras, contexto, tildes, puntuación). Propuesta: en el servidor del usuario (compatible con OpenAI, como el de IA-BotMsgs), configurado como la voz (este equipo, red local o remoto); el teclado manda la frase al cerrarla y solo acepta cambios pequeños (letras o una palabra parecida), si reescribe se descarta; sin servidor, los modelos de ahora. El problema de tenerlo en el teclado no es el tamaño sino el tiempo sin GPU (segundos por frase).
2. **Android**: llevar lo de Windows (puntuación por idioma, solo en documentos, leer el campo) cuando el usuario dé el OK; en Android el campo ya se lee (`GetTextBeforeCursor`) y el tipo de campo lo dice `EditorInfo`.
3. **Quitar los `if` de la mayúscula de frase** (`KeyboardSurface.OpensSentence`): decidirla leyendo el campo, como ya hace el modelo.
4. **Que el corrector de palabras lea el campo** para saber dónde empieza la palabra tras un clic (hoy la deja como está).
5. Probar el Bloc de notas y Word (¿se presentan como documento?).
