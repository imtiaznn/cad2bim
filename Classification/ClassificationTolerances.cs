namespace Cad2Bim.Classification {
    /// <summary>
    /// Every tunable the classifier uses, authored in millimetres and converted once, at the edge,
    /// through the drawing's own INSUNITS. Nothing downstream is allowed to invent a raw
    /// drawing-unit constant: a plan drawn in metres and the same plan drawn in millimetres must
    /// classify identically.
    /// <para>
    /// Passed by value rather than parked in statics the way <see cref="Wall.SMin"/> is, so a run
    /// cannot be perturbed by whatever ran before it.
    /// </para>
    /// </summary>
    public sealed record ClassificationTolerances(
        // --- lengths, millimetres (converted) --------------------------------------------------
        double MinOpeningWidth,
        double MaxOpeningWidth,
        double ThicknessEpsilon,
        double AxisOffsetTolerance,
        double EndpointTolerance,
        double HingeTolerance,
        double MinSwingRadius,
        double MaxSwingRadius,
        double MinFaceLength,
        double MinColumnSide,
        double MaxColumnSide,
        // --- angles and ratios, unit-free (not converted) --------------------------------------
        double AngleToleranceDegrees,
        double MinSwingSweepDegrees,
        double MaxSwingSweepDegrees) {

        /// <summary>
        /// Defaults measured against real architectural drawings rather than assumed. Notably:
        /// 600 mm is the smallest thing that is a doorway rather than a column or a short jog, and
        /// a 500 mm swing radius floor is what separates a door leaf from a swivel chair, which is
        /// drawn as the same 90-ish degree arc at about 400 mm.
        /// </summary>
        public static readonly ClassificationTolerances DefaultMillimeters = new(
            MinOpeningWidth: 600,
            MaxOpeningWidth: 4000,
            // Deliberately loose: the thickness residual is scored, not gated - see OpeningClassifier.
            ThicknessEpsilon: 150,
            // Nominally equal walls measure 107-118 mm across one drawing, so collinearity has to
            // tolerate a face drifting by about this much.
            AxisOffsetTolerance: 10,
            EndpointTolerance: 5,
            // A door leaf is drawn as a thin rectangle, so its corner sits tens of millimetres off
            // the hinge; 1 mm endpoint matching misses every real door.
            HingeTolerance: 150,
            MinSwingRadius: 500,
            MaxSwingRadius: 1500,
            MinFaceLength: 200,
            // A closed rectangle in the wall tags reads as a structural column when both sides
            // fall in this range: below it lie wall-end caps and jamb blocks, above it rooms.
            MinColumnSide: 150,
            MaxColumnSide: 800,
            AngleToleranceDegrees: 2,
            // Real swings measure 83-94 degrees, so 90 +/- 10 already clips the low end.
            MinSwingSweepDegrees: 60,
            MaxSwingSweepDegrees: 120);

        /// <summary>
        /// The same tolerances expressed in the drawing's units. Lengths divide by the scale;
        /// angles and ratios pass through untouched.
        /// </summary>
        public ClassificationTolerances ToDrawingUnits(double millimetersPerUnit) {
            if (millimetersPerUnit <= 0) return this;

            double Scale(double millimeters) => millimeters / millimetersPerUnit;

            return this with {
                MinOpeningWidth = Scale(MinOpeningWidth),
                MaxOpeningWidth = Scale(MaxOpeningWidth),
                ThicknessEpsilon = Scale(ThicknessEpsilon),
                AxisOffsetTolerance = Scale(AxisOffsetTolerance),
                EndpointTolerance = Scale(EndpointTolerance),
                HingeTolerance = Scale(HingeTolerance),
                MinSwingRadius = Scale(MinSwingRadius),
                MaxSwingRadius = Scale(MaxSwingRadius),
                MinFaceLength = Scale(MinFaceLength),
                MinColumnSide = Scale(MinColumnSide),
                MaxColumnSide = Scale(MaxColumnSide)
            };
        }
    }
}
