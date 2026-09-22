namespace Rex.AndroidMirror.Cli;

public sealed class RootPolicy
{
    private readonly ConfigStore _config;

    public RootPolicy(ConfigStore config) => _config = config;

    public bool Enabled => ReadBool("Root.Enabled", true);
    public bool ProbeOnConnect => ReadBool("Root.ProbeOnConnect", true);
    public bool RequestAutomatically => ReadBool("Root.RequestAutomatically", false);
    public bool AllowReadOnly => ReadBool("Root.AllowReadOnly", true);
    public bool AllowReversible => ReadBool("Root.AllowReversible", false);
    public bool AllowSystemChanges => ReadBool("Root.AllowSystemChanges", false);
    public bool AllowDeviceCritical => ReadBool("Root.AllowDeviceCritical", false);
    public bool RawShellEnabled => ReadBool("Root.RawShellEnabled", false);
    public int RequestTimeoutSeconds => ReadInt("Root.RequestTimeoutSeconds", 15, 3, 60);
    public int CommandTimeoutSeconds => ReadInt("Root.CommandTimeoutSeconds", 10, 1, 120);
    public int MaxOutputCharacters => ReadInt("Root.MaxOutputCharacters", 262144, 4096, 4_194_304);

    public void EnsureEnabled()
    {
        if (!Enabled)
            throw new InvalidOperationException("Privileged Android support is disabled in REX config.");
    }

    public void EnsureAllowed(PrivilegeRisk risk)
    {
        EnsureEnabled();

        var allowed = risk switch
        {
            PrivilegeRisk.ReadOnly => AllowReadOnly,
            PrivilegeRisk.Reversible => AllowReversible,
            PrivilegeRisk.SystemChanging => AllowSystemChanges,
            PrivilegeRisk.DeviceCritical => AllowDeviceCritical,
            _ => false
        };

        if (!allowed)
            throw new InvalidOperationException(
                $"REX policy blocks {risk} privileged operations.");
    }

    private bool ReadBool(string path, bool fallback)
    {
        try { return bool.Parse(_config.Get(path).Value); }
        catch (KeyNotFoundException) { return fallback; }
    }

    private int ReadInt(string path, int fallback, int min, int max)
    {
        try
        {
            var value = int.Parse(
                _config.Get(path).Value,
                System.Globalization.CultureInfo.InvariantCulture);
            return Math.Clamp(value, min, max);
        }
        catch (KeyNotFoundException)
        {
            return fallback;
        }
    }
}
