namespace HeavyFeel.Core.Physics;

/// <summary>
/// One frame of values the SimConnect client may write.
/// Inactive commands must not be sent.
/// </summary>
public sealed record InfluenceCommand
{
    public bool Active { get; init; }
    public bool WriteRates { get; init; }
    public bool WriteAxes { get; init; }
    public bool WriteYoke { get; init; }
    public double YokeXOut { get; init; }
    public double YokeYOut { get; init; }
    public double RudderOut { get; init; }

    public double RateX { get; init; }
    public double RateY { get; init; }
    public double RateZ { get; init; }

    public int ElevatorAxis { get; init; }
    public int AileronAxis { get; init; }
    public int RudderAxis { get; init; }
    public int ThrottleAxis { get; init; }
    public bool WriteThrottle { get; init; }

    public string Reason { get; init; } = "idle";
    public string Phase { get; init; } = "";
    public string ProfileName { get; init; } = "";
    public double Mix { get; init; }
    public double RawPitch { get; init; }
    public double FilteredPitch { get; init; }
    public double OutPitch { get; init; }
    public double RawRoll { get; init; }
    public double FilteredRoll { get; init; }
    public double OutRoll { get; init; }
    public double RawYaw { get; init; }
    public double FilteredYaw { get; init; }
    public double OutYaw { get; init; }
    public double AirspeedFactor { get; init; } = 1;

    public static InfluenceCommand Idle(string reason) => new()
    {
        Active = false,
        Reason = reason
    };
}
