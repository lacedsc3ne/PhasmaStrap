namespace PhasmaStrap.Models.SettingTasks
{
    public class ForceReinstallTask : BoolBaseTask
    {
        public ForceReinstallTask() : base("ForceReinstall")
        {
            OriginalState = App.State.Prop.ForceReinstall;
        }

        public override void Execute()
        {
            App.State.Prop.ForceReinstall = NewState;
            App.State.Save();

            OriginalState = NewState;
        }
    }
}
