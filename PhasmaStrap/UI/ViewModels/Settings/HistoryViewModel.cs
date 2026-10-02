using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhasmaStrap.Integrations;
using PhasmaStrap.Models;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    /// <summary>One bar in the "Most played" card.</summary>
    public sealed class MostPlayedRow
    {
        public string Name { get; init; } = "";
        public string HoursText { get; init; } = "";
        public double Fraction { get; init; }
    }

    public class HistoryViewModel : NotifyPropertyChangedViewModel
    {
        private string _statusText = "";

        public ObservableCollection<PlayTimeEntry> Entries { get; } = new();

        public ObservableCollection<MostPlayedRow> MostPlayed { get; } = new();

        private string _summaryText = "";
        public string SummaryText
        {
            get => _summaryText;
            private set { _summaryText = value; OnPropertyChanged(nameof(SummaryText)); }
        }

        public Visibility EmptyVisibility => Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ListVisibility => Entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public ICommand RefreshCommand => new RelayCommand(LoadEntries);

        public ICommand LaunchCommand => new RelayCommand<PlayTimeEntry>(Launch);

        public ICommand CopyLinkCommand => new RelayCommand<PlayTimeEntry>(CopyLink);

        public HistoryViewModel()
        {
            LoadEntries();
        }

        private void LoadEntries()
        {
            Entries.Clear();

            foreach (PlayTimeEntry entry in PlayTimeStore.GetAll())
                Entries.Add(entry);

            _ = FillPlaceNamesAsync();

            StatusText = Entries.Count == 0
                ? "No games played yet. Your play time shows up here once you have played something."
                : $"{Entries.Count} game(s) tracked.";

            double totalHours = Entries.Sum(e => e.TotalMinutes) / 60;
            string games = Entries.Count == 1 ? "1 game" : $"{Entries.Count} games";
            SummaryText = totalHours >= 1 ? $"{games}  ·  {(int)Math.Round(totalHours)} hours in total" : games;

            MostPlayed.Clear();
            List<PlayTimeEntry> top = Entries.OrderByDescending(e => e.TotalMinutes).Take(4).ToList();
            double most = top.Count > 0 ? Math.Max(top[0].TotalMinutes, 1) : 1;
            foreach (PlayTimeEntry entry in top)
            {
                MostPlayed.Add(new MostPlayedRow
                {
                    Name = entry.DisplayName,
                    HoursText = entry.TotalMinutes >= 60 ? $"{(int)(entry.TotalMinutes / 60)}h" : entry.TotalTimeText,
                    Fraction = entry.TotalMinutes / most,
                });
            }

            OnPropertyChanged(nameof(EmptyVisibility));
            OnPropertyChanged(nameof(ListVisibility));
        }

        private async Task FillPlaceNamesAsync()
        {
            if (await PhasmaStrap.Utility.PlaceNames.FillAsync(Entries.ToList()))
                LoadEntries();
        }

        private static void Launch(PlayTimeEntry? entry)
        {
            if (entry is null || entry.PlaceId <= 0)
                return;

            string uri = PhasmaStrap.Utility.RobloxLaunch.DeepLink(PhasmaStrap.Utility.PlaceNames.StartPlaceOf(entry.PlaceId));
            Process.Start(Paths.Process, $"-player \"{uri}\"");
        }

        private static void CopyLink(PlayTimeEntry? entry)
        {
            if (entry is null || entry.PlaceId <= 0)
                return;

            try
            {
                Clipboard.SetText($"https://www.roblox.com/games/{entry.PlaceId}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("HistoryViewModel", $"Failed to copy link: {ex.Message}");
            }
        }
    }
}
