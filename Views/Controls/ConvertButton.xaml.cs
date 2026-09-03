using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Cad2Bim.Views.Controls {
    /// <summary>
    /// Ribbon-style square button that drops the conversion menu underneath itself. One entry
    /// today ("...to BIM"); future targets add a command property and a flyout row each.
    /// </summary>
    public partial class ConvertButton : UserControl {
        public static readonly DependencyProperty ToBimCommandProperty =
            DependencyProperty.Register(nameof(ToBimCommand), typeof(ICommand), typeof(ConvertButton),
                new PropertyMetadata(null));

        public ICommand? ToBimCommand {
            get => (ICommand?)GetValue(ToBimCommandProperty);
            set => SetValue(ToBimCommandProperty, value);
        }

        // A StaysOpen="False" popup swallows the click that closed it, so the button
        // would immediately re-check and reopen. Ignore a press that lands right after
        // a close and the second click reads as "shut the panel", as it does in Word.
        private int _closedAtTicks;

        public ConvertButton() => InitializeComponent();

        private void OnPopupClosed(object? sender, System.EventArgs e) =>
            _closedAtTicks = System.Environment.TickCount;

        private void OnTogglePreviewMouseDown(object sender, MouseButtonEventArgs e) {
            if (System.Environment.TickCount - _closedAtTicks < 250) e.Handled = true;
        }

        private void OnItemClick(object sender, RoutedEventArgs e) =>
            PART_Toggle.IsChecked = false;
    }
}
