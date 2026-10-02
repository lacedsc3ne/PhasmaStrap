using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Wpf.Ui.Common;

using PhasmaStrap.UI.Elements.Controls;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class ShortcutsPage
    {
        private readonly ShortcutsViewModel _viewModel = new();

        public ShortcutsPage()
        {
            DataContext = _viewModel;
            InitializeComponent();

            Loaded += (_, _) => _viewModel.RefreshShortcuts();

            ItemMenu.Attach<ShortcutEntry>(ShortcutsList, (entry, menu) =>
            {
                menu.Add("Run it", SymbolRegular.Play24, () => Utilities.ShellExecute(entry.Path), bold: true)
                    .Add("Open file location", SymbolRegular.FolderOpen24, NotificationCenter.RevealFile(entry.Path))
                    .Add("Rename", SymbolRegular.Rename24, () => _viewModel.RenameShortcutCommand.Execute(entry))
                    .Separator()
                    .Add("Remove", SymbolRegular.Delete24, () => _viewModel.RemoveShortcutCommand.Execute(entry), "Del", danger: true);
            });

            // Del on a focused row removes that shortcut, like the menu says.
            ShortcutsList.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Delete)
                    return;

                if (ItemMenu.ItemAt<ShortcutEntry>(e.OriginalSource) is ShortcutEntry entry)
                {
                    _viewModel.RemoveShortcutCommand.Execute(entry);
                    e.Handled = true;
                }
            };
        }
    }
}
