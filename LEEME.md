# Instalar Extremo 3

Instalador para Windows x64. Los jugadores ejecutan **InstalarExtremo.exe**, cierran Minecraft y su launcher y pulsan **Instalar**. No tienen que descargar Java, NeoForge ni el mod por separado. No pide credenciales ni administrador.

## Qué hace

- Detecta carpetas habituales y elige Prism cuando está disponible. La selección se muestra en pantalla; si hay varios launchers, el jugador puede cambiarla.
- Descarga Extreme 3 Mechanics v14 desde una release fija de este repositorio y comprueba su SHA-256 incorporado en el EXE.
- Descarga Temurin Java 21.0.13+9 x64, verifica SHA-256, extrae de forma controlada y comprueba Java 21 de 64 bits. Lo guarda dentro de la instancia; no cambia Java del sistema.
- En el launcher oficial, descarga Minecraft 1.21.1 desde Mojang usando los hashes oficiales, descarga y verifica NeoForge 21.1.265 y ejecuta su instalador en una carpeta temporal privada. Añade un perfil propio con directorio de juego aislado y respalda los perfiles previos.
- En Prism, crea directamente una instancia con Java privado, el mod y las versiones exactas. En el primer inicio Prism descarga Minecraft/NeoForge y sus bibliotecas. No requiere importar ZIP.
- En otros launchers, descarga y prepara todo lo necesario y genera PASOS.txt. Hay que crear la instancia desde ese launcher y elegir su carpeta de juego. No existe un formato universal para automatizar cualquier launcher.

## Para jugar

