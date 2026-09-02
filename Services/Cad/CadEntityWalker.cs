using ACadSharp;
using ACadSharp.Entities;

// Cad2Bim.Arc / Cad2Bim.Point (the classification model) shadow the ACadSharp entities of the
// same name inside this namespace, so the CAD ones are always spelled through these aliases.
using CadArc = ACadSharp.Entities.Arc;
using CadPoint = ACadSharp.Entities.Point;

namespace Cad2Bim.Services.Cad {
    /// <summary>
    /// Walks a CadDocument's model space once, flattening every block into world coordinates and
    /// handing each entity to a sink. This is the single place that knows how to read a drawing;
    /// both the viewport and the classifier are sinks over it, so a newly supported entity type
    /// reaches both at once.
    /// </summary>
    internal static class CadEntityWalker {
        // Chord resolution for tessellated curves: one vertex per ~3 degrees of sweep.
        private const double StepAngle = Math.PI / 60.0;
        private const int MinCurvePoints = 2;
        private const int MaxCurvePoints = 512;

        // Blocks nest; this only guards against a self-referencing definition.
        private const int MaxDepth = 16;

        private const double FullCircle = 2 * Math.PI;

        public static void Walk(CadDocument document, ICadGeometrySink sink) {
            foreach (Entity entity in document.Entities) {
                sink.BeginEntity(new EntityContext(entity.Handle));
                Emit(entity, sink, Xform.Identity, 0, analyzable: true);
            }
        }

        /// <param name="analyzable">
        /// False inside annotation (hatch fills, dimension blocks). Such geometry still has to be
        /// drawn, but feeding it to the classifier only manufactures phantom walls and openings.
        /// </param>
        private static void Emit(Entity entity, ICadGeometrySink sink, Xform xform, int depth, bool analyzable) {
            if (depth > MaxDepth || entity.IsInvisible) {
                return;
            }

            switch (entity) {
                case Line line:
                    Add(sink, xform, analyzable, false,
                        (line.StartPoint.X, line.StartPoint.Y), (line.EndPoint.X, line.EndPoint.Y));
                    break;

                case LwPolyline lwPolyline:
                    EmitPolyline(sink, xform, analyzable, lwPolyline.IsClosed,
                        lwPolyline.Vertices.Select(v => (v.Location.X, v.Location.Y, v.Bulge)).ToList());
                    break;

                case Polyline2D polyline2D:
                    EmitPolyline(sink, xform, analyzable, polyline2D.IsClosed,
                        polyline2D.Vertices.Select(v => (v.Location.X, v.Location.Y, v.Bulge)).ToList());
                    break;

                case Polyline3D polyline3D:
                    Add(sink, xform, analyzable, polyline3D.IsClosed,
                        polyline3D.Vertices.Select(v => (v.Location.X, v.Location.Y)).ToList());
                    break;

                // Arc derives from Circle, so it has to be matched first.
                case CadArc arc:
                    EmitCurve(sink, xform, analyzable, false,
                        Flat(arc.PolygonalVertexes(CurvePoints(arc.Sweep))),
                        arc.Center.X, arc.Center.Y, arc.Radius, arc.StartAngle, arc.EndAngle);
                    break;

                case Circle circle:
                    EmitCurve(sink, xform, analyzable, true,
                        Flat(circle.PolygonalVertexes(CurvePoints(FullCircle))),
                        circle.Center.X, circle.Center.Y, circle.Radius, 0, FullCircle);
                    break;

                case Ellipse ellipse:
                    Add(sink, xform, analyzable, ellipse.IsFullEllipse,
                        Flat(ellipse.PolygonalVertexes(
                            CurvePoints(ellipse.EndParameter - ellipse.StartParameter))));
                    break;

                case Spline spline:
                    if (spline.TryPolygonalVertexes(MaxCurvePoints / 4, out var splinePoints)) {
                        Add(sink, xform, analyzable, spline.IsClosed, Flat(splinePoints));
                    }
                    break;

                case Solid solid:
                    Add(sink, xform, analyzable, true,
                        (solid.FirstCorner.X, solid.FirstCorner.Y),
                        (solid.SecondCorner.X, solid.SecondCorner.Y),
                        // DXF stores SOLID corners in bow-tie order: 3rd and 4th are swapped.
                        (solid.FourthCorner.X, solid.FourthCorner.Y),
                        (solid.ThirdCorner.X, solid.ThirdCorner.Y));
                    break;

                case Leader leader:
                    Add(sink, xform, analyzable, false, leader.Vertices.Select(v => (v.X, v.Y)).ToList());
                    break;

                case Insert insert:
                    EmitInsert(insert, sink, xform, depth, analyzable);
                    break;

                // Boundary outlines only — the pattern fill itself is not drawn. Explode() here
                // just converts the boundary paths to entities; it applies no transform of its own.
                case Hatch hatch:
                    foreach (Entity child in hatch.Explode()) {
                        Emit(child, sink, xform, depth + 1, analyzable: false);
                    }
                    break;

                // A dimension's lines and arrowheads live in an anonymous block, stored in the
                // coordinate space the dimension itself sits in.
                case Dimension dimension when dimension.Block is not null:
                    foreach (Entity child in dimension.Block.Entities) {
                        Emit(child, sink, xform, depth + 1, analyzable: false);
                    }
                    break;

                // Nothing to stroke, but the classifier wants the string and where it sits.
                case TextEntity text:
                    EmitText(sink, xform, analyzable, text.InsertPoint.X, text.InsertPoint.Y,
                             text.Height, text.Value);
                    break;

                case MText mText:
                    EmitText(sink, xform, analyzable, mText.InsertPoint.X, mText.InsertPoint.Y,
                             mText.Height, mText.Value);
                    break;

                case CadPoint:
                default:
                    break;
            }
        }

