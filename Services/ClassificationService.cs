using ACadSharp.Types.Units;

namespace Cad2Bim.Services {
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
    }
}
