# Rules

Rules for everyone working on KeyPlus, people and AI agents alike.
NO COMMITS SIN PREUNTAR
SI HAY DUDA PREGNTAR ANTE DE CAMBIaR
NO IMAGINAR, COMPROBAR
L TIEMPO ES ORO, EL METODO PRUEBA O FALLO PROHIBIDO,


mira el desperdicio de marqnes y hueco vacios . prioridad q se vean bien las etiquetas

PIENSA BIEN, LLEVAMOS 20 BUILDS DE 30 con esto
LOS TAMAÑOS DE LAS  LEYENDAS SE MIDEN , No está permitido IMAGINAR O fiarse de la vista

a , Vamos a pensar que hay como una tabla de 3x3, primera 0:0 ", 1:1 2 2:2 @ 
caso 2 etiquetas 0:0 =  1:1 0
bien?.
caso 1 1:1 g



las teclas tienen repeticion
para mover haz la barra titulo mas alta y pulsacion laga mueve





- **No co-authorship.** Commits and pull requests carry no Co-Authored-By
  line or any other attribution trailer.
- **Release builds and APKs are the owner's.** Agents check a change with a
  Debug `dotnet build` only; they never run `build-apk.ps1`, `dotnet publish`
  or a Release build.
- **English** for code, comments, docs and commit messages.
- **The version is 1.0.0.x**, x the build number 
  [version.props](version.props), which `build-apk.ps1` raises by one on every
  APK it builds. It is also the Android version code, so every APK installs
  over 