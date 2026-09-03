# cad2bim — Technical Reference

For developers extending a fork or embedding the pipeline in their own project. Everything below the ViewModels is plain .NET with no WPF dependency (except `DrawingModel.Bounds`, which uses `System.Windows.Rect`), so the load → classify → convert → export chain can be driven headlessly — `Bim/ConvertPipeline.RunHeadless` is a complete worked example.

## Pipeline overview

```
DWG/DXF ──ACadSharp──► CadEntityWalker ──► sinks ──► primitives / geometry
                                                        │
                        ┌───────────────────────────────┤
                        ▼                               ▼
                 DrawingModel                    ClassificationService
              (store, tags, undo,             (walls → runs → openings,
               spatial pick index)             or the tagged variant)
                        │                               │
                        └────────── ClassificationResult ◄──┘
                                            │
                                   CadToBimConverter
                              (footprints, parameterized
                               doors/windows, mm at the edge)
                                            │
                                         BimModel
                                            │
                                       IfcExporter → .ifc (IFC4)
```

Layer map:

| Directory | Role |
|---|---|
| `Services/Cad/` | Reading the CAD file: entity walk, transforms, sinks |
| `Services/` | Drawing store, units, spatial grid, sidecar persistence, service facade |
| `Classification/` | Wall pairing support, runs, opening/column detection, tolerances |
| `Reconstruction/` | Classified geometry → parametric footprints and opening parameters |
| `Bim/` | Neutral BIM model, conversion orchestration, IFC export |
| `ViewModels/`, `Views/` | WPF GUI (MVVM); nothing below depends on it |

## Primitives

Defined in `Geometry.cs` and `CadPrimitive.cs`.

| Type | What it is |
|---|---|
| `Point(double x, double y)` | Immutable 2D point record, drawing units, world coordinates |
| `GeometryElement` (abstract) | Base of all analyzable geometry; carries `Points` and `SourceId` |
| `Segment` | A straight span. The workhorse: direction, folded heading, projection/offset, clipping, intersection, overlap intervals, point–segment distance |
| `Arc` | A true arc: center, radius, start/end angles (radians, CCW, as ACadSharp supplies them). Sweep is preserved because it is the door discriminator |
| `PolylinePath` | Stroke-only run of straight spans; drawn but never classified |
| `TextElement` | A string and its box; not classified, available for future space labelling |
| `CadPrimitive` | One drawable, addressable piece: `Id` (dense index), `Key`, `Geometry`, `IsClassifiable` |
| `PrimitiveKey(EntityHandle, Ordinal)` | Stable identity across loads: top-level entity handle + deterministic emission ordinal. Join key for sidecar persistence |
| `PrimitiveClass` | `Unclassified, Wall, Door, Window, Annotation` — the tag a primitive carries |

`GeometryElement.SourceId` maps derived/classifier geometry back to the drawn primitive (`-1` for synthesized geometry). The classifier receives the same element instances the store holds, so results map straight back onto the drawing.

Key `Segment` conventions:

- **Folded heading**: `HeadingDegrees` is in `[0, 180)`, so a line and its reverse agree. Compare with `Segment.HeadingDifference(a, b)` (result in `[0, 90]`), never by subtraction — 179.9° and 0.1° are 0.2° apart.
- **Local projection**: `Project` (distance along) and `Offset` (signed distance off, positive left) express positions in a segment's own frame.

## CAD ingestion

`Services/Cad/CadEntityWalker.Walk(document, sink)` traverses model space once, flattening every `INSERT` into world coordinates via `Xform` (2×3 affine: rotation, non-uniform scale, mirroring, MINSERT row/column arrays). It is the *single* place that knows how to read a drawing; add support for a new entity type here and every consumer gets it.

Sinks implement `ICadGeometrySink` and opt into what they want:

| Sink | `WantsStrokes` | `WantsPrimitives` | Consumer |
|---|---|---|---|
| `CadRenderSource` (via viewport) | yes | yes | rendering + picking |
| `CadPrimitiveSink` | yes | yes | builds `CadPrimitive[]` for `DrawingModel` |
| `CadAnalysisSink` | no | yes | bare geometry lists for `CadLoader` |

Notable ingestion behavior:

