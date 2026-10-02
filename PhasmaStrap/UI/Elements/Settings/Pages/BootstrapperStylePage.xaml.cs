using System.Windows.Controls;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// The Bootstrapper section of the settings rail: style, custom themes, icon and title of the loading window.
    /// These settings used to sit at the bottom of Appearance and still use its view model.
    /// </summary>
    public partial class BootstrapperStylePage
    {
        public BootstrapperStylePage()
        {
            DataContext = new AppearanceViewModel(this);
            InitializeComponent();
        }

        public void CustomThemeSelection(object sender, SelectionChangedEventArgs e)
        {
            AppearanceViewModel viewModel = (AppearanceViewModel)DataContext;

            viewModel.SelectedCustomTheme = (string)((ListBox)sender).SelectedItem;
            viewModel.SelectedCustomThemeName = viewModel.SelectedCustomTheme;

            viewModel.OnPropertyChanged(nameof(viewModel.SelectedCustomTheme));
            viewModel.OnPropertyChanged(nameof(viewModel.SelectedCustomThemeName));
        }
    }
}
