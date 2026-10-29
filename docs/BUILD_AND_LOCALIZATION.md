# Guía Definitiva de Compilación, Lanzador y Sistema de Localización (Konsor RO3)

Este documento detalla la arquitectura completa, el funcionamiento del sistema de traducción y las instrucciones paso a paso para compilar todos los componentes sin sobreescribir ni romper cambios anteriores.

---

## 1. Arquitectura del Sistema

### A. Base Oficial en Inglés (`english`)
- **Registro de Windows:** `HKCU\Software\gravity\ro3\CurrentLanguage_h413354046 = english\0`.
- **Por qué inglés como base:** El motor Unity y los scripts de configuración de unidades (`UnitConfig.lua.bytes`) tienen los nombres de los monstruos (`波利`, `疯兔`, etc.) y NPCs grabados en texto fijo chino cuando el cliente se ejecuta en `chinesesimplified`. En `english`, el cliente utiliza las tablas oficiales en inglés: **todos los monstruos (`Poring`, `Lunatic`, `Fabre`, etc.) y NPCs se muestran de forma limpia en inglés nativo, sin caracteres en chino**.

### B. Capa de Traducción al Español (`Ro3Loc.dll` + `en_overrides.tsv`)
- `Ro3Emu.Loc.dll` intercepta todas las llamadas del juego a `TMP_Text.set_text`, `TMP_Text.SetText`, `TextMeshProUGUI.Awake`, `HUDTextHelper` y `LanguageMain.Obf_gO (sid)`.
- Revisa el diccionario `EnglishOverrides` (con coincidencia `OrdinalIgnoreCase` y trim automático).
- Contiene **27.054 reglas de traducción EN -> ES** que traducen:
  - Pantallas de autenticación y Login (`Account` -> `Cuenta`, `Password` -> `Contraseña`, `Recent Login` -> `Último inicio de sesión`, `Enter Battle` -> `Entrar al combate`, etc.).
  - Diálogos y conversaciones de NPCs.
  - Títulos y descripciones completas de misiones (`[Main]` -> `[Misión Principal]`, objetivos, etc.).
  - Botones, menús, interfaces de usuario, ajustes y tips.

---

## 2. Mapa de Archivos y Proyectos

