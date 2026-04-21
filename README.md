# SweepCore 1.0

This copy keeps the cleanup engine intact but refreshes the interface for usability:

- a calmer default layout with a dedicated left navigation
- a cleanup-first workspace with large target cards
- a persistent selection summary with clear primary actions
- a file preview so the current selection is easier to review
- uninstall support for installed apps
- startup management with enable/disable toggles
- the same safety-first cleanup flow that only moves selected cleanable items to the Windows Recycle Bin

## Included

- `SweepCoreApp/` contains the WPF application source
- `build.ps1` compiles the app locally into `bin/SweepCore.exe`
- `bin/` is ignored by Git because it only contains generated build artifacts

## Safety Principles

- cleanup requires an explicit selection
- only cleanable temporary files and browser cache entries can be cleaned
- cleanup uses the Recycle Bin instead of permanent deletion
- every cleanup run writes an action log
- app uninstall actions open the registered Windows uninstaller instead of silently removing software
- startup entries are changed through Windows startup settings only
- browser cleanup is restricted to cache directories only
- passwords, saved addresses, autofill forms, and profile databases are excluded
- protected locations and sensitive file types remain blocked

## Usability Goals

- keep the next main action visible at all times
- reduce visual noise around the cleanup flow
- surface safety information directly in the workspace
- make app uninstall and startup controls simple enough for non-technical users
- make the selected cleanup scope understandable before cleanup starts

## Build

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```