        private static void EmitInsert(Insert insert, ICadGeometrySink sink, Xform xform, int depth, bool analyzable) {
            var basePoint = insert.Block.BlockEntity.BasePoint;
            double cos = Math.Cos(insert.Rotation);
            double sin = Math.Sin(insert.Rotation);

            // world = insertPoint + R(rotation) * S(scale) * (p - basePoint), and MINSERT array
            // offsets step along the rotated axes.
            int rows = Math.Max((int)insert.RowCount, 1);
            int columns = Math.Max((int)insert.ColumnCount, 1);

            for (int row = 0; row < rows; row++) {
                for (int column = 0; column < columns; column++) {
                    double offsetX = column * insert.ColumnSpacing;
                    double offsetY = row * insert.RowSpacing;

                    double originX = insert.InsertPoint.X + (offsetX * cos) - (offsetY * sin);
                    double originY = insert.InsertPoint.Y + (offsetX * sin) + (offsetY * cos);

                    double a = cos * insert.XScale;
                    double b = sin * insert.XScale;
                    double c = -sin * insert.YScale;
                    double d = cos * insert.YScale;

                    Xform local = new(a, b, c, d,
                        originX - ((a * basePoint.X) + (c * basePoint.Y)),
                        originY - ((b * basePoint.X) + (d * basePoint.Y)));

                    Xform composed = xform.Compose(local);
                    foreach (Entity child in insert.Block.Entities) {
                        Emit(child, sink, composed, depth + 1, analyzable);
                    }
                }
            }
        }

