using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Cad2Bim.Services;

namespace Cad2Bim.Classification {
    /// <summary>
    /// Runs the whole pipeline over a file and writes what it found as text.
    /// <para>
    /// The viewport is the wrong instrument for judging a detector: it shows what was found and
    /// says nothing about what was discarded or why. This does, and it runs without a window, so
    /// a change in a tolerance can be measured against a real drawing in seconds.
    /// </para>
    /// </summary>
    public static class ClassificationDump {
        public static string Run(string filePath, double sMinMillimeters, double sMaxMillimeters,
                                 ClassificationTolerances? tolerances = null) {
            StringBuilder text = new();
            var culture = CultureInfo.InvariantCulture;
            Stopwatch stopwatch = Stopwatch.StartNew();

            ClassificationService service = new();
            service.Load(filePath);

            double scale = service.MillimetersPerUnit;
            double loadMs = stopwatch.Elapsed.TotalMilliseconds;
            stopwatch.Restart();

            ClassificationReport report = new();
            ClassificationResult result = service.ClassifyAll(sMinMillimeters, sMaxMillimeters, tolerances, report);

            text.AppendLine($"file    : {Path.GetFileName(filePath)}");
            text.AppendLine($"units   : {DrawingUnits.Name(service.Units)}  ({scale} mm per drawing unit)");
            text.AppendLine($"timing  : load {loadMs:F0} ms, classify {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
            text.AppendLine($"walls   : {result.Walls.Count}");
            text.AppendLine();
            text.Append(report);

            if (result.Openings.Count > 0) {
                text.AppendLine();
                text.AppendLine("openings:");
                text.AppendLine("  kind    width(mm)  resid  x            y            evidence");

                foreach (Opening opening in result.Openings.OrderByDescending(o => o.Kind)) {
                    text.AppendLine(string.Format(culture,
                        "  {0,-7} {1,9:F0}  {2,5:F0}  {3,12:F1} {4,12:F1}  {5}",
                        opening.Kind,
                        opening.Width * scale,
                        opening.ThicknessResidual * scale,
                        opening.Center.x,
                        opening.Center.y,
                        opening.Evidence));
                }
            }

            return text.ToString();
        }
    }
}
