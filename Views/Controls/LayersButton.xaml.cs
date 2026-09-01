using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Cad2Bim.Views.Controls {
    /// <summary>
    /// Ribbon-style square button that drops a layer list underneath itself.
    /// Knows nothing about the view model beyond each item exposing Name/IsVisible,
    /// so any toggleable collection can be handed to <see cref="ItemsSource"/>.
    /// </summary>
    public partial class LayersButton : UserControl {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(LayersButton),
                new PropertyMetadata(null));

        public IEnumerable? ItemsSource {
            get => (IEnumerable?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        // A StaysOpen="False" popup swallows the click that closed it, so the button
        // would immediately re-check and reopen. Ignore a press that lands right after
        // a close and the second click reads as "shut the panel", as it does in Word.
        private int _closedAtTicks;

        public LayersButton() => InitializeComponent();

        private void OnPopupClosed(object? sender, System.EventArgs e) =>
            _closedAtTicks = System.Environment.TickCount;

        private void OnTogglePreviewMouseDown(object sender, MouseButtonEventArgs e) {
            if (System.Environment.TickCount - _closedAtTicks < 250) e.Handled = true;
        }
    }
}
