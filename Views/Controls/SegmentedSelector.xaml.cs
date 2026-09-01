using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Cad2Bim.Views.Controls {
    /// <summary>
    /// Segmented picker: the options sit in one sunken box and a lighter thumb slides
    /// onto whichever is selected. Any number of segments, all the same fixed width,
    /// so the thumb's travel is plain arithmetic instead of a layout measurement.
    /// </summary>
    public partial class SegmentedSelector : UserControl {
        private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(160));

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(SegmentedSelector),
                new PropertyMetadata(null));

        public static readonly DependencyProperty SelectedIndexProperty =
            DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(SegmentedSelector),
                new FrameworkPropertyMetadata(0,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    (d, _) => ((SegmentedSelector)d).MoveThumb(animate: true)));

        public static readonly DependencyProperty SegmentWidthProperty =
            DependencyProperty.Register(nameof(SegmentWidth), typeof(double), typeof(SegmentedSelector),
                new PropertyMetadata(52.0, (d, _) => ((SegmentedSelector)d).MoveThumb(animate: false)));

        public IEnumerable? ItemsSource {
            get => (IEnumerable?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public int SelectedIndex {
            get => (int)GetValue(SelectedIndexProperty);
            set => SetValue(SelectedIndexProperty, value);
        }

        /// <summary>Width of one segment; the thumb matches it and steps by it.</summary>
        public double SegmentWidth {
            get => (double)GetValue(SegmentWidthProperty);
            set => SetValue(SegmentWidthProperty, value);
        }

        public SegmentedSelector() {
            InitializeComponent();

            // The first placement must not animate in from the left edge.
            Loaded += (_, _) => MoveThumb(animate: false);
        }

        private void MoveThumb(bool animate) {
            if (PART_ThumbOffset == null) return;

            double target = Math.Max(SelectedIndex, 0) * SegmentWidth;

            if (!animate || !IsLoaded) {
                PART_ThumbOffset.BeginAnimation(TranslateTransform.XProperty, null);
                PART_ThumbOffset.X = target;
                return;
            }

            PART_ThumbOffset.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(target, SlideDuration) {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
        }
    }
}
