using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WaterMeasurements.Models;

namespace WaterMeasurements.Helpers;

internal class RecordStatusToColorConverter
{
    public static object Convert(object value)
    {
        var recordStatus = (RecordStatus)value;

        return recordStatus switch
        {
            RecordStatus.WorkingSet
                => (SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            RecordStatus.Comitted
                => (SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            _ => (SolidColorBrush)Application.Current.Resources["AccentFillColorDisabledBrush"]
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
