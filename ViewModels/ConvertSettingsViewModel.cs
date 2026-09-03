using Cad2Bim.Bim;

namespace Cad2Bim.ViewModels {
    /// <summary>
    /// The Convert panel's knobs. The drawing has no third dimension, so every height here is
    /// invented at export time; stored in millimetres like <see cref="SettingsViewModel"/>.
    /// </summary>
    public class ConvertSettingsViewModel : ViewModelBase {
        private double _wallHeight = BimConversionOptions.Default.WallHeightMm;
        private double _doorHeight = BimConversionOptions.Default.DoorHeightMm;
        private double _windowSill = BimConversionOptions.Default.WindowSillMm;
        private double _windowHead = BimConversionOptions.Default.WindowHeadMm;

        public double WallHeight { get => _wallHeight; set => SetField(ref _wallHeight, value); }
        public double DoorHeight { get => _doorHeight; set => SetField(ref _doorHeight, value); }
        public double WindowSill { get => _windowSill; set => SetField(ref _windowSill, value); }
        public double WindowHead { get => _windowHead; set => SetField(ref _windowHead, value); }

        public bool IsValid => _wallHeight > 0
                            && _doorHeight > 0 && _doorHeight <= _wallHeight
                            && _windowSill >= 0 && _windowSill < _windowHead
                            && _windowHead <= _wallHeight;

        public BimConversionOptions Options => BimConversionOptions.Default with {
            WallHeightMm = _wallHeight,
            DoorHeightMm = _doorHeight,
            WindowSillMm = _windowSill,
            WindowHeadMm = _windowHead
        };
    }
}
