using System.Windows.Input;

using PhasmaStrap.UI.ViewModels.Dialogs;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public partial class PartyInviteDialog
    {
        public PartyInviteDialog()
        {
            DataContext = new PartyInviteViewModel();
            InitializeComponent();
            KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape)
                    return;

                App.Logger.WriteLine("PartyInviteDialog", "Escape pressed, closing");
                e.Handled = true;
                Close();
            };
        }

        private void Close_Click(object sender, System.Windows.RoutedEventArgs e) => Close();
    }
}