        private static void EmitPolyline(ICadGeometrySink sink, Xform xform, bool analyzable, bool isClosed,
                                         IReadOnlyList<(double X, double Y, double Bulge)> vertices) {
            if (vertices.Count < 2) {
                return;
            }

            int last = isClosed ? vertices.Count : vertices.Count - 1;

            if (sink.WantsStrokes) {
                List<(double X, double Y)> points = new(vertices.Count);

                for (int i = 0; i < last; i++) {
                    var current = vertices[i];
                    var next = vertices[(i + 1) % vertices.Count];
                    points.Add((current.X, current.Y));
                    AppendBulge(points, current.X, current.Y, next.X, next.Y, current.Bulge);
                }

                if (!isClosed) {
                    points.Add((vertices[^1].X, vertices[^1].Y));
                }

                Stroke(sink, xform, isClosed, analyzable, points);
            }

            if (!analyzable || !sink.WantsPrimitives) {
                return;
            }

            // Primitive view: each span is either a straight line or — where the vertex carries a
            // bulge — a real arc. Door swings are routinely drawn as a bulged LWPOLYLINE, so
            // interpolating these into chords the way the stroke view does would throw away the
            // strongest door cue in the drawing.
            for (int i = 0; i < last; i++) {
                var current = vertices[i];
                var next = vertices[(i + 1) % vertices.Count];

                if (TryBulgeArc(current.X, current.Y, next.X, next.Y, current.Bulge,
                                out double cx, out double cy, out double radius,
                                out double startAngle, out double sweep)) {
                    // A negative bulge sweeps clockwise; state it as the equivalent CCW arc.
                    double a0 = sweep >= 0 ? startAngle : startAngle + sweep;
                    double a1 = sweep >= 0 ? startAngle + sweep : startAngle;
                    EmitArcPrimitive(sink, xform, cx, cy, radius, a0, a1, isFullCircle: false);
                } else {
                    EmitLinePrimitive(sink, xform, current.X, current.Y, next.X, next.Y);
                }
            }
        }

        /// <summary>Interpolates the arc a polyline vertex's bulge describes, endpoints excluded.</summary>
        private static void AppendBulge(List<(double X, double Y)> points,
                                        double x1, double y1, double x2, double y2, double bulge) {
            if (!TryBulgeArc(x1, y1, x2, y2, bulge,
                             out double centerX, out double centerY, out double radius,
                             out double startAngle, out double sweep)) {
                return;
            }

            int steps = CurvePoints(sweep) - 1;
            for (int i = 1; i < steps; i++) {
                double angle = startAngle + (sweep * i / steps);
                points.Add((centerX + (radius * Math.Cos(angle)), centerY + (radius * Math.Sin(angle))));
            }
        }

        /// <summary>
        /// The arc a bulged polyline span describes: bulge = tan(sweep / 4), and the centre sits
        /// off the chord midpoint along its left normal. Shared by the stroke and primitive views
        /// so the two can never disagree about where the arc is.
        /// </summary>
        private static bool TryBulgeArc(double x1, double y1, double x2, double y2, double bulge,
                                        out double centerX, out double centerY, out double radius,
                                        out double startAngle, out double sweep) {
            centerX = centerY = radius = startAngle = sweep = 0;

            if (Math.Abs(bulge) < 1e-9) {
                return false;
            }

            double dx = x2 - x1;
            double dy = y2 - y1;
            if ((dx * dx) + (dy * dy) < 1e-24) {
                return false;
            }

            sweep = 4 * Math.Atan(bulge);
            double offset = (1 - (bulge * bulge)) / (4 * bulge);
            centerX = ((x1 + x2) / 2) - (offset * dy);
            centerY = ((y1 + y2) / 2) + (offset * dx);
            radius = Math.Sqrt(((x1 - centerX) * (x1 - centerX)) + ((y1 - centerY) * (y1 - centerY)));
            startAngle = Math.Atan2(y1 - centerY, x1 - centerX);
            return true;
        }

        /// <summary>An arc or circle: stroked as chords, analysed as the true curve.</summary>
        private static void EmitCurve(ICadGeometrySink sink, Xform xform, bool analyzable, bool isFullCircle,
                                      List<(double X, double Y)> tessellated,
                                      double centerX, double centerY, double radius,
                                      double startAngle, double endAngle) {
            if (sink.WantsStrokes) {
                Stroke(sink, xform, isFullCircle, analyzable, tessellated);
            }

            if (!analyzable || !sink.WantsPrimitives) {
                return;
            }

            if (xform.IsSimilarity) {
                EmitArcPrimitive(sink, xform, centerX, centerY, radius, startAngle, endAngle, isFullCircle);
            } else {
                // Non-uniform scale or shear: this is an ellipse now, and centre/radius/angles
                // would be a lie. Hand over the chords so the geometry still exists, accepting
                // that it no longer reads as a door swing.
                EmitChords(sink, xform, tessellated);
            }
        }

