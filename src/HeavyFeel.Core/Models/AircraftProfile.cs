namespace HeavyFeel.Core.Models;

public sealed class AircraftProfile
{
    public string Name { get; set; } = "Medium";
    public AircraftClass Class { get; set; } = AircraftClass.NarrowBody;
    public double MassFactor { get; set; } = 1.0;
    public double PitchResponse { get; set; } = 1.0;
    public double RollResponse { get; set; } = 1.0;
    public double YawResponse { get; set; } = 1.0;
    public double InputFiltering { get; set; } = 0.18;
    public double MaximumInputModification { get; set; } = 0.28;
    public double GroundInputFactor { get; set; } = 1.08;
    public double AirInputFactor { get; set; } = 1.0;
    public double MinWriteDelta { get; set; } = 0.018;
}

public static class ProfileLibrary
{
    public static AircraftProfile Light { get; } = new()
    {
        Name = "Light",
        Class = AircraftClass.LightGa,
        MassFactor = 0.55,
        PitchResponse = 1.15,
        RollResponse = 1.18,
        YawResponse = 1.10,
        InputFiltering = 0.08,
        MaximumInputModification = 0.16,
        GroundInputFactor = 1.02,
        AirInputFactor = 0.95
    };

    public static AircraftProfile Medium { get; } = new()
    {
        Name = "Medium",
        Class = AircraftClass.NarrowBody,
        MassFactor = 1.00,
        PitchResponse = 1.00,
        RollResponse = 1.00,
        YawResponse = 1.00,
        InputFiltering = 0.26,
        MaximumInputModification = 0.42,
        GroundInputFactor = 1.08,
        AirInputFactor = 1.00
    };

    public static AircraftProfile FenixA320 { get; } = new()
    {
        Name = "Fenix A320",
        Class = AircraftClass.NarrowBody,
        MassFactor = 1.00,
        PitchResponse = 1.00,
        RollResponse = 1.00,
        YawResponse = 1.00,
        InputFiltering = 0.28,
        MaximumInputModification = 0.44,
        GroundInputFactor = 1.12,
        AirInputFactor = 1.06,
        MinWriteDelta = 0.006
    };

    public static AircraftProfile Heavy { get; } = new()
    {
        Name = "Heavy",
        Class = AircraftClass.WideBody,
        MassFactor = 1.22,
        PitchResponse = 0.90,
        RollResponse = 0.88,
        YawResponse = 0.92,
        InputFiltering = 0.22,
        MaximumInputModification = 0.32,
        GroundInputFactor = 1.12,
        AirInputFactor = 1.04
    };

    public static AircraftProfile VeryHeavy { get; } = new()
    {
        Name = "Very Heavy",
        Class = AircraftClass.HeavyWide,
        MassFactor = 1.38,
        PitchResponse = 0.84,
        RollResponse = 0.80,
        YawResponse = 0.86,
        InputFiltering = 0.26,
        MaximumInputModification = 0.36,
        GroundInputFactor = 1.16,
        AirInputFactor = 1.08
    };

    public static AircraftProfile Pmdg777 { get; } = new()
    {
        Name = "PMDG 777",
        Class = AircraftClass.WideBody,
        MassFactor = 1.28,
        PitchResponse = 0.86,
        RollResponse = 0.84,
        YawResponse = 0.90,
        InputFiltering = 0.24,
        MaximumInputModification = 0.38,
        GroundInputFactor = 1.14,
        AirInputFactor = 1.06,
        MinWriteDelta = 0.006
    };

    public static AircraftProfile For(AircraftIdentity id, AircraftClass cls)
    {
        if (id.IsFenixA320)
            return Clone(FenixA320);
        if (id.IsPmdg777)
            return Clone(Pmdg777);
        return cls switch
        {
            AircraftClass.LightGa => Clone(Light),
            AircraftClass.Regional => Clone(Medium),
            AircraftClass.WideBody => Clone(Heavy),
            AircraftClass.HeavyWide => Clone(VeryHeavy),
            _ => Clone(Medium)
        };
    }

    private static AircraftProfile Clone(AircraftProfile p) => new()
    {
        Name = p.Name,
        Class = p.Class,
        MassFactor = p.MassFactor,
        PitchResponse = p.PitchResponse,
        RollResponse = p.RollResponse,
        YawResponse = p.YawResponse,
        InputFiltering = p.InputFiltering,
        MaximumInputModification = p.MaximumInputModification,
        GroundInputFactor = p.GroundInputFactor,
        AirInputFactor = p.AirInputFactor,
        MinWriteDelta = p.MinWriteDelta
    };
}
