namespace Cad2Bim.Classification {
    /// <summary>
    /// Paper eq. (6): an opening is a pair of segments lying in a wall, about the wall's own
    /// thickness apart, optionally accompanied by an arc (e3) that makes it a door.
    /// <para>
    /// The search runs the other way round from the equation, because a vector drawing gives the
    /// wall first: find where a wall is interrupted, then look inside the interruption for the
    /// evidence eq. (6) describes. That is the paper's own second cue - "discontinuities in the
    /// wall representation" - and it is what makes the rule usable on real geometry.
    /// </para>
    /// </summary>
    public static class OpeningClassifier {
        /// <summary>How much of a candidate span a line must cover to count as spanning it.</summary>
        private const double SpanCoverageRatio = 0.5;

        /// <summary>A crossing wall is only a junction if it cuts well inside the gap, not at its lip.</summary>
        private const double JunctionInsetRatio = 0.15;

        /// <summary>A door leaf is as long as its opening, give or take drawing slack.</summary>
        private const double LeafLengthTolerance = 0.25;

        public static List<Opening> Classify(IReadOnlyList<Wall> walls,
                                             IReadOnlyList<GeometryElement> geometry,
                                             ClassificationTolerances tolerances,
                                             ClassificationReport? report = null) {
            List<WallRun> runs = WallRun.Build(walls, tolerances);
            List<Segment> segments = geometry.OfType<Segment>().ToList();

            // Only arcs that could be a door leaf's sweep are worth carrying. This is where a
            // swivel chair is separated from a door: both are drawn as a quarter circle, but a
            // chair's is around 400 mm and a door's is at least 500.
            List<Arc> swings = geometry.OfType<Arc>()
                .Where(a => a.Radius >= tolerances.MinSwingRadius
                         && a.Radius <= tolerances.MaxSwingRadius
                         && a.SweepDegrees >= tolerances.MinSwingSweepDegrees
                         && a.SweepDegrees <= tolerances.MaxSwingSweepDegrees)
                .ToList();

            report?.Note(runs.Count, segments.Count, swings.Count);

            SpatialGrid grid = new(segments, tolerances.MaxOpeningWidth);

            // Only the walls matter for the junction test, and there are far fewer of them than
            // there are segments, so they get their own index.
            List<Segment> wallFaces = runs.SelectMany(r => r.Faces).ToList();
            SpatialGrid wallGrid = new(wallFaces, tolerances.MaxOpeningWidth);

            List<Opening> openings = new();
            HashSet<Arc> claimedSwings = new();

            foreach (WallRun run in runs) {
                foreach (var gap in run.Gaps(tolerances.EndpointTolerance)) {
                    Opening? opening = Evaluate(run, gap, grid, wallGrid, segments,
                                                swings, claimedSwings, tolerances, report);
                    if (opening is not null) openings.Add(opening);
                }
            }

            return openings;
        }

        /// <summary>Geometry close enough to a span to be part of the opening in it.</summary>
        private static List<Segment> NearGap(SpatialGrid grid, WallRun run,
                                             (double Start, double End) gap,
                                             ClassificationTolerances tolerances) {
            double margin = run.Thickness + tolerances.HingeTolerance;
            return grid.Near(run.FromAxis(gap.Start), run.FromAxis(gap.End), margin).ToList();
        }

