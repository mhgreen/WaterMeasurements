using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using WaterMeasurements.Models;

namespace WaterMeasurements.Helpers;
public class LocationTypeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var locationType = (LocationType)value;

        return locationType switch
        {
            LocationType.OneTime => (SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            LocationType.Permanent => (SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            _ => (SolidColorBrush)Application.Current.Resources["AccentFillColorDisabledBrush"],
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}