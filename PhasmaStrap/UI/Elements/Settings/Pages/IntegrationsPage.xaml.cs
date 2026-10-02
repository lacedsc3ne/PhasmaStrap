using System.Windows.Controls;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class IntegrationsPage
    {
        public IntegrationsPage()
        {
            DataContext = new IntegrationsViewModel();
            InitializeComponent();
        }

        /// <summary>Edit on a row opens that program in the editor below the list.</summary>
        private void EditIntegration_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is System.Windows.FrameworkElement { DataContext: CustomIntegration integration })
                CustomIntegrationsListBox.SelectedItem = integration;
        }

        public void CustomIntegrationSelection(object sender, SelectionChangedEventArgs e)
        {
            IntegrationsViewModel viewModel = (IntegrationsViewModel)DataContext;
            viewModel.SelectedCustomIntegration = (CustomIntegration)((ListBox)sender).SelectedItem;
            viewModel.OnPropertyChanged(nameof(viewModel.SelectedCustomIntegration));
        }
    }
}
