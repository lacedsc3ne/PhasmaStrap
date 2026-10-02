using System.ComponentModel;
using System.Windows;

using PhasmaStrap.UI.Elements.Controls;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class CapturePage : ISectionHostPage
    {
        public CapturePage()
        {
            InitializeComponent();

            // "Library 148": the count after the Library tab follows the capture folders.
            Loaded += (_, _) =>
            {
                CaptureViewModel.Shared.PropertyChanged -= OnCaptureChanged;
                CaptureViewModel.Shared.PropertyChanged += OnCaptureChanged;
                UpdateLibraryBadge();
            };
            Unloaded += (_, _) => CaptureViewModel.Shared.PropertyChanged -= OnCaptureChanged;

            UpdateLibraryBadge();
        }

        public SectionHost SectionHost => Host;

        private void OnCaptureChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CaptureViewModel.LibraryCount))
                UpdateLibraryBadge();
        }

        private void UpdateLibraryBadge()
        {
            SectionItem? library = Host.Sections.FirstOrDefault(s => s.PageType == typeof(CaptureLibraryPage));
            if (library is not null)
                library.Badge = CaptureViewModel.Shared.LibraryCount > 0 ? CaptureViewModel.Shared.LibraryCount.ToString() : "";
        }

        private void ReplayPill_Click(object sender, RoutedEventArgs e) => Host.Show(typeof(CaptureReplayPage));
    }
}
