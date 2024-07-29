using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using NLog;
using WaterMeasurements.Models;

namespace WaterMeasurements.Helpers;

public class LocationTypeToColorConverter : IValueConverter
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        Logger.Trace("LocationTypeToColorConverter, Convert LocationType to color.");
        var locationType = (LocationType)value;
        Logger.Trace("LocationTypeToColorConverter, Location type: {locationType}.", locationType);

        return locationType switch
        {
            LocationType.Occasional
                => (SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            LocationType.Ongoing
                => (SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            _ => (SolidColorBrush)Application.Current.Resources["AccentFillColorDisabledBrush"]
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
