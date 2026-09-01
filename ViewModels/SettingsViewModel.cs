using System.ComponentModel;
using Cad2Bim.Classification;

namespace Cad2Bim.ViewModels {
    /// <summary>Unit the thickness fields are typed and scrubbed in. Storage stays millimetres.</summary>
    public enum ThicknessUnit { Millimeters, Inches }

    public class SettingsViewModel : ViewModelBase, IDataErrorInfo {
        private const double MillimetersPerInch = 25.4;

        // Scrub range, in millimetres: anything from a thin glazing bead to a heavy retaining wall.
        private const double RangeMinMm = 1.0;
        private const double RangeMaxMm = 2000.0;

        // Canonical storage is millimetres. The drawing's own units are dealt with at
        // classification time, so what the user types never depends on how the file is scaled,
        // and switching mm <-> in re-labels the same physical thickness rather than changing it.
        private double _sMinMm;
        private double _sMaxMm;
        private ThicknessUnit _unit = ThicknessUnit.Millimeters;

        // Raised only when the pair is valid; MainViewModel wires this to re-classification.
        public event Action? Changed;

        public SettingsViewModel(double sMinMillimeters, double sMaxMillimeters) {
            _sMinMm = sMinMillimeters;
            _sMaxMm = sMaxMillimeters;
        }

        /// <summary>Thickness bounds in millimetres — what the classifier is driven with.</summary>
        public double SMinMillimeters => _sMinMm;
        public double SMaxMillimeters => _sMaxMm;

        // The displayed pair. SetField stores millimetres but notifies under the property's own
        // name, so the fields and the mm-backed values stay in step.
        public double SMin {
            get => FromMm(_sMinMm);
            set { if (SetField(ref _sMinMm, ToMm(value))) RaiseIfValid(); }
        }

        public double SMax {
            get => FromMm(_sMaxMm);
            set { if (SetField(ref _sMaxMm, ToMm(value))) RaiseIfValid(); }
        }

        // --- Opening tolerances ----------------------------------------------------------------
        // The four highest-leverage knobs, defaulted from ClassificationTolerances so there is one
        // source of truth for the numbers. The rest stay at their defaults: a panel of a dozen
        // fields is harder to use than one with four, and these are the ones that move the result.
        //
        // The width floor earns its place - most spurious openings are columns and short jogs, and
        // they cluster below it. So does the swing radius floor: a swivel chair is drawn as the
        // same quarter circle as a door, only smaller.
        private double _minOpeningMm = ClassificationTolerances.DefaultMillimeters.MinOpeningWidth;
        private double _maxOpeningMm = ClassificationTolerances.DefaultMillimeters.MaxOpeningWidth;
        private double _minSwingMm = ClassificationTolerances.DefaultMillimeters.MinSwingRadius;
        private double _maxSwingMm = ClassificationTolerances.DefaultMillimeters.MaxSwingRadius;

        public double MinOpeningWidth {
            get => FromMm(_minOpeningMm);
            set { if (SetField(ref _minOpeningMm, ToMm(value))) RaiseIfValid(); }
        }

        public double MaxOpeningWidth {
            get => FromMm(_maxOpeningMm);
            set { if (SetField(ref _maxOpeningMm, ToMm(value))) RaiseIfValid(); }
        }

        public double MinSwingRadius {
            get => FromMm(_minSwingMm);
            set { if (SetField(ref _minSwingMm, ToMm(value))) RaiseIfValid(); }
        }

        public double MaxSwingRadius {
            get => FromMm(_maxSwingMm);
            set { if (SetField(ref _maxSwingMm, ToMm(value))) RaiseIfValid(); }
        }

        /// <summary>The tolerance set to classify with, still in millimetres.</summary>
        public ClassificationTolerances Tolerances => ClassificationTolerances.DefaultMillimeters with {
            MinOpeningWidth = _minOpeningMm,
            MaxOpeningWidth = _maxOpeningMm,
            MinSwingRadius = _minSwingMm,
            MaxSwingRadius = _maxSwingMm
        };

