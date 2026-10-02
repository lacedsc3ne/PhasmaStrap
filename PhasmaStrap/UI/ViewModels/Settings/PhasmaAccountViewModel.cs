using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public class PhasmaAccountViewModel : NotifyPropertyChangedViewModel
    {
        private bool _accountBusy;
        private string _accountStatus = "";

        public string AccountHeader => PhasmaStrap.Utility.PhasmaAccount.SignedIn
            ? $"Signed in as {PhasmaStrap.Utility.PhasmaAccount.DisplayName}"
            : "Not signed in";

        public string AccountDetail
        {
            get
            {
                if (!string.IsNullOrEmpty(_accountStatus))
                    return _accountStatus;

                if (PhasmaStrap.Utility.PhasmaAccount.SignedIn)
                {
                    // The server says whether a Roblox account is linked. Until it answers, show the account ID as before.
                    if (_summary is not null)
                    {
                        string roblox = _summary.RobloxLinked
                            ? string.IsNullOrWhiteSpace(_summary.RobloxName) ? "Roblox account linked" : $"Roblox account {_summary.RobloxName} linked"
                            : "No Roblox account linked yet";
                        return $"Signed in to PhasmaStrap · {roblox}";
                    }

                    return $"Your account ID is {App.State.Prop.AccountId}. Give it to support if you ever need help.";
                }

                return "Optional. Signing in lets support find your reports when you ask for help, and is the same account as on phasmastrap.com.";
            }
        }

        /// <summary>First letter of the account name, for the round avatar on the page.</summary>
        public string AccountInitial
        {
            get
            {
                string name = PhasmaStrap.Utility.PhasmaAccount.SignedIn ? PhasmaStrap.Utility.PhasmaAccount.DisplayName : "";
                return string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();
            }
        }

        /// <summary>The account ID, on hover over the account line, for support.</summary>
        public string? AccountIdTip => PhasmaStrap.Utility.PhasmaAccount.SignedIn && !string.IsNullOrEmpty(App.State.Prop.AccountId)
            ? $"Account ID {App.State.Prop.AccountId}. Give it to support if you ever need help."
            : null;

        private PhasmaStrap.Utility.Backend.AccountSummary? _summary;

        /// <summary>Asks the server for the Roblox link and the newest backup. Quietly keeps the local view when it does not answer.</summary>
        public async Task LoadSummaryAsync(bool fresh = false)
        {
            if (!PhasmaStrap.Utility.PhasmaAccount.SignedIn)
            {
                _summary = null;
                RefreshAccountCard();
                RefreshBackupCard();
                return;
            }

            PhasmaStrap.Utility.Backend.AccountSummary? summary = await PhasmaStrap.Utility.Backend.SettingsApi.GetAccountSummaryAsync(fresh);
            if (summary is null)
                return;

            _summary = summary;
            RefreshAccountCard();
            RefreshBackupCard();
        }

        public string AccountActionText => PhasmaStrap.Utility.PhasmaAccount.SignedIn ? "Sign out" : "Sign in";

        public bool AccountActionEnabled => !_accountBusy;

        public ICommand AccountActionCommand => new RelayCommand(RunAccountAction);

        public ICommand LinkRobloxCommand => new RelayCommand(() => Utilities.ShellExecute("https://phasmastrap.com/link-roblox"));

        /// <summary>The account's page on phasmastrap.com, from GET /v1/account/summary. Only an https phasmastrap.com link is used.</summary>
        private string? ProfileUrl
        {
            get
            {
                string? url = _summary?.ProfileUrl;
                if (!PhasmaStrap.Utility.PhasmaAccount.SignedIn || string.IsNullOrWhiteSpace(url))
                    return null;

                return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps
                    && (uri.Host.Equals("phasmastrap.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".phasmastrap.com", StringComparison.OrdinalIgnoreCase))
                    ? uri.AbsoluteUri
                    : null;
            }
        }

        public Visibility ProfileVisibility => ProfileUrl is null ? Visibility.Collapsed : Visibility.Visible;

        public ICommand OpenProfileCommand => new RelayCommand(() =>
        {
            if (ProfileUrl is string url)
                Utilities.ShellExecute(url);
        });

        /// <summary>"Link Roblox" only while no Roblox account is linked (or the server has not said yet).</summary>
        public Visibility LinkRobloxVisibility => _summary is { RobloxLinked: true } ? Visibility.Collapsed : Visibility.Visible;

        private void RefreshAccountCard()
        {
            OnPropertyChanged(nameof(AccountHeader));
            OnPropertyChanged(nameof(AccountInitial));
            OnPropertyChanged(nameof(AccountDetail));
            OnPropertyChanged(nameof(AccountIdTip));
            OnPropertyChanged(nameof(AccountActionText));
            OnPropertyChanged(nameof(AccountActionEnabled));
            OnPropertyChanged(nameof(BackupVisibility));
            OnPropertyChanged(nameof(ProfileVisibility));
            OnPropertyChanged(nameof(LinkRobloxVisibility));
        }

        private void SetAccountStatus(string text)
        {
            _accountStatus = text;
            RefreshAccountCard();
        }

        private async void RunAccountAction()
        {
            if (_accountBusy)
                return;

            _accountBusy = true;
            RefreshAccountCard();

            try
            {
                if (PhasmaStrap.Utility.PhasmaAccount.SignedIn)
                {
                    await PhasmaStrap.Utility.PhasmaAccount.SignOutAsync();
                    SetAccountStatus("");
                    return;
                }

                using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(12));
                bool ok = await PhasmaStrap.Utility.PhasmaAccount.SignInAsync(SetAccountStatus, cancel.Token);

                if (ok)
                    SetAccountStatus("");
            }
            catch (OperationCanceledException)
            {
                SetAccountStatus("That took too long. Try again when you are ready.");
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("PhasmaStrapViewModel::RunAccountAction", ex);
                SetAccountStatus("Something went wrong signing in. The log has the details.");
            }
            finally
            {
                _accountBusy = false;
                RefreshAccountCard();
            }
        }

        private bool _backupBusy;
        private string _backupStatus = "";

        public Visibility BackupVisibility => PhasmaStrap.Utility.PhasmaAccount.SignedIn ? Visibility.Visible : Visibility.Collapsed;

        public bool BackupEnabled => !_backupBusy;

        public string BackupDetail
        {
            get
            {
                if (!string.IsNullOrEmpty(_backupStatus))
                    return _backupStatus;

                DateTime? last = LastBackupUtc;
                if (last is not null)
                {
                    string machine = _summary?.LastBackupMachine is { Length: > 0 } name
                        && !string.Equals(name, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                        ? $" from {name}"
                        : "";
                    return $"Last backed up {DescribeWhen(last.Value)}{machine}";
                }

                return "Keep a copy of your settings, FastFlag profiles and crosshairs on your account, so another PC can pick them up.";
            }
        }

        /// <summary>The newest backup: what the server says, or else the last one this PC made.</summary>
        private DateTime? LastBackupUtc
        {
            get
            {
                if (_summary?.LastBackupAt is DateTime server && server > DateTime.MinValue)
                    return DateTime.SpecifyKind(server, DateTimeKind.Utc);

                if (DateTime.TryParse(App.State.Prop.LastAutoBackup, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime local))
                    return local.ToUniversalTime();

                return null;
            }
        }

        /// <summary>"today at 14:02", "yesterday at 22:14", or "on 28 Sept at 09:30".</summary>
        private static string DescribeWhen(DateTime utc)
        {
            DateTime local = utc.ToLocalTime();
            DateTime today = DateTime.Now.Date;
            string time = local.ToString("HH:mm", CultureInfo.CurrentCulture);

            if (local.Date == today)
                return $"today at {time}";

            if (local.Date == today.AddDays(-1))
                return $"yesterday at {time}";

            return $"on {local.ToString("d MMM", CultureInfo.CurrentCulture)} at {time}";
        }

        public bool AutoBackupEnabled
        {
            get => App.Settings.Prop.AutoBackupEnabled;
            set
            {
                App.Settings.Prop.AutoBackupEnabled = value;
                App.Settings.SaveDeferred();

                if (value)
                    _ = PhasmaStrap.Utility.PhasmaAccount.MaybeAutoBackUpAsync();
            }
        }

        public string[] BackupScopes { get; } = { "Settings, flags and themes", "Everything" };

        /// <summary>"What to include". Everything also backs up friend notes, friends since dates and the games library.</summary>
        public string BackupScope
        {
            get => string.Equals(App.Settings.Prop.BackupScope, "Everything", StringComparison.OrdinalIgnoreCase) ? BackupScopes[1] : BackupScopes[0];
            set
            {
                App.Settings.Prop.BackupScope = value == BackupScopes[1] ? "Everything" : "Standard";
                App.Settings.SaveDeferred();
                OnPropertyChanged(nameof(BackupScope));
            }
        }

        public ICommand BackUpCommand => new RelayCommand(RunBackUp);

        public ICommand RestoreCommand => new RelayCommand(RunRestore);

        private void RefreshBackupCard()
        {
            OnPropertyChanged(nameof(BackupVisibility));
            OnPropertyChanged(nameof(BackupEnabled));
            OnPropertyChanged(nameof(BackupDetail));
        }

        private async void RunBackUp()
        {
            if (_backupBusy)
                return;

            _backupBusy = true;
            _backupStatus = "Uploading...";
            RefreshBackupCard();

            try
            {
                int count = await PhasmaStrap.Utility.PhasmaAccount.BackUpNowAsync();

                _backupStatus = count switch
                {
                    0 => "There was nothing to back up.",
                    -1 => "That did not go through. Check your connection and try again.",
                    _ => $"Backed up {count} file(s) just now.",
                };

                if (count > 0)
                {
                    // Counts as the newest backup for this PC too, so the page and the once a day backup both know.
                    App.State.Prop.LastAutoBackup = DateTime.UtcNow.ToString("o");
                    App.State.Save();
                    _ = LoadSummaryAsync(fresh: true);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("PhasmaStrapViewModel::RunBackUp", ex);
                _backupStatus = "Something went wrong backing up. The log has the details.";
            }
            finally
            {
                _backupBusy = false;
                RefreshBackupCard();
            }
        }

        private async void RunRestore()
        {
            if (_backupBusy)
                return;

            var confirm = Frontend.ShowMessageBox(
                "This replaces your settings, FastFlag profiles and themes on this PC with your last backup.\n\n"
                + "PhasmaStrap will close afterwards so the restored files are read fresh. Carry on?",
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo
            );

            if (confirm != MessageBoxResult.Yes)
                return;

            _backupBusy = true;
            _backupStatus = "Downloading...";
            RefreshBackupCard();

            try
            {
                var files = await PhasmaStrap.Utility.PhasmaAccount.RestoreAsync();

                if (files is null || files.Count == 0)
                {
                    _backupStatus = "There is no backup on your account yet.";
                    return;
                }

                int written = 0;

                foreach (var (name, contents) in files)
                {
                    if (PhasmaStrap.Utility.PhasmaAccount.RestorePathFor(name) is not string file)
                        continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);

                    if (File.Exists(file))
                        File.Copy(file, file + ".before-restore", true);

                    await File.WriteAllTextAsync(file, contents);
                    written++;
                }

                _backupStatus = $"Put back {written} file(s). Closing so they are read fresh.";
                RefreshBackupCard();

                App.Logger.WriteLine("PhasmaStrapViewModel::RunRestore", $"Restored {written} file(s) from the backup");
                await Task.Delay(1200);
                App.SoftTerminate();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("PhasmaStrapViewModel::RunRestore", ex);
                _backupStatus = "Something went wrong restoring. The log has the details.";
            }
            finally
            {
                _backupBusy = false;
                RefreshBackupCard();
            }
        }
    }
}
