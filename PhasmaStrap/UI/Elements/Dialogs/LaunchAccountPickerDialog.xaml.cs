using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    /// <summary>
    /// Deployment > "Ask which account": the saved accounts (from the account switcher's library) before Roblox starts.
    /// PickedUserId is null when the launch was called off.
    /// </summary>
    public partial class LaunchAccountPickerDialog
    {
        public sealed class Row : INotifyPropertyChanged
        {
            public long UserId { get; init; }
            public string Name { get; init; } = "";
            public string Detail { get; init; } = "";
            public string? AvatarUrl { get; init; }
            public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();

            private bool _isCurrent;
            public bool IsCurrent
            {
                get => _isCurrent;
                set
                {
                    _isCurrent = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentVisibility)));
                }
            }

            public Visibility CurrentVisibility => _isCurrent ? Visibility.Visible : Visibility.Collapsed;

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private readonly ObservableCollection<Row> _rows = new();

        public long? PickedUserId { get; private set; }

        /// <summary>The account Roblox is signed in to right now, once known. 0 when unknown.</summary>
        public long CurrentUserId { get; private set; }

        public LaunchAccountPickerDialog(IEnumerable<PhasmaStrap.Utility.AccountQuickSwitch.Account> accounts)
        {
            InitializeComponent();

            foreach (PhasmaStrap.Utility.AccountQuickSwitch.Account account in accounts)
            {
                string note = account.Note.Trim();

                _rows.Add(new Row
                {
                    UserId = account.UserId,
                    Name = string.IsNullOrWhiteSpace(account.DisplayName) ? account.Username : account.DisplayName,
                    Detail = note.Length > 0 ? $"@{account.Username} · {note}" : $"@{account.Username}",
                    AvatarUrl = Integrations.AvatarCache.TryGetFresh(account.UserId),
                });
            }

            AccountList.ItemsSource = _rows;

            Loaded += async (_, _) =>
            {
                AccountList.Focus();

                try
                {
                    // Marks the account that is signed in now, and selects it so Enter keeps it
                    Integrations.RobloxCookie.RobloxAccount? current = await Integrations.RobloxCookie.GetAccountAsync();
                    if (current is null)
                        return;

                    CurrentUserId = current.UserId;

                    foreach (Row row in _rows)
                        row.IsCurrent = row.UserId == current.UserId;

                    if (AccountList.SelectedItem is null)
                        AccountList.SelectedItem = _rows.FirstOrDefault(r => r.IsCurrent);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("LaunchAccountPickerDialog", $"Could not tell which account is signed in: {ex.Message}");
                }
            };
        }

        private void AccountList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
            LaunchButton.IsEnabled = AccountList.SelectedItem is Row;

        private void AccountList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AccountList.SelectedItem is Row)
                Pick();
        }

        private void Launch_Click(object sender, RoutedEventArgs e) => Pick();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            PickedUserId = null;
            Close();
        }

        private void Pick()
        {
            if (AccountList.SelectedItem is not Row row)
                return;

            PickedUserId = row.UserId;
            Close();
        }
    }
}
