# Rules

Rules for everyone working on KeyPlus, people and AI agents alike.
NO COMMITS SIN PREUNTAR
SI HAY DUDA PREGNTAR ANTE DE CAMBIaR
NO IMAGINAR, COMPROBAR
L TIEMPO ES ORO, EL METODO PRUEBA O FALLO PROHIBIDO,


mira el desperdicio de marqnes y hueco vacios y etiquetas con tamaño tan pequeño que casi ni se ven. el espacko en blanco  es el 75% del tamaño de l tecla.

PIENSA BIEN, LLEVAMOS 20 BUILDS DE 30 con esto

quieo un espaio para etiquetas el 75% y 25% para margenes y huecos

iguales con espacio, falta de espacio i es vertical primeo mover " a l izqierda par que no choque vericlmente on l 2, si no se puede ir reduciendo el "

las teclas tienen repeticion
para mover haz la barra titulo mas alta y pulsacion laga mueve


C:\IA\berpiztu\keyplus\dist\Screenshot_20261017_035259_Package installer.jpg las de 3 estn bien, ls de 2 muy grandes, las de 2 psamos a 50% y  al darle a shift o altgr, tienen q tener l tamaño de una eiqueta


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