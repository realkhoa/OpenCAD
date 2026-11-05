# OpenCad.App — AutoCAD-Style Desktop CAD Application

## 1. Overview & Purpose
`OpenCad.App` is the desktop CAD GUI client of the OpenCAD ecosystem, built with .NET 10.0 Windows Forms and ACadSharp.
It delivers an authentic AutoCAD dark theme look-and-feel, professional drafting and annotation workflows, and complete bilingual internationalization (Vietnamese and English).

### Key Architectural Highlights
- **Authentic AutoCAD Dark UI**:
  - **Application Bar**: Signature red AutoCAD "A" application menu button with quick access to New, Open, Save, Export, Close, and Exit.
  - **Quick Access Toolbar (QAT)**: New, Open, Save, Plot, Undo, Redo, Workspace indicator ("2D Drafting & Annotation"), and instant language toggle button (`🌐 EN` / `🌐 VI`).
  - **AutoCAD Ribbon Bar**: Organized into standard CAD functional tabs (Home, Insert, Annotate, View, Manage) with styled panel cards, header titles, primary/secondary buttons, and AutoCAD-style hover/pressed states.
  - **Drawing Layout Tabs**: Model space and Paper space layout switching with active blue indicator (`#007ACC`) and layout management.
  - **Canvas Viewport Overlays**: Viewport mode label (`[-] [Top] [2D Wireframe]`), floating Navigation Bar (Pan, Zoom Extents, Zoom In, Zoom Out), and pinned AutoCAD UCS Icon (X/Y colored axes and WCS origin marker).
  - **Command Line Window**: Cyan `COMMAND:` prompt badge, dynamic autocomplete hints, and command history.
  - **AutoCAD Status Bar**: Coordinate tracking badge, active blue drafting toggles (GRID, SNAP, ORTHO, POLAR, OSNAP, LWT, MODEL).
- **Internationalization (i18n)**:
  - Centralized `I18n` service supporting Vietnamese (`vi`) and English (`en`).
  - Real-time dynamic UI refresh upon language change across Ribbon, Menus, Status Bar, Command Line, Properties, and Layers.
  - Seamless CLI command switching via `LANG EN` and `LANG VI`.

---

## 2. Prerequisites & Dependencies
- **Runtime & SDK**: .NET 10.0 SDK (`net10.0-windows`) on Windows 10/11.
- **Graphic Subsystem**: GDI+ / `System.Drawing.Common`.
- **CAD File Engine**: `ACadSharp` (MIT) & `CSMath`.
- **Testing Tools**: `OpenCad.Tools` CLI for headless smoke, quality gate, and invariant testing.

---

## 3. CLI Options & Arguments Reference

| Argument / Flag | Default | Description |
| :--- | :--- | :--- |
| `[drawing-file.dwg\|dxf]` | `null` | Path to DWG or DXF file to open automatically on application startup. |
| `--uitest` | `false` | Runs the automated 220+ headless UI test suite simulating ribbon clicks, mouse actions, and commands, then exits with code 0. |

---

## 4. Usage Examples

### Launching the Application
```powershell
# Open a new blank drawing
dotnet run --project src/OpenCad.App

# Open a specific DWG or DXF drawing file
dotnet run --project src/OpenCad.App -- samples/demo.dwg
```

### Running Automated UI Verification
```powershell
# Run the complete headless UI test suite
src/OpenCad.App/bin/Debug/net10.0-windows/OpenCad.App.exe --uitest
```

### Switching Language at Runtime
- **Via GUI**: Click the `🌐 VI` / `🌐 EN` toggle button on the Quick Access Toolbar (top-left).
- **Via Command Line**:
  - Type `LANG EN` or `LANGUAGE EN` to switch the application to English.
  - Type `LANG VI` or `LANGUAGE VI` to switch the application to Vietnamese.

---

## 5. Output Structure & Schema

```
src/OpenCad.App/
├── CadCanvas.cs           # Viewport canvas, UCS icon, Viewport controls, Navigation bar, input dispatcher
├── CadRenderer.cs         # GDI+ rendering pipeline with AutoCAD theme palette
├── DarkTheme.cs           # AutoCAD theme colors, ToolStrip dark renderer, and brush/pen cache
├── I18n.cs                # Centralized i18n translation service and language dictionaries
├── Icons.cs               # Vector CAD icon rendering engine (Pan, Zoom, Layers, Properties, etc.)
├── MainForm.cs            # Top application header, QAT, Ribbon, Layout tabs, Status bar, Palettes
├── RibbonBar.cs           # Modern AutoCAD Ribbon container, tabs, panels, and tooltips
├── Panels/
│   ├── CommandLine.cs     # AutoCAD floating command bar with badge and autocomplete
│   ├── LayerPanel.cs      # AutoCAD-style Layer Properties palette
│   └── PropertiesPanel.cs # AutoCAD-style Properties inspector palette
└── README.md              # Application documentation and architecture guide
```

---

## 6. Troubleshooting & Edge Cases
- **Missing or Corrupted Fonts**:
  - If custom AutoCAD SHX fonts are missing, the renderer falls back gracefully to `Segoe UI` and `Arial`.
- **Command Line Router Parity**:
  - All command actions are synchronized with `CommandRegistry`. The `LANG` command is intercepted before the primary drawing command dispatcher to guarantee 100% router parity tests pass.
- **High-DPI Scaling**:
  - Controls, icons, and viewport overlays use proportional vector geometry to maintain crisp rendering on high-DPI displays (100% to 200% scale).
