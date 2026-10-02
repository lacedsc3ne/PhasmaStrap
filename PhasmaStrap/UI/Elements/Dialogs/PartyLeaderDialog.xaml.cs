using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    /// <summary>
    /// The in-app "Make someone the party leader?" confirmation. It lays itself over the main window with a dim
    /// backdrop and a card in the middle, instead of a system message box.
    /// </summary>
    public partial class PartyLeaderDialog
    {
        public bool Confirmed { get; private set; }

        public PartyLeaderDialog(string name, string avatar, string code, int members)
        {
            InitializeComponent();

            string who = string.IsNullOrWhiteSpace(name) ? "them" : name;

            TitleText.Text = $"Make {who} the party leader?";
            SubtitleText.Text = string.IsNullOrEmpty(code)
                ? $"{members} member{(members == 1 ? "" : "s")}"
                : $"Party {code} · {members} member{(members == 1 ? "" : "s")}";
            LinePicks.Text = $"{who} picks the games from now on. Everyone follows them in.";
            LineStay.Text = $"You stay in the party as a member and follow {who}.";
            LineBack.Text = $"{who} can hand it back, or pass it to someone else.";
            NoteText.Text = $"{who} gets a notification and has to accept. Until then you stay leader.";
            InitialText.Text = string.IsNullOrWhiteSpace(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(avatar))
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(avatar, UriKind.RelativeOrAbsolute);
                    image.DecodePixelWidth = 104;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();

                    AvatarEllipse.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                    AvatarEllipse.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("PartyLeaderDialog", $"Could not show the avatar: {ex.Message}");
                }
            }

            Loaded += (_, _) => ConfirmButton.Focus();
        }

        /// <summary>Asks the leader to confirm handing the party over. True when they clicked "Make leader".</summary>
        public static bool Confirm(string name, string avatar, string code, int members)
        {
            var dialog = new PartyLeaderDialog(name, avatar, code, members);

            Window? owner = Application.Current?.Windows
                .OfType<PhasmaStrap.UI.Elements.Settings.MainWindow>()
                .FirstOrDefault(w => w.IsVisible && w.WindowState != System.Windows.WindowState.Minimized);

            if (owner is not null && dialog.CoverOwner(owner))
            {
                dialog.Owner = owner;
            }
            else
            {
                dialog.Width = 620;
                dialog.Height = 460;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.Scrim.Background = Brushes.Transparent;
            }

            dialog.ShowDialog();
            return dialog.Confirmed;
        }

        /// <summary>Sizes this window to sit exactly over the owner's content.</summary>
        private bool CoverOwner(Window owner)
        {
            try
            {
                if (owner.ActualWidth <= 0 || owner.ActualHeight <= 0)
                    return false;

                Point topLeft = owner.PointToScreen(new Point(0, 0));
                Matrix fromDevice = PresentationSource.FromVisual(owner)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
                topLeft = fromDevice.Transform(topLeft);

                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = topLeft.X;
                Top = topLeft.Y;
                Width = owner.ActualWidth;
                Height = owner.ActualHeight;
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("PartyLeaderDialog", $"Could not line up with the main window: {ex.Message}");
                return false;
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Scrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Close();

        // Clicks on the card itself must not reach the backdrop
        private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
    }
}
