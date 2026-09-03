using ACadSharp;
using Cad2Bim.Services;
using Cad2Bim.Services.Cad;

namespace Cad2Bim {
    public record Point(double x, double y);
    
    // Primitives
    public abstract class GeometryElement {
        public List<Point> Points { get; protected set; } = new();

        /// <summary>
        /// Id of the CadPrimitive this element was loaded as, or -1 for derived geometry (clipped
        /// sub-segments, synthesised opening faces). The classifier passes elements through by
        /// reference, so a classification result can be mapped back onto the drawn primitives.
        /// </summary>
        public int SourceId { get; set; } = -1;
    }
    public class TextElement {
        public Point P1 { get; init; } // top-left
        public Point P2 { get; init; } // bottom-right
        public String Text { get; init; } = string.Empty;
    }

    public abstract class BuildingElement {
        public List<GeometryElement> Geometry { get; protected set; } = new();
        public List<TextElement> Text { get; protected set; } = new();
        public List<BuildingElement> SubElements { get; protected set; } = new();
    }

    // Lines and arcs
    public class Segment : GeometryElement {
        public Segment(Point p1, Point p2) => Points = new List<Point> { p1, p2 };
        public Point P1 => Points[0];
        public Point P2 => Points[1];

        public double Length => Math.Sqrt(Math.Pow(P2.x - P1.x, 2) + Math.Pow(P2.y - P1.y, 2));
        public Point Mid => new((P1.x + P2.x) / 2, (P1.y + P2.y) / 2);

        // A zero-length segment has no direction. Callers used to get (NaN, NaN) here, which made
        // every parallelism test it took part in quietly answer false; (0, 0) at least fails
        // loudly and predictably. The loader drops degenerate spans, so this is a backstop.
        public (double dx, double dy) Direction() {
            double dx = P2.x - P1.x;
            double dy = P2.y - P1.y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            return len < 1e-12 ? (0, 0) : (dx / len, dy / len);
        }

        /// <summary>
        /// Heading folded into [0, 180), so a line and the same line drawn backwards agree. Note
        /// that 179.9 and 0.1 are 0.2 apart, not 179.8 — compare headings with
        /// <see cref="HeadingDifference"/>, never by subtraction.
        /// </summary>
        public double HeadingDegrees {
            get {
                var (dx, dy) = Direction();
                double degrees = Math.Atan2(dy, dx) * (180.0 / Math.PI);
                degrees %= 180.0;
                return degrees < 0 ? degrees + 180.0 : degrees;
            }
        }

        /// <summary>Smallest angle between two folded headings, in [0, 90].</summary>
        public static double HeadingDifference(double a, double b) {
            double diff = Math.Abs(a - b) % 180.0;
            return diff > 90.0 ? 180.0 - diff : diff;
        }

        /// <summary>Where a point falls along this segment's direction, measured from P1.</summary>
        public double Project(Point p) {
            var (dx, dy) = Direction();
            return ((p.x - P1.x) * dx) + ((p.y - P1.y) * dy);
        }

        /// <summary>Signed distance from the segment's infinite line, positive to its left.</summary>
        public double Offset(Point p) {
            var (dx, dy) = Direction();
            return (-dy * (p.x - P1.x)) + (dx * (p.y - P1.y));
        }

        public Point PointAt(double distanceFromP1) {
            var (dx, dy) = Direction();
            return new(P1.x + (dx * distanceFromP1), P1.y + (dy * distanceFromP1));
        }

        /// <summary>The sub-segment between two distances along this one.</summary>
        public Segment Clip(double fromP1, double toP1) => new(PointAt(fromP1), PointAt(toP1));

        public bool IsPerpendicularTo(Segment other, double angleToleranceDegrees = 2.0) =>
            Math.Abs(HeadingDifference(HeadingDegrees, other.HeadingDegrees) - 90.0) <= angleToleranceDegrees;

