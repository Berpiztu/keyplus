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
- Se ofrece con ✓ en la barra, no se aplica solo, por sus fallos; y solo añade, nunca quita lo que se escribió.
- Se descarga por idioma al activarlo, como el resto.
