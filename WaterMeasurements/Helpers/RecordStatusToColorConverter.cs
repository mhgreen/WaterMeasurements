using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using NLog;
using WaterMeasurements.Models;

namespace WaterMeasurements.Helpers;

public class RecordStatusToColorConverter : IValueConverter
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        Logger.Trace("RecordStatusToColorConverter, Convert record status to color.");
        var recordStatus = (RecordStatus)value;
        Logger.Trace("RecordStatusToColorConverter, Record status: {recordStatus}.", recordStatus);

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
