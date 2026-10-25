using Microsoft.Win32;
using System.Windows;
using PhasmaStrap.Resources;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public partial class AddFastFlagDialog
    {
        public MessageBoxResult Result = MessageBoxResult.Cancel;

        public AddFastFlagDialog()
        {
            InitializeComponent();
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = $"{Strings.FileTypes_FastFlagFiles}|*.json;*.txt|{Strings.FileTypes_JSONFiles}|*.json|{Strings.FileTypes_TextFiles}|*.txt"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                JsonTextBox.Text = File.ReadAllText(dialog.FileName);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("AddFastFlagDialog", $"Could not read {dialog.FileName}: {ex.Message}");
                Frontend.ShowMessageBox($"That file couldn't be opened.\n\n{ex.Message}", MessageBoxImage.Error);
            }
        }

        private void PresetValuesButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FFlagPresetsDialog { Owner = this };

            dialog.ShowDialog();

            if (dialog.Result == MessageBoxResult.OK && !string.IsNullOrEmpty(dialog.SelectedValue))
                FlagValueTextBox.Text = dialog.SelectedValue;
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.OK;
            Close();
        }
    }
}
