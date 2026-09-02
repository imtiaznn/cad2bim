using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Cad2Bim.Views.Controls {
    /// <summary>
    /// Ribbon-style square button that drops the Open/Save file menu underneath itself.
    /// Knows nothing about the view model beyond the two commands it is handed.
    /// </summary>
    public partial class FileButton : UserControl {
        public static readonly DependencyProperty OpenCommandProperty =
            DependencyProperty.Register(nameof(OpenCommand), typeof(ICommand), typeof(FileButton),
                new PropertyMetadata(null));

        public static readonly DependencyProperty SaveCommandProperty =
            DependencyProperty.Register(nameof(SaveCommand), typeof(ICommand), typeof(FileButton),
                new PropertyMetadata(null));

        public ICommand? OpenCommand {
            get => (ICommand?)GetValue(OpenCommandProperty);
            set => SetValue(OpenCommandProperty, value);
        }

        public ICommand? SaveCommand {
            get => (ICommand?)GetValue(SaveCommandProperty);
            set => SetValue(SaveCommandProperty, value);
        }

        // A StaysOpen="False" popup swallows the click that closed it, so the button
        // would immediately re-check and reopen. Ignore a press that lands right after
        // a close and the second click reads as "shut the panel", as it does in Word.
        private int _closedAtTicks;

        public FileButton() => InitializeComponent();

        private void OnPopupClosed(object? sender, System.EventArgs e) =>
            _closedAtTicks = System.Environment.TickCount;

        private void OnTogglePreviewMouseDown(object sender, MouseButtonEventArgs e) {
            if (System.Environment.TickCount - _closedAtTicks < 250) e.Handled = true;
        }

        private void OnItemClick(object sender, RoutedEventArgs e) =>
            PART_Toggle.IsChecked = false;
    }
}
