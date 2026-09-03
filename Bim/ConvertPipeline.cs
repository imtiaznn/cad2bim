using System.IO;
using System.Text;
using Cad2Bim.Bim.Ifc;
using Cad2Bim.Services;

namespace Cad2Bim.Bim {
    /// <summary>
    /// The one conversion path both entry points share: the GUI's Convert command and the headless
    /// <c>--convert</c> switch classify, reconstruct and export identically.
    /// </summary>
    public static class ConvertPipeline {
        /// <summary>Convert an already-classified drawing and write the IFC file.</summary>
        public static ConversionReport Run(ClassificationResult classification, double millimetersPerUnit,
                                           BimConversionOptions options, string? sourceFile,
                                           string outputPath) {
            ConversionReport report = new();
            BimModel model = CadToBimConverter.Convert(classification, millimetersPerUnit,
                                                       options, sourceFile, report);
            new IfcExporter().Export(model, outputPath);
            return report;
        }

        /// <summary>
        /// Load, classify, convert, export; returns a text report. A saved segmentation sidecar
        /// takes precedence, exactly as in the GUI: hand tags drive the tagged pipeline (columns,
        /// forced openings); only an untagged drawing falls back to full automatic classification.
        /// </summary>
        public static string RunHeadless(string cadPath, string? outputPath, BimConversionOptions options) {
            string ifcPath = outputPath ?? Path.ChangeExtension(cadPath, ".ifc");

            DrawingModel drawing = DrawingModel.Load(CadRenderSource.Read(cadPath));
            ClassificationService service = new();
            service.Load(drawing.AnalyzableGeometry(), drawing.Units);

            int restored = SegmentationStore.TryLoad(drawing, cadPath);

            List<GeometryElement> Tagged(PrimitiveClass bucket) => drawing.IdsIn(bucket)
                .Select(id => drawing.Primitives[id].Geometry)
                .ToList();
            List<Segment> wallSegments = Tagged(PrimitiveClass.Wall).OfType<Segment>().ToList();

            ClassificationResult classification = restored > 0 && wallSegments.Count > 0
                ? service.ClassifyTagged(wallSegments,
                                         Tagged(PrimitiveClass.Door), Tagged(PrimitiveClass.Window),
                                         Wall.DefaultSMinMillimeters, Wall.DefaultSMaxMillimeters)
                : service.ClassifyAll(Wall.DefaultSMinMillimeters, Wall.DefaultSMaxMillimeters);

            ConversionReport report = Run(classification, service.MillimetersPerUnit,
                                          options, cadPath, ifcPath);

            StringBuilder text = new();
            text.AppendLine($"file    : {Path.GetFileName(cadPath)}");
            text.AppendLine($"tags    : " + (restored > 0
                ? $"{restored} restored from sidecar (tagged pipeline)"
                : "none (automatic classification)"));
            text.AppendLine($"output  : {ifcPath}");
            text.Append(report);
            return text.ToString();
        }
    }
}
