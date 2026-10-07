namespace HeavyFeel.Core.Models;

public sealed class AppSettings
{
    public bool MasterEnable { get; set; } = false;
    public bool AutoConnect { get; set; } = true;
    public bool LogTelemetry { get; set; }
    public bool DebugMode { get; set; } = true;
    public bool ViewCue { get; set; } = false;
    public bool UseHardwareCurve { get; set; } = false;
    public bool CompanionMode { get; set; } = true;
    public int TelemetryLogIntervalMs { get; set; } = 1000;

    public double Inertia { get; set; } = 56;
    public double PitchDamping { get; set; } = 50;
    public double RollDamping { get; set; } = 46;
    public double YawDamping { get; set; } = 42;
    public double ControlResponse { get; set; } = 40;
    public double GroundInertia { get; set; } = 50;
    public double TurbulenceResponse { get; set; } = 24;

    // Input dynamics tuning. A zero axis rate uses the selected aircraft preset.
    public string InputCurve { get; set; } = "Linear";
    public double InputDeadzone { get; set; } = 0.02;
    public double InputSensitivity { get; set; } = 1.0;
    public double ExpoStrength { get; set; } = 0.35;
    public double InputAccelerationScale { get; set; } = 1.0;
    public double InputDecelerationScale { get; set; } = 1.0;
    public double PitchRateLimit { get; set; }
    public double RollRateLimit { get; set; }
    public double YawRateLimit { get; set; }
    public bool AirspeedResponseEnabled { get; set; } = true;
    public double AirspeedResponse { get; set; } = 0.65;
    public string StickReleaseMode { get; set; } = "Auto";
    public double ReturnDelayMs { get; set; }

    public string AircraftProfile { get; set; } = "Auto Detect";
    public string FeelLevel { get; set; } = "RealisticPlus";
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
}

public static class SliderLimits
{
    public const double Min = 0;
    public const double Max = 100;
}
