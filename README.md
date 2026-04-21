# SweepCore 1.0

SweepCore is a Windows desktop utility focused on three everyday maintenance tasks:

- cleaning temporary files and browser cache
- uninstalling installed applications
- managing Windows startup entries

The goal is simple: make common cleanup actions easier to understand and safer to use for non-technical users.

## What It Does

### Clean up

SweepCore scans supported temporary-file and browser-cache locations, shows what was found, and lets you choose exactly which areas should be cleaned.

### Uninstall apps

SweepCore lists the applications registered in Windows and lets you open each program's normal uninstaller from a simple app view.

### Startup

SweepCore shows enabled and disabled startup entries and lets you turn them on or off from one place.

## Main Features

- guided cleanup flow with scan, selection, and cleanup steps
- clear app uninstall view with search, filters, and direct uninstall access
- startup management with enable and disable toggles
- dark default interface with a simple left navigation
- focused layout that keeps the next action visible

## Safety

- cleanup requires an explicit selection before anything is removed
- cleanup uses the Windows Recycle Bin instead of permanent deletion
- only supported cleanable files and cache locations are included
- browser cleanup is limited to cache-related locations
- passwords, saved addresses, autofill forms, and profile databases are excluded
- uninstall actions open the registered Windows uninstaller instead of silently removing software
- startup changes are made through Windows startup settings only
- cleanup runs write an action log

## Project Files

- `SweepCoreApp/` contains the WPF application source
- `build.ps1` builds the desktop app locally
- `bin/` is ignored by Git because it only contains generated build output

## Build

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

After the build, the generated executable is available locally at `bin/SweepCore.exe`.
