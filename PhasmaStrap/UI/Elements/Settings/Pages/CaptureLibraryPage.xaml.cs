using System.Windows;
using System.Windows.Input;

using Wpf.Ui.Common;

using PhasmaStrap.UI.Elements.Controls;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>Capture > Library: every screenshot, clip and GIF in one grid with a detail panel.</summary>
    public partial class CaptureLibraryPage
    {
        private readonly CaptureLibraryPageViewModel _viewModel = new();

        public CaptureLibraryPage()
        {
            DataContext = _viewModel;
            InitializeComponent();

            ItemMenu.Attach<LibraryEntry>(GroupsList, (entry, menu) =>
            {
                _viewModel.Selected = entry;

                menu.Add("Copy", SymbolRegular.Copy24, () => _viewModel.Copy(entry), "Ctrl+C")
                    .Add("Copy file path", SymbolRegular.Link24, () => _viewModel.CopyPath(entry))
                    .Separator()
                    .Add("Open", SymbolRegular.Open24, () => _viewModel.Open(entry), "Enter", bold: true)
                    .Add("Edit", SymbolRegular.Edit24, () => _viewModel.Edit(entry), enabled: entry.CanEdit)
                    .Add("Rename", SymbolRegular.Rename24, () => _viewModel.Rename(entry), "F2")
                    .Add("Show in folder", SymbolRegular.FolderOpen24, () => _viewModel.Reveal(entry))
                    .Add("Share to the gallery", SymbolRegular.Share24, () => _viewModel.Share(entry), enabled: entry.CanShare)
                    .Separator()
                    .Add("Delete", SymbolRegular.Delete24, () => _viewModel.Delete(entry), "Del", danger: true);
            });

            PreviewKeyDown += Page_PreviewKeyDown;
        }

        private void Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LibraryEntry entry })
                return;

            _viewModel.Selected = entry;
            LibraryRoot.Focus();

            if (e.ClickCount == 2)
            {
                _viewModel.Open(entry);
                e.Handled = true;
            }
        }

        private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _viewModel.Open(null);

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_viewModel.Selected is null || e.OriginalSource is System.Windows.Controls.TextBox or System.Windows.Controls.ComboBox)
                return;

            switch (e.Key)
            {
                case Key.Delete:
                    _viewModel.Delete(null);
                    break;
                case Key.F2:
                    _viewModel.Rename(null);
                    break;
                case Key.Enter:
                    _viewModel.Open(null);
                    break;
                case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                    _viewModel.Copy(null);
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }
    }
}
