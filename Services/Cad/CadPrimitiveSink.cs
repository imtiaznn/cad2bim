namespace Cad2Bim.Services.Cad {
    /// <summary>
    /// Collects one walk of a drawing as addressable <see cref="CadPrimitive"/>s — the unified
    /// store the viewport renders, the brush picks and the classifier is fed from. Analyzable
    /// geometry is kept as exact primitives (arcs stay arcs); annotation geometry (hatch
    /// boundaries, dimension blocks) only exists as strokes, so those are kept as
    /// <see cref="PolylinePath"/>s, drawable but never classified.
    /// </summary>
    internal sealed class CadPrimitiveSink : ICadGeometrySink {
        // Purely degenerate spans, not a physical filter: a zero-length segment has no direction,
        // and Segment.Direction() would divide by zero and quietly poison every parallelism test
        // it takes part in. Anything with a real length is the classifier's problem, not ours.
        private const double MinLengthSquared = 1e-18;

        public List<CadPrimitive> Primitives { get; } = new();
        public List<TextElement> Texts { get; } = new();

        private ulong _entityHandle;
        private int _ordinal;

        public bool WantsStrokes => true;
        public bool WantsPrimitives => true;

        public void BeginEntity(in EntityContext context) {
            _entityHandle = context.EntityHandle;
            _ordinal = 0;
        }

        public void Polyline(IReadOnlyList<(double X, double Y)> points, bool isClosed, bool analyzable) {
            // Analyzable strokes duplicate the primitive view; the annotation ones are the only
            // representation their geometry gets.
            if (analyzable || points.Count < 2) {
                return;
            }

            Add(new PolylinePath(points.Select(p => new Point(p.X, p.Y)).ToList(), isClosed),
                classifiable: false);
        }

        public void Line(double x1, double y1, double x2, double y2) {
            double dx = x2 - x1;
            double dy = y2 - y1;

            if ((dx * dx) + (dy * dy) < MinLengthSquared) {
                return;
            }

            Add(new Segment(new Point(x1, y1), new Point(x2, y2)), classifiable: true);
        }

        public void Arc(double centerX, double centerY, double radius, double startAngle, double endAngle) {
            if (radius <= 0) {
                return;
            }

            Add(new Arc(new Point(centerX, centerY), radius, startAngle, endAngle), classifiable: true);
        }

        public void Text(double x, double y, double height, string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return;
            }

            Texts.Add(new TextElement {
                P1 = new Point(x, y),
                P2 = new Point(x + (height * value.Length * 0.6), y + height), // rough bbox estimate
                Text = value
            });
        }

        private void Add(GeometryElement geometry, bool classifiable) {
            int id = Primitives.Count;
            geometry.SourceId = id;
            Primitives.Add(new CadPrimitive {
                Id = id,
                Key = new PrimitiveKey(_entityHandle, _ordinal++),
                Geometry = geometry,
                IsClassifiable = classifiable
            });
        }
    }
}
