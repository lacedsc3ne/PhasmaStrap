using System.Windows;
using System.Windows.Controls;

using PhasmaStrap.UI.Elements.Base;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public partial class FlagPickerDialog : WpfUiWindow
    {
        public sealed record Item(string Name, string Value);

        private readonly List<Item> _all;
        private readonly HashSet<Item> _picked = new();
        private bool _filtering;

        public List<string> Picked { get; private set; } = new();

        public FlagPickerDialog(string title, string prompt, IEnumerable<(string Name, string Value)> flags)
        {
            InitializeComponent();

            Title = title;
            TitleBarElement.Title = title;
            PromptText.Text = prompt;

            _all = flags.Select(f => new Item(f.Name, f.Value)).ToList();
            FlagList.ItemsSource = _all;
            UpdateCount();

            Loaded += (_, _) => SearchBox.Focus();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string text = SearchBox.Text.Trim();

            _filtering = true;
            FlagList.ItemsSource = text.Length == 0 ? _all : _all.Where(i => i.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (Item item in FlagList.Items.OfType<Item>().Where(_picked.Contains))
                FlagList.SelectedItems.Add(item);
            _filtering = false;

            UpdateCount();
        }

        private void FlagList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_filtering)
            {
                foreach (Item item in e.AddedItems.OfType<Item>())
                    _picked.Add(item);
                foreach (Item item in e.RemovedItems.OfType<Item>())
                    _picked.Remove(item);
            }

            UpdateCount();
        }

        private void UpdateCount()
        {
            int count = _picked.Count;
            CountText.Text = count == 0 ? $"{_all.Count} flag(s)" : $"{count} picked";
            OkButton.IsEnabled = count > 0;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Picked = _all.Where(_picked.Contains).Select(i => i.Name).ToList();
            App.Logger.WriteLine("FlagPickerDialog", $"Picked {Picked.Count} flag(s)");
            DialogResult = true;
        }
    }
}