        private static Opening? Evaluate(WallRun run, (double Start, double End) gap,
                                         SpatialGrid grid, SpatialGrid wallGrid,
                                         IReadOnlyList<Segment> segments,
                                         IReadOnlyList<Arc> swings,
                                         HashSet<Arc> claimedSwings,
                                         ClassificationTolerances tolerances,
                                         ClassificationReport? report) {
            double width = gap.End - gap.Start;

            // Hard gates. Below the minimum a gap is a column, a short jog or a drafting slip long
            // before it is a doorway; above the maximum the wall is simply missing.
            if (width < tolerances.MinOpeningWidth) {
                report?.Reject(RejectReason.TooNarrow);
                return null;
            }

            if (width > tolerances.MaxOpeningWidth) {
                report?.Reject(RejectReason.TooWide);
                return null;
            }

            List<Segment> nearby = NearGap(grid, run, gap, tolerances);

            OpeningEvidence evidence = FaceEvidence(run, gap, tolerances);
            (Arc? swing, Segment? leaf) = FindSwing(run, gap, swings, claimedSwings, nearby, tolerances);

            // A wall corner interrupts a face exactly the way a doorway does, so without this test
            // every junction in the plan is reported as an opening - it is the single largest
            // source of false positives. A door hard up against a corner survives it, because a
            // matched swing arc is stronger evidence than the crossing is against.
            if (swing is null && CrossedByAnotherWall(run, gap, wallGrid, tolerances)) {
                report?.Reject(RejectReason.WallJunction);
                return null;
            }

            (Segment? e1, Segment? e2, double residual) = FindEqSixPair(run, gap, nearby, tolerances);

            if (e1 is not null && e2 is not null) {
                evidence |= OpeningEvidence.EqSixPair;
                if (Math.Abs(run.NormalParam(e1.Mid)) < (run.Thickness / 2) - tolerances.EndpointTolerance
                 || Math.Abs(run.NormalParam(e2.Mid)) < (run.Thickness / 2) - tolerances.EndpointTolerance) {
                    evidence |= OpeningEvidence.GlazingLines;
                }
            } else {
                // Nothing drawn inside the hole - the ordinary case for a door, whose gap is left
                // empty. The paper's e1/e2 exist there too, as the edge of the wall's own fill;
                // vector geometry only implies them, so they are reconstructed from the wall.
                double half = run.Thickness / 2;
                e1 = new Segment(run.FromAxis(gap.Start, -half), run.FromAxis(gap.End, -half));
                e2 = new Segment(run.FromAxis(gap.Start, half), run.FromAxis(gap.End, half));
                residual = 0;
                evidence |= OpeningEvidence.SynthesisedFaces;
            }

            if (swing is not null) {
                evidence |= OpeningEvidence.SwingArc;
                if (leaf is not null) evidence |= OpeningEvidence.LeafSegment;
            }

            if (JambPair(run, gap, nearby, tolerances)) evidence |= OpeningEvidence.JambPair;

            // Something must positively say "opening" beyond a wall having stopped. A lone
            // one-sided gap with nothing in it is an unclosed wall end, not a doorway.
            bool supported = evidence.HasFlag(OpeningEvidence.GapBothFaces)
                          || evidence.HasFlag(OpeningEvidence.JambPair)
                          || evidence.HasFlag(OpeningEvidence.SwingArc)
                          || evidence.HasFlag(OpeningEvidence.GlazingLines);

            if (!supported) {
                report?.Reject(RejectReason.NoEvidence);
                return null;
            }

            if (swing is not null) claimedSwings.Add(swing);

            // The paper's discriminator is the swing arc: "To distinguish a door from a window, an
            // arc can be drawn to represent a door, explaining why parameter e3 is optional." That
            // half is kept exactly.
            //
            // Its other half - anything without an arc is a window - does not survive contact with
            // vector data. On a scanned plan the wall is a filled black band, so any hole in it was
            // drawn deliberately; in a DWG a hole is just where two lines stopped, and most such
            // holes are cased openings, doorways whose swing was never drawn, or the classifier
            // losing the thread. Calling all of them windows was measured here to invent about a
            // hundred of them in a drawing that has none.
            //
            // So a window has to be positively drawn - eq. (6)'s e1/e2 actually found across the
            // opening - and a bare hole is reported as what it is rather than guessed at.
            OpeningKind kind =
                swing is not null ? OpeningKind.Door
                : evidence.HasFlag(OpeningEvidence.EqSixPair) ? OpeningKind.Window
                : OpeningKind.Unknown;

            report?.Accept(kind);
            report?.AcceptWidth(width);
            return new Opening(run.Reference, e1, e2, gap, kind, evidence, residual, swing, leaf);
        }

