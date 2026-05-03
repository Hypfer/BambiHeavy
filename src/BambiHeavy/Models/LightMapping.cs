using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BambiHeavy.Models;

public partial class LightMapping : ObservableObject
{
    [ObservableProperty] private double _brightness = 1.0;
    [ObservableProperty] private string _friendlyName = "";

    [JsonIgnore] [ObservableProperty] private bool _isStale;

    [ObservableProperty] private string _zone = "";
    public ushort ShortAddress { get; set; }

    [JsonIgnore] public bool NeedsSave { get; internal set; }

    [JsonIgnore] public bool Enabled => !string.IsNullOrEmpty(Zone);

    public string? IeeeAddress { get; set; }
    public string? ModelId { get; set; }

    partial void OnZoneChanged(string value)
    {
        OnPropertyChanged(nameof(Enabled));
        NeedsSave = true;
    }

    partial void OnBrightnessChanged(double value)
    {
        NeedsSave = true;
    }
}