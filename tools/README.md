# OpenCad.Tools & Build Scripts

## 1. Overview & Purpose
`OpenCad.Tools` is the headless test runner, verification gate harness, and automation release toolkit for the OpenCAD ecosystem.
It provides comprehensive quality assurance gates (smoke testing, CAD command parity validation, drawing round-trip verification, and invariant enforcement) alongside automated release distribution publishing (`publish-release.ps1`).

---

## 2. Prerequisites & Dependencies
- **Runtime**: .NET 10.0 SDK (`dotnet`) installed on Windows 10/11 x64.
- **PowerShell**: Windows PowerShell 5.1+ or PowerShell 7+.
- **Dependencies**: `OpenCad.Core`, `ACadSharp` (3.8.30), `CSMath`.

---

## 3. CLI Options & Arguments Reference

### `OpenCad.Tools` Commands

| Command | Arguments | Description |
| :--- | :--- | :--- |
| `smoke` | None | Runs the 943 Core engine unit tests. |
| `checkall` | None | Executes all 14 quality assurance gates in sequence. |
| `checkcmds` | None | Verifies 100% parity between `CommandRegistry` and `MainForm` command router. |
| `checkdocs` | None | Validates that all CAD commands are documented in `README.md`. |
| `checkinvariants`| None | Enforces 19 critical architectural invariants across the codebase. |
| `checkfixtures`| `[dir]` | Validates all DWG fixture files for lossless opening and round-tripping. |
| `workflow` | `[dir]` | Executes the 18-step end-to-end production drawing workflow. |
| `griphunt` | None | Performs 500 grip-stretch fuzzing cycles across entity types. |
| `lossy` | None | Asserts content fidelity across DWG/DXF save and reload cycles. |
| `audit` | `[file.dwg]` | Audits a DWG file for zero-length lines, empty text, and duplicate geometry. |

### `publish-release.ps1` Script Options
The release publishing script runs non-interactively without arguments to produce standardized release artifacts.

---

## 4. Usage Examples

### Executing Quality Gates
```powershell
# Run smoke tests
dotnet run --project tools/OpenCad.Tools -- smoke

# Run all 14 gates
dotnet run --project tools/OpenCad.Tools -- checkall
```

### Publishing a New Release
```powershell
# Build and package both Portable and Standalone release archives
powershell -ExecutionPolicy Bypass -File tools/publish-release.ps1
```

---

## 5. Output Structure & Schema

```
dist/
├── publish-win-x64/                # Framework-dependent build folder
├── publish-win-x64-standalone/     # Self-contained single-file build folder
└── releases/                       # Distribution zip archives
    ├── OpenCAD-v1.0.0-win-x64-portable.zip     # Portable archive (~0.9 MB)
    └── OpenCAD-v1.0.0-win-x64-standalone.zip   # Standalone archive (~44 MB)
```

---

## 6. Troubleshooting & Edge Cases
- **Missing Desktop Runtime for Portable Build**:
  - The portable package requires the .NET 10.0 Desktop Runtime installed on the target machine.
  - Users without .NET installed should run the Standalone package (`OpenCAD-v1.0.0-win-x64-standalone.zip`).
- **Antivirus False Positives**:
  - Single-file self-extracting executables might trigger heuristic scans; binaries are clean and built directly from source using official Microsoft .NET 10 SDK toolchains.
