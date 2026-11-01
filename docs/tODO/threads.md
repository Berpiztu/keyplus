# To do: the screen's thread draws, everything else elsewhere

The owner, 2026-11-01: "la pantalla tiene que ir en un hilo y todo lo demás en otro hilo o hilos. Igual no es ahora
para hacer, pero apúntalo." Not started; to be reviewed whole, on Android and Windows.

## The rule to reach

- The UI thread only draws and takes touches and keys.
- Every other work runs away from it: the context model, Hunspell, the word lists, the dictionary, the downloads,
  reading and writing files.
- Its answer comes back to the UI thread, and is shown only where the word or field it was for is still the one in
  sight (as the model's suggestions already do: `KeyPanel.OfferModelWords`, `KeyboardWindow.OfferModelWords`).

## What it has cost already

- 2026-11-01, Android: a context model's download touched the network on the main thread; Android closed the app
  (`NetworkOnMainThreadException`). Moved to a task.
- 2026-11-01, both: what a word nothing starts as may have meant (`SpellingEngine.Meant`, Hunspell) took 6 s for
  "corrrecta" on the keys' thread; the keyboard stopped, then typed three keys at once. Moved to a task.

## Still on the UI thread, to look at

- A word's correction at a space (`KeyPanel.FixWord`, `KeyboardSurface.FixWord` through `SpellingEngine.Fix`):
  Hunspell, cached by word.
- The word list's suggestions as each letter is typed (`SpellingEngine.Suggest`).
- `IsWord`, `Unknown`, `Knows` as a word is typed and at a space.
- Loading a language's word list and dictionary, and the context model (`ContextCorrector.Load`).
- Anything else found by walking every key's path, on both keyboards.

## How to check it

- Android: StrictMode on in Debug, so any disk or network on the main thread is said in the log.
- Both: the time each step takes in the log (Detail), the slowest looked at.
