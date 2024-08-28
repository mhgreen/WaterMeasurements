using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using NLog;
using WaterMeasurements.Models;

namespace WaterMeasurements.Helpers;

public class RecordStatusToVisibilityConverter : IValueConverter
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        Logger.Trace("RecordStatusToVisibilityConverter, Convert record status to visibility.");
        var recordStatus = (RecordStatus)value;
        Logger.Trace(
            "RecordStatusToVisibilityConverter, Record status: {recordStatus}.",
            recordStatus
        );

        return recordStatus switch
        {
            RecordStatus.WorkingSet => Visibility.Visible,
            RecordStatus.Comitted => Visibility.Collapsed,
            _ => Visibility.Visible,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
