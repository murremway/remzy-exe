# Building and running Yinka on Windows

This guide creates a Windows `Yinka.exe` release from the existing WPF app.

The Windows app is a native WPF desktop application. It uses:

- **.NET 8 / WPF** for the UI.
- **Windows Speech Recognition APIs** for live sermon captions.
- The bundled **offline KJV** at `Data\en_kjv.json`.
- A separate borderless broadcast window titled exactly:
  **`Yinka — OBS Window Capture (KJV verse)`**

---

## 1. Prerequisites

Install these on the Windows machine that will build the app.

| Requirement | Why | Install / check |
|-------------|-----|-----------------|
| **Windows 10 2004+ or Windows 11** | The project targets `net8.0-windows10.0.19041.0`. | Settings → System → About. |
| **.NET 8 SDK** | Required to compile and publish WPF. | Install from [dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0), then run `dotnet --list-sdks`. |
| **PowerShell** | Used by `build-windows.ps1`. Windows PowerShell 5.1 works; PowerShell 7 also works. | Start → PowerShell. |
| **Microphone permission** *(for live speech)* | Windows speech needs mic access. | Settings → Privacy & security → Microphone → allow desktop apps. |
| **English speech pack** *(recommended)* | Improves or enables recognition. | Settings → Time & language → Speech. |

You do **not** need Visual Studio to build the `.exe` if the .NET 8 SDK is installed.

---

## 2. Build the executable

Open **PowerShell** in the repo root:

```powershell
cd C:\path\to\yinka
```

Build the default Windows x64 release:

```powershell
.\build-windows.ps1
```

What the script does:

1. Verifies it is running on Windows.
2. Verifies the .NET 8 SDK is installed.
3. Restores `Yinka.sln`.
4. Publishes `Yinka\Yinka.csproj` as a **self-contained** `win-x64` app.
5. Verifies `Yinka.exe` exists.
6. Verifies `Data\en_kjv.json` was copied.
7. Creates a zip package.

Expected output:

```text
==> Publishing Yinka for win-x64 (Release)
Repo:        C:\path\to\yinka
Project:     C:\path\to\yinka\Yinka\Yinka.csproj
Output:      C:\path\to\yinka\dist\windows\Yinka-win-x64

==> Restoring NuGet packages
...

==> Publishing self-contained application
...

==> Verifying output
Yinka.exe:          ... MB
Data/en_kjv.json:  4.3 MB

==> Creating zip package
Zip: C:\path\to\yinka\dist\windows\Yinka-win-x64.zip

==> Done
```

The output is:

```text
dist\windows\
  Yinka-win-x64\
    Yinka.exe
    Yinka.dll
    Yinka.Core.dll
    Data\
      en_kjv.json
    ... .NET runtime files ...
  Yinka-win-x64.zip
```

Because this is **self-contained**, users do not need to install .NET to run it.

---

## 3. Build variants

### Windows x64 (default)

```powershell
.\build-windows.ps1
```

### Windows ARM64

Use this for ARM Windows devices:

```powershell
.\build-windows.ps1 -Runtime win-arm64
```

Output:

```text
dist\windows\Yinka-win-arm64\
dist\windows\Yinka-win-arm64.zip
```

### Custom output directory

```powershell
.\build-windows.ps1 -OutputDir C:\Builds\Yinka
```

### Skip zip creation

```powershell
.\build-windows.ps1 -SkipZip
```

### Command Prompt users

Use the wrapper:

```cmd
build-windows.cmd
```

It calls the PowerShell script with execution policy bypassed for this one run.

---

## 4. Run the built app

Double-click:

```text
dist\windows\Yinka-win-x64\Yinka.exe
```

Or from PowerShell:

```powershell
.\dist\windows\Yinka-win-x64\Yinka.exe
```

The app should open with the title:

```text
Yinka — KJV verse presenter (offline text + Windows speech)
```

At startup it loads:

```text
Data\en_kjv.json
```

from beside the executable. If that file is missing, Yinka will warn that the bundled KJV could not be loaded.

**Important:** distribute the whole `Yinka-win-x64` folder or the generated zip. Do not send only `Yinka.exe`; it needs the included runtime files and `Data\en_kjv.json`.

---

## 5. Windows Defender / SmartScreen

The generated executable is unsigned. On first run, Windows may show:

```text
Windows protected your PC
```

Click:

1. **More info**
2. **Run anyway**

