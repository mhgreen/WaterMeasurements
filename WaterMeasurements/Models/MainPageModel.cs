using CommunityToolkit.Mvvm.ComponentModel;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using NLog;
using WaterMeasurements.ViewModels;

namespace WaterMeasurements.Models;

// Record for UIQueue subscription.
public readonly record struct UIQueue(DispatcherQueue? UIDispatcherQueue);

// Record to notify modules that the MapPage has been unloaded.
public readonly record struct MapPageUnloadedMessage();

public struct SecchiMeasurement
{
    public MapPoint Location { get; set; }
    public int Measurement1 { get; set; }
    public int Measurement2 { get; set; }
    public int Measurement3 { get; set; }
}

// Observable to update the map on the UI.
public partial class SecchiPageSelection : ObservableRecipient
{
    [ObservableProperty]
    public string? secchiSelectView;
}
