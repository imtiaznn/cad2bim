using Cad2Bim.Classification;
using Cad2Bim.Reconstruction;
using Cad2Bim.Services;

namespace Cad2Bim.Bim {
    /// <summary>
    /// Orchestrates classification results into a <see cref="BimModel"/>: analytic wall footprints
    /// from the runs (the drawn coordinates survive verbatim), the opening parameterizers for
    /// doors and windows, and the millimetre conversion at the edge. Drawing units in,
    /// millimetres out.
    /// </summary>
    public static class CadToBimConverter {
        public static BimModel Convert(ClassificationResult classification, double millimetersPerUnit,
                                       BimConversionOptions options, string? sourceFile = null,
                                       ConversionReport? report = null) {
            // The interval-merge slack, in drawing units — the same endpoint tolerance the
            // classifier joins faces with, so a wall never splits where classification did not.
            double joinTolerance =
                ClassificationTolerances.DefaultMillimeters.EndpointTolerance / millimetersPerUnit;

            // Openings grouped under their host run. Opening.Wall is the run's reference
            // fragment; the fragment-membership fallback covers the day that coupling changes.
            Dictionary<Wall, WallRun> runByReference = classification.Runs.ToDictionary(r => r.Reference);
            Dictionary<Wall, WallRun> runByFragment = classification.Runs
                .SelectMany(r => r.Fragments.Select(f => (Fragment: f, Run: r)))
                .ToDictionary(p => p.Fragment, p => p.Run);

            Dictionary<WallRun, List<Opening>> openingsByRun = new();
            int skipped = 0;

            foreach (Opening opening in classification.Openings) {
                if (!runByReference.TryGetValue(opening.Wall, out WallRun? run)
                    && !runByFragment.TryGetValue(opening.Wall, out run)) {
                    report?.Warn("an opening's host wall belongs to no run — opening skipped");
                    skipped++;
                    continue;
                }
                if (!openingsByRun.TryGetValue(run, out List<Opening>? list)) {
                    openingsByRun[run] = list = new List<Opening>();
                }
                list.Add(opening);
            }

            List<BimWall> walls = new();
            int doors = 0, windows = 0, unknown = 0;

            foreach (WallRun run in classification.Runs) {
                openingsByRun.TryGetValue(run, out List<Opening>? runOpenings);
                runOpenings ??= new List<Opening>();

                foreach (WallPiece piece in WallFootprintBuilder.Build(run, runOpenings, joinTolerance)) {
                    List<BimOpening> pieceOpenings = new();

                    foreach (Opening opening in runOpenings) {
                        if (!piece.Contains((opening.AxisSpan.Start + opening.AxisSpan.End) / 2)) continue;

                        BimOpening? element = opening.Kind switch {
                            OpeningKind.Door => Count(OpeningParameterizer.Door(opening, run, options, millimetersPerUnit), ref doors),
                            OpeningKind.Window => Count(OpeningParameterizer.Window(opening, run, options, millimetersPerUnit), ref windows),
                            _ => options.UnknownOpenings switch {
                                UnknownOpeningPolicy.Skip => null,
                                UnknownOpeningPolicy.AsWindow => Count(OpeningParameterizer.Window(opening, run, options, millimetersPerUnit), ref windows),
                                _ => Count(OpeningParameterizer.Unknown(opening, run, options, millimetersPerUnit), ref unknown)
                            }
                        };

                        if (element is null) skipped++;
                        else pieceOpenings.Add(element);
                    }

                    walls.Add(BuildWall(piece, walls.Count + 1, pieceOpenings, options, millimetersPerUnit));
                }
            }

            List<BimColumn> columns = classification.Columns
                .Select((c, i) => new BimColumn {
                    Name = $"Column-{i + 1}",
                    HeightMm = options.WallHeightMm,
                    Footprint = new BimPolygon(c.Corners
                        .Select(p => new BimPoint(p.x * millimetersPerUnit, p.y * millimetersPerUnit))
                        .ToList())
                })
                .ToList();

            if (report is not null) {
                report.WallCount = walls.Count;
                report.ColumnCount = columns.Count;
                report.DoorCount = doors;
                report.WindowCount = windows;
                report.UnknownOpeningCount = unknown;
                report.SkippedOpenings = skipped;
            }

            return new BimModel {
                ProjectName = string.IsNullOrEmpty(sourceFile)
                    ? "Cad2Bim project"
                    : System.IO.Path.GetFileNameWithoutExtension(sourceFile),
                SourceFile = sourceFile,
                Storeys = new[] {
                    new BimStorey { Name = "Level 1", ElevationMm = 0, Walls = walls, Columns = columns }
                }
            };
        }

        private static BimOpening Count(BimOpening element, ref int counter) {
            counter++;
            return element;
        }

        private static BimWall BuildWall(WallPiece piece, int ordinal, IReadOnlyList<BimOpening> openings,
                                         BimConversionOptions options, double millimetersPerUnit) {
            WallRun run = piece.Run;
            Point axisStart = run.FromAxis(piece.Start), axisEnd = run.FromAxis(piece.End);

            return new BimWall {
                Name = $"Wall-{ordinal}",
                ThicknessMm = run.Thickness * millimetersPerUnit,
                HeightMm = options.WallHeightMm,
                AxisStart = new BimPoint(axisStart.x * millimetersPerUnit, axisStart.y * millimetersPerUnit),
                AxisEnd = new BimPoint(axisEnd.x * millimetersPerUnit, axisEnd.y * millimetersPerUnit),
                Footprint = new BimPolygon(piece.Corners
                    .Select(p => new BimPoint(p.x * millimetersPerUnit, p.y * millimetersPerUnit))
                    .ToList()),
                Openings = openings
            };
        }
    }
}
