namespace HeavyFeel.Core.Models;

/// <summary>
/// One sample of user-aircraft state. All fields come from documented SimVars.
/// Phase 1 is read-only; nothing in this type is written back to the sim.
/// </summary>
public sealed class FlightSnapshot
{
    public DateTime Utc { get; init; } = DateTime.UtcNow;

    public AircraftIdentity Aircraft { get; init; } = new();
    public AircraftClass Class { get; init; }

    public double AirspeedIndicatedKnots { get; init; }
    public double AirspeedTrueKnots { get; init; }
    public double AirspeedMach { get; init; }
    public double GroundSpeedKnots { get; init; }
    public double VerticalSpeedFpm { get; init; }

    public double PitchDegrees { get; init; }
    public double BankDegrees { get; init; }
    public double HeadingTrueDegrees { get; init; }

    public double LatitudeDegrees { get; init; }
    public double LongitudeDegrees { get; init; }
    public double AltitudeFeet { get; init; }

    public double AngleOfAttackDegrees { get; init; }
    public double SideslipDegrees { get; init; }
    public double GForce { get; init; }

    /// <summary>Body-axis rotation rates. Mapping confirmed against live data in Phase 1.</summary>
    public double RotationVelocityBodyX { get; init; }
    public double RotationVelocityBodyY { get; init; }
    public double RotationVelocityBodyZ { get; init; }

    public double AccelerationBodyX { get; init; }
    public double AccelerationBodyY { get; init; }
    public double AccelerationBodyZ { get; init; }

    public double VelocityBodyX { get; init; }
    public double VelocityBodyY { get; init; }
    public double VelocityBodyZ { get; init; }

    public double DynamicPressurePsf { get; init; }
    public bool OnGround { get; init; }
    public double AltitudeAglFeet { get; init; }
    public double RadioHeightFeet { get; init; }

    public double TotalWeightPounds { get; init; }
    public double EmptyWeightPounds { get; init; }
    public double MaxGrossWeightPounds { get; init; }
    public double PitchMoi { get; init; }
    public double RollMoi { get; init; }
    public double YawMoi { get; init; }
    public double FuelWeightPounds { get; init; }

    public double FlapsHandlePercent { get; init; }
    public double FlapsHandleIndex { get; init; }
    public double TrailingEdgeFlapsPercent { get; init; }
    public double GearHandlePosition { get; init; }
    public double GearTotalExtendedPercent { get; init; }
    public double SpoilersHandlePercent { get; init; }

    public double Throttle1Percent { get; init; }
    public double Throttle2Percent { get; init; }
    public double EngineN1_1 { get; init; }
    public double EngineN1_2 { get; init; }

    public double YokeX { get; init; }
    public double YokeY { get; init; }
    public double YokeXWithAp { get; init; }
    public double YokeYWithAp { get; init; }
    public double YokeXIndicator { get; init; }
    public double YokeYIndicator { get; init; }
    public double RudderPedal { get; init; }

    // When available, these are the incoming official SimConnect AXIS event values
    // before HeavyPro writes its processed output. SimVar values remain the fallback.
    public double RawAileronInput { get; init; }
    public double RawElevatorInput { get; init; }
    public double RawRudderInput { get; init; }
    public bool HasRawAileronInput { get; init; }
    public bool HasRawElevatorInput { get; init; }
    public bool HasRawRudderInput { get; init; }

    public double StickX => Largest(YokeX, YokeXWithAp, YokeXIndicator);
    public double StickY => Largest(YokeY, YokeYWithAp, YokeYIndicator);

    public string InputSource
    {
        get
        {
            if (Math.Abs(YokeYWithAp) >= Math.Abs(YokeY) && Math.Abs(YokeYWithAp) >= Math.Abs(YokeYIndicator) && Math.Abs(YokeYWithAp) > 0.04)
                return "yoke+AP";
            if (Math.Abs(YokeYIndicator) > Math.Abs(YokeY) && Math.Abs(YokeYIndicator) > 0.04)
                return "yoke indicator";
            if (Math.Abs(YokeY) > 0.04 || Math.Abs(YokeX) > 0.04)
                return "yoke";
            if (Math.Abs(StickY) > 0.04 || Math.Abs(StickX) > 0.04)
                return "combined";
            return "none";
        }
    }

    private static double Largest(params double[] values)
    {
        double best = 0;
        foreach (var v in values)
        {
            if (Math.Abs(v) > Math.Abs(best))
                best = v;
        }
        return best;
    }
    public double ElevatorPosition { get; init; }
    public double AileronPosition { get; init; }
    public double RudderPosition { get; init; }
    public double ElevatorDeflectionPct { get; init; }
    public double AileronDeflectionPct { get; init; }
    public double RudderDeflectionPct { get; init; }
    public double ElevatorTrimPct { get; init; }
    public bool AutopilotMaster { get; init; }
    public string AutopilotLabel { get; init; } = "AP OFF";
    public double FenixAp1 { get; init; }
    public double FenixAp2 { get; init; }

    public double MassFraction
    {
        get
        {
            var span = MaxGrossWeightPounds - EmptyWeightPounds;
            if (span <= 1)
                return 0;
            return Numeric.Clamp((TotalWeightPounds - EmptyWeightPounds) / span, 0, 1);
        }
    }
}
