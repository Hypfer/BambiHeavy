namespace BambiHeavy.Models;

public class PipelineSettings
{
    public bool GainEnabled { get; set; } = true;
    public bool CompressEnabled { get; set; } = true;
    public bool SaturationEnabled { get; set; } = true;
    public bool NoiseGateEnabled { get; set; } = true;
    public bool IntegrationEnabled { get; set; } = true;

    public bool TransientBypassEnabled { get; set; } = true;
    public int TransientThreshold { get; set; } = 40;
    public int SettleThreshold { get; set; } = 15;
    public int SettleFrames { get; set; } = 3;
    public int BypassCoefficient { get; set; } = 2;
}