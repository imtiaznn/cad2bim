namespace Cad2Bim.ViewModels {
    // One toggleable viewport layer, backed by a classification bucket. The layers flyout only
    // needs Name/IsVisible; the viewport maps Bucket to its per-bucket visual.
    public class LayerViewModel : ViewModelBase {
        public string Name { get; }

        /// <summary>The classification bucket this layer shows.</summary>
        public PrimitiveClass Bucket { get; }

        private bool _isVisible = true;
        public bool IsVisible { get => _isVisible; set => SetField(ref _isVisible, value); }

        public LayerViewModel(string name, PrimitiveClass bucket) {
            Name = name;
            Bucket = bucket;
        }
    }
}
