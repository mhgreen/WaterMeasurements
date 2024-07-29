using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using NLog;
using WaterMeasurements.Models;

namespace WaterMeasurements.Helpers;

public class LocationTypeToVisibilityConverter : IValueConverter
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        Logger.Debug("LocationTypeToVisibilityConverter, Convert LocationType to visibility.");
        var locationType = (LocationType)value;
        Logger.Debug(
            "LocationTypeToVisibilityConverter, Location type: {locationType}.",
            locationType
        );

        return locationType switch
        {
            LocationType.Occasional => Visibility.Visible,
            LocationType.Ongoing => Visibility.Collapsed,
            _ => Visibility.Collapsed,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
