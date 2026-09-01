namespace Cad2Bim.Classification {
    /// <summary>
    /// A uniform grid over segment bounding boxes, so the search for what lies inside an opening
    /// visits only nearby geometry instead of the whole drawing.
    /// <para>
    /// Without it every candidate opening is compared against every segment in the file. On a real
    /// plan that is a few thousand candidates against sixty-odd thousand segments, and the pass
    /// takes half a minute — far too slow to sit behind a settings slider that re-runs on every
    /// change.
    /// </para>
    /// <para>
    /// Segments are filed into every cell their bounding box touches, so a long wall line is found
    /// from anywhere along its length, not just near its midpoint.
    /// </para>
    /// </summary>
    internal sealed class SpatialGrid {
        private readonly double _cellSize;
        private readonly Dictionary<(int X, int Y), List<int>> _cells = new();
        private readonly IReadOnlyList<Segment> _segments;

        public SpatialGrid(IReadOnlyList<Segment> segments, double cellSize) {
            _segments = segments;
            _cellSize = cellSize > 0 ? cellSize : 1.0;

            for (int i = 0; i < segments.Count; i++) {
                Segment segment = segments[i];
                var (minX, minY, maxX, maxY) = Bounds(segment);

                for (int x = Cell(minX); x <= Cell(maxX); x++) {
                    for (int y = Cell(minY); y <= Cell(maxY); y++) {
                        if (!_cells.TryGetValue((x, y), out List<int>? cell)) {
                            _cells[(x, y)] = cell = new List<int>();
                        }
                        cell.Add(i);
                    }
                }
            }
        }

        /// <summary>Segments whose bounding box touches the given box, each returned once.</summary>
        public IEnumerable<Segment> Near(double minX, double minY, double maxX, double maxY) {
            HashSet<int> seen = new();

            for (int x = Cell(minX); x <= Cell(maxX); x++) {
                for (int y = Cell(minY); y <= Cell(maxY); y++) {
                    if (!_cells.TryGetValue((x, y), out List<int>? cell)) continue;

                    foreach (int index in cell) {
                        if (seen.Add(index)) yield return _segments[index];
                    }
                }
            }
        }

        /// <summary>Segments near a span, widened by <paramref name="margin"/> on every side.</summary>
        public IEnumerable<Segment> Near(Point a, Point b, double margin) =>
            Near(Math.Min(a.x, b.x) - margin, Math.Min(a.y, b.y) - margin,
                 Math.Max(a.x, b.x) + margin, Math.Max(a.y, b.y) + margin);

        private static (double MinX, double MinY, double MaxX, double MaxY) Bounds(Segment segment) =>
            (Math.Min(segment.P1.x, segment.P2.x), Math.Min(segment.P1.y, segment.P2.y),
             Math.Max(segment.P1.x, segment.P2.x), Math.Max(segment.P1.y, segment.P2.y));

        private int Cell(double value) => (int)Math.Floor(value / _cellSize);
    }
}