For a production release, sign `Yinka.exe` with an Authenticode certificate after publishing:

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a .\dist\windows\Yinka-win-x64\Yinka.exe
```

You only need signing if you plan to distribute outside your own machine or church computers.

---

## 6. Live speech setup

1. Open Windows **Settings**.
2. Go to **Privacy & security → Microphone**.
3. Turn on:
   - **Microphone access**
   - **Let desktop apps access your microphone**
4. Go to **Time & language → Speech**.
5. Confirm your speech language is installed. English is recommended.
6. Run `Yinka.exe`.
7. Click **Start Windows speech caption** in the **Transcript** panel.

If it works:

- The status changes to **Listening (Windows speech).**
- The **Listening...** line shows live hypothesis text.
- Finalized phrases are appended into the transcript.
- Bible references like `John 3:16` or `Romans chapter 12 verse 1` appear in **Detected references**.

If it fails:

- Check microphone permissions.
- Check your input device in Settings → System → Sound.
- Install an English speech pack.
- Restart Yinka after changing speech settings.

---

## 7. Projector / extended display setup

Yinka’s Windows app uses native monitor enumeration through Windows Forms.

1. Connect the projector or second monitor.
2. In Windows, press **Win + P**.
3. Choose **Extend**.
4. Open Yinka.
5. In the top bar, use **Broadcast screen** to select the target monitor.
6. Click **Open OBS / broadcast window**.

Modes:

| Option | What it does |
|--------|--------------|
| **Primary display — windowed (centered)** | Opens a large centered window on the primary display. Useful for testing. |
| **Screen 1 / Screen 2 / ...** | Opens a borderless broadcast window on that exact display and maximizes it. |
| **1920×1080 window** | Opens a fixed 1080p window centered on the selected screen. Useful for consistent OBS capture scaling. |
| **Broadcast window stays on top** | Keeps the verse output above other windows. |
| **Chroma green background** | Makes the broadcast background `#00FF00` for OBS Color Key. |

---

## 8. OBS setup

1. Run `Yinka.exe`.
2. Click **Open OBS / broadcast window**.
3. In OBS, add **Window Capture**.
4. Select the window titled:

   ```text
   Yinka — OBS Window Capture (KJV verse)
   ```

5. Optional: enable **Chroma green background** in Yinka, then add an OBS **Color Key** filter.

For projector-only use, you do not need OBS. Select the projector in **Broadcast screen**, then click **Open OBS / broadcast window**.

---

## 9. Basic usage

### Manual lookup

1. Type a reference in **Manual lookup**, e.g.:

   ```text
   John 3:16
   ```

2. Click **Lookup KJV**.
3. The verse loads into **Preview**.
4. Click **Go live (L)** or press **L** to send it to the broadcast window.

### Transcript detection

1. Paste or type sermon text into **Transcript**.
2. Click **Scan for references**.
3. Select a detected reference.
4. Click **Load KJV into preview**.
5. Click **Go live (L)**.

### Queue

1. Load a verse into Preview.
2. Click **Add to queue**.
3. Select it later in **Queue**.
4. Click **Present selected**.

### Keyboard shortcut

| Key | Action |
|-----|--------|
| **L** | Send the current Preview verse to Live / broadcast, as long as a text box is not focused. |

---

## 10. Troubleshooting

### `dotnet` is not recognized

Install the **.NET 8 SDK**, then open a new PowerShell window:

```powershell
dotnet --list-sdks
```

You should see an `8.x.x` SDK.

### Build fails on macOS or Linux

Expected. The Windows app is WPF and must be published on Windows.

Use a Windows machine, Windows VM, or a `windows-latest` CI runner.

### `Data\en_kjv.json` missing

Run the build script again:

```powershell
.\build-windows.ps1
```

Then distribute the whole output folder or zip.

### Speech captions do not start

Check:

- Windows microphone permission.
- Input device selection.
- Speech language pack installation.
- That another app is not monopolizing the microphone.

### Broadcast opens on the wrong screen

1. Press **Win + P** and choose **Extend**.
2. Reopen Yinka.
3. Select the intended screen in **Broadcast screen**.
4. Click **Open OBS / broadcast window** again.

### OBS cannot find the window

Confirm the broadcast window is open and titled:

```text
Yinka — OBS Window Capture (KJV verse)
```

If OBS was already open before the broadcast window, refresh the Window Capture source or recreate it.

---

## 11. Quick reference

```powershell
# Build x64 Windows exe release
.\build-windows.ps1

# Run it
.\dist\windows\Yinka-win-x64\Yinka.exe

# Zip to distribute
.\dist\windows\Yinka-win-x64.zip
```

Send `Yinka-win-x64.zip` to the Windows machine, extract it, then double-click `Yinka.exe`.
