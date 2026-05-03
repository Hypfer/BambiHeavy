using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using BambiHeavy.Types;

namespace BambiHeavy.Converters;

public class InvertedBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : false;
    }
}

public class DiscoverButtonTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? b ? "Discovering..." : "Discover Lights" : "Discover Lights";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToIsVisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? b : false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StringToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s && !string.IsNullOrEmpty(s);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class RgbToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Rgb rgb
            ? new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B))
            : new SolidColorBrush(Colors.Black);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToPlayStopTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? b ? "\u25A0 Stop" : "\u25B6 Start" : "\u25B6 Start";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public enum MqttConnectionStatus
{
    Idle,
    Testing,
    Successful,
    Failed,
    LiveConnected
}

public class MqttStatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is MqttConnectionStatus status
            ? status switch
            {
                MqttConnectionStatus.Successful => Colors.Green,
                MqttConnectionStatus.Testing => Colors.Orange,
                MqttConnectionStatus.Failed => Colors.Red,
                MqttConnectionStatus.LiveConnected => Colors.Green,
                _ => Colors.Gray
            }
            : Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class MqttStatusTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is MqttConnectionStatus status
            ? status switch
            {
                MqttConnectionStatus.Successful => "Test successful",
                MqttConnectionStatus.Testing => "Testing...",
                MqttConnectionStatus.Failed => "Test failed",
                MqttConnectionStatus.LiveConnected => "Connected",
                _ => ""
            }
            : "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToEyeIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? b ? "Hide" : "Show" : "Show";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StyleToBorderBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b
            ? new SolidColorBrush(Colors.SteelBlue)
            : new SolidColorBrush(Color.Parse("#E0E0E0"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StyleToBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b ? new SolidColorBrush(Color.Parse("#E8F0FE")) : Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class IsActiveStyleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is BambiStyle style && parameter is BambiStyle active && style == active;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StyleDescriptionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is BambiStyle style
            ? style switch
            {
                BambiStyle.Standard =>
                    "Subdued daylight ambient. Heaviest compression, base gain and saturation. Minimal dark suppression.",

                BambiStyle.Cinema =>
                    "Moderate ambient for dark rooms. Medium compression, slight gain bump. High dark suppression for movie blacks.",

                BambiStyle.Sports =>
                    "Maximum vividness. Lightest compression, highest gain and saturation. Fast temporal response.",

                BambiStyle.Gaming =>
                    "Vivid and responsive. Light compression, high saturation, fastest temporal response for instant reaction.",

                _ => ""
            }
            : "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}