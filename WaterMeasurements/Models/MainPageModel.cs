using Esri.ArcGISRuntime.Geometry;
using Microsoft.UI.Dispatching;

namespace WaterMeasurements.Models;

// Record for UIQueue subscription.
public readonly record struct UIQueue(DispatcherQueue? UIDispatcherQueue);

public readonly record struct SecchiMeasurements(
    MapPoint Location,
    int Measurement1,
    int Measurement2,
    int Measurement3
);

// Record to notify modules that the MapPage has been unloaded.
public readonly record struct MapPageUnloadedMessage();
