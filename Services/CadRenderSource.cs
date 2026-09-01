using ACadSharp;
using ACadSharp.IO;
using Cad2Bim.Services.Cad;

namespace Cad2Bim.Services {
    /// <summary>
    /// Turns a CadDocument straight into render-ready polylines — the viewport's ground truth.
    /// Blocks are flattened into world coordinates, every curve is tessellated, nothing is
    /// classified or filtered.
    /// <para>
    /// The traversal itself lives in <see cref="CadEntityWalker"/>, which the classifier reads the
    /// same document through. This is only the stroke half of it: what the classifier keeps stays
    /// comparable against the ground truth drawn underneath it, because both come from one walk of
    /// one file.
    /// </para>
    /// </summary>
    public static class CadRenderSource {
        public static CadDocument Read(string filePath) =>
            filePath.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)
                ? new DxfReader(filePath).Read()
                : new DwgReader(filePath).Read();

        /// <summary>Model-space entities, flattened to polylines. Text/annotation glyphs are skipped.</summary>
        public static List<object> Flatten(CadDocument document) {
            CadStrokeSink sink = new();
            CadEntityWalker.Walk(document, sink);
            return sink.Shapes;
        }
    }
}
