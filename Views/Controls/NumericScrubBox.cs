using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Cad2Bim.Views.Controls {
    using Point = System.Windows.Point; // Cad2Bim.Point (model record) would shadow it here

    /// <summary>
    /// Numeric field that supports horizontal click-and-drag scrubbing and, on a plain
    /// click, typed entry. Esc cancels either mode; Enter or focus loss commits typing.
    /// </summary>
    [TemplatePart(Name = ValueTextPartName, Type = typeof(TextBlock))]
    [TemplatePart(Name = EditorPartName, Type = typeof(TextBox))]
    public sealed class NumericScrubBox : Control {
        public const string DisplayPartName = "PART_Display";
        public const string ValueTextPartName = "PART_ValueText";
        public const string EditorPartName = "PART_Editor";

        private const double DragThresholdPx = 3.0;

        // How far inside the window edge the pointer is put back down after a wrap.
        private const int WrapInsetPx = 8;

        // Dependency Properties
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(NumericScrubBox),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, _) => ((NumericScrubBox)d).UpdateValueText(),
                (d, value) => ((NumericScrubBox)d).Clamp((double)value)));

        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(double), typeof(NumericScrubBox),
            new PropertyMetadata(double.MinValue, OnRangeChanged));

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(double), typeof(NumericScrubBox),
            new PropertyMetadata(double.MaxValue, OnRangeChanged));

        public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
            nameof(Step), typeof(double), typeof(NumericScrubBox),
            new PropertyMetadata(1.0));

        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
            nameof(Label), typeof(string), typeof(NumericScrubBox),
            new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
            nameof(Format), typeof(string), typeof(NumericScrubBox),
            new PropertyMetadata("0.##", (d, _) => ((NumericScrubBox)d).UpdateValueText()));

        // 0..1 position of Value inside [Minimum, Maximum]. The template scales the fill
        // bar by this, so no width converter and no layout pass per drag frame.
        private static readonly DependencyPropertyKey FillFractionKey = DependencyProperty.RegisterReadOnly(
            nameof(FillFraction), typeof(double), typeof(NumericScrubBox),
            new PropertyMetadata(0.0));

        public static readonly DependencyProperty FillFractionProperty = FillFractionKey.DependencyProperty;

        private TextBlock? valueText;
        private TextBox? editor;

        private bool mouseDown;
        private bool dragging;
        private double dragStartValue;

        // Scrubbing accumulates per-move deltas rather than measuring from the press point,
        // because wrapping the pointer at the window edge moves that origin out from under it.
        private Point lastScreenPos;
        private double draggedDips;

        static NumericScrubBox() {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(NumericScrubBox),
                new FrameworkPropertyMetadata(typeof(NumericScrubBox)));
        }

        public double Value {
            get => (double)this.GetValue(ValueProperty);
            set => this.SetValue(ValueProperty, value);
        }

        public double Minimum {
            get => (double)this.GetValue(MinimumProperty);
            set => this.SetValue(MinimumProperty, value);
        }

        public double Maximum {
            get => (double)this.GetValue(MaximumProperty);
            set => this.SetValue(MaximumProperty, value);
        }

        /// <summary>Value change per pixel of horizontal drag.</summary>
        public double Step {
            get => (double)this.GetValue(StepProperty);
            set => this.SetValue(StepProperty, value);
        }

        public string Label {
            get => (string)this.GetValue(LabelProperty);
            set => this.SetValue(LabelProperty, value);
        }

        public string Format {
            get => (string)this.GetValue(FormatProperty);
            set => this.SetValue(FormatProperty, value);
        }

        /// <summary>Value's position in its range, 0..1; 0 when the range is unbounded.</summary>
        public double FillFraction => (double)this.GetValue(FillFractionProperty);

        private bool IsEditing => this.editor?.Visibility == Visibility.Visible;

        public override void OnApplyTemplate() {
            base.OnApplyTemplate();

            if (this.editor != null) {
                this.editor.KeyDown -= this.OnEditorKeyDown;
                this.editor.LostKeyboardFocus -= this.OnEditorLostFocus;
            }

            this.valueText = this.GetTemplateChild(ValueTextPartName) as TextBlock;
            this.editor = this.GetTemplateChild(EditorPartName) as TextBox;

            if (this.editor != null) {
                this.editor.KeyDown += this.OnEditorKeyDown;
                this.editor.LostKeyboardFocus += this.OnEditorLostFocus;
            }

            this.UpdateValueText();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {
            if (this.IsEditing) {
                base.OnMouseLeftButtonDown(e);
                return;
            }

            this.mouseDown = true;
            this.dragging = false;
            this.draggedDips = 0.0;
            this.lastScreenPos = this.PointToScreen(e.GetPosition(this));
            this.dragStartValue = this.Value;
            this.CaptureMouse();
            this.Focus();
            e.Handled = true;

            base.OnMouseLeftButtonDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            if (this.mouseDown) {
                Point screenPos = this.PointToScreen(e.GetPosition(this));
                double scale = this.DeviceScaleX();

                this.draggedDips += (screenPos.X - this.lastScreenPos.X) / scale;
                this.lastScreenPos = screenPos;

                if (!this.dragging && Math.Abs(this.draggedDips) >= DragThresholdPx) {
                    this.dragging = true;
                }

                if (this.dragging) {
                    this.Value = this.Clamp(this.dragStartValue + (this.draggedDips * this.Step));

                    // Endless scrubbing: run the pointer out one side of the window and it
                    // reappears on the other, so a long drag never dead-ends at the edge.
                    if (this.TryWrapPointer(ref screenPos)) {
                        this.lastScreenPos = screenPos;
                    }
                }
            }

            base.OnMouseMove(e);
        }

        /// <summary>
        /// Teleports the pointer to the opposite edge when it leaves the window's bounds.
        /// Returns true (with <paramref name="screenPos"/> updated) when it moved it.
        /// </summary>
        private bool TryWrapPointer(ref Point screenPos) {
            Window? window = Window.GetWindow(this);
            if (window == null || window.ActualWidth <= 0 || window.ActualHeight <= 0) {
                return false;
            }

            Point topLeft = window.PointToScreen(new Point(0, 0));
            Point bottomRight = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));

            double left = Math.Min(topLeft.X, bottomRight.X);
            double right = Math.Max(topLeft.X, bottomRight.X);
            double top = Math.Min(topLeft.Y, bottomRight.Y);
            double bottom = Math.Max(topLeft.Y, bottomRight.Y);

            // A window narrower than two insets would make the wrap oscillate.
            if (right - left < WrapInsetPx * 4 || bottom - top < WrapInsetPx * 4) {
                return false;
            }

            double x = screenPos.X;
            double y = screenPos.Y;
            bool wrapped = false;

            if (x <= left + WrapInsetPx) {
                x = right - WrapInsetPx * 2;
                wrapped = true;
            } else if (x >= right - WrapInsetPx) {
                x = left + WrapInsetPx * 2;
                wrapped = true;
            }

            if (y <= top + WrapInsetPx) {
                y = bottom - WrapInsetPx * 2;
                wrapped = true;
            } else if (y >= bottom - WrapInsetPx) {
                y = top + WrapInsetPx * 2;
                wrapped = true;
            }

            if (!wrapped) {
                return false;
            }

            // SetCursorPos works in physical pixels, which is what PointToScreen already returns.
            SetCursorPos((int)Math.Round(x), (int)Math.Round(y));
            screenPos = new Point(x, y);
            return true;
        }

        private double DeviceScaleX() {
            double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            return scale > 0 ? scale : 1.0;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int x, int y);

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) {
            if (this.mouseDown) {
                bool wasClick = !this.dragging;
                this.mouseDown = false;
                this.dragging = false;
                this.ReleaseMouseCapture();
                e.Handled = true;

                if (wasClick) {
                    this.BeginEdit();
                }
            }

            base.OnMouseLeftButtonUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            // Esc mid-drag restores the pre-drag value.
            if (e.Key == Key.Escape && this.mouseDown) {
                this.Value = this.dragStartValue;
                this.mouseDown = false;
                this.dragging = false;
                this.ReleaseMouseCapture();
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var box = (NumericScrubBox)d;
            box.CoerceValue(ValueProperty);
            box.UpdateFillFraction();
        }

        private void BeginEdit() {
            if (this.editor == null) {
                return;
            }

            this.editor.Text = this.Value.ToString(CultureInfo.CurrentCulture);
            this.editor.Visibility = Visibility.Visible;
            this.editor.Focus();
            this.editor.SelectAll();
        }

        private void EndEdit(bool commit) {
            if (this.editor == null || !this.IsEditing) {
                return;
            }

            if (commit && TryParse(this.editor.Text, out double parsed)) {
                this.Value = this.Clamp(parsed);
            }

            this.editor.Visibility = Visibility.Collapsed;
        }

        private void OnEditorKeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Enter) {
                this.EndEdit(commit: true);
                e.Handled = true;
            } else if (e.Key == Key.Escape) {
                this.EndEdit(commit: false);
                e.Handled = true;
            }
        }

        private void OnEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e)
            => this.EndEdit(commit: true);

        private static bool TryParse(string text, out double value)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private double Clamp(double value)
            => Math.Clamp(value, this.Minimum, this.Maximum);

        private void UpdateValueText() {
            if (this.valueText != null) {
                this.valueText.Text = this.Value.ToString(this.Format, CultureInfo.CurrentCulture);
            }

            this.UpdateFillFraction();
        }

        private void UpdateFillFraction() {
            double range = this.Maximum - this.Minimum;

            // The unset defaults are +/-double.Max, so an unbounded field simply shows no fill.
            double fraction = double.IsFinite(range) && range > 0
                ? Math.Clamp((this.Value - this.Minimum) / range, 0.0, 1.0)
                : 0.0;

            this.SetValue(FillFractionKey, fraction);
        }
    }
}
