using System.Text;

namespace Cad2Bim.Classification {
    public enum RejectReason { TooNarrow, TooWide, WallJunction, NoEvidence }

    /// <summary>
    /// Tallies what the opening pass saw, accepted and threw away, and why.
    /// <para>
    /// The rejection counts are the point. Tuning a detector by looking only at what it found is
    /// guesswork; the per-rule counts say which threshold is doing the work and which is merely in
    /// the way, and a histogram of candidate widths shows a false-positive cluster - columns and
    /// short jogs pile up at one width - at a glance.
    /// </para>
    /// </summary>
    public sealed class ClassificationReport {
        private readonly Dictionary<RejectReason, int> _rejected = new();
        private readonly Dictionary<OpeningKind, int> _accepted = new();
        private readonly List<double> _acceptedWidths = new();

        public int Runs { get; private set; }
        public int Segments { get; private set; }
        public int SwingCandidates { get; private set; }

        /// <summary>Divides drawing units back into millimetres for anything human-readable.</summary>
        public double MillimetersPerUnit { get; set; } = 1.0;

        internal void Note(int runs, int segments, int swingCandidates) {
            Runs = runs;
            Segments = segments;
            SwingCandidates = swingCandidates;
        }

        internal void Reject(RejectReason reason) =>
            _rejected[reason] = _rejected.GetValueOrDefault(reason) + 1;

        internal void Accept(OpeningKind kind) =>
            _accepted[kind] = _accepted.GetValueOrDefault(kind) + 1;

        internal void AcceptWidth(double widthInDrawingUnits) =>
            _acceptedWidths.Add(widthInDrawingUnits);

        public int Count(OpeningKind kind) => _accepted.GetValueOrDefault(kind);

        public override string ToString() {
            StringBuilder text = new();

            text.AppendLine($"wall runs        : {Runs}");
            text.AppendLine($"segments         : {Segments}");
            text.AppendLine($"swing candidates : {SwingCandidates}");
            text.AppendLine();

            text.AppendLine("accepted:");
            foreach (OpeningKind kind in new[] { OpeningKind.Door, OpeningKind.Window, OpeningKind.Unknown }) {
                if (_accepted.GetValueOrDefault(kind) > 0) {
                    text.AppendLine($"  {kind,-8} {_accepted[kind]}");
                }
            }

            text.AppendLine();
            text.AppendLine("rejected:");
            foreach (RejectReason reason in Enum.GetValues<RejectReason>()) {
                text.AppendLine($"  {reason,-13} {_rejected.GetValueOrDefault(reason)}");
            }

            if (_acceptedWidths.Count > 0) {
                text.AppendLine();
                text.AppendLine("accepted widths (100 mm buckets):");

                var buckets = _acceptedWidths
                    .GroupBy(w => (int)(w * MillimetersPerUnit / 100) * 100)
                    .OrderBy(g => g.Key);

                foreach (var bucket in buckets) {
                    text.AppendLine($"  {bucket.Key,5}-{bucket.Key + 100,-5} mm : {bucket.Count()}");
                }
            }

            return text.ToString();
        }
    }
}