        /// <summary>Scrub range for the opening fields, which run far wider than a wall's thickness.</summary>
        public double OpeningMaximum => FromMm(6000);
        public double OpeningStep => _unit == ThicknessUnit.Inches ? 0.05 : 2.0;

        public ThicknessUnit Unit {
            get => _unit;
            set {
                if (!SetField(ref _unit, value)) return;

                // The stored millimetres do not move — only how they are shown, so no
                // re-classification, just a re-read of everything unit-dependent.
                OnPropertyChanged(nameof(SMin));
                OnPropertyChanged(nameof(SMax));
                OnPropertyChanged(nameof(Minimum));
                OnPropertyChanged(nameof(Maximum));
                OnPropertyChanged(nameof(Step));
                OnPropertyChanged(nameof(Format));
                OnPropertyChanged(nameof(UnitSuffix));
                OnPropertyChanged(nameof(SMinLabel));
                OnPropertyChanged(nameof(SMaxLabel));
                OnPropertyChanged(nameof(UnitIndex));
                OnPropertyChanged(nameof(IsMillimeters));
                OnPropertyChanged(nameof(IsInches));
                OnPropertyChanged(nameof(MinOpeningWidth));
                OnPropertyChanged(nameof(MaxOpeningWidth));
                OnPropertyChanged(nameof(MinSwingRadius));
                OnPropertyChanged(nameof(MaxSwingRadius));
                OnPropertyChanged(nameof(OpeningMaximum));
                OnPropertyChanged(nameof(OpeningStep));
            }
        }

        /// <summary>Segmented-picker facade: 0 = millimetres, 1 = inches.</summary>
        public int UnitIndex {
            get => _unit == ThicknessUnit.Inches ? 1 : 0;
            set => Unit = value == 1 ? ThicknessUnit.Inches : ThicknessUnit.Millimeters;
        }

        // Radio-button facades: only the incoming true matters, the other button clears itself.
        public bool IsMillimeters {
            get => _unit == ThicknessUnit.Millimeters;
            set { if (value) Unit = ThicknessUnit.Millimeters; }
        }

        public bool IsInches {
            get => _unit == ThicknessUnit.Inches;
            set { if (value) Unit = ThicknessUnit.Inches; }
        }

        public string UnitSuffix => _unit == ThicknessUnit.Inches ? "in" : "mm";
        public string SMinLabel => $"SMin ({UnitSuffix})";
        public string SMaxLabel => $"SMax ({UnitSuffix})";

        public double Minimum => FromMm(RangeMinMm);
        public double Maximum => FromMm(RangeMaxMm);

        /// <summary>Value change per pixel of scrub — half a millimetre, or a comparable 1/50 inch.</summary>
        public double Step => _unit == ThicknessUnit.Inches ? 0.02 : 0.5;

        public string Format => _unit == ThicknessUnit.Inches ? "0.###" : "0.#";

        private double ToMm(double value) => _unit == ThicknessUnit.Inches ? value * MillimetersPerInch : value;
        private double FromMm(double mm) => _unit == ThicknessUnit.Inches ? mm / MillimetersPerInch : mm;

        private bool IsValid => _sMinMm > 0 && _sMinMm < _sMaxMm
                             && _minOpeningMm > 0 && _minOpeningMm < _maxOpeningMm
                             && _minSwingMm > 0 && _minSwingMm < _maxSwingMm;

        private void RaiseIfValid() {
            if (IsValid) Changed?.Invoke();
        }

        public string Error => string.Empty;

        public string this[string columnName] => columnName switch {
            nameof(SMin) when _sMinMm <= 0 => $"SMin must be > 0 {UnitSuffix}.",
            nameof(SMin) when _sMinMm >= _sMaxMm => "SMin must be < SMax.",
            nameof(SMax) when _sMaxMm <= _sMinMm => "SMax must be > SMin.",
            _ => string.Empty
        };
    }
}
