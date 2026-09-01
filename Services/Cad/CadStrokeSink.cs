using Cad2Bim.ViewModels.Shapes;

namespace Cad2Bim.Services.Cad {
    /// <summary>
    /// Collects the walker's stroke view as render-ready polylines — the viewport's ground truth.
    /// Nothing is classified or filtered; every drawable entity in the file lands here exactly as
    /// drawn, so a classified overlay always has something honest to be compared against.
    /// </summary>
    internal sealed class CadStrokeSink : ICadGeometrySink {
        public List<object> Shapes { get; } = new();

        public bool WantsStrokes => true;
        public bool WantsPrimitives => false;

        public void Polyline(IReadOnlyList<(double X, double Y)> points, bool isClosed) =>
            Shapes.Add(new PolylineShape(points, isClosed));

        public void Line(double x1, double y1, double x2, double y2) { }
        public void Arc(double centerX, double centerY, double radius, double startAngle, double endAngle) { }
        public void Text(double x, double y, double height, string value) { }
    }
}
