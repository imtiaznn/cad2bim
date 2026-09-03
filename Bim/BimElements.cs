namespace Cad2Bim.Bim {
    /// <summary>One physical wall: a footprint polygon extruded to a height, with its openings.</summary>
    public sealed class BimWall {
        public required string Name { get; init; }
        public required double ThicknessMm { get; init; }
        public required double HeightMm { get; init; }

        /// <summary>Endpoints of the wall's centreline — the BIM location line.</summary>
        public required BimPoint AxisStart { get; init; }
        public required BimPoint AxisEnd { get; init; }

        /// <summary>Exact plan footprint: the piece's span-by-thickness rectangle.</summary>
        public required BimPolygon Footprint { get; init; }

        public IReadOnlyList<BimOpening> Openings { get; init; } = Array.Empty<BimOpening>();
    }

    /// <summary>A structural column: its exact plan footprint extruded to a height.</summary>
    public sealed class BimColumn {
        public required string Name { get; init; }
        public required double HeightMm { get; init; }
        public required BimPolygon Footprint { get; init; }
    }

    public enum BimOpeningKind { Unknown, Door, Window }

    /// <summary>A hole in a wall, optionally filled by a door or window element.</summary>
    public class BimOpening {
        public BimOpeningKind Kind { get; init; }

        /// <summary>Four plan corners of the void (opening span by wall thickness).</summary>
        public required IReadOnlyList<BimPoint> FootprintRect { get; init; }

        public required double WidthMm { get; init; }
        public required double SillMm { get; init; }
        public required double HeadMm { get; init; }
        public required BimPoint Center { get; init; }

        /// <summary>Heading of the host wall's axis, degrees CCW from +X.</summary>
        public required double WallHeadingDeg { get; init; }
    }

    public sealed class BimDoor : BimOpening {
        public required double LeafWidthMm { get; init; }

        /// <summary>Hinge sits at the start (lower axis parameter) end of the opening span.</summary>
        public required bool HingeAtStart { get; init; }

        /// <summary>The leaf swings toward the host wall's positive normal side.</summary>
        public required bool SwingsPositiveNormal { get; init; }
    }

    public sealed class BimWindow : BimOpening { }
}
