using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Cad2Bim.Views.Controls {
    /// <summary>
    /// Ribbon-style square button that drops the Automatic/Manual segmentation menu underneath
    /// itself. Knows nothing about the view model beyond the two commands it is handed.
    /// </summary>
    public partial class SegmentButton : UserControl {
        public static readonly DependencyProperty AutomaticCommandProperty =
            DependencyProperty.Register(nameof(AutomaticCommand), typeof(ICommand), typeof(SegmentButton),
                new PropertyMetadata(null));

        public static readonly DependencyProperty ManualCommandProperty =
            DependencyProperty.Register(nameof(ManualCommand), typeof(ICommand), typeof(SegmentButton),
                new PropertyMetadata(null));

        public ICommand? AutomaticCommand {
            get => (ICommand?)GetValue(AutomaticCommandProperty);
            set => SetValue(AutomaticCommandProperty, value);
        }

        public ICommand? ManualCommand {
            get => (ICommand?)GetValue(ManualCommandProperty);
            set => SetValue(ManualCommandProperty, value);
        }

        // A StaysOpen="False" popup swallows the click that closed it, so the button
        // would immediately re-check and reopen. Ignore a press that lands right after
        // a close and the second click reads as "shut the panel", as it does in Word.
        private int _closedAtTicks;

        public SegmentButton() => InitializeComponent();

        private void OnPopupClosed(object? sender, System.EventArgs e) =>
            _closedAtTicks = System.Environment.TickCount;

        private void OnTogglePreviewMouseDown(object sender, MouseButtonEventArgs e) {
            if (System.Environment.TickCount - _closedAtTicks < 250) e.Handled = true;
        }

        private void OnItemClick(object sender, RoutedEventArgs e) =>
            PART_Toggle.IsChecked = false;
    }
}
