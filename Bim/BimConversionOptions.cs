namespace Cad2Bim.Bim {
    /// <summary>What to do with an opening the classifier could not name.</summary>
    public enum UnknownOpeningPolicy {
        /// <summary>Cut the void but place no filling element — the wall is honestly punctured
        /// and a modeller decides later. The default.</summary>
        VoidOnly,
        /// <summary>Leave the wall solid.</summary>
        Skip,
        /// <summary>Treat it as a window.</summary>
        AsWindow
    }

    /// <summary>
    /// Everything the CAD-to-BIM conversion can be tuned by. Heights are millimetres above the
    /// storey; the drawing itself carries no third dimension, so these are the invented Z values.
    /// </summary>
    public sealed record BimConversionOptions(
        double WallHeightMm,
        double DoorHeightMm,
        double WindowSillMm,
        double WindowHeadMm,
        UnknownOpeningPolicy UnknownOpenings) {

        public static readonly BimConversionOptions Default = new(
            WallHeightMm: 3000,
            DoorHeightMm: 2100,
            WindowSillMm: 900,
            WindowHeadMm: 2100,
            UnknownOpenings: UnknownOpeningPolicy.VoidOnly);
    }
}
