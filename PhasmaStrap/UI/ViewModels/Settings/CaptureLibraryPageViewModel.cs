using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    /// <summary>One screenshot, clip or GIF in the Library tab. Wraps the item the other Capture tabs use, so their commands work on it.</summary>
    public sealed class LibraryEntry : NotifyPropertyChangedViewModel
    {
        public ScreenshotItem? Shot { get; }
        public ReplayClipItem? Clip { get; }

        public string Path { get; }
        public DateTime Taken { get; }
        public long Bytes { get; }
        public CaptureKind Kind { get; }
        public string? Game { get; }

        public LibraryEntry(ScreenshotItem shot, long bytes, string? game)
        {
            Shot = shot;
            Path = shot.Path;
            Taken = shot.Taken;
            Bytes = bytes;
            Kind = CaptureKind.Screenshot;
            Game = game;
        }

        public LibraryEntry(ReplayClipItem clip)
        {
            Clip = clip;
            Path = clip.Path;
            Taken = clip.Taken;
            Bytes = clip.Bytes;
            Kind = clip.IsGif ? CaptureKind.Gif : CaptureKind.Video;
            Game = clip.Game;

            clip.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ReplayClipItem.DurationText))
                    OnPropertyChanged(nameof(TagText));
            };
        }

        public string FileName => System.IO.Path.GetFileName(Path);
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

        public string GameText => string.IsNullOrEmpty(Game) ? "Unknown game" : Game!;

        /// <summary>"Clip 0:30", "Screenshot", "GIF".</summary>
        public string TagText => Kind switch
        {
            CaptureKind.Screenshot => "Screenshot",
            CaptureKind.Gif => "GIF",
            _ => Clip is { DurationText.Length: > 0 } ? $"Clip {Clip.DurationText}" : "Clip",
        };

        public string KindText => Kind switch
        {
            CaptureKind.Screenshot => "Screenshot",
            CaptureKind.Gif => "GIF",
            _ => "Clip",
        };

        public string WhenText => LibraryDays.Label(Taken);

        public bool CanEdit => Kind != CaptureKind.Gif;
        public bool CanShare => Kind == CaptureKind.Screenshot;

        public Visibility PlayVisibility => Kind == CaptureKind.Video ? Visibility.Visible : Visibility.Collapsed;

        public Wpf.Ui.Common.SymbolRegular Symbol => Kind switch
        {
            CaptureKind.Gif => Wpf.Ui.Common.SymbolRegular.Gif24,
            CaptureKind.Video => Wpf.Ui.Common.SymbolRegular.VideoClip24,
            _ => Wpf.Ui.Common.SymbolRegular.Image24,
        };

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }

        private BitmapSource? _thumbnail;
        private bool _requested;

        public BitmapSource? Thumbnail
        {
            get
            {
                if (!_requested)
                {
                    _requested = true;
                    var dispatcher = Application.Current.Dispatcher;
                    ClipThumbnails.Request(Path, image => dispatcher.BeginInvoke(() =>
                    {
                        _thumbnail = image;
                        OnPropertyChanged(nameof(Thumbnail));
                        OnPropertyChanged(nameof(PlaceholderVisibility));
                    }));
                }
                return _thumbnail;
            }
        }

        public Visibility PlaceholderVisibility => _thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
    }

    public sealed class LibraryGroup
    {
        public string Title { get; init; } = "";
        public List<LibraryEntry> Items { get; init; } = new();
    }

    internal static class LibraryDays
    {
        public static string Label(DateTime taken)
        {
            DateTime today = DateTime.Today;
            if (taken.Date == today)
                return "Today";
            if (taken.Date == today.AddDays(-1))
                return "Yesterday";
            if (taken.Date > today.AddDays(-7))
                return taken.ToString("dddd");
            return taken.Year == today.Year ? taken.ToString("d MMMM") : taken.ToString("d MMMM yyyy");
        }
    }

    /// <summary>The Library tab: every screenshot, clip and GIF in one grid, with filters and a detail panel.</summary>
    public sealed class CaptureLibraryPageViewModel : NotifyPropertyChangedViewModel
    {
        public const string AnyGame = "Any game";
        public const string NoGame = "No game recorded";

        private const int PageSize = 240;

        public CaptureViewModel Capture { get; } = CaptureViewModel.Shared;

        public ObservableCollection<LibraryGroup> Groups { get; } = new();

        public ObservableCollection<string> GameOptions { get; } = new() { AnyGame };

        private readonly Dictionary<string, LibraryEntry> _known = new(StringComparer.OrdinalIgnoreCase);
        private List<LibraryEntry> _all = new();
        private List<LibraryEntry> _shown = new();
        private int _limit = PageSize;
        private bool _rebuildQueued;

        public CaptureLibraryPageViewModel()
        {
            Capture.Screenshots.CollectionChanged += OnSourceChanged;
            Capture.Replays.CollectionChanged += OnSourceChanged;
            Capture.PropertyChanged += OnCapturePropertyChanged;

            Rebuild();
        }

        private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRebuild();

        private void QueueRebuild()
        {
            if (_rebuildQueued)
                return;

            _rebuildQueued = true;
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                _rebuildQueued = false;
                Rebuild();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnCapturePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(CaptureViewModel.Status):
                    Status = Capture.Status;
                    break;
                case nameof(CaptureViewModel.ReplayStatus):
                    Status = Capture.ReplayStatus;
                    break;
                case nameof(CaptureViewModel.Storage):
                    OnPropertyChanged(nameof(StorageText));
                    break;
            }
        }

        // ---- Building the list ----

        private void Rebuild()
        {
            CaptureLibrary.GameIndex? games = null;
            var fresh = new List<LibraryEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ScreenshotItem shot in Capture.Screenshots)
            {
                seen.Add(shot.Path);
                if (_known.TryGetValue(shot.Path, out LibraryEntry? entry) && entry.Shot == shot)
                {
                    fresh.Add(entry);
                    continue;
                }

                long bytes = 0;
                try { bytes = new FileInfo(shot.Path).Length; } catch { }

                games ??= new CaptureLibrary.GameIndex();
                entry = new LibraryEntry(shot, bytes, games.GameAt(shot.Taken));
                _known[shot.Path] = entry;
                fresh.Add(entry);
            }

            foreach (ReplayClipItem clip in Capture.Replays)
            {
                seen.Add(clip.Path);
                if (_known.TryGetValue(clip.Path, out LibraryEntry? entry) && entry.Clip == clip)
                {
                    fresh.Add(entry);
                    continue;
                }

                entry = new LibraryEntry(clip);
                _known[clip.Path] = entry;
                fresh.Add(entry);
            }

            foreach (string gone in _known.Keys.Where(k => !seen.Contains(k)).ToList())
                _known.Remove(gone);

            _all = fresh.OrderByDescending(e => e.Taken).ToList();

            BuildGameOptions();
            Apply();
            OnPropertyChanged(nameof(StorageText));
        }

        private void BuildGameOptions()
        {
            var options = new List<string> { AnyGame };
            options.AddRange(_all.Where(e => !string.IsNullOrEmpty(e.Game)).Select(e => e.Game!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g, StringComparer.OrdinalIgnoreCase));
            if (_all.Any(e => string.IsNullOrEmpty(e.Game)))
                options.Add(NoGame);

            if (!options.Contains(_game))
                _game = AnyGame;

            if (!options.SequenceEqual(GameOptions))
            {
                GameOptions.Clear();
                foreach (string option in options)
                    GameOptions.Add(option);
            }

            OnPropertyChanged(nameof(SelectedGame));
        }

        private void Apply()
        {
            IEnumerable<LibraryEntry> items = _all;

            items = _kind switch
            {
                1 => items.Where(e => e.Kind == CaptureKind.Video),
                2 => items.Where(e => e.Kind == CaptureKind.Screenshot),
                3 => items.Where(e => e.Kind == CaptureKind.Gif),
                _ => items,
            };

            if (_game == NoGame)
                items = items.Where(e => string.IsNullOrEmpty(e.Game));
            else if (_game != AnyGame)
                items = items.Where(e => string.Equals(e.Game, _game, StringComparison.OrdinalIgnoreCase));

            _shown = items.ToList();

            Groups.Clear();
            foreach (var day in _shown.Take(_limit).GroupBy(e => e.Taken.Date))
                Groups.Add(new LibraryGroup { Title = LibraryDays.Label(day.Key), Items = day.ToList() });

            if (Selected is not null && !_shown.Contains(Selected))
                Selected = null;

            OnPropertyChanged(nameof(MoreVisibility));
            OnPropertyChanged(nameof(MoreText));
            OnPropertyChanged(nameof(EmptyVisibility));
            OnPropertyChanged(nameof(EmptyText));
            OnPropertyChanged(nameof(CountText));
        }

        // ---- Filters ----

        private int _kind;

        public bool ShowEverything { get => _kind == 0; set { if (value) SetKind(0); } }
        public bool ShowClips { get => _kind == 1; set { if (value) SetKind(1); } }
        public bool ShowScreenshots { get => _kind == 2; set { if (value) SetKind(2); } }
        public bool ShowGifs { get => _kind == 3; set { if (value) SetKind(3); } }

        private void SetKind(int kind)
        {
            if (_kind == kind)
                return;

            _kind = kind;
            _limit = PageSize;
            OnPropertyChanged(nameof(ShowEverything));
            OnPropertyChanged(nameof(ShowClips));
            OnPropertyChanged(nameof(ShowScreenshots));
            OnPropertyChanged(nameof(ShowGifs));
            Apply();
        }

        private string _game = AnyGame;
        public string? SelectedGame
        {
            get => _game;
            set
            {
                if (value is null || value == _game)
                    return;
                _game = value;
                _limit = PageSize;
                OnPropertyChanged(nameof(SelectedGame));
                Apply();
            }
        }

        public string StorageText
        {
            get
            {
                CaptureViewModel.StorageSummary storage = Capture.Storage;
                int limitMb = App.Settings.Prop.CaptureStorageLimitMB;
                string limit = limitMb <= 0 ? "" : limitMb >= 1024 ? $" of {limitMb / 1024.0:0.#} GB" : $" of {limitMb} MB";
                return $"{storage.UsedText}{limit} used";
            }
        }

        public string CountText => _shown.Count == _all.Count ? Plural(_all.Count, "capture") : $"{_shown.Count} of {_all.Count}";

        private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

        public Visibility EmptyVisibility => _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public string EmptyText => _all.Count == 0
            ? "Nothing captured yet. Press your screenshot or Instant Replay hotkey in a game."
            : "Nothing matches these filters.";

        public Visibility MoreVisibility => _shown.Count > _limit ? Visibility.Visible : Visibility.Collapsed;

        public string MoreText => $"Show more ({_shown.Count - Math.Min(_limit, _shown.Count)} left)";

        public ICommand ShowMoreCommand => new RelayCommand(() =>
        {
            _limit += PageSize;
            Apply();
        });

        // ---- Selection and detail panel ----

        private LibraryEntry? _selected;
        public LibraryEntry? Selected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                    return;

                if (_selected is not null)
                    _selected.IsSelected = false;

                _selected = value;

                if (_selected is not null)
                    _selected.IsSelected = true;

                OnPropertyChanged(nameof(Selected));
                OnPropertyChanged(nameof(DetailVisibility));
                OnPropertyChanged(nameof(NoDetailVisibility));
                OnPropertyChanged(nameof(EditVisibility));
                OnPropertyChanged(nameof(ShareVisibility));
                DetailMeta = "";
                Preview = null;
                LoadDetail(_selected);
            }
        }

        public Visibility DetailVisibility => _selected is null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility NoDetailVisibility => _selected is null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility EditVisibility => _selected is { CanEdit: true } ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ShareVisibility => _selected is { CanShare: true } ? Visibility.Visible : Visibility.Collapsed;

        private BitmapSource? _preview;
        public BitmapSource? Preview { get => _preview; private set { _preview = value; OnPropertyChanged(nameof(Preview)); } }

        private string _detailMeta = "";
        public string DetailMeta { get => _detailMeta; private set { _detailMeta = value; OnPropertyChanged(nameof(DetailMeta)); } }

        private static string Size(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576.0:0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";

        private static string When(DateTime taken)
        {
            string day = taken.Date == DateTime.Today ? "today" : taken.Date == DateTime.Today.AddDays(-1) ? "yesterday" : taken.ToString("d MMM yyyy");
            return $"{day} at {taken:HH:mm}";
        }

        private void LoadDetail(LibraryEntry? entry)
        {
            if (entry is null)
                return;

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(entry.Game))
                parts.Add(entry.Game!);
            parts.Add(When(entry.Taken));
            if (entry.Clip is { DurationText.Length: > 0 })
                parts.Add(entry.Clip.DurationText);
            parts.Add(Size(entry.Bytes));
            DetailMeta = string.Join(" · ", parts);

            // Clips and GIFs use their thumbnail; screenshots get a sharper preview and their size, read off the UI thread.
            if (entry.Kind != CaptureKind.Screenshot)
            {
                Preview = entry.Thumbnail;
                entry.PropertyChanged += FollowThumbnail;
                return;
            }

            string path = entry.Path;
            Task.Run(() =>
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    image.DecodePixelWidth = 640;
                    image.UriSource = new Uri(path, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();

                    int width = 0, height = 0;
                    using (FileStream stream = File.OpenRead(path))
                    {
                        BitmapFrame frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                        width = frame.PixelWidth;
                        height = frame.PixelHeight;
                    }

                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        if (_selected != entry)
                            return;

                        Preview = image;
                        if (width > 0 && height > 0)
                            DetailMeta += $" · {width}×{height}";
                    });
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("CaptureLibraryPage", $"No preview for {System.IO.Path.GetFileName(path)}: {ex.Message}");
                }
            });
        }

        private void FollowThumbnail(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not LibraryEntry entry)
                return;

            if (entry != _selected)
            {
                entry.PropertyChanged -= FollowThumbnail;
                return;
            }

            if (e.PropertyName == nameof(LibraryEntry.Thumbnail))
                Preview = entry.Thumbnail;
            else if (e.PropertyName == nameof(LibraryEntry.TagText))
                LoadDetailMetaOnly(entry);
        }

        private void LoadDetailMetaOnly(LibraryEntry entry)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(entry.Game))
                parts.Add(entry.Game!);
            parts.Add(When(entry.Taken));
            if (entry.Clip is { DurationText.Length: > 0 })
                parts.Add(entry.Clip.DurationText);
            parts.Add(Size(entry.Bytes));
            DetailMeta = string.Join(" · ", parts);
        }

        private string _status = "";
        public string Status { get => _status; private set { _status = value; OnPropertyChanged(nameof(Status)); } }

        // ---- Actions: the same commands the Screenshots and Instant Replay tabs use ----

        private static void Run(ICommand screenshot, ICommand clip, LibraryEntry? entry)
        {
            if (entry?.Shot is ScreenshotItem shot && screenshot.CanExecute(shot))
                screenshot.Execute(shot);
            else if (entry?.Clip is ReplayClipItem item && clip.CanExecute(item))
                clip.Execute(item);
        }

        public void Open(LibraryEntry? entry) => Run(Capture.OpenScreenshotCommand, Capture.OpenReplayCommand, entry ?? _selected);
        public void Edit(LibraryEntry? entry) => Run(Capture.EditScreenshotCommand, Capture.EditReplayCommand, entry ?? _selected);
        public void Copy(LibraryEntry? entry) => Run(Capture.CopyScreenshotCommand, Capture.CopyReplayCommand, entry ?? _selected);
        public void CopyPath(LibraryEntry? entry) => Run(Capture.CopyScreenshotPathCommand, Capture.CopyReplayPathCommand, entry ?? _selected);
        public void Rename(LibraryEntry? entry) => Run(Capture.RenameScreenshotCommand, Capture.RenameReplayCommand, entry ?? _selected);
        public void Reveal(LibraryEntry? entry) => Run(Capture.RevealScreenshotCommand, Capture.RevealReplayCommand, entry ?? _selected);

        public void Share(LibraryEntry? entry)
        {
            entry ??= _selected;
            if (entry?.Shot is ScreenshotItem shot && Capture.ShareScreenshotCommand.CanExecute(shot))
                Capture.ShareScreenshotCommand.Execute(shot);
        }

        public void Delete(LibraryEntry? entry)
        {
            entry ??= _selected;
            if (entry is null)
                return;

            // Select the next capture so Delete can be pressed again.
            int index = _shown.IndexOf(entry);
            LibraryEntry? next = index >= 0 && index + 1 < _shown.Count ? _shown[index + 1] : index > 0 ? _shown[index - 1] : null;

            Run(Capture.DeleteScreenshotCommand, Capture.DeleteReplayCommand, entry);

            if (!File.Exists(entry.Path))
            {
                Status = $"{entry.FileName} moved to the Recycle Bin.";
                Rebuild();
                Selected = next is not null && _shown.Contains(next) ? next : null;
            }
        }

        public ICommand OpenCommand => new RelayCommand(() => Open(null));
        public ICommand EditCommand => new RelayCommand(() => Edit(null));
        public ICommand CopyCommand => new RelayCommand(() => Copy(null));
        public ICommand CopyPathCommand => new RelayCommand(() => CopyPath(null));
        public ICommand RenameCommand => new RelayCommand(() => Rename(null));
        public ICommand RevealCommand => new RelayCommand(() => Reveal(null));
        public ICommand ShareCommand => new RelayCommand(() => Share(null));
        public ICommand DeleteCommand => new RelayCommand(() => Delete(null));
    }
}
