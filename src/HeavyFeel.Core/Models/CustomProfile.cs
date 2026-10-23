namespace HeavyFeel.Core.Models;

/// <summary>
/// A named snapshot of the user's feel tuning. Stick setup, input route and
/// Master Enable are deliberately left out: a profile changes how the controls
/// feel, never which device is read or whether HeavyPro writes.
/// </summary>
public sealed class CustomProfile
{
    public string Name { get; set; } = "";
    public string FeelLevel { get; set; } = "Custom";
    public string AircraftProfile { get; set; } = "Auto Detect";

    public double Inertia { get; set; }
    public double PitchDamping { get; set; }
    public double RollDamping { get; set; }
    public double YawDamping { get; set; }
    public double ControlResponse { get; set; }
    public double GroundInertia { get; set; }
    public double TurbulenceResponse { get; set; }

    public string InputCurve { get; set; } = "Linear";
    public double InputDeadzone { get; set; }
    public double InputSensitivity { get; set; } = 1.0;
    public double ExpoStrength { get; set; }
    public double InputAccelerationScale { get; set; } = 1.0;
    public double InputDecelerationScale { get; set; } = 1.0;
    public double PitchRateLimit { get; set; }
    public double RollRateLimit { get; set; }
    public double YawRateLimit { get; set; }
    public bool AirspeedResponseEnabled { get; set; } = true;
    public double AirspeedResponse { get; set; } = 0.65;
    public bool RudderLimiter { get; set; } = true;
    public bool RealisticRudder { get; set; }
    public string StickReleaseMode { get; set; } = "Auto";
    public double ReturnDelayMs { get; set; }

    public static CustomProfile Capture(string name, AppSettings s) => new()
    {
        Name = name.Trim(),
        FeelLevel = s.FeelLevel,
        AircraftProfile = s.AircraftProfile,
        Inertia = s.Inertia,
        PitchDamping = s.PitchDamping,
        RollDamping = s.RollDamping,
        YawDamping = s.YawDamping,
        ControlResponse = s.ControlResponse,
        GroundInertia = s.GroundInertia,
        TurbulenceResponse = s.TurbulenceResponse,
        InputCurve = s.InputCurve,
        InputDeadzone = s.InputDeadzone,
        InputSensitivity = s.InputSensitivity,
        ExpoStrength = s.ExpoStrength,
        InputAccelerationScale = s.InputAccelerationScale,
        InputDecelerationScale = s.InputDecelerationScale,
        PitchRateLimit = s.PitchRateLimit,
        RollRateLimit = s.RollRateLimit,
        YawRateLimit = s.YawRateLimit,
        AirspeedResponseEnabled = s.AirspeedResponseEnabled,
        AirspeedResponse = s.AirspeedResponse,
        RudderLimiter = s.RudderLimiter,
        RealisticRudder = s.RealisticRudder,
        StickReleaseMode = s.StickReleaseMode,
        ReturnDelayMs = s.ReturnDelayMs
    };

    public void ApplyTo(AppSettings s)
    {
        s.FeelLevel = FeelLevel;
        s.AircraftProfile = AircraftProfile;
        s.Inertia = Inertia;
        s.PitchDamping = PitchDamping;
        s.RollDamping = RollDamping;
        s.YawDamping = YawDamping;
        s.ControlResponse = ControlResponse;
        s.GroundInertia = GroundInertia;
        s.TurbulenceResponse = TurbulenceResponse;
        s.InputCurve = InputCurve;
        s.InputDeadzone = InputDeadzone;
        s.InputSensitivity = InputSensitivity;
        s.ExpoStrength = ExpoStrength;
        s.InputAccelerationScale = InputAccelerationScale;
        s.InputDecelerationScale = InputDecelerationScale;
        s.PitchRateLimit = PitchRateLimit;
        s.RollRateLimit = RollRateLimit;
        s.YawRateLimit = YawRateLimit;
        s.AirspeedResponseEnabled = AirspeedResponseEnabled;
        s.AirspeedResponse = AirspeedResponse;
        s.RudderLimiter = RudderLimiter;
        s.RealisticRudder = RealisticRudder;
        s.StickReleaseMode = StickReleaseMode;
        s.ReturnDelayMs = ReturnDelayMs;
    }

    public override string ToString() => Name;
}
