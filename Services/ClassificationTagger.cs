using System.Diagnostics;

namespace Cad2Bim.Services {
    /// <summary>Which categories an automatic run is allowed to (re)write.</summary>
    public sealed record AutoSegmentationOptions(bool Walls, bool Doors, bool Windows);

    /// <summary>
    /// Maps an automatic classification result back onto the drawn primitives. The classifier
    /// passes geometry through by reference, so every wall face and opening line still carries the
    /// <see cref="GeometryElement.SourceId"/> the sink stamped at load; synthesised faces and
    /// clipped sub-segments carry -1 and are correctly skipped — nothing was drawn for them.
    /// </summary>
    public static class ClassificationTagger {
        public static void Apply(ClassificationResult result, DrawingModel model, AutoSegmentationOptions options) {
            using IDisposable scope = model.BeginEditScope();

            // Each checked category is rewritten from scratch: clear it, then tag the new result.
            // Unchecked categories keep whatever they had, manual edits included.
            if (options.Walls) Clear(model, PrimitiveClass.Wall);
            if (options.Doors) Clear(model, PrimitiveClass.Door);
            if (options.Windows) Clear(model, PrimitiveClass.Window);

            if (options.Walls) {
                List<int> ids = new();
                foreach (Wall wall in result.Walls) {
                    AddSource(ids, wall.Face1, model);
                    AddSource(ids, wall.Face2, model);
                }
                model.SetClasses(ids, PrimitiveClass.Wall, ChangeSource.Auto);
            }

            List<int> doorIds = new();
            List<int> windowIds = new();

            foreach (Opening opening in result.Openings) {
                switch (opening.Kind) {
                    case OpeningKind.Door when options.Doors:
                        foreach (GeometryElement element in opening.Geometry) {
                            AddSource(doorIds, element, model);
                        }
                        break;

                    case OpeningKind.Window when options.Windows:
                        foreach (GeometryElement element in opening.Geometry) {
                            AddSource(windowIds, element, model);
                        }
                        break;
                }
            }

            if (options.Doors) model.SetClasses(doorIds, PrimitiveClass.Door, ChangeSource.Auto);
            if (options.Windows) model.SetClasses(windowIds, PrimitiveClass.Window, ChangeSource.Auto);
        }

        private static void Clear(DrawingModel model, PrimitiveClass bucket) =>
            model.SetClasses(model.IdsIn(bucket).ToList(), PrimitiveClass.Unclassified, ChangeSource.Auto);

        private static void AddSource(List<int> ids, GeometryElement element, DrawingModel model) {
            int id = element.SourceId;
            if (id < 0) {
                return; // synthesised or clipped geometry: nothing drawn to tag
            }

            // Guards the pass-through invariant: the classifier must hand back the loaded
            // instances, not clones, or SourceId points at the wrong line.
            Debug.Assert(ReferenceEquals(model.Primitives[id].Geometry, element)
                         || SameEndpoints(model.Primitives[id].Geometry, element),
                         "Classification result geometry no longer matches its source primitive.");

            ids.Add(id);
        }

        private static bool SameEndpoints(GeometryElement a, GeometryElement b) {
            if (a.Points.Count == 0 || b.Points.Count == 0) return false;

            static bool Close(Point p, Point q) =>
                Math.Abs(p.x - q.x) < 1e-6 && Math.Abs(p.y - q.y) < 1e-6;

            return (Close(a.Points[0], b.Points[0]) && Close(a.Points[^1], b.Points[^1]))
                || (Close(a.Points[0], b.Points[^1]) && Close(a.Points[^1], b.Points[0]));
        }
    }
}
