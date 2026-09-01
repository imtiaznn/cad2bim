namespace Cad2Bim.ViewModels.Shapes {
    // Angles are radians, CCW from +X, matching the model's Arc. They matter: a door swing is a
    // quarter circle, and drawing it as a whole one - which is all this could express before -
    // buries the wall it belongs to under a full circle of stroke.
    public record ArcShape(double Cx, double Cy, double Radius, double StartAngle, double EndAngle);
}
