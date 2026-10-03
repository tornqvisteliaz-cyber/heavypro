namespace HeavyFeel.Core.Models;

public sealed class AppSettings
{
    public bool MasterEnable { get; set; } = true;
    public bool AutoConnect { get; set; } = true;
    public bool LogTelemetry { get; set; }
    public bool DebugMode { get; set; } = true;
    public bool ViewCue { get; set; } = false;
    public bool CompanionMode { get; set; } = true;
    public int TelemetryLogIntervalMs { get; set; } = 1000;

    public double Inertia { get; set; } = 62;
    public double PitchDamping { get; set; } = 68;
    public double RollDamping { get; set; } = 46;
    public double YawDamping { get; set; } = 76;
    public double ControlResponse { get; set; } = 38;
    public double GroundInertia { get; set; } = 52;
    public double TurbulenceResponse { get; set; } = 24;

    public string AircraftProfile { get; set; } = "Auto Detect";
    public string FeelLevel { get; set; } = "Medium";
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
}

public static class SliderLimits
{
    public const double Min = 0;
    public const double Max = 100;
}
