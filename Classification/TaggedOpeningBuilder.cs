namespace Cad2Bim.Classification {
    /// <summary>
    /// Openings forced by the user's own Door/Window tags. Where somebody painted a door, there
    /// <em>is</em> a door — no gap-evidence reading required. Tagged geometry is clustered by
    /// proximity, each cluster is projected onto the nearest wall run, and the projection becomes
    /// the opening's span. Gap-detected openings that overlap a forced one are discarded by the
    /// caller, so a hand tag always wins over a heuristic.
    /// </summary>
    public static class TaggedOpeningBuilder {
        public static List<Opening> Build(IReadOnlyList<WallRun> runs,
                                          IReadOnlyList<GeometryElement> taggedGeometry,
                                          OpeningKind kind,
                                          ClassificationTolerances tolerances) {
            List<Opening> openings = new();
            if (runs.Count == 0) return openings;

            foreach (List<GeometryElement> cluster in Cluster(taggedGeometry, tolerances.HingeTolerance)) {
                Point centre = BoundsCentre(cluster);

                WallRun? host = FindHost(runs, centre, tolerances);
                if (host is null) continue;

                // The opening spans what the tagged geometry covers along the wall, clamped to
                // the run itself.
                double start = double.MaxValue, end = double.MinValue;
                foreach (Point p in cluster.SelectMany(e => e.Points)) {
                    double along = host.AxisParam(p);
                    start = Math.Min(start, along);
                    end = Math.Max(end, along);
                }
                var span = host.Span;
                start = Math.Max(start, span.Start);
                end = Math.Min(end, span.End);
                if (end - start < tolerances.MinOpeningWidth / 2) continue; // tag too small to be real

                Arc? swing = kind == OpeningKind.Door
                    ? cluster.OfType<Arc>().OrderByDescending(a => a.Radius).FirstOrDefault()
                    : null;

                // Faces synthesised from the wall itself, exactly like the classifier does when
                // nothing is drawn inside a gap.
                double half = host.Thickness / 2;
                Segment e1 = new(host.FromAxis(start, -half), host.FromAxis(end, -half));
                Segment e2 = new(host.FromAxis(start, half), host.FromAxis(end, half));

                openings.Add(new Opening(host.Reference, e1, e2, (start, end), kind,
                                         OpeningEvidence.SynthesisedFaces
                                         | (swing is not null ? OpeningEvidence.SwingArc : OpeningEvidence.None),
                                         thicknessResidual: 0, swingArc: swing));
            }

            return openings;
        }

        /// <summary>Drops gap-detected openings that overlap a forced one on the same run frame.</summary>
        public static List<Opening> MergeDetected(List<Opening> forced, List<Opening> detected) {
            List<Opening> merged = new(forced);

            foreach (Opening candidate in detected) {
                bool duplicate = forced.Any(f =>
                    f.Wall == candidate.Wall
                    && candidate.AxisSpan.Start < f.AxisSpan.End
                    && f.AxisSpan.Start < candidate.AxisSpan.End);
                if (!duplicate) merged.Add(candidate);
            }

            return merged;
        }

        /// <summary>Union-find-by-proximity: elements whose bounding boxes come within the gap
        /// distance belong to one physical door or window.</summary>
        private static List<List<GeometryElement>> Cluster(IReadOnlyList<GeometryElement> elements,
                                                           double gapDistance) {
            List<List<GeometryElement>> clusters = new();
            var boxes = elements
                .Where(e => e.Points.Count > 0)
                .Select(e => (Element: e, Box: BoundsOf(e)))
                .ToList();
            bool[] assigned = new bool[boxes.Count];

            for (int i = 0; i < boxes.Count; i++) {
                if (assigned[i]) continue;

                List<GeometryElement> cluster = new() { boxes[i].Element };
                assigned[i] = true;

                // Grow until nothing else is close to any member.
                bool grew = true;
                var reach = boxes[i].Box;
                while (grew) {
                    grew = false;
                    for (int j = 0; j < boxes.Count; j++) {
                        if (assigned[j]) continue;
                        if (!Touches(reach, boxes[j].Box, gapDistance)) continue;
                        cluster.Add(boxes[j].Element);
                        assigned[j] = true;
                        reach = Union(reach, boxes[j].Box);
                        grew = true;
                    }
                }

                clusters.Add(cluster);
            }

            return clusters;
        }

        private static WallRun? FindHost(IReadOnlyList<WallRun> runs, Point centre,
                                         ClassificationTolerances tolerances) {
            WallRun? best = null;
            double bestOffset = double.MaxValue;

            foreach (WallRun run in runs) {
                double offset = Math.Abs(run.NormalParam(centre));
                // A door's own geometry (leaf, swing) sits beside the wall band, so allow the
                // swing's whole reach when matching, not just the wall thickness.
                if (offset > (run.Thickness / 2) + tolerances.MaxSwingRadius) continue;

                var (start, end) = run.Span;
                double along = run.AxisParam(centre);
                if (along < start - run.Thickness || along > end + run.Thickness) continue;

                if (offset < bestOffset) { bestOffset = offset; best = run; }
            }

            return best;
        }

        private static (double MinX, double MinY, double MaxX, double MaxY) BoundsOf(GeometryElement element) {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (Point p in element.Points) {
                minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x);
                minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y);
            }
            return (minX, minY, maxX, maxY);
        }

        private static Point BoundsCentre(List<GeometryElement> cluster) {
            var box = cluster.Select(BoundsOf).Aggregate(Union);
            return new Point((box.MinX + box.MaxX) / 2, (box.MinY + box.MaxY) / 2);
        }

        private static bool Touches((double MinX, double MinY, double MaxX, double MaxY) a,
                                    (double MinX, double MinY, double MaxX, double MaxY) b,
                                    double gap) =>
            a.MinX - gap <= b.MaxX && b.MinX - gap <= a.MaxX
            && a.MinY - gap <= b.MaxY && b.MinY - gap <= a.MaxY;

        private static (double MinX, double MinY, double MaxX, double MaxY) Union(
            (double MinX, double MinY, double MaxX, double MaxY) a,
            (double MinX, double MinY, double MaxX, double MaxY) b) =>
            (Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY),
             Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY));
    }
}
