using System.Windows.Input;
using PhasmaStrap.Integrations;
using CommunityToolkit.Mvvm.Input;

namespace PhasmaStrap.UI.ViewModels.ContextMenu
{
    internal class ServerHistoryViewModel : NotifyPropertyChangedViewModel
    {
        private readonly ActivityWatcher _activityWatcher;

        public List<ActivityData>? GameHistory { get; private set; }

        public GenericTriState LoadState { get; private set; } = GenericTriState.Unknown;

        public string Error { get; private set; } = String.Empty;

        public ICommand CloseWindowCommand => new RelayCommand(RequestClose);

        public EventHandler? RequestCloseEvent;

        public ServerHistoryViewModel(ActivityWatcher activityWatcher)
        {
            _activityWatcher = activityWatcher;

            _activityWatcher.OnGameLeave += OnGameLeave;

            LoadData();
        }

        public void Detach() => _activityWatcher.OnGameLeave -= OnGameLeave;

        private void OnGameLeave(object? sender, EventArgs e)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;

            if (dispatcher is null)
                return;

            dispatcher.BeginInvoke(new Action(LoadData));
        }

        private async void LoadData()
        {
            LoadState = GenericTriState.Unknown;
            OnPropertyChanged(nameof(LoadState));

            List<ActivityData> history;

            lock (_activityWatcher.History)
                history = new List<ActivityData>(_activityWatcher.History);

            var entries = history.Where(x => x.UniverseDetails is null).ToList();

            if (entries.Any())
            {
                string universeIds = String.Join(',', entries.Select(x => x.UniverseId).Distinct());

                try
                {
                    await UniverseDetails.FetchBulk(universeIds);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException("ServerHistoryViewModel::LoadData", ex);

                    Error = ex.Message;
                    OnPropertyChanged(nameof(Error));

                    LoadState = GenericTriState.Failed;
                    OnPropertyChanged(nameof(LoadState));

                    return;
                }

                foreach (var entry in entries)
                    entry.UniverseDetails = UniverseDetails.LoadFromCache(entry.UniverseId);
            }

            GameHistory = new(history);

            var consolidatedJobIds = new List<ActivityData>();

            foreach (var entry in history)
            {
                if (entry.RootActivity is not null)
                {
                    if (entry.RootActivity.TimeLeft < entry.TimeLeft)
                        entry.RootActivity.TimeLeft = entry.TimeLeft;

                    if (entry.ServerType == ServerType.Public && !consolidatedJobIds.Contains(entry))
                    {
                        entry.RootActivity.JobId = entry.JobId;
                        consolidatedJobIds.Add(entry);
                    }

                    GameHistory.Remove(entry);
                }
            }

            OnPropertyChanged(nameof(GameHistory));

            LoadState = GenericTriState.Successful;
            OnPropertyChanged(nameof(LoadState));
        }

        private void RequestClose() => RequestCloseEvent?.Invoke(this, EventArgs.Empty);
    }
}
