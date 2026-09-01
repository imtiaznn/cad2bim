using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Cad2Bim.Services;
using Cad2Bim.ViewModels.Shapes;

namespace Cad2Bim.ViewModels {
    public class MainViewModel : ViewModelBase {
        private readonly ClassificationService _service;
        private readonly Func<string?> _pickFile;

        private readonly LayerViewModel _rawLayer = new("CAD Drawing (raw)");
        private readonly LayerViewModel _wallsLayer = new("Classified Walls", highlightIndex: 0);

        // Doors purple and windows green follow the paper's own legend (Fig. 7). The third layer
        // is the honest one: a hole in a wall that is neither drawn as glazing nor swung as a
        // door. Giving those their own colour means a miss shows up as a miss instead of being
        // quietly filed as a window.
        private readonly LayerViewModel _doorsLayer = new("Doors", highlightIndex: 2);
        private readonly LayerViewModel _windowsLayer = new("Windows", highlightIndex: 3);
        private readonly LayerViewModel _openingsLayer = new("Unclassified openings", highlightIndex: 1);

        public ObservableCollection<LayerViewModel> Layers { get; }
        public SettingsViewModel Settings { get; }
        public ICommand OpenFileCommand { get; }

        private Rect _bounds = Rect.Empty;
        public Rect Bounds { get => _bounds; private set => SetField(ref _bounds, value); }

        private string _statusText = "Open a CAD file to begin.";
        public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }

        public MainViewModel(ClassificationService service, Func<string?> pickFile) {
            _service = service;
            _pickFile = pickFile;

            Settings = new SettingsViewModel(Wall.DefaultSMinMillimeters, Wall.DefaultSMaxMillimeters);
            Settings.Changed += Reclassify;

            Layers = new ObservableCollection<LayerViewModel> {
                _rawLayer, _wallsLayer, _openingsLayer, _windowsLayer, _doorsLayer
            };

            OpenFileCommand = new RelayCommand(() => {
                var path = _pickFile();
                if (path is not null) LoadFile(path);
            });
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

            // The base layer is the CAD file itself — every drawable entity, blocks exploded,
            // curves tessellated. Geometry.cs/CadLoader feeds classification only, so what the
            // classifier keeps stays comparable against the ground truth drawn underneath it.
            var raw = CadRenderSource.Flatten(document);
            _rawLayer.Items = raw;
            Bounds = ComputeBounds(raw);

            try {
                _service.Load(document);
            }
            catch (Exception ex) {
                StatusText = $"Failed to classify '{path}': {ex.Message}";
                return;
            }

            Reclassify();
        }

        private void Reclassify() {
            if (!_service.HasData) return;

            ClassificationResult result;
            try {
                result = _service.ClassifyAll(Settings.SMinMillimeters, Settings.SMaxMillimeters,
                                              Settings.Tolerances);
            }
            catch (Exception ex) {
                StatusText = $"Classification failed: {ex.Message}";
                return;
            }

            _wallsLayer.Items = result.Walls.Select(w => {
                var edges = w.Geometry.OfType<Segment>().ToList();
                return (object)new WallShape(ToShape(edges[0]), ToShape(edges[1]));
            }).ToList();

            _doorsLayer.Items = ToShapes(result.Openings, OpeningKind.Door);
            _windowsLayer.Items = ToShapes(result.Openings, OpeningKind.Window);
            _openingsLayer.Items = ToShapes(result.Openings, OpeningKind.Unknown);

            string unit = Settings.UnitSuffix;
            StatusText = $"{_rawLayer.Items.Count} drawn, {_service.SegmentCount} segments, {result.Walls.Count} walls, "
                       + $"{_doorsLayer.Items.Count} doors, {_windowsLayer.Items.Count} windows, "
                       + $"{_openingsLayer.Items.Count} unclassified  "
                       + $"(SMin={Settings.SMin:0.###} {unit}, SMax={Settings.SMax:0.###} {unit}, drawing units: {DrawingUnits.Name(_service.Units)})";
        }

        private static SegmentShape ToShape(Segment s) =>
            new(s.P1.x, s.P1.y, s.P2.x, s.P2.y);

        private static List<object> ToShapes(IReadOnlyList<Opening> openings, OpeningKind kind) =>
            openings.Where(o => o.Kind == kind).Select(o => (object)new OpeningShape(
                o.Rectangle.Select(p => (p.x, p.y)).ToList(),
                ToShape(new Segment(o.Wall.FromAxis(o.AxisSpan.Start), o.Wall.FromAxis(o.AxisSpan.End))),
                o.SwingArc is null ? null
                    : new ArcShape(o.SwingArc.Center.x, o.SwingArc.Center.y, o.SwingArc.Radius,
                                   o.SwingArc.StartAngle, o.SwingArc.EndAngle),
                o.Leaf is null ? null : ToShape(o.Leaf))).ToList();

        // Extents of the drawn geometry, so a fit-to-extents shows exactly what is rendered.
        private static Rect ComputeBounds(IReadOnlyList<object> shapes) {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var shape in shapes.OfType<PolylineShape>()) {
                foreach (var (x, y) in shape.Points) {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            return minX > maxX ? Rect.Empty : new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }
}
