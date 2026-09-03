using ACadSharp.Types.Units;
using Cad2Bim.Classification;

namespace Cad2Bim.Services {
    /// <summary>Walls, the physical runs they assemble into, the openings found in them, and any
    /// structural columns claimed out of the wall tags before pairing.</summary>
    public sealed record ClassificationResult(IReadOnlyList<Wall> Walls, IReadOnlyList<Opening> Openings,
                                              IReadOnlyList<WallRun> Runs,
                                              IReadOnlyList<ColumnFootprint> Columns);

    // Sole bridge between ViewModels and the Model layer.
    public class ClassificationService {
        private List<GeometryElement> _geometry = new();
        private List<Segment> _segments = new();

        public IReadOnlyList<GeometryElement> Geometry => _geometry;
        public int SegmentCount => _segments.Count;
        public bool HasData => _geometry.Count > 0;

        /// <summary>INSUNITS of the loaded drawing — Unitless when the file does not declare one.</summary>
        public UnitsType Units { get; private set; } = UnitsType.Unitless;

        /// <summary>Millimetres per drawing unit, the scale every physical setting is converted through.</summary>
        public double MillimetersPerUnit { get; private set; } = DrawingUnits.MillimetersPerUnit(DrawingUnits.Fallback);

        public void Load(string filePath) => Load(CadRenderSource.Read(filePath));

        public void Load(ACadSharp.CadDocument document) {
            var (geometry, _) = CadLoader.LoadCadEntities(document);
            Load(geometry, document.Header?.InsUnits ?? UnitsType.Unitless);
        }

        /// <summary>Feed from an already-built store (<see cref="DrawingModel.AnalyzableGeometry"/>).</summary>
        public void Load(List<GeometryElement> geometry, UnitsType units) {
            _geometry = geometry;
            _segments = geometry.OfType<Segment>().ToList();

            Units = units;
            MillimetersPerUnit = DrawingUnits.MillimetersPerUnit(Units);
        }

        /// <summary>
        /// Bounds are millimetres, independent of how the file happens to be scaled; they are
        /// converted into the drawing's own units here, which is what the geometry is in.
        /// </summary>
        public List<Wall> Classify(double sMinMillimeters, double sMaxMillimeters) {
            Wall.SMin = sMinMillimeters / MillimetersPerUnit;
            Wall.SMax = sMaxMillimeters / MillimetersPerUnit;
            return CadClassifier.ClassifyWalls(_segments);
        }

        /// <summary>
        /// Walls and the openings in them. Tolerances arrive in millimetres and are converted here,
        /// the one place that knows the drawing's scale, so nothing downstream ever sees a raw
        /// drawing unit.
        /// </summary>
        public ClassificationResult ClassifyAll(double sMinMillimeters, double sMaxMillimeters,
                                                ClassificationTolerances? toleranceMillimeters = null,
                                                ClassificationReport? report = null) {
            List<Wall> walls = Classify(sMinMillimeters, sMaxMillimeters);

            ClassificationTolerances tolerances =
                (toleranceMillimeters ?? ClassificationTolerances.DefaultMillimeters)
                    .ToDrawingUnits(MillimetersPerUnit);

            if (report is not null) report.MillimetersPerUnit = MillimetersPerUnit;

            // Runs are built here rather than inside the classifier so they survive into the
            // result — BIM conversion wants one wall element per physical run, not per fragment.
            List<WallRun> runs = WallRun.Build(walls, tolerances);
            List<Opening> openings = OpeningClassifier.Classify(runs, _geometry, tolerances, report);
            return new ClassificationResult(walls, openings, runs, Array.Empty<ColumnFootprint>());
        }

        /// <summary>
        /// The same result, but driven by the user's own tags instead of the whole drawing:
        /// closed rectangles in the wall tags become columns before pairing can steal their
        /// faces; door and window tags force openings on their runs, and win over anything the
        /// gap heuristics detect in the same place. Opening evidence is still searched against
        /// the full geometry — jambs and swings are rarely tagged as wall.
        /// </summary>
        public ClassificationResult ClassifyTagged(IReadOnlyList<Segment> wallSegments,
                                                   IReadOnlyList<GeometryElement> doorGeometry,
                                                   IReadOnlyList<GeometryElement> windowGeometry,
                                                   double sMinMillimeters, double sMaxMillimeters,
                                                   ClassificationTolerances? toleranceMillimeters = null) {
            Wall.SMin = sMinMillimeters / MillimetersPerUnit;
            Wall.SMax = sMaxMillimeters / MillimetersPerUnit;

            ClassificationTolerances tolerances =
                (toleranceMillimeters ?? ClassificationTolerances.DefaultMillimeters)
                    .ToDrawingUnits(MillimetersPerUnit);

            var (columns, remaining) = ColumnDetector.Detect(wallSegments, tolerances);
            List<Wall> walls = CadClassifier.ClassifyWalls(remaining);
            List<WallRun> runs = WallRun.Build(walls, tolerances);

            List<Opening> forced = TaggedOpeningBuilder.Build(runs, doorGeometry, OpeningKind.Door, tolerances);
            forced.AddRange(TaggedOpeningBuilder.Build(runs, windowGeometry, OpeningKind.Window, tolerances));

            List<Opening> detected = OpeningClassifier.Classify(runs, _geometry, tolerances);
            List<Opening> openings = TaggedOpeningBuilder.MergeDetected(forced, detected);

            return new ClassificationResult(walls, openings, runs, columns);
        }
    }
}