        private static void EmitArcPrimitive(ICadGeometrySink sink, Xform xform,
                                             double centerX, double centerY, double radius,
                                             double startAngle, double endAngle, bool isFullCircle) {
            if (!xform.IsSimilarity) {
                return;
            }

            var (worldX, worldY) = xform.Apply(centerX, centerY);
            double worldRadius = radius * xform.ScaleX;

            if (worldRadius <= 0) {
                return;
            }

            if (isFullCircle) {
                sink.Arc(worldX, worldY, worldRadius, 0, FullCircle);
                return;
            }

            double start = xform.TransformAngle(startAngle);
            double end = xform.TransformAngle(endAngle);

            // A mirrored block (negative determinant — the left-hand/right-hand door case, which
            // accounts for a large share of the doors in a real plan) reverses the sense of the
            // sweep. Swapping the ends restates the same arc as a CCW one; without this the sweep
            // computes as 360 − s and every mirrored door fails the swing test.
            if (xform.Determinant < 0) {
                (start, end) = (end, start);
            }

            sink.Arc(worldX, worldY, worldRadius, start, end);
        }

        private static void EmitLinePrimitive(ICadGeometrySink sink, Xform xform,
                                              double x1, double y1, double x2, double y2) {
            var (ax, ay) = xform.Apply(x1, y1);
            var (bx, by) = xform.Apply(x2, y2);
            sink.Line(ax, ay, bx, by);
        }

        private static void EmitChords(ICadGeometrySink sink, Xform xform,
                                       IReadOnlyList<(double X, double Y)> points) {
            for (int i = 0; i + 1 < points.Count; i++) {
                EmitLinePrimitive(sink, xform, points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y);
            }
        }

        private static void EmitText(ICadGeometrySink sink, Xform xform, bool analyzable,
                                     double x, double y, double height, string value) {
            if (!analyzable || !sink.WantsPrimitives) {
                return;
            }

            var (worldX, worldY) = xform.Apply(x, y);
            sink.Text(worldX, worldY, height * xform.ScaleY, value);
        }

        private static int CurvePoints(double sweep) =>
            Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / StepAngle) + 1, MinCurvePoints, MaxCurvePoints);

        private static List<(double X, double Y)> Flat(IEnumerable<CSMath.XYZ> vertices) =>
            vertices.Select(v => (v.X, v.Y)).ToList();

        private static void Add(ICadGeometrySink sink, Xform xform, bool analyzable, bool isClosed,
                                params (double X, double Y)[] points) =>
            Add(sink, xform, analyzable, isClosed, (IReadOnlyList<(double X, double Y)>)points);

        /// <summary>A run of straight spans: stroked whole, analysed span by span.</summary>
        private static void Add(ICadGeometrySink sink, Xform xform, bool analyzable, bool isClosed,
                                IReadOnlyList<(double X, double Y)> points) {
            if (points.Count < 2) {
                return;
            }

            if (sink.WantsStrokes) {
                Stroke(sink, xform, isClosed, analyzable, points);
            }

            if (!analyzable || !sink.WantsPrimitives) {
                return;
            }

            EmitChords(sink, xform, points);

            if (isClosed) {
                EmitLinePrimitive(sink, xform, points[^1].X, points[^1].Y, points[0].X, points[0].Y);
            }
        }

        private static void Stroke(ICadGeometrySink sink, Xform xform, bool isClosed, bool analyzable,
                                   IReadOnlyList<(double X, double Y)> points) {
            if (points.Count < 2) {
                return;
            }

            var mapped = new (double X, double Y)[points.Count];
            for (int i = 0; i < points.Count; i++) {
                mapped[i] = xform.Apply(points[i].X, points[i].Y);
            }

            sink.Polyline(mapped, isClosed, analyzable);
        }
    }
}
