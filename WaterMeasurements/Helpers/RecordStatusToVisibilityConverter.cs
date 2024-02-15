using Microsoft.UI.Xaml;
using WaterMeasurements.Models;

namespace WaterMeasurements.Helpers;

internal class RecordStatusToVisibilityConverter
{
    public static object Convert(object value)
    {
        var recordStatus = (RecordStatus)value;

        return recordStatus switch
        {
            RecordStatus.WorkingSet => Visibility.Visible,
            RecordStatus.Comitted => Visibility.Collapsed,
            _ => Visibility.Collapsed,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
