using CommunityToolkit.Mvvm.ComponentModel;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using WaterMeasurements.ViewModels;

namespace WaterMeasurements.Models;

// Record for UIQueue subscription.
public readonly record struct UIQueue(DispatcherQueue? UIDispatcherQueue);

// Record to notify modules that the MapPage has been unloaded.
public readonly record struct MapPageUnloadedMessage();

public readonly record struct SecchiMeasurements(
    MapPoint Location,
    int Measurement1,
    int Measurement2,
    int Measurement3
);

// Observable to update the map on the UI.
public partial class SecchiPageSelection : ObservableRecipient
{
    [ObservableProperty]
    public string? secchiSelectView;
}
