using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Cad2Bim.Services;

namespace Cad2Bim.Views.Rendering {
    using Cad2Bim.ViewModels;
    using Point = System.Windows.Point; // Cad2Bim.Point (model record) would shadow it here
    using CadPoint = Cad2Bim.Point;

    /// <summary>
    /// Draws the <see cref="DrawingModel"/> as one DrawingVisual per classification bucket, all
    /// sharing a single CAD-to-screen MatrixTransform (Y-flip + pan + zoom). Pan/zoom only mutate
    /// the matrix; buckets re-render when their classifications change (dirty chunks only, via
    /// <see cref="BucketGeometryCache"/>) or after a zoom settles (to keep pen widths
    /// screen-constant). Also hosts the manual brush/eraser interaction.
    /// </summary>
    public sealed class CadViewport : FrameworkElement {
        private const double MinScale = 1e-6;
        private const double MaxScale = 1e6;
        private const double BaseWidthPx = 1.0;
        private const double HighlightWidthPx = 3.0;
        private const double HoverWidthPx = 3.0;
        private const double PickTolerancePx = 6.0;
        private const double BrushRadiusPx = 10.0;
        private const byte HighlightAlpha = 0xB0;

        private static readonly SolidColorBrush BaseStroke = Frozen(Color.FromRgb(0x88, 0x88, 0x88));
        private static readonly SolidColorBrush HoverStroke = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF));

        // Same highlight palette as before the rework: walls cyan, doors orchid, windows green.
        private static readonly Dictionary<PrimitiveClass, Color> BucketColors = new() {
            [PrimitiveClass.Wall] = Color.FromRgb(0x00, 0xFF, 0xFF),
            [PrimitiveClass.Door] = Color.FromRgb(0xDA, 0x70, 0xD6),
            [PrimitiveClass.Window] = Color.FromRgb(0x7C, 0xFC, 0x00),
        };

        // Composition order, bottom to top: base drawing first, classified colour on top of it.
        private static readonly PrimitiveClass[] BucketOrder = [
            PrimitiveClass.Annotation, PrimitiveClass.Unclassified,
            PrimitiveClass.Wall, PrimitiveClass.Window, PrimitiveClass.Door,
        ];

        public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
            nameof(Model), typeof(DrawingModel), typeof(CadViewport),
            new PropertyMetadata(null, (d, e) => ((CadViewport)d).OnModelChanged(e)));

        public static readonly DependencyProperty LayersSourceProperty = DependencyProperty.Register(
            nameof(LayersSource), typeof(IEnumerable<LayerViewModel>), typeof(CadViewport),
            new PropertyMetadata(null, (d, e) => ((CadViewport)d).OnLayersSourceChanged(e)));

        public static readonly DependencyProperty ContentBoundsProperty = DependencyProperty.Register(
            nameof(ContentBounds), typeof(Rect), typeof(CadViewport),
            new PropertyMetadata(Rect.Empty, (d, _) => ((CadViewport)d).OnContentBoundsChanged()));

        public static readonly DependencyProperty ToolProperty = DependencyProperty.Register(
            nameof(Tool), typeof(ManualToolViewModel), typeof(CadViewport),
            new PropertyMetadata(null, (d, e) => ((CadViewport)d).OnToolChanged(e)));

        private readonly VisualCollection visuals;
        private readonly Dictionary<PrimitiveClass, DrawingVisual> bucketVisuals = new();
        private readonly DrawingVisual interactionVisual;
        private readonly MatrixTransform transform = new(Matrix.Identity);
        private readonly DispatcherTimer zoomSettleTimer;
        private readonly ViewportInteraction interaction = new();

        private BucketGeometryCache? cache;
        private bool fitted;
        private bool panning;
        private Point lastMousePosition;
        private int? hoveredId;

        public CadViewport() {
            visuals = new VisualCollection(this);

            foreach (PrimitiveClass bucket in BucketOrder) {
                var visual = new DrawingVisual { Transform = transform };
                bucketVisuals[bucket] = visual;
                visuals.Add(visual);
            }

            interactionVisual = new DrawingVisual { Transform = transform };
            visuals.Add(interactionVisual);

            zoomSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            zoomSettleTimer.Tick += (_, _) => {
                zoomSettleTimer.Stop();
                RenderAllBuckets();
            };

            ClipToBounds = true;
            SizeChanged += OnSizeChanged;
        }

        public DrawingModel? Model {
            get => (DrawingModel?)GetValue(ModelProperty);
            set => SetValue(ModelProperty, value);
        }

        public IEnumerable<LayerViewModel>? LayersSource {
            get => (IEnumerable<LayerViewModel>?)GetValue(LayersSourceProperty);
            set => SetValue(LayersSourceProperty, value);
        }

        public Rect ContentBounds {
            get => (Rect)GetValue(ContentBoundsProperty);
            set => SetValue(ContentBoundsProperty, value);
        }

        public ManualToolViewModel? Tool {
            get => (ManualToolViewModel?)GetValue(ToolProperty);
            set => SetValue(ToolProperty, value);
        }

        private double Scale => transform.Matrix.M11;

        protected override int VisualChildrenCount => visuals.Count;

        protected override Visual GetVisualChild(int index) => visuals[index];

        protected override void OnRender(DrawingContext dc) {
            // Opaque backdrop doubles as the mouse hit-test surface.
            dc.DrawRectangle(Brushes.Black, null, new Rect(RenderSize));
        }

        // --- Dependency property plumbing ---------------------------------------------------

        private void OnModelChanged(DependencyPropertyChangedEventArgs e) {
            if (e.OldValue is DrawingModel oldModel) {
                oldModel.ClassificationChanged -= OnClassificationChanged;
            }

            if (e.NewValue is DrawingModel newModel) {
                newModel.ClassificationChanged += OnClassificationChanged;
                cache = new BucketGeometryCache(newModel);
            } else {
                cache = null;
            }

            interaction.Model = e.NewValue as DrawingModel;
            hoveredId = null;
            RenderHover();
            RenderAllBuckets();
        }

        private void OnClassificationChanged(ClassificationDelta delta) {
            cache?.Invalidate(delta.Ids);

            // Only dirty chunks rebuild; re-issuing the cached frozen geometries is cheap.
            foreach (PrimitiveClass bucket in BucketOrder) {
                if (bucket != PrimitiveClass.Annotation) {
                    RenderBucket(bucket);
                }
            }
        }

        private void OnLayersSourceChanged(DependencyPropertyChangedEventArgs e) {
            if (e.OldValue is IEnumerable<LayerViewModel> oldLayers) {
                foreach (LayerViewModel layer in oldLayers) layer.PropertyChanged -= OnLayerPropertyChanged;
                if (oldLayers is INotifyCollectionChanged oldIncc) oldIncc.CollectionChanged -= OnLayersCollectionChanged;
            }

            if (e.NewValue is IEnumerable<LayerViewModel> newLayers) {
                foreach (LayerViewModel layer in newLayers) layer.PropertyChanged += OnLayerPropertyChanged;
                if (newLayers is INotifyCollectionChanged newIncc) newIncc.CollectionChanged += OnLayersCollectionChanged;
            }

            ApplyLayerVisibility();
        }

        private void OnLayersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            foreach (LayerViewModel layer in LayersSource ?? []) {
                layer.PropertyChanged -= OnLayerPropertyChanged;
                layer.PropertyChanged += OnLayerPropertyChanged;
            }
            ApplyLayerVisibility();
        }

        private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(LayerViewModel.IsVisible)) {
                ApplyLayerVisibility();
            }
        }

        private void ApplyLayerVisibility() {
            foreach (LayerViewModel layer in LayersSource ?? []) {
                if (bucketVisuals.TryGetValue(layer.Bucket, out DrawingVisual? visual)) {
                    visual.Opacity = layer.IsVisible ? 1.0 : 0.0;
                }
            }
        }

        private void OnToolChanged(DependencyPropertyChangedEventArgs e) {
            if (e.OldValue is ManualToolViewModel oldTool) oldTool.PropertyChanged -= OnToolPropertyChanged;
            if (e.NewValue is ManualToolViewModel newTool) newTool.PropertyChanged += OnToolPropertyChanged;

            interaction.Tool = e.NewValue as ManualToolViewModel;
            UpdateCursor();
        }

        private void OnToolPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName != nameof(ManualToolViewModel.ActiveTool)) return;

            if (!interaction.ToolArmed) {
                interaction.EndStroke();
                hoveredId = null;
                RenderHover();
            }
            UpdateCursor();
        }

        private void UpdateCursor() => Cursor = Tool?.ActiveTool switch {
            ManualToolKind.Brush => Cursors.Pen,
            ManualToolKind.Eraser => Cursors.Cross,
            _ => null,
        };

        private void OnContentBoundsChanged() {
            // New document: refit next chance we have a size.
            fitted = false;
            if (ActualWidth > 0 && ActualHeight > 0) {
                FitToExtents();
                RenderAllBuckets();
                fitted = true;
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e) {
            if (!fitted && cache is not null) {
                FitToExtents();
                RenderAllBuckets();
                fitted = true;
            }
        }

        private void FitToExtents() {
            Rect bounds = ContentBounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 || ActualWidth <= 0 || ActualHeight <= 0) {
                return;
            }

            double scale = 0.95 * Math.Min(ActualWidth / bounds.Width, ActualHeight / bounds.Height);
            scale = Math.Clamp(scale, MinScale, MaxScale);

            double centerX = bounds.X + (bounds.Width / 2.0);
            double centerY = bounds.Y + (bounds.Height / 2.0);

            // CAD (x, y) -> screen (s*x + tx, -s*y + ty), content center at viewport center.
            transform.Matrix = new Matrix(
                scale, 0, 0, -scale,
                (ActualWidth / 2.0) - (scale * centerX),
                (ActualHeight / 2.0) + (scale * centerY));
        }

        // --- Mouse: middle always pans; left pans until a tool is armed, then it paints -------

        protected override void OnMouseDown(MouseButtonEventArgs e) {
            if (e.ChangedButton == MouseButton.Left && interaction.ToolArmed) {
                hoveredId = null;
                RenderHover();
                interaction.BeginStroke(ToCad(e.GetPosition(this)), BrushRadiusPx / Math.Max(Scale, MinScale));
                CaptureMouse();
            } else if (e.ChangedButton is MouseButton.Left or MouseButton.Middle) {
                panning = true;
                lastMousePosition = e.GetPosition(this);
                CaptureMouse();
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            if (interaction.IsPainting) {
                interaction.ContinueStroke(ToCad(e.GetPosition(this)), BrushRadiusPx / Math.Max(Scale, MinScale));
            } else if (panning) {
                Point position = e.GetPosition(this);
                Matrix matrix = transform.Matrix;
                matrix.Translate(position.X - lastMousePosition.X, position.Y - lastMousePosition.Y);
                transform.Matrix = matrix;
                lastMousePosition = position;
            } else if (interaction.ToolArmed) {
                int? hit = interaction.Pick(ToCad(e.GetPosition(this)), PickTolerancePx / Math.Max(Scale, MinScale));
                if (hit != hoveredId) {
                    hoveredId = hit;
                    RenderHover();
                }
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e) {
            if (interaction.IsPainting && e.ChangedButton == MouseButton.Left) {
                interaction.EndStroke();
                ReleaseMouseCapture();
            } else if (panning && e.ChangedButton is MouseButton.Left or MouseButton.Middle) {
                panning = false;
                ReleaseMouseCapture();
            }

            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(MouseEventArgs e) {
            if (hoveredId is not null) {
                hoveredId = null;
                RenderHover();
            }

            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e) {
            double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
            double newScale = Math.Clamp(Scale * factor, MinScale, MaxScale);
            factor = newScale / Scale;

            Point position = e.GetPosition(this);
            Matrix matrix = transform.Matrix;
            matrix.ScaleAt(factor, factor, position.X, position.Y);
            transform.Matrix = matrix;

            // Re-render once zooming settles so pen widths stay screen-constant.
            zoomSettleTimer.Stop();
            zoomSettleTimer.Start();

            base.OnMouseWheel(e);
        }

        private CadPoint ToCad(Point screen) {
            Matrix matrix = transform.Matrix;
            matrix.Invert();
            Point cad = matrix.Transform(screen);
            return new CadPoint(cad.X, cad.Y);
        }

        // --- Rendering ----------------------------------------------------------------------

        private void RenderAllBuckets() {
            foreach (PrimitiveClass bucket in BucketOrder) {
                RenderBucket(bucket);
            }
        }

        private void RenderBucket(PrimitiveClass bucket) {
            DrawingVisual visual = bucketVisuals[bucket];
            using DrawingContext dc = visual.RenderOpen();

            if (cache is null) {
                return;
            }

            double scale = Math.Max(Scale, MinScale);
            Pen pen = BucketColors.TryGetValue(bucket, out Color color)
                ? new Pen(Frozen(WithAlpha(color)), HighlightWidthPx / scale)
                : new Pen(BaseStroke, BaseWidthPx / scale);
            pen.Freeze();

            for (int chunk = 0; chunk < cache.ChunkCount; chunk++) {
                if (cache.Get(bucket, chunk) is Geometry geometry) {
                    dc.DrawGeometry(null, pen, geometry);
                }
            }
        }

        private void RenderHover() {
            using DrawingContext dc = interactionVisual.RenderOpen();

            if (hoveredId is not int id || Model is not DrawingModel model) {
                return;
            }

            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open()) {
                BucketGeometryCache.Append(ctx, model.Primitives[id].Geometry);
            }
            geometry.Freeze();

            var pen = new Pen(HoverStroke, HoverWidthPx / Math.Max(Scale, MinScale));
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        private static Color WithAlpha(Color color) => Color.FromArgb(HighlightAlpha, color.R, color.G, color.B);

        private static SolidColorBrush Frozen(Color color) {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
