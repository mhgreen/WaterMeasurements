using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using WaterMeasurements.Models;

namespace WaterMeasurements.Helpers;

internal class LocationTypeToVisibilityConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var locationType = (LocationType)value;

        return locationType switch
        {
            LocationType.OneTime => Visibility.Visible,
            LocationType.Permanent => Visibility.Collapsed,
            _ => Visibility.Collapsed,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
