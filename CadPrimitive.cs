namespace Cad2Bim {
    /// <summary>Which layer a primitive currently belongs to. Annotation is geometry that is
    /// drawn but never classified (hatch boundaries, dimension blocks).</summary>
    public enum PrimitiveClass : byte { Unclassified, Wall, Door, Window, Annotation }

    /// <summary>
    /// Stable identity for a primitive across loads of the same file: the top-level entity's
    /// handle plus the primitive's emission ordinal within that entity. Leaf handles repeat when
    /// a block is inserted more than once, but the walk order is deterministic, so the pair is
    /// unique. This is the join key a future save format persists classifications under.
    /// </summary>
    public readonly record struct PrimitiveKey(ulong EntityHandle, int Ordinal);

    /// <summary>
    /// A stroke-only run of straight spans, kept for geometry that is drawn but never analysed.
    /// Unlike Segment/Arc it never reaches the classifier.
    /// </summary>
    public sealed class PolylinePath : GeometryElement {
        public bool IsClosed { get; }

        public PolylinePath(IReadOnlyList<Point> points, bool isClosed) {
            Points = points.ToList();
            IsClosed = isClosed;
        }
    }

    /// <summary>
    /// One drawable, individually addressable piece of the drawing: a straight span, a true arc,
    /// or an annotation stroke. The unit the brush paints and the classifier tags.
    /// </summary>
    public sealed class CadPrimitive {
        public required int Id { get; init; }
        public required PrimitiveKey Key { get; init; }
        public required GeometryElement Geometry { get; init; }

        /// <summary>False for annotation strokes: drawn, but neither classified nor pickable.</summary>
        public required bool IsClassifiable { get; init; }
    }
}