- **Two views of curves.** Arcs/circles are *stroked* as chord tessellations (~3° per vertex) but *analyzed* as true `Arc` primitives. Bulged `LWPOLYLINE` vertices become real arcs too (`bulge = tan(sweep/4)`) — door swings are routinely drawn this way, and chord-interpolating them would destroy the strongest door cue in the drawing.
- **Mirrored blocks.** A negative-determinant transform (left-hand vs right-hand doors) reverses the sweep sense; the walker swaps the transformed start/end angles so the arc restates as CCW. Without this every mirrored door fails the swing test.
- **Non-similarity transforms.** Under shear or non-uniform scale a circle is an ellipse; the walker degrades the primitive to chords rather than emit a lying center/radius.
- **Annotation flag.** Hatch boundaries and dimension blocks are emitted with `analyzable: false`: drawn, pickable never, classified never. Feeding them to the classifier manufactures phantom walls.

## Units

Geometry stays in the file's own coordinates. All physical settings and tolerances are authored in **millimetres** and converted exactly once, at the edge, using the drawing's `INSUNITS` header (`Services/DrawingUnits.MillimetersPerUnit`). A file that declares no units is assumed millimetres. `ClassificationService` owns the conversion; nothing downstream ever sees a raw drawing-unit constant, so a plan drawn in metres and the same plan in millimetres classify identically.

## The drawing store: `DrawingModel`

`Services/DrawingModel.cs` — the loaded drawing as one addressable store:

