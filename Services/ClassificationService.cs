using ACadSharp.Types.Units;
using Cad2Bim.Classification;

namespace Cad2Bim.Services {
    /// <summary>Walls and the openings found in them.</summary>
    public sealed record ClassificationResult(IReadOnlyList<Wall> Walls, IReadOnlyList<Opening> Openings);

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
            _geometry = geometry;
            _segments = geometry.OfType<Segment>().ToList();

            Units = document.Header?.InsUnits ?? UnitsType.Unitless;
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

            List<Opening> openings = OpeningClassifier.Classify(walls, _geometry, tolerances, report);
            return new ClassificationResult(walls, openings);
        }
    }
}
