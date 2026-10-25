using System.Windows.Controls;
using System.Windows.Input;

using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public partial class RPCTemplatesWindow
    {
        public RPCTemplatesWindow(IntegrationsViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
            KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape)
                    return;

                App.Logger.WriteLine("RPCTemplatesWindow", "Escape pressed, closing");
                e.Handled = true;
                Close();
            };
        }

        private void RPCTemplateSelection(object sender, SelectionChangedEventArgs e)
        {
            IntegrationsViewModel viewModel = (IntegrationsViewModel)DataContext;
            viewModel.SelectedRPCTemplate = (RPCTemplate)((ListBox)sender).SelectedItem;
            viewModel.OnPropertyChanged(nameof(viewModel.SelectedRPCTemplate));
        }
    }
}
