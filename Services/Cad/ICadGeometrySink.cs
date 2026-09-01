namespace Cad2Bim.Services.Cad {
    /// <summary>
    /// Receives what <see cref="CadEntityWalker"/> finds, in world coordinates with every block
    /// already flattened.
    /// <para>
    /// The walker offers each entity in two views, because the viewport and the classifier want
    /// incompatible things from the same drawing. The viewport wants one ready-to-stroke run of
    /// straight lines per entity (<see cref="Polyline"/>); the classifier wants the entity's exact
    /// primitives — and above all it wants an arc to still be an arc, since a door swing is the
    /// single strongest door cue and tessellation destroys it.
    /// </para>
    /// <para>
    /// A sink declares which views it wants via <see cref="WantsStrokes"/> and
    /// <see cref="WantsPrimitives"/>, and the walker skips the work for the ones it does not.
    /// </para>
    /// </summary>
    internal interface ICadGeometrySink {
        /// <summary>Whether <see cref="Polyline"/> is worth calling.</summary>
        bool WantsStrokes { get; }

        /// <summary>Whether <see cref="Line"/>, <see cref="Arc"/> and <see cref="Text"/> are worth calling.</summary>
        bool WantsPrimitives { get; }

        /// <summary>Stroke view: one entity, curves already tessellated.</summary>
        void Polyline(IReadOnlyList<(double X, double Y)> points, bool isClosed);

        /// <summary>Primitive view: one straight span.</summary>
        void Line(double x1, double y1, double x2, double y2);

        /// <summary>
        /// Primitive view: one true arc, angles in radians CCW from +X. A full circle arrives as
        /// a 2π sweep. Emitted only when the block transform is a similarity — under a
        /// non-uniform scale the shape is really an ellipse, and the walker falls back to
        /// <see cref="Line"/> spans instead.
        /// </summary>
        void Arc(double centerX, double centerY, double radius, double startAngle, double endAngle);

        /// <summary>Primitive view: one text insertion, for space naming later.</summary>
        void Text(double x, double y, double height, string value);
    }
}
