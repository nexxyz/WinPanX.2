namespace WinPanX2.Config;

public class AppConfig
{
    public int PollingIntervalMs { get; set; } = 30;
    public double SmoothingFactor { get; set; } = 0.5;
    public string LogLevel { get; set; } = "Info";
    public string BindingMode { get; set; } = BindingModes.Sticky;
    public double MaxPan { get; set; } = 1.0;
    public double CenterBias { get; set; } = 0.0;
    public bool ApplyToAllDevices { get; set; } = false;
    public List<string> ExcludedProcesses { get; set; } = new();

    public void Normalize()
    {
        PollingIntervalMs = Math.Clamp(PollingIntervalMs, 5, 1000);
        SmoothingFactor = Math.Clamp(SmoothingFactor, 0.0, 1.0);
        MaxPan = Math.Clamp(MaxPan, 0.0, 1.0);
        CenterBias = Math.Clamp(CenterBias, 0.0, 1.0);

        if (!Enum.TryParse<Logging.LogLevel>(LogLevel, ignoreCase: true, out _))
            LogLevel = "Info";

        if (!string.Equals(BindingMode, BindingModes.Sticky, StringComparison.Ordinal)
            && !BindingModes.IsFollowMostRecent(BindingMode)
            && !BindingModes.IsFollowMostRecentOpened(BindingMode))
        {
            BindingMode = BindingModes.Sticky;
        }

        ExcludedProcesses ??= new List<string>();
        ExcludedProcesses = ExcludedProcesses
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
