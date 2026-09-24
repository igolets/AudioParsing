namespace AudioParsing.Win.Services;

/// <summary>
/// Loads and saves the shared appsettings.json model.
/// </summary>
public interface ISettingsStore
{
    public string SettingsFilePath { get; }

    public AppSettingsModel Load();

    public void Save(AppSettingsModel model);
}
