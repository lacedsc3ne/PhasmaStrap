using System.Windows;

using PhasmaStrap.Models.SettingTasks.Base;

namespace PhasmaStrap.Models.SettingTasks
{
    public class EmojiModPresetTask : EnumBaseTask<EmojiType>
    {
        private string _filePath => Path.Combine(Paths.Modifications, @"content\fonts\TwemojiMozilla.ttf");

        private IEnumerable<KeyValuePair<EmojiType, string>>? QueryCurrentValue()
        {
            if (!File.Exists(_filePath))
                return null;

            using var fileStream = File.OpenRead(_filePath);
            string hash = MD5Hash.Stringify(App.MD5Provider.ComputeHash(fileStream));

            return EmojiTypeEx.Hashes.Where(x => x.Value == hash);
        }

        public EmojiModPresetTask() : base("ModPreset", "EmojiFont")
        {
            var query = QueryCurrentValue();

            if (query is not null)
                OriginalState = query.FirstOrDefault().Key;
        }

        public override async void Execute()
        {
            const string LOG_IDENT = "EmojiModPresetTask::Execute";

            var query = QueryCurrentValue();

            if (NewState != EmojiType.Default && (query is null || query.FirstOrDefault().Key != NewState))
            {
                try
                {
                    using var response = await App.HttpClient.GetAsync(NewState.GetUrl());

                    response.EnsureSuccessStatusCode();

                    byte[] font = await response.Content.ReadAsByteArrayAsync();

                    Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

                    string tempPath = _filePath + ".download";
                    await File.WriteAllBytesAsync(tempPath, font);

                    if (File.Exists(_filePath))
                        Filesystem.AssertReadOnly(_filePath);

                    File.Move(tempPath, _filePath, true);

                    App.Logger.WriteLine(LOG_IDENT, $"Installed the {NewState} emoji font ({font.Length} bytes)");

                    OriginalState = NewState;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteException(LOG_IDENT, ex);

                    try
                    {
                        if (File.Exists(_filePath + ".download"))
                            File.Delete(_filePath + ".download");
                    }
                    catch (Exception deleteEx)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Could not remove the partial download: {deleteEx.Message}");
                    }

                    Frontend.ShowConnectivityDialog(
                        String.Format(Strings.Dialog_Connectivity_UnableToConnect, "GitHub"),
                        $"{Strings.Menu_Mods_Presets_EmojiType_Error}\n\n{Strings.Dialog_Connectivity_TryAgainLater}",
                        MessageBoxImage.Warning,
                        ex
                    );
                }
            }
            else if (query is not null && query.Any())
            {
                try
                {
                    Filesystem.AssertReadOnly(_filePath);
                    File.Delete(_filePath);

                    OriginalState = NewState;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Could not remove the emoji font at {_filePath}");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
        }
    }
}
