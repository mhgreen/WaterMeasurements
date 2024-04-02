using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Esri.ArcGISRuntime.Data;

namespace WaterMeasurements.Helpers;

public class FeatureToType<T1, ConvertedSuccess>(T1 value, ConvertedSuccess convertedOK)
{
    public T1 Value { get; } = value;
    public ConvertedSuccess Success { get; } = convertedOK;

    public FeatureToType<short?, bool> ConvertInt16ToShort(string attribute, Feature feature)
    {
        if (feature.Attributes.TryGetValue(attribute, out var value))
        {
            if (value is short v)
            {
                return new FeatureToType<short?, bool>(v, true);
            }
        }
        return new FeatureToType<short?, bool>(null, false);
    }

    public FeatureToType<int?, bool> ConvertInt32ToInt(string attribute, Feature feature)
    {
        if (feature.Attributes.TryGetValue(attribute, out var value))
        {
            if (value is int v)
            {
                return new FeatureToType<int?, bool>(v, true);
            }
        }
        return new FeatureToType<int?, bool>(null, false);
    }

    public FeatureToType<long?, bool> ConvertInt64ToLong(string attribute, Feature feature)
    {
        if (feature.Attributes.TryGetValue(attribute, out var value))
        {
            if (value is long v)
            {
                return new FeatureToType<long?, bool>(v, true);
            }
        }
        return new FeatureToType<long?, bool>(null, false);
    }

    public FeatureToType<double?, bool> ConvertFloat64ToDouble(string attribute, Feature feature)
    {
        if (feature.Attributes.TryGetValue(attribute, out var value))
        {
            if (value is double v)
            {
                return new FeatureToType<double?, bool>(v, true);
            }
        }
        return new FeatureToType<double?, bool>(null, false);
    }

    public FeatureToType<string?, bool> ConvertTextToString(string attribute, Feature feature)
    {
        if (feature.Attributes.TryGetValue(attribute, out var value))
        {
            if (value is string v)
            {
                return new FeatureToType<string?, bool>(v, true);
            }
        }
        return new FeatureToType<string?, bool>(null, false);
    }

    public FeatureToType<Guid?, bool> ConvertGlobalIdToGuid(string attribute, Feature feature)
    {
        if (feature.Attributes.TryGetValue(attribute, out var value))
        {
            if (value is Guid guid)
            {
                return new FeatureToType<Guid?, bool>(guid, true);
            }
        }
        return new FeatureToType<Guid?, bool>(null, false);
    }

    public FeatureToType<DateTime?, bool> ConvertDateTimeToDateTime(
        string attribute,
        Feature feature
    )
    {
        if (feature.Attributes.TryGetValue(attribute, out var value))
        {
            if (value is DateTime dateTime)
            {
                return new FeatureToType<DateTime?, bool>(dateTime, true);
            }
        }
        return new FeatureToType<DateTime?, bool>(null, false);
    }
}
