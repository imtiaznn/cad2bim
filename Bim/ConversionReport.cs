using System.Text;

namespace Cad2Bim.Bim {
    /// <summary>
    /// What the conversion actually did, in the spirit of <see cref="Classification.ClassificationReport"/>:
    /// the viewport shows the result, this explains it.
    /// </summary>
    public sealed class ConversionReport {
        public int WallCount { get; set; }
        public int ColumnCount { get; set; }
        public int DoorCount { get; set; }
        public int WindowCount { get; set; }
        public int UnknownOpeningCount { get; set; }
        public int SkippedOpenings { get; set; }

        private readonly List<string> _warnings = new();
        public IReadOnlyList<string> Warnings => _warnings;
        public void Warn(string message) => _warnings.Add(message);

        public override string ToString() {
            StringBuilder text = new();
            text.AppendLine($"walls   : {WallCount}");
            text.AppendLine($"columns : {ColumnCount}");
            text.AppendLine($"doors   : {DoorCount}");
            text.AppendLine($"windows : {WindowCount}");
            text.AppendLine($"unknown : {UnknownOpeningCount} openings ({SkippedOpenings} skipped)");
            foreach (string warning in _warnings) text.AppendLine($"warning : {warning}");
            return text.ToString();
        }
    }
}
