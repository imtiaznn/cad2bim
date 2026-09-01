namespace Cad2Bim.Classification {
    /// <summary>
    /// One physical wall, reassembled from the fragments the wall pass produced.
    /// <para>
    /// <see cref="CadClassifier.ClassifyWalls"/> pairs whole segments exclusively, so a wall that
    /// an opening interrupts arrives as several collinear <see cref="Wall"/>s. The paper's
    /// "discontinuities in the wall representation" therefore appear <em>between</em> fragments,
    /// not inside any one of them, and a run is what makes them visible.
    /// </para>
    /// <para>
    /// Every position is expressed in the run's own frame — how far along the wall, and how far off
    /// its centreline — taken from the first fragment.
    /// </para>
    /// </summary>
    public sealed class WallRun {
        private readonly List<Wall> _fragments = new();

        /// <summary>The fragment whose local frame the whole run is measured in.</summary>
        public Wall Reference { get; }

        public IReadOnlyList<Wall> Fragments => _fragments;
        public double Thickness { get; private set; }

        public WallRun(Wall first) {
            Reference = first;
            _fragments.Add(first);
            Thickness = first.Thickness;
        }

        public double Heading => Reference.Face1.HeadingDegrees;

        public double AxisParam(Point p) => Reference.AxisParam(p);
        public double NormalParam(Point p) => Reference.NormalParam(p);
        public Point FromAxis(double along, double off = 0) => Reference.FromAxis(along, off);

        internal void Add(Wall fragment) {
            _fragments.Add(fragment);
            Thickness = _fragments.Average(f => f.Thickness);
        }

        /// <summary>How much wall this run actually draws, ignoring the holes in it.</summary>
        public double CoveredLength(double joinTolerance) =>
            Covered(joinTolerance).Sum(i => i.End - i.Start);

        public IEnumerable<Segment> Faces =>
            _fragments.SelectMany(f => new[] { f.Face1, f.Face2 });

        /// <summary>Every axis position the run touches, whichever face touches it.</summary>
        public (double Start, double End) Span {
            get {
                double start = double.MaxValue, end = double.MinValue;
                foreach (Segment face in Faces) {
                    double a = AxisParam(face.P1), b = AxisParam(face.P2);
                    start = Math.Min(start, Math.Min(a, b));
                    end = Math.Max(end, Math.Max(a, b));
                }
                return start > end ? (0, 0) : (start, end);
            }
        }

        /// <summary>
        /// The stretches of the run that any face covers, merged. Deliberately the <em>union</em>
        /// of the two faces rather than their intersection: at a real doorway one face is
        /// interrupted while the other simply ends, so demanding that both agree misses most
        /// openings outright.
        /// </summary>
        public List<(double Start, double End)> Covered(double joinTolerance) {
            List<(double Start, double End)> raw = new();

            foreach (Segment face in Faces) {
                double a = AxisParam(face.P1), b = AxisParam(face.P2);
                raw.Add((Math.Min(a, b), Math.Max(a, b)));
            }

            return Intervals.Merge(raw, joinTolerance);
        }

        /// <summary>The holes: what the run spans but does not draw.</summary>
        public List<(double Start, double End)> Gaps(double joinTolerance) =>
            Intervals.Complement(Covered(joinTolerance), Span);

        /// <summary>The line across the middle of a span — the opening's threshold.</summary>
        public Segment Threshold(double start, double end) => new(FromAxis(start), FromAxis(end));

        // --- Grouping ---------------------------------------------------------------------------

        /// <summary>
        /// Gathers walls into runs by collinearity and matching thickness. Greedy agglomeration
        /// against each run's own frame, so no global heading buckets are needed — which also
        /// sidesteps the trap that folding a heading to [0,180) and then rounding puts 179.6 and
        /// 0.4 in different buckets despite describing the same line.
        /// </summary>
        public static List<WallRun> Build(IReadOnlyList<Wall> walls, ClassificationTolerances tolerances) {
            const double bucketDegrees = 5.0;
            const int bucketCount = (int)(180 / bucketDegrees);

            List<WallRun> runs = new();

            // Runs are filed by heading so a wall only meets the ones it could join. Comparing
            // against every run so far is quadratic, and on a large plan that is tens of millions
            // of comparisons for nothing - a wall running north can never join one running east.
            Dictionary<int, List<WallRun>> byHeading = new();

            foreach (Wall wall in walls) {
                double heading = wall.Face1.HeadingDegrees;
                int bucket = Math.Clamp((int)(heading / bucketDegrees), 0, bucketCount - 1);
                WallRun? host = null;

                // Headings wrap at 180, so the first and last buckets are neighbours.
                for (int delta = -1; delta <= 1 && host is null; delta++) {
                    int key = ((bucket + delta) % bucketCount + bucketCount) % bucketCount;
                    if (!byHeading.TryGetValue(key, out List<WallRun>? candidates)) continue;

                    foreach (WallRun run in candidates) {
                        if (Segment.HeadingDifference(run.Heading, heading)
                            > tolerances.AngleToleranceDegrees) continue;

                        if (Math.Abs(run.NormalParam(wall.Origin)) > tolerances.AxisOffsetTolerance) continue;
                        if (Math.Abs(run.Thickness - wall.Thickness) > tolerances.AxisOffsetTolerance) continue;

                        host = run;
                        break;
                    }
                }

                if (host is null) {
                    WallRun run = new(wall);
                    runs.Add(run);

                    if (!byHeading.TryGetValue(bucket, out List<WallRun>? bucketRuns)) {
                        byHeading[bucket] = bucketRuns = new List<WallRun>();
                    }
                    bucketRuns.Add(run);
                } else {
                    host.Add(wall);
                }
            }

            return runs;
        }
    }

    /// <summary>Interval arithmetic on a wall's axis: what is drawn, and what is missing.</summary>
    internal static class Intervals {
        public static List<(double Start, double End)> Merge(
            List<(double Start, double End)> intervals, double joinTolerance) {

            List<(double Start, double End)> merged = new();
            if (intervals.Count == 0) return merged;

            intervals.Sort((x, y) => x.Start.CompareTo(y.Start));
            var current = intervals[0];

            for (int i = 1; i < intervals.Count; i++) {
                var next = intervals[i];

                // Butt-jointed collinear faces leave a hairline crack that is not a doorway.
                if (next.Start <= current.End + joinTolerance) {
                    current = (current.Start, Math.Max(current.End, next.End));
                } else {
                    merged.Add(current);
                    current = next;
                }
            }

            merged.Add(current);
            return merged;
        }

        public static List<(double Start, double End)> Complement(
            List<(double Start, double End)> covered, (double Start, double End) span) {

            List<(double Start, double End)> gaps = new();
            double cursor = span.Start;

            foreach (var interval in covered) {
                if (interval.Start > cursor) gaps.Add((cursor, interval.Start));
                cursor = Math.Max(cursor, interval.End);
            }

            if (cursor < span.End) gaps.Add((cursor, span.End));
            return gaps;
        }
    }
}
