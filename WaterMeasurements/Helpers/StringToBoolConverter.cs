using Microsoft.UI.Xaml.Data;

namespace WaterMeasurements.Helpers;

public class StringToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string strValue)
        {
            return strValue.ToLower() == "true";
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue ? "True" : "False";
        }
        return "False";
    }
}
