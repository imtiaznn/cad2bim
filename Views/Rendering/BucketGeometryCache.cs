using System.Windows;
using System.Windows.Media;
using Cad2Bim.Services;

namespace Cad2Bim.Views.Rendering {
    using Point = System.Windows.Point;

    /// <summary>
    /// Frozen stroke geometry per (bucket, chunk of 4096 primitive ids), built lazily and
    /// invalidated per chunk. A brush dab therefore rebuilds a few thousand primitives at most,
    /// not the whole drawing; a zoom re-stroke reuses every cached geometry with a new pen.
    /// </summary>
    internal sealed class BucketGeometryCache {
        public const int ChunkSize = 4096;

        private readonly DrawingModel _model;
        private readonly Dictionary<(PrimitiveClass Bucket, int Chunk), Geometry?> _cache = new();

        public BucketGeometryCache(DrawingModel model) => _model = model;

        public int ChunkCount => (_model.Primitives.Count + ChunkSize - 1) / ChunkSize;

        /// <summary>Drops every bucket's geometry for the chunks containing these ids.</summary>
        public void Invalidate(IReadOnlyList<int> ids) {
            HashSet<int> chunks = new();
            foreach (int id in ids) {
                chunks.Add(id / ChunkSize);
            }

            foreach (int chunk in chunks) {
                foreach (PrimitiveClass bucket in Enum.GetValues<PrimitiveClass>()) {
                    _cache.Remove((bucket, chunk));
                }
            }
        }

        public Geometry? Get(PrimitiveClass bucket, int chunk) {
            if (_cache.TryGetValue((bucket, chunk), out Geometry? cached)) {
                return cached;
            }

            Geometry? built = Build(bucket, chunk);
            _cache[(bucket, chunk)] = built;
            return built;
        }

        private Geometry? Build(PrimitiveClass bucket, int chunk) {
            int start = chunk * ChunkSize;
            int end = Math.Min(start + ChunkSize, _model.Primitives.Count);

            StreamGeometry? geometry = null;
            StreamGeometryContext? ctx = null;

            for (int id = start; id < end; id++) {
                if (_model.ClassOf(id) != bucket) {
                    continue;
                }

                geometry ??= new StreamGeometry();
                ctx ??= geometry.Open();
                Append(ctx, _model.Primitives[id].Geometry);
            }

            ctx?.Close();
            geometry?.Freeze();
            return geometry;
        }

        /// <summary>Stroke figure for one primitive, shared by the buckets and the hover highlight.</summary>
        public static void Append(StreamGeometryContext ctx, GeometryElement element) {
            switch (element) {
                case Segment s:
                    ctx.BeginFigure(new Point(s.P1.x, s.P1.y), false, false);
                    ctx.LineTo(new Point(s.P2.x, s.P2.y), true, false);
                    break;

                case Arc a:
                    AppendArc(ctx, a);
                    break;

                case PolylinePath p when p.Points.Count >= 2:
                    ctx.BeginFigure(new Point(p.Points[0].x, p.Points[0].y), false, p.IsClosed);
                    ctx.PolyLineTo(p.Points.Skip(1).Select(q => new Point(q.x, q.y)).ToList(), true, false);
                    break;
            }
        }

        private static void AppendArc(StreamGeometryContext ctx, Arc arc) {
            Point At(double angle) => new(arc.Center.x + (arc.Radius * Math.Cos(angle)),
                                          arc.Center.y + (arc.Radius * Math.Sin(angle)));

            double sweep = arc.Sweep;
            Size size = new(arc.Radius, arc.Radius);

            ctx.BeginFigure(At(arc.StartAngle), false, false);

            // Model angles run CCW about a Y-up CAD axis, but WPF resolves sweep direction in its
            // own Y-down convention, so a CAD CCW arc is asked for as Clockwise. A (near-)full
            // circle has no distinct endpoints for a single ArcTo, so it is drawn as two halves.
            if (sweep >= (2 * Math.PI) - 1e-9) {
                ctx.ArcTo(At(arc.StartAngle + Math.PI), size, 0, false, SweepDirection.Clockwise, true, false);
                ctx.ArcTo(At(arc.StartAngle), size, 0, false, SweepDirection.Clockwise, true, false);
            } else {
                ctx.ArcTo(At(arc.StartAngle + sweep), size, 0, sweep > Math.PI,
                          SweepDirection.Clockwise, true, false);
            }
        }
    }
}
