namespace Cad2Bim.ViewModels.Shapes {
    // A classified opening: the paper's "rectangle included in a wall", plus whatever identified
    // it - the swing arc and door leaf for a door. Drawing the evidence alongside the box is what
    // lets a wrong call be recognised as wrong at a glance.
    public record OpeningShape(
        IReadOnlyList<(double X, double Y)> Rectangle,
        SegmentShape Threshold,
        ArcShape? Swing,
        SegmentShape? Leaf);
}
