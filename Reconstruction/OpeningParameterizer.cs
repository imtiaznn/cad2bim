using Cad2Bim.Bim;
using Cad2Bim.Classification;

namespace Cad2Bim.Reconstruction {
    /// <summary>
    /// Stages 2 and 3: turn a classified <see cref="Opening"/> into a placeable BIM element.
    /// Doors follow the paper's line–arc reading — the swing arc's centre is the hinge, its radius
    /// the leaf; windows take their position and width straight from the span the classifier
    /// grouped on the run (the paper's representative-line step, already done upstream).
    /// </summary>
    public static class OpeningParameterizer {
        public static BimDoor Door(Opening opening, WallRun run, BimConversionOptions options,
                                   double millimetersPerUnit) {
            var common = Common(opening, run, millimetersPerUnit);

            Arc? swing = opening.SwingArc;
            double leafWidthMm = (swing?.Radius ?? opening.Width) * millimetersPerUnit;

            bool hingeAtStart = true;
            bool swingsPositive = true;
            if (swing is not null) {
                double hingeAlong = run.AxisParam(swing.Center);
                hingeAtStart = Math.Abs(hingeAlong - opening.AxisSpan.Start)
                             <= Math.Abs(hingeAlong - opening.AxisSpan.End);
                swingsPositive = run.NormalParam(swing.MidPoint) > 0;
            }

            return new BimDoor {
                Kind = BimOpeningKind.Door,
                FootprintRect = common.Rect,
                WidthMm = common.WidthMm,
                SillMm = 0,
                HeadMm = options.DoorHeightMm,
                Center = common.Center,
                WallHeadingDeg = common.HeadingDeg,
                LeafWidthMm = Math.Min(leafWidthMm, common.WidthMm),
                HingeAtStart = hingeAtStart,
                SwingsPositiveNormal = swingsPositive
            };
        }

        public static BimWindow Window(Opening opening, WallRun run, BimConversionOptions options,
                                       double millimetersPerUnit) {
            var common = Common(opening, run, millimetersPerUnit);
            return new BimWindow {
                Kind = BimOpeningKind.Window,
                FootprintRect = common.Rect,
                WidthMm = common.WidthMm,
                SillMm = options.WindowSillMm,
                HeadMm = options.WindowHeadMm,
                Center = common.Center,
                WallHeadingDeg = common.HeadingDeg
            };
        }

        /// <summary>An opening the classifier could not name: cut it door-height so the wall is
        /// honestly punctured, but place no filling element.</summary>
        public static BimOpening Unknown(Opening opening, WallRun run, BimConversionOptions options,
                                         double millimetersPerUnit) {
            var common = Common(opening, run, millimetersPerUnit);
            return new BimOpening {
                Kind = BimOpeningKind.Unknown,
                FootprintRect = common.Rect,
                WidthMm = common.WidthMm,
                SillMm = 0,
                HeadMm = options.DoorHeightMm,
                Center = common.Center,
                WallHeadingDeg = common.HeadingDeg
            };
        }

        private static (IReadOnlyList<BimPoint> Rect, double WidthMm, BimPoint Center, double HeadingDeg)
            Common(Opening opening, WallRun run, double millimetersPerUnit) {
            List<BimPoint> rect = opening.Rectangle
                .Select(p => new BimPoint(p.x * millimetersPerUnit, p.y * millimetersPerUnit))
                .ToList();

            Point centre = opening.Center;
            var (dx, dy) = run.Reference.Axis;
            return (rect,
                    opening.Width * millimetersPerUnit,
                    new BimPoint(centre.x * millimetersPerUnit, centre.y * millimetersPerUnit),
                    Math.Atan2(dy, dx) * 180.0 / Math.PI);
        }
    }
}
