using System.Drawing;
using CommunityToolkit.Mvvm.ComponentModel;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Location;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;
using Esri.ArcGISRuntime.UI.Editing;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using NLog;
using WaterMeasurements.ViewModels;

namespace WaterMeasurements.Models;

// Record for UIQueue subscription.
public readonly record struct UIQueue(DispatcherQueue? UIDispatcherQueue);

// Record to notify modules that the MapPage has been unloaded.
public readonly record struct MapPageUnloadedMessage();

public enum CurrentMeasurement
{
    Secchi,
    Turbidity,
    Quality,
    Temperature
}

public static class MapSymbols
{
    // Marker symbol for a collection location.
    public static readonly SimpleMarkerSymbol CollectionLocationSymbol =
        new(SimpleMarkerSymbolStyle.Circle, Color.FromArgb(255, 0, 120, 212), 8);

    // Symbol for location highlighting.
    public static readonly SimpleMarkerSymbol HighlightLocationSymbol =
        new()
        {
            // Create a clear color, make it a bit bigger than the point on the map.
            // Then create a circle with a black outline.
            // The result is a black circle with a clear center that can be used as a highlight.
            Color = Color.FromArgb(0, 0, 0, 0),
            Size = 3,
            Style = SimpleMarkerSymbolStyle.Circle,
            Outline = new SimpleLineSymbol(
                SimpleLineSymbolStyle.Solid,
                Color.FromArgb(255, 174, 232, 255),
                3
            )
        };

    // Cross marker symbol.
    public static readonly SimpleMarkerSymbol CrossMarkerSymbol =
        new(SimpleMarkerSymbolStyle.Cross, Color.FromArgb(255, 23, 217, 232), 10);

    public static readonly GeometryEditorStyle GeometryEditorStyle =
        new()
        {
            VertexSymbol = new SimpleMarkerSymbol(
                SimpleMarkerSymbolStyle.Circle,
                Color.FromArgb(255, 255, 0, 0),
                10
            ),
            LineSymbol = new SimpleLineSymbol(
                SimpleLineSymbolStyle.Solid,
                Color.FromArgb(255, 0, 0, 255),
                2
            ),
            FillSymbol = new SimpleFillSymbol(
                SimpleFillSymbolStyle.Solid,
                Color.FromArgb(100, 0, 0, 255),
                new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, Color.FromArgb(255, 0, 0, 255), 2)
            ),
            SelectedVertexSymbol = MapSymbols.CrossMarkerSymbol
        };
}

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