        /// <summary>
        /// Whether each side of the wall is interrupted here or merely ends. Measured per side,
        /// because at a real doorway one face is commonly bracketed while the other stops short -
        /// requiring both to agree is what makes a strict reading of eq. (6) miss most doors.
        /// </summary>
        private static OpeningEvidence FaceEvidence(WallRun run, (double Start, double End) gap,
                                                    ClassificationTolerances tolerances) {
            int bracketed = 0;

            foreach (var side in new[] { 1, -1 }) {
                bool before = false, after = false;

                foreach (Segment face in run.Faces) {
                    if (Math.Sign(run.NormalParam(face.Mid)) != side) continue;

                    double a = run.AxisParam(face.P1), b = run.AxisParam(face.P2);
                    double lo = Math.Min(a, b), hi = Math.Max(a, b);

                    // The face has to stop where the hole starts and pick up again where it ends.
                    // Merely lying somewhere further along the wall says nothing: on a run of
                    // several fragments that is true of almost every gap, which would make this
                    // test fire everywhere and mean nothing.
                    if (Math.Abs(hi - gap.Start) <= tolerances.EndpointTolerance) before = true;
                    if (Math.Abs(lo - gap.End) <= tolerances.EndpointTolerance) after = true;
                }

                if (before && after) bracketed++;
            }

            return bracketed >= 2 ? OpeningEvidence.GapBothFaces
                 : bracketed == 1 ? OpeningEvidence.GapOneFace
                 : OpeningEvidence.None;
        }

        /// <summary>
        /// eq. (6) proper: two segments inside the wall band, running along it, separated by about
        /// the wall's thickness. The pair furthest apart wins, since a window's outer lines sit on
        /// the faces and any mullions fall between them.
        /// </summary>
        private static (Segment? E1, Segment? E2, double Residual) FindEqSixPair(
            WallRun run, (double Start, double End) gap,
            IReadOnlyList<Segment> segments, ClassificationTolerances tolerances) {

            double half = run.Thickness / 2;
            double needed = (gap.End - gap.Start) * SpanCoverageRatio;
            List<Segment> inside = new();

            foreach (Segment segment in segments) {
                if (Segment.HeadingDifference(segment.HeadingDegrees, run.Heading)
                    > tolerances.AngleToleranceDegrees) continue;

                double offset = run.NormalParam(segment.Mid);
                if (Math.Abs(offset) > half + tolerances.AxisOffsetTolerance) continue;

                double a = run.AxisParam(segment.P1), b = run.AxisParam(segment.P2);
                double covered = Math.Min(Math.Max(a, b), gap.End) - Math.Max(Math.Min(a, b), gap.Start);
                if (covered < needed) continue;

                inside.Add(segment);
            }

            Segment? bestA = null, bestB = null;
            double bestResidual = double.MaxValue, bestSeparation = -1;

            for (int i = 0; i < inside.Count; i++) {
                for (int j = i + 1; j < inside.Count; j++) {
                    double separation = Math.Abs(run.NormalParam(inside[i].Mid) - run.NormalParam(inside[j].Mid));
                    double residual = Math.Abs(separation - run.Thickness);

                    if (residual > tolerances.ThicknessEpsilon) continue;
                    if (separation <= bestSeparation) continue;

                    bestSeparation = separation;
                    bestResidual = residual;
                    bestA = inside[i];
                    bestB = inside[j];
                }
            }

            return (bestA, bestB, bestA is null ? 0 : bestResidual);
        }

        /// <summary>Two cross-wall lines closing the ends of the span — a jamb, reveal or frame.</summary>
        private static bool JambPair(WallRun run, (double Start, double End) gap,
                                     IReadOnlyList<Segment> segments, ClassificationTolerances tolerances) {
            bool atStart = false, atEnd = false;

            foreach (Segment segment in segments) {
                if (Math.Abs(Segment.HeadingDifference(segment.HeadingDegrees, run.Heading) - 90)
                    > tolerances.AngleToleranceDegrees) continue;

                // It has to reach across the wall, not just touch it.
                if (Math.Abs(segment.Length - run.Thickness) > tolerances.ThicknessEpsilon) continue;
                if (Math.Abs(run.NormalParam(segment.Mid)) > tolerances.AxisOffsetTolerance) continue;

                double along = run.AxisParam(segment.Mid);
                if (Math.Abs(along - gap.Start) <= tolerances.HingeTolerance) atStart = true;
                if (Math.Abs(along - gap.End) <= tolerances.HingeTolerance) atEnd = true;
            }

            return atStart && atEnd;
        }

