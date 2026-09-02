using System.IO;
using System.Text.Json;

namespace Cad2Bim.Services {
    /// <summary>
    /// Sidecar persistence for classifications: a small JSON file next to the drawing, keyed by
    /// each primitive's <see cref="PrimitiveKey"/> (entity handle + emission ordinal). The
    /// drawing itself is never touched, and the walk order is deterministic, so the keys resolve
    /// back to the same primitives on the next load of the same file.
    /// </summary>
    public static class SegmentationStore {
        private const int FormatVersion = 1;

        private sealed record Entry(ulong H, int O, byte C);
        private sealed record SidecarFile(int Version, List<Entry> Entries);

        public static string PathFor(string drawingPath) => drawingPath + ".c2b.json";

        /// <summary>Writes every classified primitive's tag. Unclassified is the default and is omitted.</summary>
        public static void Save(DrawingModel model, string drawingPath) {
            List<Entry> entries = new();

            foreach (CadPrimitive primitive in model.Primitives) {
                PrimitiveClass cls = model.ClassOf(primitive.Id);
                if (cls is PrimitiveClass.Unclassified or PrimitiveClass.Annotation) {
                    continue;
                }

                entries.Add(new Entry(primitive.Key.EntityHandle, primitive.Key.Ordinal, (byte)cls));
            }

            string json = JsonSerializer.Serialize(new SidecarFile(FormatVersion, entries));
            File.WriteAllText(PathFor(drawingPath), json);
        }

        /// <summary>
        /// Applies a sidecar file to a freshly loaded model, if one exists. Returns how many
        /// primitives were classified; 0 when there is no sidecar or nothing matched.
        /// </summary>
        public static int TryLoad(DrawingModel model, string drawingPath) {
            string path = PathFor(drawingPath);
            if (!File.Exists(path)) {
                return 0;
            }

            SidecarFile? sidecar;
            try {
                sidecar = JsonSerializer.Deserialize<SidecarFile>(File.ReadAllText(path));
            }
            catch (JsonException) {
                return 0;
            }

            if (sidecar is null || sidecar.Version != FormatVersion) {
                return 0;
            }

            Dictionary<PrimitiveKey, int> byKey = new(model.Primitives.Count);
            foreach (CadPrimitive primitive in model.Primitives) {
                byKey[primitive.Key] = primitive.Id;
            }

            Dictionary<PrimitiveClass, List<int>> byClass = new();
            foreach (Entry entry in sidecar.Entries) {
                if (!byKey.TryGetValue(new PrimitiveKey(entry.H, entry.O), out int id)) {
                    continue; // drawing changed since the save; skip what no longer resolves
                }

                var cls = (PrimitiveClass)entry.C;
                if (cls is PrimitiveClass.Unclassified or PrimitiveClass.Annotation) {
                    continue;
                }

                if (!byClass.TryGetValue(cls, out List<int>? ids)) {
                    byClass[cls] = ids = new List<int>();
                }
                ids.Add(id);
            }

            int applied = 0;
            foreach ((PrimitiveClass cls, List<int> ids) in byClass) {
                model.SetClasses(ids, cls, ChangeSource.Load);
                applied += ids.Count;
            }

            return applied;
        }
    }
}
