# Branch Type 1 Windows installer

The installer is a reusable Branch Type 1 profile. It contains the self-contained Windows application and no branch ID, enrollment token, device credential, database, exports, backups, or production data.

Build on a Windows runner with .NET SDK 9.0.203 and Inno Setup 6:

```powershell
.\installers\branch-type-1\build-installer.ps1 -Version 0.2.0
```

The script publishes `win-x64`, blocks forbidden runtime data and secret-bearing file types, and writes the installer to `installers\branch-type-1\artifacts`. The stable AppId supports in-place updates. User data remains under `%LOCALAPPDATA%\Sugar ERP\Branch Type 1` and is not bundled or deleted by uninstall.

Before release, sign the generated installer in the approved CI environment and verify install, update, and uninstall on a clean Windows VM with the actual thermal printer driver. Signing keys must never be added to this repository.
