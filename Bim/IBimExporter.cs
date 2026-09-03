namespace Cad2Bim.Bim {
    /// <summary>
    /// Writes a <see cref="BimModel"/> to a file. The seam that keeps serialization formats
    /// (IFC today, JSON or DWG tagging tomorrow) independent of how the model was reconstructed.
    /// </summary>
    public interface IBimExporter {
        void Export(BimModel model, string path);
    }
}