| Componente | Código Fuente | Binario de Salida | Despliegue en Juego |
| :--- | :--- | :--- | :--- |
| **Traductor Cliente** | `E:\ro3emu-server-konsor\src\Ro3Emu.Loc` | `bin\Release\netstandard2.1\Ro3Emu.Loc.dll` | `ROO_PC\ro3_Data\Managed\Ro3Emu.Loc.dll`<br>`client\Ro3Emu.Loc.dll` |
| **Reglas EN -> ES** | `scratch\build_full_en_overrides.js` | `en_overrides.tsv` (2.8 MB) | `data\en_overrides.tsv`<br>`data\en_overrides_es.tsv`<br>`ROO_PC\ro3emu\en_overrides.tsv` |
| **Herramienta Patcher** | `E:\ro3emu-server-konsor\src\Ro3Emu.Setup` | `RO3Emu.exe` (Single-File) | `RO3Emu.exe` |
| **Lanzador Gráfico** | `E:\ro3-emu-launcher` | `dist\KonsorRO.exe` | `KonsorRO.exe` |
| **Lanzador Bat/PS1** | `brand\patch\konsor.ps1` | `konsor.ps1` / `run_konsor_ro3.bat` | Carpeta raíz del juego |
| **Archivo ZIP Distribución** | `dist\*` | `konsor-ro3-patch.zip` | `C:\Users\Erno\Downloads\`<br>`E:\KonsorRO_Release\` |

---

## 3. Instrucciones Paso a Paso de Compilación

### Paso 1: Generar las Reglas de Traducción `en_overrides.tsv`
Ejecutar el script Node.js para combinar las tablas oficiales (`loc_en.tsv` + `loc_es.tsv`) y generar las más de 27.000 reglas:
```powershell
node "C:\Users\Erno\.gemini\antigravity\brain\9452ec60-dde2-4d35-aedf-198e6c3588de\scratch\build_full_en_overrides.js"
```
Copiar `en_overrides.tsv` a las carpetas correspondientes:
- `E:\ro3emu-server-konsor\src\Ro3Emu.Loc\en_overrides.tsv`
- `E:\Genesis Launcher\data\en_overrides.tsv`
- `E:\Genesis Launcher\data\en_overrides_es.tsv`
- `E:\Genesis Launcher\ROO_PC\ro3emu\en_overrides.tsv`
- `E:\ro3-emu-launcher\dist\data\en_overrides.tsv`
- `E:\ro3-emu-launcher\dist\data\en_overrides_es.tsv`

### Paso 2: Compilar `Ro3Emu.Loc.dll`
Compilar la biblioteca interceptora de Unity:
```powershell
dotnet build "E:\ro3emu-server-konsor\src\Ro3Emu.Loc\Ro3Emu.Loc.csproj" -c Release -p:ClientManaged="E:\Genesis Launcher\ROO_PC\ro3_Data\Managed"
```
Desplegar el DLL compilado:
```powershell
# Nota: ro3.exe y KonsorRO.exe deben estar cerrados
node -e "const fs = require('fs'); const src = 'E:/ro3emu-server-konsor/src/Ro3Emu.Loc/bin/Release/netstandard2.1/Ro3Emu.Loc.dll'; const targets = ['E:/ro3emu-server-konsor/prebuilt/client/Ro3Emu.Loc.dll', 'E:/Genesis Launcher/client/Ro3Emu.Loc.dll', 'E:/Genesis Launcher/ROO_PC/ro3_Data/Managed/Ro3Emu.Loc.dll', 'E:/ro3-emu-launcher/dist/client/Ro3Emu.Loc.dll']; for (const t of targets) { fs.copyFileSync(src, t); }"
```

### Paso 3: Compilar y Publicar `RO3Emu.exe`
Recompilar el ejecutable principal para que incruste la nueva versión de `Ro3Emu.Loc.dll`:
```powershell
dotnet publish "E:\ro3emu-server-konsor\src\Ro3Emu.Setup" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -p:IsTransformWebConfigDisabled=true -p:StaticWebAssetsEnabled=false -o "E:\ro3emu-server-konsor\dist-publish"
copy "E:\ro3emu-server-konsor\dist-publish\RO3Emu.exe" "E:\Genesis Launcher\RO3Emu.exe"
copy "E:\ro3emu-server-konsor\dist-publish\RO3Emu.exe" "E:\ro3-emu-launcher\dist\RO3Emu.exe"
```

### Paso 4: Compilar y Publicar el Lanzador `KonsorRO.exe`
Compilar la UI standalone:
```powershell
dotnet publish "E:\ro3-emu-launcher\Ro3Launcher.csproj" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o "E:\ro3-emu-launcher\dist"
copy "E:\ro3-emu-launcher\dist\KonsorRO.exe" "E:\Genesis Launcher\KonsorRO.exe"
```

### Paso 5: Generar el Paquete de Distribución `konsor-ro3-patch.zip`
```powershell
tar.exe -a -c -f "E:\ro3-emu-launcher\konsor-ro3-patch.zip" -C "E:\ro3-emu-launcher\dist" *
copy "E:\ro3-emu-launcher\konsor-ro3-patch.zip" "C:\Users\Erno\Downloads\konsor-ro3-patch.zip"
copy "E:\ro3-emu-launcher\konsor-ro3-patch.zip" "E:\KonsorRO_Release\konsor-ro3-patch.zip"
```

---

## 4. Salvaguardas en el Lanzador contra Sobrescrituras

Tanto en `GameOrchestrator.cs` como en `konsor.ps1`, se implementaron salvaguardas explícitas posteriores a la ejecución de `RO3Emu.exe join`:
```csharp
// GameOrchestrator.cs (después de process.WaitForExitAsync())
if (File.Exists(clientLocDll) && Directory.Exists(Path.GetDirectoryName(managedLocDll)))
{
    File.Copy(clientLocDll, managedLocDll, overwrite: true);
}
```
Esto previene que si una versión anterior o estándar de `RO3Emu.exe` extrae su copia local durante el comando `join`, `Ro3Emu.Loc.dll` y `en_overrides.tsv` vuelvan a ser restauradas inmediatamente con nuestras versiones de traducción completas.

---

## 5. Script de Verificación Rápida de Traducciones

Para verificar en cualquier momento que los textos clave están mapeados correctamente:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Users\Erno\.gemini\antigravity\brain\9452ec60-dde2-4d35-aedf-198e6c3588de\scratch\test_loc_v2.ps1"
```
Debe responder:
- `Account => Cuenta`
- `Password => Contraseña`
- `Recent Login => Último inicio de sesión`
- `Enter Battle => Entrar al combate`
- `[Main Quest] ... => [Misión Principal] ...`
- `Poring => Poring` (monstruos en inglés).
