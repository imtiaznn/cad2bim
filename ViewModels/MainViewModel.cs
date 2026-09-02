using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Cad2Bim.Services;

namespace Cad2Bim.ViewModels {
    /// <summary>Which right-docked panel is open. Top-level so XAML can reference it via x:Static.</summary>
    public enum SegmentPanel { None, Automatic, Manual }

    public class MainViewModel : ViewModelBase {
        private readonly ClassificationService _service;
        private readonly Func<string?> _pickFile;

        public ObservableCollection<LayerViewModel> Layers { get; }
        public SettingsViewModel Settings { get; }
        public ManualToolViewModel ManualTool { get; } = new();

        public ICommand OpenFileCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ShowAutomaticPanelCommand { get; }
        public ICommand ShowManualPanelCommand { get; }
        public ICommand ClosePanelCommand { get; }
        public ICommand SegmentCommand { get; }
        public ICommand UndoCommand { get; }

        private DrawingModel? _model;
        public DrawingModel? Model { get => _model; private set => SetField(ref _model, value); }

        private string? _filePath;

        private SegmentPanel _activePanel = SegmentPanel.None;
        public SegmentPanel ActivePanel {
            get => _activePanel;
            set {
                if (!SetField(ref _activePanel, value)) return;
                // Never leave an invisible brush armed once its panel is gone.
                if (value != SegmentPanel.Manual) ManualTool.ActiveTool = ManualToolKind.None;
            }
        }

        private bool _segmentWalls = true;
        public bool SegmentWalls { get => _segmentWalls; set => SetField(ref _segmentWalls, value); }

        private bool _segmentDoors = true;
        public bool SegmentDoors { get => _segmentDoors; set => SetField(ref _segmentDoors, value); }

        private bool _segmentWindows = true;
        public bool SegmentWindows { get => _segmentWindows; set => SetField(ref _segmentWindows, value); }

        private Rect _bounds = Rect.Empty;
        public Rect Bounds { get => _bounds; private set => SetField(ref _bounds, value); }

        private string _statusText = "Open a CAD file to begin.";
        public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }

        public MainViewModel(ClassificationService service, Func<string?> pickFile) {
            _service = service;
            _pickFile = pickFile;

            // Settings changes no longer trigger anything by themselves — classification runs
            // only when the Automatic panel's Confirm button is pressed.
            Settings = new SettingsViewModel(Wall.DefaultSMinMillimeters, Wall.DefaultSMaxMillimeters);

            Layers = new ObservableCollection<LayerViewModel> {
                new("CAD Drawing (raw)", PrimitiveClass.Unclassified),
                new("Annotations", PrimitiveClass.Annotation),
                new("Walls", PrimitiveClass.Wall),
                new("Windows", PrimitiveClass.Window),
                new("Doors", PrimitiveClass.Door),
            };

            OpenFileCommand = new RelayCommand(() => {
                var path = _pickFile();
                if (path is not null) LoadFile(path);
            });

            SaveCommand = new RelayCommand(SaveSegmentation, () => Model is not null && _filePath is not null);

            ShowAutomaticPanelCommand = new RelayCommand(() => ActivePanel = SegmentPanel.Automatic);
            ShowManualPanelCommand = new RelayCommand(() => ActivePanel = SegmentPanel.Manual);
            ClosePanelCommand = new RelayCommand(() => ActivePanel = SegmentPanel.None);

            SegmentCommand = new RelayCommand(
                RunAutoSegmentation,
                () => _service.HasData && (SegmentWalls || SegmentDoors || SegmentWindows));

            UndoCommand = new RelayCommand(() => Model?.Undo(), () => Model?.CanUndo == true);
        }

        public void LoadFile(string path) {
            ACadSharp.CadDocument document;
            try {
                document = CadRenderSource.Read(path);
            }
            catch (Exception ex) {
                StatusText = $"Failed to load '{path}': {ex.Message}";
                return;
            }

            if (Model is not null) Model.ClassificationChanged -= OnClassificationChanged;

            // One walk of the file builds the whole store: every drawable entity, blocks
            // exploded, arcs kept as arcs. The classifier is fed the same instances, so its
            // results map straight back onto what is drawn.
            DrawingModel model = DrawingModel.Load(document);
            model.ClassificationChanged += OnClassificationChanged;
            Model = model;
            Bounds = model.Bounds;
            _filePath = path;

            _service.Load(model.AnalyzableGeometry(), model.Units);

            int restored = SegmentationStore.TryLoad(model, path);
            StatusText = $"{model.Primitives.Count} primitives, {_service.SegmentCount} segments "
                       + $"(drawing units: {DrawingUnits.Name(_service.Units)})"
                       + (restored > 0
                           ? $" — restored {restored} classified lines from {System.IO.Path.GetFileName(SegmentationStore.PathFor(path))}."
                           : ". Use Segment to classify.");
        }

        private void SaveSegmentation() {
            if (Model is null || _filePath is null) return;

            try {
                SegmentationStore.Save(Model, _filePath);
                StatusText = $"Segmentation saved to {System.IO.Path.GetFileName(SegmentationStore.PathFor(_filePath))}.";
            }
            catch (Exception ex) {
                StatusText = $"Save failed: {ex.Message}";
            }
        }

        private void RunAutoSegmentation() {
            if (Model is null || !_service.HasData) return;

            ClassificationResult result;
            try {
                result = _service.ClassifyAll(Settings.SMinMillimeters, Settings.SMaxMillimeters,
                                              Settings.Tolerances);
            }
            catch (Exception ex) {
                StatusText = $"Classification failed: {ex.Message}";
                return;
            }

            ClassificationTagger.Apply(result, Model,
                new AutoSegmentationOptions(SegmentWalls, SegmentDoors, SegmentWindows));

            string unit = Settings.UnitSuffix;
            StatusText = $"{result.Walls.Count} walls, "
                       + $"{result.Openings.Count(o => o.Kind == OpeningKind.Door)} doors, "
                       + $"{result.Openings.Count(o => o.Kind == OpeningKind.Window)} windows, "
                       + $"{result.Openings.Count(o => o.Kind == OpeningKind.Unknown)} unknown openings  "
                       + $"(SMin={Settings.SMin:0.###} {unit}, SMax={Settings.SMax:0.###} {unit}, "
                       + $"drawing units: {DrawingUnits.Name(_service.Units)})";
        }

        private void OnClassificationChanged(ClassificationDelta delta) {
            if (Model is null || delta.Source != ChangeSource.Manual) return;

            StatusText = $"{Model.IdsIn(PrimitiveClass.Wall).Count} wall, "
                       + $"{Model.IdsIn(PrimitiveClass.Door).Count} door, "
                       + $"{Model.IdsIn(PrimitiveClass.Window).Count} window lines "
                       + $"({Model.IdsIn(PrimitiveClass.Unclassified).Count} unclassified)";
        }
    }
}
