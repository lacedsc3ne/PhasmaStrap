using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class PhasmaStrapPage
    {
        public PhasmaStrapPage()
        {
            var viewModel = new PhasmaStrapViewModel();
            DataContext = viewModel;
            InitializeComponent();

            _ = viewModel.LoadUpdateStateAsync();

            var account = new PhasmaAccountViewModel();
            PhasmaAccountSection.DataContext = account;

            // Roblox link state and the newest backup come from the server; the page shows the local view until then.
            _ = account.LoadSummaryAsync();
        }

        private void HistoryExpander_Expanded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is PhasmaStrapViewModel viewModel)
                viewModel.RefreshBackups();
        }
    }
}
