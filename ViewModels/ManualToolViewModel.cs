namespace Cad2Bim.ViewModels {
    /// <summary>Which manual segmentation tool is armed in the viewport.</summary>
    public enum ManualToolKind { None, Brush, Eraser }

    /// <summary>The layer the brush paints onto.</summary>
    public enum SegmentTarget { Walls, Doors, Windows }

    /// <summary>
    /// State of the manual segmentation panel: the armed tool and the brush's target layer.
    /// The viewport reads this to decide whether a left-drag pans or paints.
    /// </summary>
    public class ManualToolViewModel : ViewModelBase {
        private ManualToolKind _activeTool = ManualToolKind.None;
        public ManualToolKind ActiveTool {
            get => _activeTool;
            set {
                if (!SetField(ref _activeTool, value)) return;
                OnPropertyChanged(nameof(IsBrushActive));
                OnPropertyChanged(nameof(IsEraserActive));
            }
        }

        // ToggleButton facades, following the IsMillimeters/IsInches pattern in SettingsViewModel.
        // Unchecking the lit tool returns to None, so clicking it again disarms it.
        public bool IsBrushActive {
            get => _activeTool == ManualToolKind.Brush;
            set => ActiveTool = value ? ManualToolKind.Brush
                                      : (IsBrushActive ? ManualToolKind.None : _activeTool);
        }

        public bool IsEraserActive {
            get => _activeTool == ManualToolKind.Eraser;
            set => ActiveTool = value ? ManualToolKind.Eraser
                                      : (IsEraserActive ? ManualToolKind.None : _activeTool);
        }

        private SegmentTarget _targetLayer = SegmentTarget.Walls;
        public SegmentTarget TargetLayer {
            get => _targetLayer;
            set {
                if (SetField(ref _targetLayer, value)) OnPropertyChanged(nameof(TargetLayerIndex));
            }
        }

        /// <summary>Segmented-picker facade: 0 = walls, 1 = doors, 2 = windows.</summary>
        public int TargetLayerIndex {
            get => (int)_targetLayer;
            set => TargetLayer = (SegmentTarget)value;
        }

        /// <summary>The bucket the armed tool writes: eraser always clears, brush follows the target.</summary>
        public PrimitiveClass PaintClass => _activeTool == ManualToolKind.Eraser
            ? PrimitiveClass.Unclassified
            : _targetLayer switch {
                SegmentTarget.Doors => PrimitiveClass.Door,
                SegmentTarget.Windows => PrimitiveClass.Window,
                _ => PrimitiveClass.Wall
            };
    }
}
