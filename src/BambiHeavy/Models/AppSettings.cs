namespace BambiHeavy.Models;

public class AppSettings
{
    public string MqttBrokerUrl { get; set; } = "";
    public int MqttBrokerPort { get; set; } = 1883;
    public string MqttUsername { get; set; } = "";
    public string MqttPassword { get; set; } = "";
    public bool MqttUseTls { get; set; } = false;
    public string MqttClientId { get; set; } = "";

    public List<LightMapping> LightMappings { get; set; } = new();

    public BambiStyle ActiveStyle { get; set; } = BambiStyle.Standard;
    public double GlobalBrightnessLimit { get; set; } = 0.7;
    public int NetworkFps { get; set; } = 15;

    public PipelineSettings Pipeline { get; set; } = new();
}