namespace Cad2Bim.Services.Cad {
    /// <summary>
    /// A 2D affine map, block space to world space: (x, y) -> (Ax + Cy + E, Bx + Dy + F).
    /// Block contents are mapped through this rather than through ACadSharp's
    /// Insert.Explode()/Entity.ApplyTransform(), which drops the translation on
    /// mirrored inserts (negative scale) for LwPolyline, Ellipse and nested Insert.
    /// </summary>
    internal readonly record struct Xform(double A, double B, double C, double D, double E, double F) {
        // A circle only survives as a circle under a similarity transform. Anything beyond this
        // much anisotropy or shear is really an ellipse, and the arc parameters are then a lie.
        private const double SimilarityTolerance = 1e-3;

        public static readonly Xform Identity = new(1, 0, 0, 1, 0, 0);

        public (double X, double Y) Apply(double x, double y) =>
            ((A * x) + (C * y) + E, (B * x) + (D * y) + F);

        // this ∘ inner: inner runs first, so inner maps into this one's input space.
        public Xform Compose(Xform inner) => new(
            (A * inner.A) + (C * inner.B),
            (B * inner.A) + (D * inner.B),
            (A * inner.C) + (C * inner.D),
            (B * inner.C) + (D * inner.D),
            (A * inner.E) + (C * inner.F) + E,
            (B * inner.E) + (D * inner.F) + F);

        /// <summary>Length the x and y basis vectors are scaled by.</summary>
        public double ScaleX => Math.Sqrt((A * A) + (B * B));
        public double ScaleY => Math.Sqrt((C * C) + (D * D));

        /// <summary>Negative once the map mirrors, which reverses the sense of every angle.</summary>
        public double Determinant => (A * D) - (B * C);

        /// <summary>
        /// True when the map is a rotation, a uniform scale and possibly a mirror — the only case
        /// in which a transformed circle is still a circle, and so the only case in which an arc's
        /// centre/radius/angles survive the mapping.
        /// </summary>
        public bool IsSimilarity {
            get {
                double sx = ScaleX;
                double sy = ScaleY;
                double largest = Math.Max(sx, sy);

                if (largest < 1e-12) return false;
                if (Math.Abs(sx - sy) / largest > SimilarityTolerance) return false;

                // Shear check: the basis vectors must stay perpendicular.
                double dot = (A * C) + (B * D);
                return Math.Abs(dot) / (largest * largest) <= SimilarityTolerance;
            }
        }

        /// <summary>
        /// Where an angle measured about an arc's centre ends up. A point at angle θ sits at
        /// centre + r·(cos θ, sin θ), so its image direction is the basis vectors combined by the
        /// same weights — the translation drops out.
        /// </summary>
        public double TransformAngle(double radians) {
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            return Math.Atan2((B * cos) + (D * sin), (A * cos) + (C * sin));
        }
    }
}
