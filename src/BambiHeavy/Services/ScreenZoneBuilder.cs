using BambiHeavy.Models;

namespace BambiHeavy.Services;

public static class ScreenZoneBuilder
{
    public static List<ScreenZone> BuildFromSettings()
    {
        var mappings = Config.Settings.LightMappings.Where(m => !string.IsNullOrEmpty(m.Zone)).ToList();
        var zones = mappings.GroupBy(m => m.Zone).OrderBy(g => g.Key);
        return zones.Select(g => new ScreenZone(g.Key,
            g.Select(m => new Light(m.FriendlyName, m.ShortAddress, m.Brightness)).ToList())).ToList();
    }
}