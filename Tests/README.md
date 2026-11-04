# MwlTest

Automated checks for DMWLT4. `MwlTest.cs` starts a mock Modality Worklist SCP on `127.0.0.1:41112` and drives the app's real `MainViewModel` and `SettingsDialog`. Message boxes are captured and closed automatically.

## Run

From the repo root, in PowerShell:

```powershell
.\Tests\run-tests.ps1
```

The script builds the solution (Release), compiles the harness into `Tests\bin` and runs it. Each check prints `PASS` or `FAIL`. The exit code is 0 only if all checks pass.

Requirements: Visual Studio (or Build Tools) with MSBuild, and .NET Framework 4.8 reference assemblies. Ports 41112, 41113 and 41114 (TLS) must be free.

## What is covered

- Busy guard: a double Query sends only one C-FIND.
- FF01 pending responses are kept.
- Non-ASCII patient names (Specific Character Set).
- Wildcards, lowercase modality, open and inverted date ranges.
- C-ECHO: success, failure status, stopped server.
- Status bar messages and message box titles.
- Host and port validation.
- Settings dialog Cancel, Reset to default, timeout validation.
- Cancel and response timeouts (query and echo).
- Query statistics, character set option, row details tree, grid filter.
- Connection profiles (save, overwrite, select, delete, broken setting).
- TLS against a mock SCP with a self-signed certificate (skipped if no certificate can be created).
- Saving, applying and resetting the column layout.
- View Filter Results toggle, and the help window (topics and navigation).
- Additional query keys: fields from the setting, top-level and SPS keys, upper-casing, character set, date/time/UID validation, and the Settings Query tab.

Not covered: clicking the real main window, picking a client certificate from the store, and real worklist servers.

Your settings in `%LocalAppData%\DMWLT4\user.config` are read but never saved. The script warns if the file changes.
