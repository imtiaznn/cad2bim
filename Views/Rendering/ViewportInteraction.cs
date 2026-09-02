using Cad2Bim.Services;
using Cad2Bim.ViewModels;

namespace Cad2Bim.Views.Rendering {
    /// <summary>
    /// The manual tools' half of the viewport's mouse handling: picking under the cursor and
    /// sweeping brush strokes into the model. Works entirely in CAD coordinates; the viewport
    /// converts from screen space and decides which gestures reach it.
    /// </summary>
    internal sealed class ViewportInteraction {
        public DrawingModel? Model { get; set; }
        public ManualToolViewModel? Tool { get; set; }

        private IDisposable? _stroke;
        private Point? _lastPaint;

        public bool IsPainting => _stroke is not null;

        /// <summary>Whether a left-press should paint rather than pan.</summary>
        public bool ToolArmed => Model is not null
                              && Tool is not null
                              && Tool.ActiveTool != ManualToolKind.None;

        /// <summary>Id of the pickable primitive under the point, for the hover highlight.</summary>
        public int? Pick(Point cadPoint, double toleranceCad) =>
            Model?.Grid.NearestWithin(cadPoint, toleranceCad);

        public void BeginStroke(Point cadPoint, double radiusCad) {
            if (!ToolArmed || _stroke is not null) return;

            // One stroke = one edit scope = one undo entry; each dab still raises its own
            // change event so painting shows up live.
            _stroke = Model!.BeginEditScope();
            _lastPaint = cadPoint;
            Paint(cadPoint, cadPoint, radiusCad);
        }

        public void ContinueStroke(Point cadPoint, double radiusCad) {
            if (_stroke is null || _lastPaint is null) return;

            Paint(_lastPaint, cadPoint, radiusCad);
            _lastPaint = cadPoint;
        }

        public void EndStroke() {
            _stroke?.Dispose();
            _stroke = null;
            _lastPaint = null;
        }

        private void Paint(Point from, Point to, double radiusCad) {
            List<int> ids = Model!.Grid.IntersectingCapsule(from, to, radiusCad).ToList();
            if (ids.Count > 0) {
                Model.SetClasses(ids, Tool!.PaintClass, ChangeSource.Manual);
            }
        }
    }
}
