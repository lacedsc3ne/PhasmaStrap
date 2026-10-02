using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using PhasmaStrap.Utility;

namespace PhasmaStrap.UI.Elements.Dialogs
{
    public sealed class QuickSignInDialog : Window
    {
        private const string LOG_IDENT = "QuickSignInDialog";

        private readonly TextBlock _code = new() { FontSize = 40, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 14) };
        private readonly TextBlock _status = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        private readonly CancellationTokenSource _cancel = new();

        public string? Cookie { get; private set; }

        public QuickSignInDialog()
        {
            Title = "Sign in with a code";
            Width = 420;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
            SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
            _code.SetResourceReference(TextBlock.ForegroundProperty, "PhasmaAccentBrush");

            var steps = new TextBlock
            {
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Text = "On a phone or PC where that Roblox account is already signed in, open Settings, choose Quick Sign In, and type this code.",
            };

            var close = new Button { Content = "Cancel", Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(18, 6, 18, 6), HorizontalAlignment = HorizontalAlignment.Center };
            close.Click += (_, _) => Close();

            var panel = new StackPanel { Margin = new Thickness(24) };
            panel.Children.Add(steps);
            panel.Children.Add(_code);
            panel.Children.Add(_status);
            panel.Children.Add(close);

            Content = panel;

            Loaded += async (_, _) => await RunAsync();
            Closed += (_, _) => _cancel.Cancel();
        }

        private async Task RunAsync()
        {
            CancellationToken token = _cancel.Token;

            try
            {
                _status.Text = "Asking Roblox for a code...";
                RobloxQuickSignIn.Ticket ticket = await RobloxQuickSignIn.CreateAsync(token);

                _code.Text = ticket.Code;
                _status.Text = "Waiting for the code to be entered.";

                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), token);

                    if (DateTime.UtcNow >= ticket.ExpiresUtc)
                    {
                        _status.Text = "That code ran out. Close this and try again for a new one.";
                        return;
                    }

                    RobloxQuickSignIn.Progress progress = await RobloxQuickSignIn.StatusAsync(ticket, token);

                    if (progress.Cancelled)
                    {
                        _status.Text = "The sign in was turned down on the other device.";
                        return;
                    }

                    if (progress.Validated)
                    {
                        _status.Text = $"Approved{(progress.AccountName.Length > 0 ? $" as {progress.AccountName}" : "")}. Signing in...";
                        Cookie = await RobloxQuickSignIn.RedeemAsync(ticket, token);

                        if (Cookie is null)
                        {
                            _status.Text = "Roblox approved the code but did not hand over a session. Try again.";
                            return;
                        }

                        Close();
                        return;
                    }

                    if (progress.AccountName.Length > 0)
                        _status.Text = $"Code entered by {progress.AccountName}. Confirm it on that device.";
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Sign in with a code failed: {ex.Message}");
                _status.Text = $"That did not work: {ex.Message}";
            }
        }
    }
}
