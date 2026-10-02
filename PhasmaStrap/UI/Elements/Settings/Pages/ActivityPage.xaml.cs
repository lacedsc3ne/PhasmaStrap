using Wpf.Ui.Common;

using PhasmaStrap.UI.Elements.Controls;
using PhasmaStrap.UI.ViewModels.Settings;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class ActivityPage
    {
        public ActivityPage()
        {
            var vm = new ActivityViewModel();
            DataContext = vm;
            InitializeComponent();

            ItemMenu.Attach<TimelineVisit>(SessionsList, (visit, menu) =>
            {
                menu.Add("Rejoin this server", SymbolRegular.ArrowClockwise24, () => RobloxLaunch.Join(visit.PlaceId, visit.JobId), enabled: visit.CanRejoin, bold: true)
                    .Add("Launch this game", SymbolRegular.Play24, () => RobloxLaunch.Join(visit.PlaceId), enabled: visit.PlaceId > 0)
                    .Separator()
                    .Copy("Copy server ID", visit.JobId)
                    .Separator()
                    .Add(visit.ScreenshotCount > 0 ? $"Open its screenshots ({visit.ScreenshotCount})" : "Open its screenshots", SymbolRegular.Image24,
                        () => ActivityViewModel.OpenCapturesOf(visit, clips: false), enabled: visit.ScreenshotCount > 0)
                    .Add(visit.ClipCount > 0 ? $"Open its clips ({visit.ClipCount})" : "Open its clips", SymbolRegular.VideoClip24,
                        () => ActivityViewModel.OpenCapturesOf(visit, clips: true), enabled: visit.ClipCount > 0)
                    .Separator()
                    .Add("Delete this session", SymbolRegular.Delete24, () => vm.DeleteSession(visit), danger: true);
            });
        }
    }
}
