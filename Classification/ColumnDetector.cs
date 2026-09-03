namespace Cad2Bim.Classification {
    /// <summary>A detected structural column: its four corners, in drawing units.</summary>
    public sealed record ColumnFootprint(IReadOnlyList<Point> Corners, Point Center);

    /// <summary>
    /// Finds small closed rectangular loops in the wall-tagged linework and claims them as
    /// structural columns before wall pairing runs. Without this, a column's faces are stolen as
    /// pairing partners by the walls around them (pairing is exclusive, closest wins), so columns
    /// only ever "appeared" where two wall runs happened to cross.
    /// <para>
    /// Runs only on segments the user tagged as wall — on the whole drawing the same small
    /// rectangle is as likely a chair as a column.
    /// </para>
    /// </summary>
    public static class ColumnDetector {
        public static (List<ColumnFootprint> Columns, List<Segment> Remaining) Detect(
            IReadOnlyList<Segment> wallSegments, ClassificationTolerances tolerances) {
            double maxSide = tolerances.MaxColumnSide;
            double snap = tolerances.EndpointTolerance * 4; // corners are drawn sloppier than faces

            // Only short segments can be column sides; the rest never enter the walk. Exploded
            // blocks leave stacks of identical strokes, and every duplicate multiplies the corner
            // walk below, so coincident segments collapse to one first.
            List<Segment> shorts = DistinctByEndpoints(
                wallSegments.Where(s => s.Length <= maxSide + snap), snap);

            // Endpoint adjacency over snapped coordinates.
            Dictionary<(int X, int Y), List<Segment>> corners = new();
            (int, int) Key(Point p) => ((int)Math.Round(p.x / snap), (int)Math.Round(p.y / snap));

            void File(Point p, Segment s) {
                if (!corners.TryGetValue(Key(p), out List<Segment>? bucket)) corners[Key(p)] = bucket = new();
                bucket.Add(s);
            }
            foreach (Segment s in shorts) { File(s.P1, s); File(s.P2, s); }

            IEnumerable<Segment> At(Point p) {
                var (cx, cy) = Key(p);
                for (int dx = -1; dx <= 1; dx++) {
                    for (int dy = -1; dy <= 1; dy++) {
                        if (corners.TryGetValue((cx + dx, cy + dy), out List<Segment>? bucket)) {
                            foreach (Segment entry in bucket) yield return entry;
                        }
                    }
                }
            }

            List<ColumnFootprint> columns = new();
            HashSet<Segment> claimed = new();

            foreach (Segment first in shorts) {
                if (claimed.Contains(first)) continue;

                // Walk: first → a side sharing its P2 → a side sharing that end → one sharing
                // the next — closing back on first.P1 makes a quad.
                double firstHeading = first.HeadingDegrees;

                foreach (Segment second in At(first.P2)) {
                    if (second == first || claimed.Contains(second)) continue;
                    // Each turn of the walk must be a right angle — prunes the corner fan-out
                    // long before the geometry checks run.
                    if (Segment.HeadingDifference(firstHeading, second.HeadingDegrees) < 80) continue;
                    if (Segment.Distance(NearEnd(second, first.P2), first.P2) > snap) continue;
                    Point b2 = FarEnd(second, first.P2);

                    foreach (Segment third in At(b2)) {
                        if (third == first || third == second || claimed.Contains(third)) continue;
                        if (Segment.HeadingDifference(firstHeading, third.HeadingDegrees) > 10) continue;
                        Point b3 = FarEnd(third, b2);
                        if (Segment.Distance(NearEnd(third, b2), b2) > snap) continue;

                        foreach (Segment fourth in At(b3)) {
                            if (fourth == first || fourth == second || fourth == third
                                || claimed.Contains(fourth)) continue;
                            if (Segment.HeadingDifference(firstHeading, fourth.HeadingDegrees) < 80) continue;
                            if (Segment.Distance(NearEnd(fourth, b3), b3) > snap) continue;
                            if (Segment.Distance(FarEnd(fourth, b3), first.P1) > snap) continue;

                            // Both sides in the column range: smaller rectangles are wall-end
                            // caps and jamb blocks, and belong to the wall pass.
                            double sideA = Segment.Distance(first.P1, first.P2);
                            double sideB = Segment.Distance(first.P2, b2);
                            if (Math.Min(sideA, sideB) < tolerances.MinColumnSide) continue;

                            if (!IsRectangle(first.P1, first.P2, b2, b3, tolerances.AngleToleranceDegrees)) continue;

                            columns.Add(new ColumnFootprint(
                                new List<Point> { first.P1, first.P2, b2, b3 },
                                new Point((first.P1.x + b2.x) / 2, (first.P1.y + b2.y) / 2)));
                            claimed.Add(first); claimed.Add(second); claimed.Add(third); claimed.Add(fourth);
                            goto nextSeed;
                        }
                    }
                }
                nextSeed: ;
            }

            List<Segment> remaining = wallSegments.Where(s => !claimed.Contains(s)).ToList();
            return (columns, remaining);
        }

        private static List<Segment> DistinctByEndpoints(IEnumerable<Segment> segments, double snap) {
            List<Segment> distinct = new();
            HashSet<(long, long, long, long)> seen = new();

            foreach (Segment s in segments) {
                (long, long) Q(Point p) => ((long)Math.Round(p.x / snap), (long)Math.Round(p.y / snap));
                var (a, b) = (Q(s.P1), Q(s.P2));
                var key = a.CompareTo(b) <= 0 ? (a.Item1, a.Item2, b.Item1, b.Item2)
                                              : (b.Item1, b.Item2, a.Item1, a.Item2);
                if (seen.Add(key)) distinct.Add(s);
            }

            return distinct;
        }

        private static Point NearEnd(Segment s, Point p) =>
            Segment.Distance(s.P1, p) <= Segment.Distance(s.P2, p) ? s.P1 : s.P2;

        private static Point FarEnd(Segment s, Point p) =>
            Segment.Distance(s.P1, p) <= Segment.Distance(s.P2, p) ? s.P2 : s.P1;

        /// <summary>All four turns close to 90° — a rectangle, not just any closed quad.</summary>
        private static bool IsRectangle(Point a, Point b, Point c, Point d, double angleToleranceDegrees) {
            // Give drawn corners a few times the face tolerance; they wobble more than faces do.
            double tolerance = Math.Max(angleToleranceDegrees * 4, 8);

            static double TurnDegrees(Point p, Point q, Point r) {
                double ux = q.x - p.x, uy = q.y - p.y, vx = r.x - q.x, vy = r.y - q.y;
                double lu = Math.Sqrt((ux * ux) + (uy * uy)), lv = Math.Sqrt((vx * vx) + (vy * vy));
                if (lu < 1e-12 || lv < 1e-12) return 0;
                double cosine = Math.Clamp(((ux * vx) + (uy * vy)) / (lu * lv), -1, 1);
                return Math.Acos(cosine) * 180.0 / Math.PI;
            }

            return Math.Abs(TurnDegrees(a, b, c) - 90) <= tolerance
                && Math.Abs(TurnDegrees(b, c, d) - 90) <= tolerance
                && Math.Abs(TurnDegrees(c, d, a) - 90) <= tolerance
                && Math.Abs(TurnDegrees(d, a, b) - 90) <= tolerance;
        }
    }
}