        public bool isParallelTo(Segment other, double angleToleranceDegrees = 2.0) {
            var (dx1, dy1) = Direction();
            var (dx2, dy2) = other.Direction();

            double cross = Math.Abs(dx1 * dy2 - dy1 * dx2);
            double angleRad = Math.Asin(Math.Clamp(cross, -1.0, 1.0));
            double angleDeg = angleRad * (180.0 / Math.PI);

            return angleDeg <= angleToleranceDegrees;
        }

        public static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.x - b.x, 2) + Math.Pow(a.y - b.y, 2));

        /// <summary>
        /// Whether two segments properly cross. Used to tell a wall junction from an opening: a
        /// corner leaves a gap in a wall face just as a doorway does, but only the corner has
        /// another wall running through it.
        /// </summary>
        public static bool Intersects(Segment a, Segment b) {
            static double Cross(Point o, Point p, Point q) =>
                ((p.x - o.x) * (q.y - o.y)) - ((p.y - o.y) * (q.x - o.x));

            double d1 = Cross(a.P1, a.P2, b.P1);
            double d2 = Cross(a.P1, a.P2, b.P2);
            double d3 = Cross(b.P1, b.P2, a.P1);
            double d4 = Cross(b.P1, b.P2, a.P2);

            // Straddling on both sides is a crossing; touching endpoints count, since a wall that
            // ends exactly on another wall's face is still a junction.
            return ((d1 <= 0 && d2 >= 0) || (d1 >= 0 && d2 <= 0))
                && ((d3 <= 0 && d4 >= 0) || (d3 >= 0 && d4 <= 0));
        }
        public static double Distance(Segment a, Segment b) {
            Point mid = new((a.P1.x + a.P2.x) / 2, (a.P1.y + a.P2.y) / 2);
            return DistancePointToLine(mid, b.P1, b.P2);
        }

        public static bool Overlaps(Segment a, Segment b) {
            double dx = a.P2.x - a.P1.x;
            double dy = a.P2.y - a.P1.y;
            double len = Math.Sqrt(dx * dx + dy * dy);

            if (len == 0) return false; // degenerate segment has no direction to project onto

            dx /= len;
            dy /= len;

            double Project(Point p) => (p.x - a.P1.x) * dx + (p.y - a.P1.y) * dy;

            double b1 = Project(b.P1);
            double b2 = Project(b.P2);

            // a spans [0, len] by construction; overlap is the intersection of the two spans.
            double start = Math.Max(0, Math.Min(b1, b2));
            double end = Math.Min(len, Math.Max(b1, b2));

            return end - start > 0;
        }

        /// <summary>
        /// The stretch of <paramref name="a"/> that <paramref name="b"/> covers, in distances along
        /// a from its P1, or null when they do not overlap. Same test <see cref="Overlaps"/> makes,
        /// but keeping the interval — which is what finding a gap in a wall face needs.
        /// </summary>
        public static (double Start, double End)? OverlapInterval(Segment a, Segment b) {
            double len = a.Length;
            if (len < 1e-12) return null;

            double b1 = a.Project(b.P1);
            double b2 = a.Project(b.P2);

            double start = Math.Max(0, Math.Min(b1, b2));
            double end = Math.Min(len, Math.Max(b1, b2));

            return end - start > 0 ? (start, end) : null;
        }

        /// <summary>
        /// Distance to the segment itself, clamped at its ends — unlike
        /// <see cref="DistancePointToLine"/>, which measures to the infinite line and so reports a
        /// point far off the end as being close by.
        /// </summary>
        public static double DistancePointToSegment(Point p, Segment s) {
            double len = s.Length;
            if (len < 1e-12) return Distance(p, s.P1);

            double t = Math.Clamp(s.Project(p), 0, len);
            return Distance(p, s.PointAt(t));
        }

        private static double DistancePointToLine(Point p, Point linePt1, Point linePt2) {
            double dx = linePt2.x - linePt1.x;
            double dy = linePt2.y - linePt1.y;
            double lineLen = Math.Sqrt(dx * dx + dy * dy);

            if (lineLen == 0) return Distance(p, linePt1); // degenerate line

            // |cross product| / |line vector| = perpendicular distance
            double cross = Math.Abs(dx * (linePt1.y - p.y) - (linePt1.x - p.x) * dy);
            return cross / lineLen;
        }

    }

    public class Arc : GeometryElement {
        private const double FullCircle = 2 * Math.PI;

        public Point Center { get; }
        public double Radius { get; }

        // Radians, CCW from +X, as ACadSharp supplies them. A door swing is told from a chair or a
        // detail motif by its sweep and radius, so an arc that keeps only centre and radius is
        // useless for classification - hence these are part of the primitive, not an extra.
        public double StartAngle { get; }
        public double EndAngle { get; }

        public Arc(Point center, double radius, double startAngle = 0, double endAngle = FullCircle) {
            Center = center;
            Radius = radius;
            StartAngle = startAngle;
            EndAngle = endAngle;
            Points = new List<Point> { StartPoint, EndPoint };
        }

        /// <summary>How far the arc turns, CCW, in (0, 2π]. A full circle reads as 2π, never 0.</summary>
        public double Sweep {
            get {
                double sweep = (EndAngle - StartAngle) % FullCircle;
                if (sweep <= 0) sweep += FullCircle;
                return sweep;
            }
        }

        public double SweepDegrees => Sweep * (180.0 / Math.PI);

        public Point StartPoint => PointAt(StartAngle);
        public Point EndPoint => PointAt(EndAngle);

        /// <summary>The point halfway round the sweep — which side of a wall the swing occupies.</summary>
        public Point MidPoint => PointAt(StartAngle + (Sweep / 2));

        public Point PointAt(double radians) =>
            new(Center.x + (Radius * Math.Cos(radians)), Center.y + (Radius * Math.Sin(radians)));
    }

    // Building Elements
    public class Wall : BuildingElement {
        // Default thickness bounds in millimetres: a 50 mm partition up to a 400 mm structural wall.
        public const double DefaultSMinMillimeters = 50.0;
        public const double DefaultSMaxMillimeters = 400.0;

        // Thickness bounds in the *drawing's* units, not millimetres — ClassificationService
        // converts the millimetre settings through the file's INSUNITS before every run, since
        // Segment coordinates are the raw CAD ones.
        public static double SMin = DefaultSMinMillimeters;
        public static double SMax = DefaultSMaxMillimeters;

        public double Thickness { get; }
        public bool IsOutdoor { get; set; }

        public Segment Face1 { get; }
        public Segment Face2 { get; }

        public Wall(Segment e1, Segment e2) {
            double d = Segment.Distance(e1, e2);

            if (!e1.isParallelTo(e2)) throw new ArgumentException("Wall segments must be parallel.");
            if (d < SMin || d > SMax) throw new ArgumentException("Wall thickness out of bounds.");

            Geometry = new List<GeometryElement> {e1, e2};
            Face1 = e1;
            Face2 = e2;
            Thickness = d;

            var (dx, dy) = e1.Direction();
            bool flip = dx < -1e-12 || (Math.Abs(dx) <= 1e-12 && dy < 0);
            Axis = flip ? (-dx, -dy) : (dx, dy);
            Normal = (-Axis.dy, Axis.dx);
            Origin = new((e1.Mid.x + e2.Mid.x) / 2, (e1.Mid.y + e2.Mid.y) / 2);
        }

        // --- Local frame -------------------------------------------------------------------
        // Everything about an opening is easier to say in the wall's own coordinates: how far
        // along it something sits, and how far off its centreline. These give that frame.
        //
        // Computed once, because a wall's faces never move and grouping walls into runs queries
        // this frame tens of millions of times on a real plan - recomputing a square root and
        // allocating a point on every access dominated the whole pass.

        /// <summary>
        /// Unit vector along the wall, sign-normalised so that the same physical wall always
        /// yields the same axis however its faces happen to be drawn. Without this, two fragments
        /// of one wall can end up with opposite axes and their extents cannot be compared.
        /// </summary>
        public (double dx, double dy) Axis { get; }

        public (double dx, double dy) Normal { get; }

        /// <summary>A point on the wall's centreline, midway between the two faces.</summary>
        public Point Origin { get; }

        /// <summary>How far along the wall a point sits, measured from <see cref="Origin"/>.</summary>
        public double AxisParam(Point p) {
            var (dx, dy) = Axis;
            Point o = Origin;
            return ((p.x - o.x) * dx) + ((p.y - o.y) * dy);
        }

        /// <summary>How far off the centreline a point sits; the sign says which face it is nearer.</summary>
        public double NormalParam(Point p) {
            var (dx, dy) = Normal;
            Point o = Origin;
            return ((p.x - o.x) * dx) + ((p.y - o.y) * dy);
        }

        public Point FromAxis(double along, double off = 0) {
            var (ax, ay) = Axis;
            var (nx, ny) = Normal;
            Point o = Origin;
            return new(o.x + (ax * along) + (nx * off), o.y + (ay * along) + (ny * off));
        }

        /// <summary>The stretch of wall both faces agree on, in axis parameters.</summary>
        public (double Start, double End) Extent {
            get {
                double a1 = AxisParam(Face1.P1), a2 = AxisParam(Face1.P2);
                double b1 = AxisParam(Face2.P1), b2 = AxisParam(Face2.P2);
                return (Math.Max(Math.Min(a1, a2), Math.Min(b1, b2)),
                        Math.Min(Math.Max(a1, a2), Math.Max(b1, b2)));
            }
        }

        public Segment Centerline {
            get {
                var (start, end) = Extent;
                return new Segment(FromAxis(start), FromAxis(end));
            }
        }
    }

    /// <summary>What an opening turned out to be. The swing arc is the discriminator.</summary>
    public enum OpeningKind { Unknown, Door, Window }

    /// <summary>
    /// Why an opening was accepted. Kept on the result so a detection can be explained rather than
    /// just asserted, which is what makes the tolerances tunable against a real drawing.
    /// </summary>
    [Flags]
    public enum OpeningEvidence {
        None = 0,
        GapBothFaces = 1 << 0,   // both wall faces are interrupted here
        GapOneFace = 1 << 1,   // only one is - common, and still a real opening
        JambPair = 1 << 2,   // two cross-wall jamb lines bound the span
        FacePair = 1 << 3,   // e1/e2 found at ~wall thickness across the span
        SynthesisedFaces = 1 << 4,   // nothing drawn inside; e1/e2 taken from the wall itself
        SwingArc = 1 << 5,   // a door swing sits in the span
        LeafSegment = 1 << 6,   // the swing has a matching door leaf
        GlazingLines = 1 << 7    // lines running across the span inside the wall band
    }

    /// <summary>
    /// An opening is two segments inside a wall, separated by about the wall's own thickness,
    /// optionally with an arc (e3) that makes it a door rather than a window.
    /// </summary>
    public class Opening : BuildingElement {
        public Wall Wall { get; }
        public OpeningKind Kind { get; }
        public bool IsDoor => Kind == OpeningKind.Door;

        /// <summary>The swing arc, e3. Null for a window.</summary>
        public Arc? SwingArc { get; }

        /// <summary>The door leaf, when one was found alongside the swing.</summary>
        public Segment? Leaf { get; }

        /// <summary>The span the opening occupies, in its wall's axis parameters.</summary>
        public (double Start, double End) AxisSpan { get; }
        public double Width => AxisSpan.End - AxisSpan.Start;

        public Point Center => Wall.FromAxis((AxisSpan.Start + AxisSpan.End) / 2);

        /// <summary>The four corners of the rectangle the opening cuts out of the wall.</summary>
        public IReadOnlyList<Point> Rectangle { get; }

        public OpeningEvidence Evidence { get; }

        /// <summary>The ε actually achieved: |d(e1,e2) − wall thickness|. Lower is a better fit.</summary>
        public double ThicknessResidual { get; }

        public Opening(Wall wall, Segment e1, Segment e2, (double Start, double End) axisSpan,
                       OpeningKind kind, OpeningEvidence evidence, double thicknessResidual,
                       Arc? swingArc = null, Segment? leaf = null) {
            Wall = wall;
            Kind = kind;
            Evidence = evidence;
            ThicknessResidual = thicknessResidual;
            AxisSpan = axisSpan;
            SwingArc = swingArc;
            Leaf = leaf;

            // e1 and e2 are the opening, not incidental inputs - the old version accepted them and
            // dropped them on the floor, leaving a result nothing could inspect or draw.
            Geometry = new List<GeometryElement> { e1, e2 };
            if (leaf is not null) Geometry.Add(leaf);
            if (swingArc is not null) Geometry.Add(swingArc);

            double half = wall.Thickness / 2;
            Rectangle = new List<Point> {
                wall.FromAxis(axisSpan.Start, -half), wall.FromAxis(axisSpan.End, -half),
                wall.FromAxis(axisSpan.End, half), wall.FromAxis(axisSpan.Start, half)
            };
        }
    }

    public class Space : BuildingElement {
        public const double AMin = 1.0; // predefined minimum area
        public double Area { get; set; }

        public Space(TextElement text, List<Wall> walls, List<Opening> openinngs) {
            Text = new List<TextElement> { text };
            SubElements = walls.Cast<BuildingElement>().Concat(openinngs).ToList();
        }
    }

    public class CadLoader {
        public static (List<GeometryElement> AllGeometry, List<TextElement> AllText) LoadCadEntities(string filePath) =>
            LoadCadEntities(CadRenderSource.Read(filePath));

        // Overload for callers that already hold the document (the viewport reads it too), so a
        // file is parsed once per load.
        public static (List<GeometryElement> AllGeometry, List<TextElement> AllText) LoadCadEntities(CadDocument cadDocument) {
            CadAnalysisSink sink = new();
            CadEntityWalker.Walk(cadDocument, sink);
            return (sink.Geometry, sink.Texts);
        }
    }

    public class CadClassifier {

        // Bw = {e1, e2 | both segments, e1 != e2, parallel, SMin <= d(e1, e2) <= SMax}.
        // Two additions on top of the definition, both about which partner a face is paired with
        // rather than which pairs are admissible: candidates must overlap when projected onto the
        // shared direction, and the closest admissible candidate wins instead of the first one
        // scanned. Pairing is still exclusive - a face belongs to at most one wall.
        //
        // Candidates come from a spatial index rather than a full scan. That is not a rewrite of
        // the rule: the index only narrows the search to segments that could possibly satisfy it,
        // and every pair it returns still faces the same four tests in the same order, with the
        // same lowest-index-wins tie-break. It became necessary once blocks were exploded - the
        // feed went from about four thousand segments to sixty-seven thousand, and the original
        // full scan is quadratic.
        public static List<Wall> ClassifyWalls(List<Segment> Segments) {

            List<Wall> walls = new List<Wall>();
            HashSet<Segment> used = new HashSet<Segment>();
            SegmentIndex index = new(Segments, Wall.SMax);

            for (int i=0; i < Segments.Count; i++) {
                Segment s1 = Segments[i];
                if(used.Contains(s1)) continue;

                Segment? nearest = null;
                double nearestDistance = double.MaxValue;

                foreach (int j in index.CandidatesFor(i)) {
                    if(j <= i) continue;
                    Segment s2 = Segments[j];

                    if(used.Contains(s2)) continue;
                    if(!s1.isParallelTo(s2)) continue;

                    double d = Segment.Distance(s1, s2);
                    if(d<Wall.SMin || d>Wall.SMax) continue;
                    if(!Segment.Overlaps(s1, s2)) continue;

                    if(d >= nearestDistance) continue;

                    nearest = s2;
                    nearestDistance = d;
                }

                if(nearest is null) continue;

                walls.Add(new Wall(s1, nearest));

                used.Add(s1);
                used.Add(nearest);
            }

            return walls;
        }

        public static void ClassifySpaces() { return; }

        public static void SplitWalls() { return; } // split walls into indoor and outdoor
        public static void CreateTopologicalPoint() { return; }
    }
}