- `Primitives`, per-primitive `PrimitiveClass` array, and per-class id buckets (`IdsIn`).
- `SetClasses(ids, class, source)` is the **single mutation seam**; every tag change (brush, eraser, auto-confirm, sidecar restore) goes through it and raises `ClassificationChanged` with a `ClassificationDelta`. The viewport listens to exactly this event.
- `BeginEditScope()` groups many `SetClasses` calls into one undo entry (one brush stroke, one auto run). Undo stack holds 64 entries.
- `Grid` (`PrimitiveGrid`) is a uniform spatial hash (256 cells across the drawing's larger extent) used for cursor picking and capsule-shaped brush queries.
- `AnalyzableGeometry()` yields exactly what the classifier consumes, with `SourceId` = primitive id.

## Classification

`Services/ClassificationService` is the facade the GUI and headless mode both use. Two entry points:

- `ClassifyAll(sMin, sMax, tolerances?)` — fully automatic.
- `ClassifyTagged(wallSegments, doorGeometry, windowGeometry, ...)` — driven by user tags (see below).

Both return a `ClassificationResult(Walls, Openings, Runs, Columns)`.

### Walls

`CadClassifier.ClassifyWalls` (in `Geometry.cs`) implements the pairing rule: a wall is two **parallel segments**, `SMin ≤ d(e1, e2) ≤ SMax`, that **overlap** when projected onto their shared direction. Pairing is exclusive (a face belongs to at most one wall) and the **closest** admissible partner wins. Candidates come from `Classification/SegmentIndex` (a spatial hash over segment bounding boxes inflated by `SMax`) rather than a quadratic scan — necessary once blocks are exploded and the feed reaches tens of thousands of segments; the index only narrows the search, it never changes the rule.

The `Wall` object precomputes a **local frame** — sign-normalized unit `Axis`, `Normal`, centreline `Origin` — and everything downstream speaks in it: `AxisParam(p)` (distance along the wall) and `NormalParam(p)` (signed distance off the centreline). Default thickness bounds: 50–400 mm.

### Wall runs

Exclusive pairing means a wall interrupted by an opening arrives as several collinear `Wall` fragments. `Classification/WallRun.Build` reassembles them: greedy agglomeration by matching heading (within `AngleToleranceDegrees`), centreline offset, and thickness (both within `AxisOffsetTolerance`), bucketed by heading (5° buckets, wrap-aware) to stay near-linear. Every position on a run is expressed in the first fragment's frame.

A run then exposes interval arithmetic on its axis (`Intervals.Merge` / `Complement`):

- `Covered(joinTolerance)` — the **union** of what either face draws (deliberately not the intersection: at a real doorway one face is bracketed while the other merely ends).
- `Gaps(joinTolerance)` — spanned but not drawn. **These gaps are the opening candidates.**

### Openings (doors and windows)

`Classification/OpeningClassifier` works from a simple definition — an opening is a pair of segments inside a wall, about the wall's thickness apart, optionally with an arc that makes it a door — but searches in the opposite order: a vector drawing gives the wall first, so it finds where a wall is *interrupted*, then looks inside the interruption for evidence. Per gap, `Evaluate` runs:

1. **Width gates.** Reject below `MinOpeningWidth` (600 mm — smaller is a column or a jog) or above `MaxOpeningWidth` (4000 mm — the wall is simply missing).
2. **Face evidence.** Per side of the wall, is the face bracketed (stops at the gap start, resumes at the gap end, within `EndpointTolerance`)? Both sides → `GapBothFaces`; one → `GapOneFace`.
3. **Swing search** (`FindSwing`). Candidate arcs were pre-filtered by radius (500–1500 mm) and sweep (60–120°) — the radius floor is what separates a door from a swivel chair drawn as the same quarter circle at ~400 mm. A candidate must have radius ≈ gap width (the leaf spans the opening) and its center (the hinge) at one end of the gap, within `HingeTolerance`. Best combined error wins; each arc is claimed once. A matching **leaf** segment (hinge to arc end, length ≈ radius) upgrades the evidence.
4. **Junction test** (`CrossedByAnotherWall`). A wall corner interrupts a face exactly like a doorway — this is the largest source of false positives. If another (non-parallel) wall face properly crosses the gap's inset threshold, the gap is a junction and rejected. A matched swing overrides the test, so a door hard against a corner survives.
5. **Face pair inside the gap** (`FindFacePair`). Segments running along the wall, inside the wall band, covering ≥ 50 % of the gap, in pairs separated by ≈ wall thickness (within `ThicknessEpsilon`); the widest-separated pair wins (a window's outer lines sit on the faces; mullions fall between). Found pair → `FacePair` (+ `GlazingLines` if a line runs strictly inside the band). No pair → faces are **synthesized** from the wall itself (`SynthesisedFaces`) — the ordinary case for a door, whose gap is drawn empty.
6. **Jamb pair** (`JambPair`). Two cross-wall lines of length ≈ wall thickness closing the gap's ends → `JambPair`.
7. **Acceptance.** Something must *positively* say "opening": `GapBothFaces`, `JambPair`, `SwingArc`, or `GlazingLines`. A lone one-sided gap with nothing in it is an unclosed wall end, not a doorway.
8. **Kind decision:**
   - swing arc present → **`Door`**
   - face pair actually drawn across the gap → **`Window`**
   - otherwise → **`Unknown`** (never guessed as a window — doing so was measured to invent ~100 windows in a drawing that has none)

The result is an `Opening` carrying the host wall, kind, axis span, the four-corner rectangle, the `OpeningEvidence` flags explaining *why* it was accepted, and the thickness residual. `ClassificationReport` / `ClassificationDump` (`--dump`) count acceptances and per-reason rejections so the tolerances can be tuned against real drawings.

### Columns

`Classification/ColumnDetector` runs **only in the tagged pipeline**, on segments the user tagged as wall (on the whole drawing the same small rectangle is as likely a chair). It walks endpoint-adjacent short segments into closed right-angled quads with both sides in 150–800 mm, claims them as `ColumnFootprint`s, and removes their segments *before* wall pairing — otherwise the exclusive closest-wins pairing steals a column's faces for the walls around it.

### The tagged pipeline

`ClassificationService.ClassifyTagged` turns manual tags into a result:

1. `ColumnDetector` claims columns out of the wall tags.
2. Wall pairing + run building on the remaining wall-tagged segments.
3. `TaggedOpeningBuilder.Build` forces openings from door/window tags: tagged geometry is clustered by bounding-box proximity, each cluster projected onto the nearest run, the projection becomes the span, faces are synthesized, and the largest tagged arc becomes a door's swing. Where somebody painted a door, there *is* a door — no gap evidence required.
4. The gap detector still runs over the full geometry; `MergeDetected` drops any detected opening that overlaps a forced one on the same run. Hand tags always win.

### Tolerances

`Classification/ClassificationTolerances` — every tunable, authored in millimetres, converted once per run via `ToDrawingUnits`. Passed by value so runs cannot perturb each other. Defaults (measured against real drawings, not assumed):

| Tolerance | Default (mm) | Meaning |
|---|---|---|
| `MinOpeningWidth` / `MaxOpeningWidth` | 600 / 4000 | Gap width gates |
| `ThicknessEpsilon` | 150 | Slack on "pair separated by wall thickness" (scored, not gated) |
| `AxisOffsetTolerance` | 10 | Collinearity slack for run grouping / band membership |
| `EndpointTolerance` | 5 | Face-bracketing and interval-join slack |
| `HingeTolerance` | 150 | Hinge/leaf/jamb position slack (leaf rectangles put corners tens of mm off the hinge) |
| `MinSwingRadius` / `MaxSwingRadius` | 500 / 1500 | Door swing radius band |
| `MinColumnSide` / `MaxColumnSide` | 150 / 800 | Column rectangle side band |
| `AngleToleranceDegrees` | 2° | Parallel/perpendicular slack |
| `MinSwingSweepDegrees` / `MaxSwingSweepDegrees` | 60° / 120° | Swing sweep band (real swings measure 83–94°) |

## Reconstruction

`Reconstruction/WallFootprintBuilder` turns each run into `WallPiece`s: the run's covered intervals plus the accepted openings' spans, merged — so a wall spans its doorways as one solid (the opening's void cut leaves the lintel), while gaps with *no* opening stay gaps and split the wall. Corners come from `run.FromAxis(±thickness/2)`; the drawn coordinates survive verbatim.

`Reconstruction/OpeningParameterizer` turns an `Opening` into a placeable element:

- **Door**: leaf width = swing radius (clamped to the opening width); hinge end = whichever span end the arc center is nearer; swing side = sign of the arc midpoint's normal offset. Sill 0, head = door height option.
- **Window**: span and width from the classifier; sill/head from options.
- **Unknown**: a door-height void with no filling element (policy-dependent, see below).

## BIM model and IFC export

`Bim/BimModel.cs`, `Bim/BimElements.cs` — a neutral model deliberately independent of both the CAD types and any output schema. All lengths **millimetres**, world XY, heights Z above the storey. `BimWall` (centreline + exact footprint polygon + openings), `BimColumn`, `BimOpening`/`BimDoor`/`BimWindow`. Currently everything lands on one storey ("Level 1", elevation 0).

`Bim/CadToBimConverter.Convert` orchestrates: groups openings under their host run, builds pieces, parameterizes openings per kind, applies `BimConversionOptions` (wall/door heights, window sill/head, and `UnknownOpeningPolicy`: `VoidOnly` default / `Skip` / `AsWindow`), converts to mm at this edge, and fills a `ConversionReport`.

`Bim/Ifc/IfcExporter` (implements `IBimExporter`) writes IFC4 as a raw STEP file via `StepWriter` — no external IFC library. Spatial tree (project → site → building → storey), walls as extruded footprint profiles with an axis curve, openings cut with `IfcRelVoidsElement` and filled via `IfcRelFillsElement`, identity placements, millimetre units. `IfcGuid` produces the 22-character IFC GUID encoding.

**To add another output format** implement `IBimExporter` against `BimModel` and swap it into `ConvertPipeline.Run` — nothing upstream changes.

## Persistence

`Services/SegmentationStore` — sidecar JSON at `<drawing>.c2b.json`, versioned, holding `(entityHandle, ordinal, class)` per classified primitive. The drawing is never modified. Because the walk order is deterministic, `PrimitiveKey` resolves back to the same primitives on the next load; entries that no longer resolve (drawing edited) are silently skipped.

## Entry points for integration

| Goal | Call |
|---|---|
| Full headless conversion (respects sidecar) | `ConvertPipeline.RunHeadless(cadPath, outPath, options)` |
| Convert an existing classification | `ConvertPipeline.Run(result, mmPerUnit, options, src, outPath)` |
| Load a drawing into the store | `DrawingModel.Load(CadRenderSource.Read(path))` |
| Automatic classification only | `ClassificationService.Load(...)` then `ClassifyAll(...)` |
| Tag-driven classification | `ClassificationService.ClassifyTagged(...)` |
| Diagnostic detection report | `ClassificationDump.Run(path, sMin, sMax)` or `--dump` |
| IFC as a string (tests) | `new IfcExporter().ExportToString(model)` |

## Extension points, in practice

- **New CAD entity type** → one `case` in `CadEntityWalker.Emit`; every sink (viewport, store, classifier) gets it at once.
- **New detection heuristic** → add an `OpeningEvidence` flag and a check in `OpeningClassifier.Evaluate`; wire it into the `supported` acceptance and, if diagnostic, `ClassificationReport`.
- **New export format** → implement `IBimExporter`.
- **New BIM element kind** → extend `BimModel`/`BimElements`, fill it in `CadToBimConverter`, emit it in the exporter.
- **Different tolerances** → pass a custom `ClassificationTolerances` (millimetres) into `ClassifyAll`/`ClassifyTagged`; never edit drawing-unit values downstream.

Known stubs: `CadClassifier.ClassifySpaces`, `SplitWalls`, `CreateTopologicalPoint` are placeholders for room/space detection; `Space` and `TextElement` exist to support them.