1. Descargar el EXE desde [Releases](https://github.com/cazapan/extremo3-instalador/releases).
2. Cerrar Minecraft, el launcher oficial y Prism. Ejecutar el EXE como usuario normal.
3. Revisar el launcher seleccionado y pulsar Instalar. Esperar el mensaje final.
4. Abrir el launcher y elegir **Extremo 3 · 1.21.1** (oficial) o **Extremo 3 - 1.21.1** (Prism).

El launcher oficial debe haberse abierto al menos una vez para crear launcher_profiles.json. No hace falta haber ejecutado Minecraft 1.21.1. Las credenciales se introducen solo en el launcher habitual. Si Prism es portable, seleccionar el modo Prism e indicar su carpeta **instances**. Las rutas detectadas por carpetas son orientativas.

## Mod y compatibilidad

Archivo del autor: [mcextremo-v14.jar, file 8401810](https://www.curseforge.com/minecraft/mc-mods/extreme-3-mechanics/files/8401810). SHA-256: `802ec898098ae43bd15a7bab69f3063e32803aedcb926253eb4654f0b2f565a8`.

El JAR declara internamente versión **1.3.1**, ID **mcextremo**, Minecraft `[1.21.1,1.21.2)` y NeoForge `[21.1.228,)`, ambos con `side=BOTH`. No declara otras dependencias ni contiene JAR de dependencias adicionales. La versión 21.1.265 cumple sus rangos. El nombre de archivo v14 y la versión interna 1.3.1 son distintos en el archivo original.

Incluye HUD, animaciones y sincronización: se prepara para **cliente y servidor**; no se trata como server-side-only. El servidor debe usar las mismas versiones. El instalador no configura el servidor ni acepta su EULA.

El organizador confirmó que tiene permiso del creador para redistribuir este JAR. El mod conserva **All Rights Reserved**. La licencia MIT del proyecto cubre únicamente el código propio del instalador. Fuente y autor: [EricReck / Extreme 3 Mechanics](https://www.curseforge.com/minecraft/mc-mods/extreme-3-mechanics).

## Comprobaciones y límites

El EXE lleva versiones, URLs y hashes incorporados. No usa latest ni una API key de CurseForge. Mod, Java y el instalador de NeoForge se verifican con SHA-256. Mojang publica SHA-1 para metadata y cliente; se validan esos hashes oficiales. Las bibliotecas transitivas se verifican mediante el instalador oficial NeoForge o Prism.

Solo se aceptan HTTPS y los servidores previstos, incluidas las redirecciones de GitHub Releases. Se limitan tamaño, redirecciones y tiempo; se limpian descargas incompletas y se puede cancelar. La extracción ZIP rechaza rutas fuera de la carpeta y enlaces Unix. No se recopila telemetría ni se leen archivos de cuentas. La publicación de este repositorio usa la cuenta del organizador; el EXE no contiene esa autenticación.

Los mundos, mods y opciones de cada instalación quedan en una carpeta nueva Extremo3-*. Repetir la instalación crea otra instancia; no actualiza ni borra las anteriores. El modo oficial puede añadir bibliotecas compartidas y definiciones de versión, sin reemplazar archivos existentes con bytes diferentes. Si algo falla al final pueden quedar bibliotecas o una carpeta sin perfil; no se promete una transacción completa. Mantener el launcher cerrado evita que sobrescriba perfiles durante la instalación.

El ejecutable no tiene firma Authenticode. No desactivar protecciones de Windows. Es Windows x64, no ARM64 nativo ni x86. Una partida real y los flujos completos de todos los launchers requieren prueba con cuenta de juego; no se afirma compatibilidad con cualquier launcher.

## Compilar y preparar otra distribución

Requiere .NET SDK 8 y Python 3.11+, o Python 3.10 con `py -m pip install tomli`. Los jugadores no necesitan estas herramientas.

```powershell
py tools/seal_release.py "C:\ruta\mcextremo-v14.jar" --confirm-official-file-8401810 --mod-url "https://github.com/cazapan/extremo3-instalador/releases/download/mod-v14/mcextremo-v14.jar"
.\build.ps1
```

La preparación contrasta el JAR alojado con el archivo revisado, valida ID, requisitos Minecraft/NeoForge/FML y el hash de NeoForge. Rechaza dependencias externas desconocidas y rangos no soportados. No habilitar reviewed manualmente. Resultado: `dist-auto/InstalarExtremo.exe`. El hash de referencia se incluye en `dist-auto/SHA256.txt`.

`./build.ps1 -Preparation` permite compilar una vista previa bloqueada cuando falta el mod. Las pruebas locales se ejecutan con `dotnet run --project tests/Checks.csproj -c Release` y `py -m unittest discover -s tests -p "test_*.py"`. Agregar `-- --network` a la primera prueba descarga y comprueba Java y Minecraft en una carpeta temporal, sin tocar una instalación personal.

## Fuentes oficiales

Validación de esta versión: 11 comprobaciones locales de integridad/perfiles/URLs/extracción y 4 pruebas de requisitos. Además se descargaron y verificaron Java y Minecraft y se ejecutó el instalador oficial de NeoForge en una carpeta temporal: las 14 comprobaciones C# de la prueba con red e instalación terminaron correctamente. No se probó una partida real ni la ejecución completa de Prism con cuenta de juego.

- [NeoForge: cliente](https://docs.neoforged.net/user/docs/client/) y [Java 21 x64 para 1.21.1](https://docs.neoforged.net/docs/1.21.1/gettingstarted/).
- [SHA-256 NeoForge 21.1.265](https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.265/neoforge-21.1.265-installer.jar.sha256).
- [Temurin Java 21](https://github.com/adoptium/temurin21-binaries/releases).
- [Prism: metadata de NeoForge](https://github.com/PrismLauncher/meta-launcher/tree/master/net.neoforged).

## Desinstalar

Eliminar el perfil o instancia desde el launcher. Respaldar los mundos antes de borrar su carpeta Extremo3-*. No borrar bibliotecas compartidas de .minecraft. Las copias de launcher_profiles.json se guardan con sufijo extremo-backup; restaurarlas con el launcher cerrado y respaldo del archivo actual, pues una copia antigua puede omitir perfiles nuevos.
