# cad2bim

A Windows desktop application that converts 2D CAD floor plans (DWG/DXF) into BIM models (IFC4). It reads a vector drawing, identifies walls, doors, and windows — automatically, manually, or both — and exports an IFC file that Revit and other BIM tools can open or link.

## What it does

- Loads DWG/DXF files, flattening all blocks into world coordinates (nested inserts, mirrored blocks, MINSERT arrays, bulged polylines all handled).
- **Automatic segmentation**: detects walls as parallel line pairs, then finds the openings in them and decides which are doors (swing arc present) and which are windows.
- **Manual segmentation**: brush and eraser tools for tagging lines as wall, door, or window by hand — used alone or to correct the automatic pass.
- Saves classifications to a sidecar file next to the drawing, so work survives restarts without ever touching the CAD file.
- Exports the classified plan as an IFC4 model: walls extruded from their exact drawn footprints, openings cut into them, door and window elements placed with hinge side and swing direction preserved.
- Headless CLI mode for scripted conversion, no GUI required.

## Prerequisites

- Windows 10/11 (the app is WPF; `net8.0-windows`)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Optionally Visual Studio 2022 (17.8+) or VS Code with the C# extension

The only NuGet dependency is [ACadSharp](https://github.com/DomCR/ACadSharp) (DWG/DXF parsing); it restores automatically.

## Setup

```powershell
git clone <repo-url>
cd cad2bim
dotnet restore
dotnet build
dotnet run
```

Or open `cad2bim.csproj` in Visual Studio and press F5. To pass a drawing at startup:

```powershell
dotnet run -- path\to\plan.dwg
```

### Headless CLI

The same executable runs without a GUI. Being a WinExe it has no console, so output goes to a file:

```powershell
# Classify and write a diagnostic report (plan.dump.txt)
cad2bim.exe --dump plan.dwg [report.txt]

# Classify with defaults and export IFC (plan.ifc + plan.convert.txt report)
cad2bim.exe --convert plan.dwg [out.ifc]
```

`--convert` respects a saved segmentation sidecar: hand tags drive the conversion when present; otherwise it falls back to full automatic classification.

## User guide

### Opening a file

Click **Open** and pick a `.dwg` or `.dxf`. The status bar reports the primitive count and the drawing's units (a file that declares no units is read as millimetres). If a segmentation sidecar (`<file>.c2b.json`) exists next to the drawing, its tags are restored automatically.

### Layers

The **Layers** button toggles visibility of the five display layers: the raw CAD drawing, annotations (hatches, dimensions — drawn but never classified), and the Walls, Windows, and Doors result layers.

### Automatic segmentation

Open the **Automatic** panel, choose what to segment (walls / doors / windows), tune the settings, and press **Segment**:

- **SMin / SMax** — wall thickness bounds (default 50–400 mm). Two parallel lines this far apart become a wall.
- **Min / max opening width** — gaps outside this range are ignored (default 600–4000 mm).
- **Min / max swing radius** — the door-swing filter (default 500–1500 mm); this is what separates a door arc from a swivel chair.
- Fields can be typed or scrubbed, in millimetres or inches — storage is always metric, so switching units never changes the value.

The status bar reports how many walls, doors, windows, and unknown openings were found.

### Manual segmentation

Open the **Manual** panel, pick the **Brush** or **Eraser**, choose the target layer (Walls / Doors / Windows), then click or drag across lines in the drawing. The eraser returns lines to unclassified. **Ctrl+Z** undoes one full stroke (or one automatic run) at a time. Hand edits supersede the cached automatic result — conversion rebuilds from your corrected tags.

### Saving

**Save** writes all current tags to `<drawing>.c2b.json` next to the CAD file. The drawing itself is never modified. Tags are keyed by entity handle, so they reload correctly as long as the drawing is unchanged.

### Converting to BIM

Open the **Convert** panel. The drawing has no third dimension, so the heights are yours to set: **wall height** (default 3000 mm), **door height** (2100), **window sill** (900) and **window head** (2100). Press **Convert**, choose an output path, and an IFC4 file is written. The status bar reports the exported element counts.

Conversion works from the last automatic run when there is one, and otherwise from whatever is tagged as wall — so a fully manual workflow (tag walls, tag doors and windows, convert) needs no automatic pass at all.

## Documentation

Technical details — primitives, the classification algorithms, the BIM pipeline, and extension points — are in [docs/TECHNICAL.md](docs/TECHNICAL.md).
