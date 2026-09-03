namespace Cad2Bim.Bim {
    /// <summary>A 2D point in the BIM model. Millimetres, world coordinates.</summary>
    public readonly record struct BimPoint(double X, double Y);

    /// <summary>A closed polygon. First point is not repeated at the end; winding is CCW for
    /// outer boundaries and CW for holes.</summary>
    public sealed record BimPolygon(IReadOnlyList<BimPoint> Points);

    /// <summary>
    /// The neutral building model every exporter consumes. Deliberately independent of both the
    /// CAD geometry types and any output schema: reconstruction fills it, exporters read it.
    /// All lengths are millimetres; plan coordinates are world XY, heights are Z above the storey.
    /// </summary>
    public sealed class BimModel {
        public required string ProjectName { get; init; }
        public string? SourceFile { get; init; }
        public required IReadOnlyList<BimStorey> Storeys { get; init; }
    }

    public sealed class BimStorey {
        public required string Name { get; init; }
        public double ElevationMm { get; init; }
        public required IReadOnlyList<BimWall> Walls { get; init; }
        public IReadOnlyList<BimColumn> Columns { get; init; } = Array.Empty<BimColumn>();
    }
}
