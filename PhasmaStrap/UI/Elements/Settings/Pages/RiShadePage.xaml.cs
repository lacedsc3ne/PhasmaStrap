using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class RiShadePage
    {
        private const string LOG_IDENT = "RiShadePage";

        private readonly RiShadeViewModel _viewModel;
        private readonly DispatcherTimer _previewTimer;

        private int _previewGeneration;
        private double _split = 0.5;
        private bool _dragging;
        private bool _attached;

        public RiShadePage()
        {
            _viewModel = RenderingViewModel.Shared.RiShade;
            DataContext = _viewModel;
            InitializeComponent();

            // Re-render at most a few times a second while a slider is being dragged.
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _previewTimer.Tick += (_, _) =>
            {
                _previewTimer.Stop();
                _ = RenderPreviewAsync();
            };

            Loaded += Page_Loaded;
            Unloaded += Page_Unloaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_attached)
            {
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                _attached = true;
            }

            _ = RenderPreviewAsync();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_attached)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _attached = false;
            }

            _previewTimer.Stop();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RiShadeViewModel.RiShadeEnabled) || e.PropertyName == nameof(RiShadeViewModel.PreviewEffectLabel))
                return;

            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private async Task RenderPreviewAsync()
        {
            int generation = ++_previewGeneration;

            try
            {
                RiShadePreview.Picture sample = RiShadePreview.GetSample();

                if (PreviewBefore.Source is null)
                    PreviewBefore.Source = RiShadePreview.ToBitmap(RiShadePreview.Original(sample), sample.Width, sample.Height);

                // Work on a copy so dragging a slider can't change the settings halfway through a render.
                RiShadeSettings snapshot =
                    JsonSerializer.Deserialize<RiShadeSettings>(JsonSerializer.Serialize(App.Settings.Prop.RiShade)) ?? new RiShadeSettings();
                snapshot.Normalize();

                byte[] pixels = await Task.Run(() => RiShadePreview.Render(sample, snapshot));

                if (generation != _previewGeneration)
                    return;

                PreviewAfter.Source = RiShadePreview.ToBitmap(pixels, sample.Width, sample.Height);
                UpdateSplit();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Preview render failed: {ex.Message}");
            }
        }

        private void UpdateSplit()
        {
            double width = PreviewGrid.ActualWidth;
            double height = PreviewGrid.ActualHeight;
            if (width <= 0 || height <= 0)
                return;

            double x = Math.Clamp(_split, 0, 1) * width;

            PreviewGrid.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 11, 11);
            PreviewAfter.Clip = new RectangleGeometry(new Rect(x, 0, Math.Max(0, width - x), height));
            PreviewDivider.Margin = new Thickness(Math.Max(0, x - 1), 0, 0, 0);
        }

        private void SetSplitFrom(MouseEventArgs e)
        {
            double width = PreviewGrid.ActualWidth;
            if (width <= 0)
                return;

            _split = Math.Clamp(e.GetPosition(PreviewGrid).X / width, 0.0, 1.0);
            UpdateSplit();
        }

        private void PreviewGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSplit();

        private void PreviewFrame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = PreviewFrame.CaptureMouse();
            SetSplitFrom(e);
            e.Handled = true;
        }

        private void PreviewFrame_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging && e.LeftButton == MouseButtonState.Pressed)
                SetSplitFrom(e);
        }

        private void PreviewFrame_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            PreviewFrame.ReleaseMouseCapture();
        }
    }
}
