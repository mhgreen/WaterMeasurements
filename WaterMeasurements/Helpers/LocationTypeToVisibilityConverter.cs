﻿using Microsoft.UI.Xaml;
using WaterMeasurements.Models;

namespace WaterMeasurements.Helpers;

internal class LocationTypeToVisibilityConverter
{
    public static object Convert(object value)
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
