using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.ViewModels.Settings
{
    public sealed class PartyMemberRow
    {
        public string Name { get; init; } = "";

        public string Id { get; init; } = "";

        public bool You { get; init; }

        /// <summary>You lead, this is someone else, and the server knows who they are.</summary>
        public bool CanManage { get; init; }

        public Visibility ManageVisibility => CanManage ? Visibility.Visible : Visibility.Collapsed;

        public Visibility LeaderTagVisibility => Leader ? Visibility.Visible : Visibility.Collapsed;

        public string Role { get; init; } = "";

        public bool Leader { get; init; }

        public string Avatar { get; init; } = "";

        public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();

        public Visibility AvatarVisibility => string.IsNullOrEmpty(Avatar) ? Visibility.Collapsed : Visibility.Visible;

        public Visibility InitialVisibility => string.IsNullOrEmpty(Avatar) ? Visibility.Visible : Visibility.Collapsed;

        public Thickness Ring => Leader ? new Thickness(2) : new Thickness(0);

        public string Game { get; init; } = "";

        public bool Together { get; init; }

        public string Where => string.IsNullOrEmpty(Game)
            ? "Not in a game"
            : Together ? Game + ", same server" : Game;

        /// <summary>Where this member is playing, when the party server says.</summary>
        public long PlaceId { get; init; }

        public string JobId { get; init; } = "";

        public bool CanJoinGame => !You && PlaceId > 0;

        /// <summary>The ⋯ button shows when there is anything in its menu besides copying the name.</summary>
        public Visibility MoreVisibility => CanManage || CanJoinGame ? Visibility.Visible : Visibility.Collapsed;
    }

    public sealed class PartyInviteRow
    {
        public string Id { get; init; } = "";

        public string Code { get; init; } = "";

        public string Summary { get; init; } = "";
    }

    public class PartyViewModel : NotifyPropertyChangedViewModel
    {
        public PartyViewModel()
        {
            PartyService.Changed += OnPartyChanged;
        }

        public bool PartyEnabled
        {
            get => App.Settings.Prop.PartyEnabled;
            set
            {
                App.Settings.Prop.PartyEnabled = value;
                App.Settings.Save();

                PartyBackground.ApplyStartup();

                if (value)
                    PartyBackground.StartIfWanted();

                OnPropertyChanged(nameof(PartyEnabled));
                OnPropertyChanged(nameof(Status));
            }
        }

        public bool PartyBackgroundEnabled
        {
            get => App.Settings.Prop.PartyBackgroundEnabled;
            set
            {
                App.Settings.Prop.PartyBackgroundEnabled = value;
                App.Settings.Save();

                PartyBackground.ApplyStartup();

                if (value)
                    PartyBackground.StartIfWanted();

                OnPropertyChanged(nameof(PartyBackgroundEnabled));
            }
        }

        public string[] JoinModeOptions { get; } = { "Join straight away", "Ask if I am already in a game", "Always ask first" };

        private static readonly string[] JoinModeKeys = { "Always", "AskWhenInGame", "AskAlways" };

        public int JoinModeIndex
        {
            get
            {
                int index = Array.IndexOf(JoinModeKeys, App.Settings.Prop.PartyJoinMode);
                return index < 0 ? 1 : index;
            }
            set
            {
                if (value < 0 || value >= JoinModeKeys.Length)
                    return;

                App.Settings.Prop.PartyJoinMode = JoinModeKeys[value];
                App.Settings.Save();
                OnPropertyChanged(nameof(JoinModeIndex));
            }
        }

        public bool SignedIn => PhasmaAccount.SignedIn;

        public Visibility SignedOutVisibility => PhasmaAccount.SignedIn ? Visibility.Collapsed : Visibility.Visible;

        public Visibility InPartyVisibility => PartyService.InParty ? Visibility.Visible : Visibility.Collapsed;

        public Visibility NoPartyVisibility => PhasmaAccount.SignedIn && !PartyService.InParty ? Visibility.Visible : Visibility.Collapsed;

        public string Code => PartyService.Current.Code;

        public string Status
        {
            get
            {
                if (!PhasmaAccount.SignedIn)
                    return "Sign in to your PhasmaStrap account on the PhasmaStrap page to use parties.";

                if (!App.Settings.Prop.PartyEnabled)
                    return "Parties are turned off.";

                if (!PartyService.InParty)
                    return "You are not in a party.";

                return PartyService.IsLeader
                    ? $"You are the leader. Anyone in the party follows you into a game. Share the code {PartyService.Current.Code}."
                    : $"{PartyService.Current.LeaderName} is leading. You follow them into whatever they launch.";
            }
        }

        public ObservableCollection<PartyMemberRow> Members { get; } = new();

        public ObservableCollection<PartyInviteRow> Invites { get; } = new();

        public string MemberCount => $"{Members.Count} of 12";

        public string LeaderLine => PartyService.IsLeader
            ? "You are leading a party · everyone follows you into what you launch"
            : $"{PartyService.Current.LeaderName} is leading · you follow them into what they launch";

        public string LeaderHint => PartyService.IsLeader
            ? "Anyone with this code can join. When you launch a game, they come with you."
            : "You follow the leader into whatever they launch.";

        public Visibility InvitesVisibility => Invites.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        private static PartyLeaderOffer? Offer => PartyService.InParty ? PartyService.Current.LeaderOffer : null;

        public Visibility OfferForYouVisibility => Offer is { ForYou: true } ? Visibility.Visible : Visibility.Collapsed;

        public string OfferForYouText => Offer is { } offer
            ? $"{(string.IsNullOrEmpty(offer.FromName) ? "The leader" : offer.FromName)} wants you to lead the party"
            : "";

        public string OfferForYouDetail => $"Party {PartyService.Current.Code} · you would pick the games";

        private static PartyMember? OfferFrom => PartyService.Current.Members.FirstOrDefault(m => m.Leader);

        public string OfferFromInitial => Offer is { } offer && offer.FromName.Length > 0 ? offer.FromName.Substring(0, 1).ToUpperInvariant() : "?";

        public string? OfferFromAvatar => string.IsNullOrEmpty(OfferFrom?.Avatar) ? null : OfferFrom!.Avatar;

        public Visibility OfferFromAvatarVisibility => OfferFromAvatar is null ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Choices for "If the leader leaves", in the order of PartyService.LeaderLeaves keys below.</summary>
        public string[] LeaderLeavesOptions { get; } = { "Longest member", "End the party" };

        private static readonly string[] LeaderLeavesKeys = { PartyService.LeaderLeaves.Pass, PartyService.LeaderLeaves.End };

        /// <summary>Shown while in a party whose server knows this setting.</summary>
        public Visibility LeaderLeavesVisibility => PartyService.InParty && PartyService.Current.OnLeaderLeave.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>It's a party wide setting, so only the leader can change it. Everyone else sees what the leader picked.</summary>
        public bool CanSetLeaderLeaves => PartyService.IsLeader;

        public string LeaderLeavesHint => PartyService.IsLeader
            ? "Who takes over so the party keeps going"
            : $"{(string.IsNullOrEmpty(PartyService.Current.LeaderName) ? "The leader" : PartyService.Current.LeaderName)} picks this for the party";

        public int LeaderLeavesIndex
        {
            get
            {
                int index = Array.IndexOf(LeaderLeavesKeys, PartyService.Current.OnLeaderLeave);
                return index < 0 ? 0 : index;
            }
            set
            {
                if (value < 0 || value >= LeaderLeavesKeys.Length || !PartyService.IsLeader)
                    return;

                string key = LeaderLeavesKeys[value];
                if (key == PartyService.Current.OnLeaderLeave)
                    return;

                _ = SetLeaderLeavesAsync(key);
            }
        }

        private async Task SetLeaderLeavesAsync(string key)
        {
            Message = "";

            if (!await PartyService.SetLeaderLeavesAsync(key))
            {
                Message = "The party server didn't take that. Try again in a moment.";
                OnPropertyChanged(nameof(LeaderLeavesIndex));
            }
        }

        public Visibility OfferPendingVisibility => PartyService.IsLeader && Offer is { ForYou: false } ? Visibility.Visible : Visibility.Collapsed;

        public string OfferPendingText => Offer is { } offer ? $"Waiting for {offer.ToName} to accept" : "";

        /// <summary>Members the leader can hand the party to, for the picker in the side panel.</summary>
        public ObservableCollection<PartyMemberRow> LeaderCandidates { get; } = new();

        public Visibility LeaderPickerVisibility => LeaderCandidates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        private PartyMemberRow? _selectedCandidate;

        public PartyMemberRow? SelectedCandidate
        {
            get => _selectedCandidate;
            set
            {
                _selectedCandidate = value;
                OnPropertyChanged(nameof(SelectedCandidate));
            }
        }

        public ICommand MakeLeaderCommand => new AsyncRelayCommand(async () =>
        {
            if (SelectedCandidate is { } row)
                await MakeLeaderAsync(row);
        });

        private string _joinCode = "";

        public string JoinCode
        {
            get => _joinCode;
            set
            {
                _joinCode = (value ?? "").Trim().ToUpperInvariant();
                OnPropertyChanged(nameof(JoinCode));
            }
        }

        private string _message = "";

        public string Message
        {
            get => _message;
            private set
            {
                _message = value;
                OnPropertyChanged(nameof(Message));
            }
        }

        public ICommand StartPartyCommand => new AsyncRelayCommand(async () =>
        {
            Message = "";
            await PartyService.CreateAsync();
        });

        public ICommand JoinPartyCommand => new AsyncRelayCommand(async () =>
        {
            if (JoinCode.Length == 0)
            {
                Message = "Type the code someone gave you first.";
                return;
            }

            Message = "";

            if (!await PartyService.JoinAsync(JoinCode))
                Message = "That code did not work. Check it and try again.";
            else
                JoinCode = "";
        });

        public ICommand InviteFriendCommand => new RelayCommand(() =>
        {
            var dialog = new Elements.Dialogs.PartyInviteDialog();
            dialog.ShowDialog();
        });

        public ICommand LeavePartyCommand => new AsyncRelayCommand(async () => await PartyService.LeaveAsync());

        public ICommand CopyCodeCommand => new RelayCommand(() =>
        {
            try
            {
                System.Windows.Clipboard.SetText(PartyService.Current.Code);
                Message = "Code copied.";
            }
            catch (Exception ex)
            {
                Message = "Could not copy the code: " + ex.Message;
            }
        });

        public ICommand AcceptInviteCommand => new AsyncRelayCommand<PartyInviteRow?>(async row =>
        {
            if (row is null)
                return;

            await PartyService.JoinAsync(row.Code);
        });

        public ICommand DeclineInviteCommand => new AsyncRelayCommand<PartyInviteRow?>(async row =>
        {
            if (row is null)
                return;

            await PartyService.DeclineInviteAsync(row.Id);
        });

        public ICommand AcceptLeaderCommand => new AsyncRelayCommand(async () => await AnswerOfferAsync(true));

        public ICommand DeclineLeaderCommand => new AsyncRelayCommand(async () => await AnswerOfferAsync(false));

        public ICommand CancelLeaderOfferCommand => new AsyncRelayCommand(async () =>
        {
            if (!await PartyService.CancelLeaderOfferAsync())
                Message = "Could not cancel that right now. Try again in a moment.";
        });

        private async Task AnswerOfferAsync(bool accept)
        {
            if (Offer is not { ForYou: true } offer)
                return;

            Message = "";

            if (!await PartyService.AnswerLeaderOfferAsync(offer.Id, accept))
                Message = "That didn't go through. The offer may have run out.";
        }

        public async Task MakeLeaderAsync(PartyMemberRow row)
        {
            if (!row.CanManage)
                return;

            if (!Elements.Dialogs.PartyLeaderDialog.Confirm(row.Name, row.Avatar, PartyService.Current.Code, PartyService.Current.Members.Count))
                return;

            Message = await PartyService.OfferLeaderAsync(row.Id)
                ? $"Asked {row.Name} to take over. Nothing changes until they accept."
                : "The party server didn't take that. It may need an update before the leader can be passed on.";
        }

        /// <summary>Joins the exact server this member is in, or just their game when the server isn't known.</summary>
        public void JoinMemberGame(PartyMemberRow row)
        {
            if (!row.CanJoinGame)
                return;

            Message = RobloxLaunch.Join(row.PlaceId, row.JobId.Length > 0 ? row.JobId : null)
                ? $"Joining {row.Name}..."
                : "Could not start Roblox.";
        }

        public async Task RemoveMemberAsync(PartyMemberRow row)
        {
            if (!row.CanManage)
                return;

            MessageBoxResult answer = Frontend.ShowMessageBox(
                $"Remove {row.Name} from the party?\n\nThey can join again if they still have the code.",
                MessageBoxImage.Question,
                MessageBoxButton.YesNo);

            if (answer != MessageBoxResult.Yes)
                return;

            Message = await PartyService.RemoveMemberAsync(row.Id)
                ? $"{row.Name} is out of the party."
                : "The party server didn't take that. It may need an update before people can be removed.";
        }

        private void OnPartyChanged(object? sender, EventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(Refresh));
        }

        private void OnAccountChanged(object? sender, EventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (App.Settings.Prop.PartyEnabled && PhasmaAccount.SignedIn)
                {
                    PartyService.Start();
                    _ = PartyService.RefreshAsync();
                }

                Refresh();
            }));
        }

        public void Refresh()
        {
            Members.Clear();

            bool canManage = PartyService.CanManageMembers;

            foreach (PartyMember member in PartyService.Current.Members)
            {
                Members.Add(new PartyMemberRow
                {
                    Name = member.Name,
                    Id = member.Id,
                    You = member.You,
                    CanManage = canManage && !member.You && !member.Leader && !string.IsNullOrEmpty(member.Id),
                    Role = member.Leader ? "Leader" : "Member",
                    Leader = member.Leader,
                    Avatar = member.Avatar,
                    Game = member.Game,
                    Together = member.Together,
                    PlaceId = long.TryParse(member.PlaceId, out long placeId) ? placeId : 0,
                    JobId = member.JobId ?? "",
                });
            }

            string? candidateId = _selectedCandidate?.Id;
            LeaderCandidates.Clear();

            foreach (PartyMemberRow row in Members.Where(m => m.CanManage))
                LeaderCandidates.Add(row);

            _selectedCandidate = LeaderCandidates.FirstOrDefault(r => r.Id == candidateId) ?? LeaderCandidates.FirstOrDefault();

            Invites.Clear();

            foreach (PartyInvite invite in PartyService.Current.Invites)
                Invites.Add(new PartyInviteRow { Id = invite.Id, Code = invite.Code, Summary = $"{invite.From} invited you to a party of {invite.Members}." });

            OnPropertyChanged(string.Empty);
        }

        public void Attach()
        {
            PartyService.Changed -= OnPartyChanged;
            PartyService.Changed += OnPartyChanged;

            PhasmaAccount.Changed -= OnAccountChanged;
            PhasmaAccount.Changed += OnAccountChanged;

            if (App.Settings.Prop.PartyEnabled)
                PartyService.Start();

            Refresh();
        }

        public void Detach()
        {
            PartyService.Changed -= OnPartyChanged;
            PhasmaAccount.Changed -= OnAccountChanged;
        }
    }
}
