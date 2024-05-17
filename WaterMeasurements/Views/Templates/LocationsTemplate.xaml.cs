using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Location;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;
using Esri.ArcGISRuntime.UI.Editing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NLog;
using WaterMeasurements.Models;
using Windows.Media.Capture.Frames;

namespace WaterMeasurements.Views.Templates;

public abstract partial class LocationsTemplate
{
    private readonly double latitude;
    private readonly double longitude;
    private readonly LocationType locationType;
    private readonly int locationId;

    public string LocationName => LocationName;
    public double Latitude => latitude;
    public double Longitude => longitude;
    public LocationType LocationType => locationType;
    public int LocationId => locationId;
    public string LatLon => $"Lat: {Latitude:F4}, Lon: {Longitude:F4}";

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public virtual void Edit_Location_Click(object sender, RoutedEventArgs eventArgs)
    {
        _ = eventArgs;
        // Log to trace that the Edit_Location_Click method was called.
        Logger.Trace(
            "LocationsTemplate.xaml.cs, Edit_Location_Click: Edit_Location_Click method called."
        );

        var button = sender as Button;
        try
        {
            Guard.Against.Null(button, nameof(button), "Button in Edit_Location_Click is null.");

            var locationId = (int)button.Tag;

            // Log to trace the value of sender and eventArgs.
            Logger.Trace(
                "LocationsTemplate.xaml.cs, Edit_Location_Click: LocationId: {locationId}",
                locationId
            );
        }
        catch (Exception exception)
        {
            // Log to trace the exception message.
            Logger.Error(
                exception,
                "LocationsTemplate.xaml.cs, Edit_Location_Click: An error occurred in Edit_Location_Click: {exception}",
                exception.Message
            );
        }
    }

    public virtual void Delete_Location_Click(object sender, RoutedEventArgs eventArgs)
    {
        _ = eventArgs;
        // Log to trace that the Delete_Location_Click method was called.
        Logger.Trace(
            "LocationsTemplate.xaml.cs, Delete_Location_Click: Delete_Location_Click method called."
        );

        var button = sender as Button;
        try
        {
            Guard.Against.Null(button, nameof(button), "Button in Delete_Location_Click is null.");

            // Log to trace the name of the button.
            Logger.Trace(
                "LocationsTemplate.xaml.cs, Delete_Location_Click: Button Name: {button.Name}",
                button.Name
            );

            // Get the locationId from the button's tag.
            var locationId = (int)button.Tag;

            // Log to trace the value of sender and eventArgs.
            Logger.Trace(
                "LocationsTemplate.xaml.cs, Delete_Location_Click: LocationId: {locationId}",
                locationId
            );
        }
        catch (Exception exception)
        {
            // Log to trace the exception message.
            Logger.Error(
                exception,
                "LocationsTemplate.xaml.cs, Delete_Location_Click: An error occurred in Delete_Location_Click: {exception}",
                exception.Message
            );
        }
    }
}
