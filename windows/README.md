# QCAPI Windows Tray (prototype)

Deze map bevat een **losse Windows tray-app** (C#/.NET) om je IBM Quantum account snel te checken via dezelfde REST calls als `qcapi`.

## Prereqs (Windows)

- Windows 10/11
- .NET 10 SDK

## Credentials

De app gebruikt dezelfde bronnen als `qcapi`:

1. Qiskit config (aanbevolen):
   - `%USERPROFILE%\\.qiskit\\qiskit-ibm.json`
   - account met `channel: "ibm_cloud"`
2. Of environment variables:
   - `IBM_CLOUD_API_KEY`
   - `QCAPI_SERVICE_CRN`

Optioneel:

- `QCAPI_BASE_URL`
- `QCAPI_API_VERSION`
- `QCAPI_QISKIT_ACCOUNT`
- `QCAPI_QISKIT_CONFIG_PATH`

## Run

```powershell
dotnet run --project .\\windows\\Qcapi.Tray\\Qcapi.Tray.csproj
```

## Debug / Logs

De tray app schrijft een logbestand naar:

- `%LOCALAPPDATA%\\Qcapi.Tray\\qcapi-tray.log`

Je kunt ook een custom tray icoon instellen via:

- `QCAPI_TRAY_ICON_PATH` (pad naar `.ico` of `.png`/`.jpg`/`.bmp`)

In de tray menu zit ook:

- `Open log file...`
- `Test connection (GET /versions)`

## Build / Publish (EXE)

Build (compile):

```powershell
dotnet build .\\windows\\Qcapi.WindowsHub.sln -c Release
```

Publish (maak een runnable exe, self-contained):

```powershell
.\scripts\publish-tray.ps1 -Configuration Release -Runtime win-x64
```

Output staat dan in:

- `dist\\Qcapi.Tray\\win-x64\\Qcapi.Tray.exe`

Tray menu:

- `Refresh backends (devices)` haalt `GET /backends` op en toont de lijst.
- `Show latest quantum job...` haalt recente jobs op en toont de nieuwste job-id (met fallback naar "latest job" als er geen quantum-job matcht).
- `Open backends window...` toont een venster met lijst + raw JSON.
