namespace Cad2Bim.Services {
    /// <summary>
    /// Uniform spatial index over the classifiable primitives, for picking: "which line is under
    /// the cursor" and "which lines does this brush stroke sweep across". Each primitive is held
    /// as a short polyline (a segment is its two endpoints, an arc a coarse tessellation), which
    /// makes every distance query a point-or-capsule-to-span test.
    /// </summary>
    public sealed class PrimitiveGrid {
        private const int ArcStepsPerRadian = 8;
        private const int MaxArcSteps = 64;

        private readonly double _cellSize;
        private readonly Dictionary<(int X, int Y), List<int>> _cells = new();

        // Indexed by primitive id; null for primitives that are not pickable.
        private readonly (double X, double Y)[]?[] _polylines;

        public PrimitiveGrid(IReadOnlyList<CadPrimitive> primitives, double cellSize) {
            _cellSize = cellSize > 0 ? cellSize : 1.0;
            _polylines = new (double X, double Y)[primitives.Count][];

            foreach (CadPrimitive primitive in primitives) {
                if (!primitive.IsClassifiable) {
                    continue;
                }

                var polyline = Flatten(primitive.Geometry);
                if (polyline is null) {
                    continue;
                }

                _polylines[primitive.Id] = polyline;
                File(primitive.Id, polyline);
            }
        }

        /// <summary>Id of the pickable primitive nearest to <paramref name="p"/> within <paramref name="tolerance"/>, or null.</summary>
        public int? NearestWithin(Point p, double tolerance) {
            int? best = null;
            double bestDistance = tolerance;

            foreach (int id in Candidates(p.x - tolerance, p.y - tolerance, p.x + tolerance, p.y + tolerance)) {
                double d = DistanceToPolyline(p, _polylines[id]!);
                if (d <= bestDistance) {
                    bestDistance = d;
                    best = id;
                }
            }

            return best;
        }

        /// <summary>
        /// Ids of pickable primitives within <paramref name="radius"/> of the segment a→b — the
        /// swept-brush query, so a fast drag between two mouse samples leaves no gaps.
        /// </summary>
        public IEnumerable<int> IntersectingCapsule(Point a, Point b, double radius) {
            double minX = Math.Min(a.x, b.x) - radius, minY = Math.Min(a.y, b.y) - radius;
            double maxX = Math.Max(a.x, b.x) + radius, maxY = Math.Max(a.y, b.y) + radius;
            Segment sweep = new(a, b.Equals(a) ? new Point(a.x + 1e-9, a.y) : b);

            foreach (int id in Candidates(minX, minY, maxX, maxY)) {
                var polyline = _polylines[id]!;
                for (int i = 0; i + 1 < polyline.Length; i++) {
                    Segment span = new(new Point(polyline[i].X, polyline[i].Y),
                                       new Point(polyline[i + 1].X, polyline[i + 1].Y));
                    if (DistanceSegmentToSegment(sweep, span) <= radius) {
                        yield return id;
                        break;
                    }
                }
            }
        }

        private IEnumerable<int> Candidates(double minX, double minY, double maxX, double maxY) {
            HashSet<int> seen = new();

            for (int x = Cell(minX); x <= Cell(maxX); x++) {
                for (int y = Cell(minY); y <= Cell(maxY); y++) {
                    if (!_cells.TryGetValue((x, y), out List<int>? cell)) continue;

                    foreach (int id in cell) {
                        if (seen.Add(id)) yield return id;
                    }
                }
            }
        }

        private void File(int id, (double X, double Y)[] polyline) {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var (x, y) in polyline) {
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }

            for (int x = Cell(minX); x <= Cell(maxX); x++) {
                for (int y = Cell(minY); y <= Cell(maxY); y++) {
                    if (!_cells.TryGetValue((x, y), out List<int>? cell)) {
                        _cells[(x, y)] = cell = new List<int>();
                    }
                    cell.Add(id);
                }
            }
        }

        private static (double X, double Y)[]? Flatten(GeometryElement geometry) {
            switch (geometry) {
                case Segment s:
                    return [(s.P1.x, s.P1.y), (s.P2.x, s.P2.y)];

                case Arc a: {
                    int steps = Math.Clamp((int)Math.Ceiling(a.Sweep * ArcStepsPerRadian), 2, MaxArcSteps);
                    var points = new (double X, double Y)[steps + 1];
                    for (int i = 0; i <= steps; i++) {
                        Point p = a.PointAt(a.StartAngle + (a.Sweep * i / steps));
                        points[i] = (p.x, p.y);
                    }
                    return points;
                }

                default:
                    return null;
            }
        }

        private static double DistanceToPolyline(Point p, (double X, double Y)[] polyline) {
            double best = double.MaxValue;
            for (int i = 0; i + 1 < polyline.Length; i++) {
                Segment span = new(new Point(polyline[i].X, polyline[i].Y),
                                   new Point(polyline[i + 1].X, polyline[i + 1].Y));
                best = Math.Min(best, Segment.DistancePointToSegment(p, span));
            }
            return best;
        }

        // Zero when they cross; otherwise the closest endpoint-to-span distance, which is exact
        // for non-crossing segments.
        private static double DistanceSegmentToSegment(Segment a, Segment b) {
            if (Segment.Intersects(a, b)) return 0;

            return Math.Min(
                Math.Min(Segment.DistancePointToSegment(a.P1, b), Segment.DistancePointToSegment(a.P2, b)),
                Math.Min(Segment.DistancePointToSegment(b.P1, a), Segment.DistancePointToSegment(b.P2, a)));
        }

        private int Cell(double value) => (int)Math.Floor(value / _cellSize);
    }
}
