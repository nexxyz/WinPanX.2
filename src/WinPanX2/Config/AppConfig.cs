namespace WinPanX2.Config;

public class AppConfig
{
    // Faster polling without noticeable CPU impact
    public int PollingIntervalMs { get; set; } = 30;
    // Snappier response
    public double SmoothingFactor { get; set; } = 0.5;
    public string? DeviceId { get; set; } = null;
    public string LogLevel { get; set; } = "Info";
    public string BindingMode { get; set; } = "Sticky";
    public double MaxPan { get; set; } = 1.0;

    // 0.0 = off. Higher values bias panning toward center.
    public double CenterBias { get; set; } = 0.0;

    // When true, apply spatialization to all active stereo (2+ channel) render devices.
    public bool ApplyToAllDevices { get; set; } = false;

    public List<string> ExcludedProcesses { get; set; } = new();
}
