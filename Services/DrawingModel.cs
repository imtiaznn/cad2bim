using ACadSharp;
using ACadSharp.Types.Units;
using Cad2Bim.Services.Cad;

namespace Cad2Bim.Services {
    /// <summary>Who changed a classification — load reset, the auto classifier, the brush, or undo.</summary>
    public enum ChangeSource { Load, Auto, Manual, Undo }

    /// <summary>One batch of classification changes, already applied.</summary>
    public sealed record ClassificationDelta(IReadOnlyList<int> Ids, ChangeSource Source);

    /// <summary>
    /// The loaded drawing as one addressable store: every primitive, its current classification,
    /// a spatial index for picking, and undo. Every tag change — brush, eraser, auto confirm —
    /// goes through <see cref="SetClasses"/>, so there is exactly one seam that mutates
    /// classification state and one event the viewport listens to.
    /// </summary>
    public sealed class DrawingModel {
        private const int GridResolution = 256;
        private const int MaxUndoEntries = 64;

        private readonly CadPrimitive[] _primitives;
        private readonly PrimitiveClass[] _classes;
        private readonly Dictionary<PrimitiveClass, HashSet<int>> _buckets = new();

        private Stack<List<(int Id, PrimitiveClass Old)>> _undoStack = new();
        private List<(int Id, PrimitiveClass Old)>? _openScope;

        public IReadOnlyList<CadPrimitive> Primitives => _primitives;
        public IReadOnlyList<TextElement> Texts { get; }
        public UnitsType Units { get; }
        public System.Windows.Rect Bounds { get; }
        public PrimitiveGrid Grid { get; }

        /// <summary>Raised after every applied <see cref="SetClasses"/> batch.</summary>
        public event Action<ClassificationDelta>? ClassificationChanged;

        private DrawingModel(CadPrimitive[] primitives, List<TextElement> texts, UnitsType units) {
            _primitives = primitives;
            _classes = new PrimitiveClass[primitives.Length];
            Texts = texts;
            Units = units;

            foreach (PrimitiveClass bucket in Enum.GetValues<PrimitiveClass>()) {
                _buckets[bucket] = new HashSet<int>();
            }

            foreach (CadPrimitive primitive in primitives) {
                PrimitiveClass initial = primitive.IsClassifiable
                    ? PrimitiveClass.Unclassified
                    : PrimitiveClass.Annotation;
                _classes[primitive.Id] = initial;
                _buckets[initial].Add(primitive.Id);
            }

            Bounds = ComputeBounds(primitives);
            double extent = Math.Max(Bounds.Width, Bounds.Height);
            Grid = new PrimitiveGrid(primitives, extent > 0 ? extent / GridResolution : 1.0);
        }

        public static DrawingModel Load(CadDocument document) {
            CadPrimitiveSink sink = new();
            CadEntityWalker.Walk(document, sink);
            return new DrawingModel(sink.Primitives.ToArray(), sink.Texts,
                                    document.Header?.InsUnits ?? UnitsType.Unitless);
        }

        public PrimitiveClass ClassOf(int id) => _classes[id];

        /// <summary>Ids currently in a bucket. Do not mutate; snapshot before passing to <see cref="SetClasses"/>.</summary>
        public IReadOnlyCollection<int> IdsIn(PrimitiveClass bucket) => _buckets[bucket];

        /// <summary>
        /// The classifiable primitives' geometry, exactly what the classifier consumes. Each
        /// element's <see cref="GeometryElement.SourceId"/> is its primitive id, so results map
        /// straight back onto the store.
        /// </summary>
        public List<GeometryElement> AnalyzableGeometry() =>
            _primitives.Where(p => p.IsClassifiable).Select(p => p.Geometry).ToList();

        /// <summary>
        /// Groups every <see cref="SetClasses"/> call until disposal into one undo entry — one
        /// brush stroke, or one auto run, undoes as a unit. Events still fire per call, so
        /// painting gives live feedback.
        /// </summary>
        public IDisposable BeginEditScope() {
            if (_openScope is not null) throw new InvalidOperationException("An edit scope is already open.");
            _openScope = new List<(int, PrimitiveClass)>();
            return new EditScope(this);
        }

        /// <summary>The single mutation seam. Non-classifiable ids and no-op changes are ignored.</summary>
        public void SetClasses(IReadOnlyList<int> ids, PrimitiveClass newClass, ChangeSource source) {
            List<int>? changed = null;

            foreach (int id in ids) {
                if (!_primitives[id].IsClassifiable || _classes[id] == newClass) {
                    continue;
                }

                (changed ??= new List<int>()).Add(id);
                _openScope?.Add((id, _classes[id]));
                if (_openScope is null && source != ChangeSource.Undo) {
                    // Unscoped call: its own undo entry.
                    PushUndo(new List<(int, PrimitiveClass)> { (id, _classes[id]) });
                }

                _buckets[_classes[id]].Remove(id);
                _classes[id] = newClass;
                _buckets[newClass].Add(id);
            }

            if (changed is not null) {
                ClassificationChanged?.Invoke(new ClassificationDelta(changed, source));
            }
        }

        public bool CanUndo => _undoStack.Count > 0;

        /// <summary>Reverts the most recent edit scope. Returns false when there is nothing to undo.</summary>
        public bool Undo() {
            if (_undoStack.Count == 0) return false;

            List<(int Id, PrimitiveClass Old)> entry = _undoStack.Pop();
            List<int> changed = new(entry.Count);

            // Reverse order, so a primitive painted twice in one stroke lands on its pre-stroke class.
            for (int i = entry.Count - 1; i >= 0; i--) {
                var (id, old) = entry[i];
                if (_classes[id] == old) continue;

                _buckets[_classes[id]].Remove(id);
                _classes[id] = old;
                _buckets[old].Add(id);
                changed.Add(id);
            }

            if (changed.Count > 0) {
                ClassificationChanged?.Invoke(new ClassificationDelta(changed, ChangeSource.Undo));
            }

            return true;
        }

        private void PushUndo(List<(int Id, PrimitiveClass Old)> entry) {
            if (entry.Count == 0) return;

            _undoStack.Push(entry);
            if (_undoStack.Count > MaxUndoEntries) {
                // Stack has no bottom-drop; rebuild without the oldest entry.
                var kept = _undoStack.Take(MaxUndoEntries).Reverse().ToList();
                _undoStack = new Stack<List<(int Id, PrimitiveClass Old)>>(kept);
            }
        }

        private static System.Windows.Rect ComputeBounds(IReadOnlyList<CadPrimitive> primitives) {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (CadPrimitive primitive in primitives) {
                foreach (Point p in primitive.Geometry.Points) {
                    if (p.x < minX) minX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y > maxY) maxY = p.y;
                }
            }

            return minX > maxX ? System.Windows.Rect.Empty
                               : new System.Windows.Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private sealed class EditScope : IDisposable {
            private DrawingModel? _model;

            public EditScope(DrawingModel model) => _model = model;

            public void Dispose() {
                if (_model is null) return;

                List<(int, PrimitiveClass)>? scope = _model._openScope;
                _model._openScope = null;
                if (scope is not null) _model.PushUndo(scope);
                _model = null;
            }
        }
    }
}
