using Cad2Bim.Classification;

namespace Cad2Bim.Reconstruction {
    /// <summary>One contiguous stretch of a run: its axis interval and exact plan rectangle.</summary>
    public sealed record WallPiece(WallRun Run, double Start, double End, IReadOnlyList<Point> Corners) {
        public bool Contains(double axisPosition) => axisPosition >= Start && axisPosition <= End;
    }

    /// <summary>
    /// Analytic wall footprints, straight from the classified geometry — the coordinates the user
    /// drew (or tagged) survive verbatim into the model. Each run's covered intervals are bridged
    /// wherever an accepted opening sits, so a wall spans its doorways as one solid and the
    /// opening's void cut is what leaves the lintel above. Gaps with no opening in them stay
    /// gaps: there the wall really is interrupted, and each side becomes its own piece.
    /// </summary>
    public static class WallFootprintBuilder {
        public static List<WallPiece> Build(WallRun run, IEnumerable<Opening> openings, double joinTolerance) {
            List<(double Start, double End)> covered = run.Covered(joinTolerance);
            covered.AddRange(openings.Select(o => o.AxisSpan));

            List<WallPiece> pieces = new();
            double half = run.Thickness / 2;

            foreach (var (start, end) in Intervals.Merge(covered, joinTolerance)) {
                if (end - start <= joinTolerance) continue; // a sliver, not a wall

                pieces.Add(new WallPiece(run, start, end, new List<Point> {
                    run.FromAxis(start, -half), run.FromAxis(end, -half),
                    run.FromAxis(end, half), run.FromAxis(start, half)
                }));
            }

            return pieces;
        }
    }
}
