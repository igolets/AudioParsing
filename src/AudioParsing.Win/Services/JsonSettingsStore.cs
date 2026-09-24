namespace AudioParsing.Win.Services;

/// <summary>
/// <see cref="ISettingsStore"/> over <see cref="AppSettingsFile"/>.
/// The optional override path exists as a test seam; production uses the default resolution.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string? _overridePath;

    public JsonSettingsStore()
        : this(null)
    {
    }

    public JsonSettingsStore(string? overridePath)
    {
        _overridePath = overridePath;
    }

    public string SettingsFilePath => AppSettingsFile.ResolvePath(_overridePath);

    public AppSettingsModel Load() => AppSettingsFile.Load(_overridePath);

    public void Save(AppSettingsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        AppSettingsFile.Save(model, _overridePath);
    }
}
