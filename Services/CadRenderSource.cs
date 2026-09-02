using ACadSharp;
using ACadSharp.IO;

namespace Cad2Bim.Services {
    /// <summary>Picks the right ACadSharp reader for a DWG/DXF path.</summary>
    public static class CadRenderSource {
        public static CadDocument Read(string filePath) =>
            filePath.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)
                ? new DxfReader(filePath).Read()
                : new DwgReader(filePath).Read();
    }
}
