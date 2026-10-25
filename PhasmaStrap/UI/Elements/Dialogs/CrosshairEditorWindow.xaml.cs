using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

using PhasmaStrap.UI.ViewModels.Dialogs;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public partial class CrosshairEditorWindow
    {
        private readonly CrosshairEditorViewModel _viewModel = new();
        private bool _applied;

        public bool Applied => _applied;

        public CrosshairEditorWindow()
        {
            DataContext = _viewModel;
            _viewModel.Applied += () => _applied = true;

            InitializeComponent();
            Closing += OnClosing;
            KeyDown += OnKeyDown;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;

            App.Logger.WriteLine("CrosshairEditorWindow", "Escape pressed, closing");
            e.Handled = true;
            Close();
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (!_viewModel.HasChanges)
                return;

            MessageBoxResult answer = Frontend.ShowMessageBox("Use this crosshair?\n\nYes applies it, No closes the editor without changing anything.", MessageBoxImage.Question, MessageBoxButton.YesNoCancel);

            if (answer is MessageBoxResult.Cancel or MessageBoxResult.None)
                e.Cancel = true;
            else if (answer == MessageBoxResult.Yes)
                _viewModel.Apply();
        }
    }
}
