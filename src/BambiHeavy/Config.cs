using BambiHeavy.Models;

namespace BambiHeavy;

public enum BambiStyle
{
    Standard,
    Cinema,
    Sports,
    Gaming
}

public class TuningProfile
{
    public TuningProfile(double compressExponent, double gainFactor, double satBoostFactor, double satBlendFactor,
        double noiseGateThreshold,
        int framesLumaAttack, int framesLumaDecay, int framesChromaAttack, int framesChromaDecay)
    {
        CompressExponent = compressExponent;
        GainFactor = gainFactor;
        SatBoostFactor = satBoostFactor;
        SatBlendFactor = satBlendFactor;
        NoiseGateThreshold = noiseGateThreshold;
        FramesLumaAttack = framesLumaAttack;
        FramesLumaDecay = framesLumaDecay;
        FramesChromaAttack = framesChromaAttack;
        FramesChromaDecay = framesChromaDecay;
    }

    public double
        CompressExponent { get; } // Power curve exponent for Compress stage: 3.0 = heavy crush, 2.0 = squaring

    public double GainFactor { get; } // Multiplier applied to base gain (1.5×): 1.0 = standard, 1.21 = sports

    public double
        SatBoostFactor { get; } // Multiplier applied to base saturation boost (2.0×): 1.0 = standard, 1.25 = sports

    public double
        SatBlendFactor
    {
        get;
    } // Saturation floor the adaptive gate blends down to (base 1.0×): 1.0 = standard, 1.12 = sports

    public double NoiseGateThreshold { get; } // percentage 0-100, signals below this brightness are suppressed
    public int FramesLumaAttack { get; }
    public int FramesLumaDecay { get; }
    public int FramesChromaAttack { get; }
    public int FramesChromaDecay { get; }
}

public static class Config
{
    public const string Z2MBaseTopic = "zigbee2mqtt";

    public const int EdgePixels = 1;

    private static TuningProfile? _cachedProfile;
    private static BambiStyle _cachedStyle;
    private static readonly Lock ProfileLock = new();
    public static AppSettings Settings { get; set; } = new();

    public static BambiStyle ActiveStyle
    {
        get => Settings.ActiveStyle;
        set => Settings.ActiveStyle = value;
    }

    public static TuningProfile GetProfile()
    {
        lock (ProfileLock)
        {
            if (_cachedProfile != null && _cachedStyle == ActiveStyle)
                return _cachedProfile;

            _cachedStyle = ActiveStyle;
            return _cachedProfile = ActiveStyle switch
            {
                // CompressExponent: 3.0 = heavy crush (daylight), 2.6 = medium (cinema), 2.0 = squaring (sports/gaming)
                // GainFactor: relative to base 1.5x — Cinema/Gaming +7%, Sports +21%
                // SatBoostFactor: relative to base 2.0x — Sports/Gaming +25%
                // SatBlendFactor: saturation floor for adaptive gate — Sports 1.12x, others 1.0x
                // NoiseGateThreshold: percentage 0-100, signals below this brightness are suppressed
                BambiStyle.Standard => new TuningProfile(3.0, 1.0, 1.0, 1.0, 6.25, 8, 16, 8, 10),
                BambiStyle.Cinema => new TuningProfile(2.6, 1.07, 1.0, 1.0, 31.25, 8, 16, 8, 10),
                BambiStyle.Sports => new TuningProfile(2.0, 1.21, 1.25, 1.12, 43.75, 6, 12, 6, 8),
                BambiStyle.Gaming => new TuningProfile(2.0, 1.07, 1.25, 1.0, 31.25, 3, 2, 3, 2),
                _ => new TuningProfile(3.0, 1.0, 1.0, 1.0, 6.25, 8, 16, 8, 10)
            };
        }
    }

    public static void Reset()
    {
        lock (ProfileLock)
        {
            _cachedProfile = null;
            _cachedStyle = default;
            Settings.ActiveStyle = BambiStyle.Standard;
        }
    }
}