using System.IO;
using System.Windows;
using Cad2Bim.Classification;
using Cad2Bim.Services;
using Cad2Bim.ViewModels;
using Cad2Bim.Views;
using Microsoft.Win32;

namespace Cad2Bim {
    public partial class App : Application {
        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);

            // "--dump <file> [outFile]": classify headlessly and write a report instead of showing
            // a window, so detection can be measured against a real drawing without the GUI.
            if (e.Args.Length >= 2 && e.Args[0] == "--dump") {
                RunDump(e.Args[1], e.Args.Length >= 3 ? e.Args[2] : null);
                return;
            }

            var service = new ClassificationService();
            var viewModel = new MainViewModel(service, PickFile);
            var window = new MainWindow { DataContext = viewModel };
            window.Show();

            if (e.Args.Length > 0) viewModel.LoadFile(e.Args[0]);
        }

        private void RunDump(string filePath, string? outputPath) {
            string report;
            try {
                report = ClassificationDump.Run(filePath,
                    Wall.DefaultSMinMillimeters, Wall.DefaultSMaxMillimeters);
            }
            catch (Exception ex) {
                report = $"Failed to classify '{filePath}': {ex}";
            }

            // A WinExe has no console of its own, so the report goes to a file unless one is
            // already attached (the caller redirected stdout, say).
            File.WriteAllText(outputPath ?? Path.ChangeExtension(filePath, ".dump.txt"), report);

            // Shutdown() here would queue onto a dispatcher that never runs, because no window was
            // ever shown and OnStartup has not returned - the process would sit idle forever.
            Environment.Exit(0);
        }

        private static string? PickFile() {
            var dialog = new OpenFileDialog {
                Filter = "CAD files (*.dwg;*.dxf)|*.dwg;*.dxf|All files (*.*)|*.*"
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
