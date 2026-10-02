using PhasmaStrap.UI.Elements.Controls;

namespace PhasmaStrap.UI.Elements.Settings.Pages
{
    public partial class SettingsPage : ISectionHostPage
    {
        public SettingsPage()
        {
            InitializeComponent();
        }

        public SectionHost SectionHost => Host;
    }
}