        /// <summary>
        /// e3: the door swing. The hinge is the arc's centre and sits at one jamb, and the leaf is
        /// as long as the opening is wide, so the radius doubles as a width check - a far stronger
        /// test than the radius band alone, which only rules out furniture.
        /// </summary>
        private static (Arc? Swing, Segment? Leaf) FindSwing(
            WallRun run, (double Start, double End) gap,
            IReadOnlyList<Arc> swings, HashSet<Arc> claimed,
            IReadOnlyList<Segment> segments, ClassificationTolerances tolerances) {

            double width = gap.End - gap.Start;
            Arc? best = null;
            double bestError = double.MaxValue;

            foreach (Arc arc in swings) {
                if (claimed.Contains(arc)) continue;

                // The leaf spans the opening, so its sweep radius should match the opening's width.
                double radiusError = Math.Abs(arc.Radius - width);
                if (radiusError > tolerances.HingeTolerance) continue;

                // The hinge sits at one end of the opening, within the wall's own thickness of it.
                double along = run.AxisParam(arc.Center);
                double across = Math.Abs(run.NormalParam(arc.Center));
                if (across > run.Thickness + tolerances.HingeTolerance) continue;

                double hingeError = Math.Min(Math.Abs(along - gap.Start), Math.Abs(along - gap.End));
                if (hingeError > tolerances.HingeTolerance) continue;

                double error = radiusError + hingeError;
                if (error >= bestError) continue;

                bestError = error;
                best = arc;
            }

            return best is null ? (null, null) : (best, FindLeaf(best, segments, tolerances));
        }

        /// <summary>
        /// The door panel: a line from the hinge out to where the arc ends. Optional, since plenty
        /// of drawings show only the sweep, and since the leaf is often drawn as a thin rectangle
        /// rather than a single line - in which case one of its long sides matches.
        /// </summary>
        private static Segment? FindLeaf(Arc swing, IReadOnlyList<Segment> segments,
                                         ClassificationTolerances tolerances) {
            double tolerance = tolerances.HingeTolerance;

            foreach (Segment segment in segments) {
                if (Math.Abs(segment.Length - swing.Radius) > swing.Radius * LeafLengthTolerance) continue;

                foreach (var (hinge, tip) in new[] { (segment.P1, segment.P2), (segment.P2, segment.P1) }) {
                    if (Segment.Distance(hinge, swing.Center) > tolerance) continue;

                    if (Segment.Distance(tip, swing.StartPoint) <= tolerance
                     || Segment.Distance(tip, swing.EndPoint) <= tolerance) {
                        return segment;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Whether another wall runs through this gap, which makes it a junction rather than an
        /// opening. The span is inset a little first, so a doorway that begins right at a corner
        /// is not thrown away along with the corner itself.
        /// </summary>
        private static bool CrossedByAnotherWall(WallRun run, (double Start, double End) gap,
                                                 SpatialGrid wallGrid,
                                                 ClassificationTolerances tolerances) {
            double inset = (gap.End - gap.Start) * JunctionInsetRatio;
            Segment threshold = run.Threshold(gap.Start + inset, gap.End - inset);

            // Tested against the crossing wall's drawn faces rather than its centreline: a
            // fragment's centreline spans only the stretch both its faces share, which for an
            // offset pair can be a sliver that stops short of the gap entirely.
            foreach (Segment face in wallGrid.Near(threshold.P1, threshold.P2, run.Thickness)) {
                // A parallel neighbour is this wall itself, or a second wall alongside it -
                // neither is a junction.
                if (Segment.HeadingDifference(face.HeadingDegrees, run.Heading)
                    <= tolerances.AngleToleranceDegrees * 2) continue;

                if (Segment.Intersects(threshold, face)) return true;
            }

            return false;
        }
    }
}
