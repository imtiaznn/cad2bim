namespace Cad2Bim.Services.Cad {
    /// <summary>
    /// Collects the walker's primitive view as the model types the classifier reasons about.
    /// Unlike the stroke view this keeps arcs as arcs, and it sees inside blocks — in a real
    /// architectural drawing essentially every door is a block insert, so a classifier fed only
    /// top-level entities finds nothing at all.
    /// </summary>
    internal sealed class CadAnalysisSink : ICadGeometrySink {
        // Purely degenerate spans, not a physical filter: a zero-length segment has no direction,
        // and Segment.Direction() would divide by zero and quietly poison every parallelism test
        // it takes part in. Anything with a real length is the classifier's problem, not ours.
        private const double MinLengthSquared = 1e-18;

        public List<GeometryElement> Geometry { get; } = new();
        public List<TextElement> Texts { get; } = new();

        public bool WantsStrokes => false;
        public bool WantsPrimitives => true;

        public void BeginEntity(in EntityContext context) { }

        public void Polyline(IReadOnlyList<(double X, double Y)> points, bool isClosed, bool analyzable) { }

        public void Line(double x1, double y1, double x2, double y2) {
            double dx = x2 - x1;
            double dy = y2 - y1;

            if ((dx * dx) + (dy * dy) < MinLengthSquared) {
                return;
            }

            Geometry.Add(new Segment(new Point(x1, y1), new Point(x2, y2)));
        }

        public void Arc(double centerX, double centerY, double radius, double startAngle, double endAngle) {
            if (radius <= 0) {
                return;
            }

            Geometry.Add(new Arc(new Point(centerX, centerY), radius, startAngle, endAngle));
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
    }
}
