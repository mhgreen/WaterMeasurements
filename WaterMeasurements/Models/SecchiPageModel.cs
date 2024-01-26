using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WaterMeasurements.Models;

// State of the SecchiViewModel service.
public enum SecchiServiceState
{
    WaitingForObservations,
    WaitingForLocations,
    HaveObservationsAndLocations,
    Running,
    RunningInternetAvailable,
    RunningInternetUnavailable,
    AppClosing
}

// Triggers for the SecchiViewModel service.
public enum SecchiServiceTrigger
{
    Startup,
    AppClosing,
    InternetUnavailableRecieved,
    InternetAvailableRecieved,
    ObservationFeatureTableReceived,
    LocationFeatureTableReceived,
    ObservationAndLocationFeatureTablesReceived,
    UiThreadRecievedorPresent,
    Cancel
}

public static class SecchiConfiguration
{
    public enum Key
    {
        InitialRun,
        SecchiObservationsGeodatabase,
        SecchiLocationsGeodatabase,
        GeoTriggerDistanceMeters
    }

    public static Dictionary<Key, string> Item
    {
        get; private set;
    } =
        new()
        {
            { Key.InitialRun, "SecchiInitialRun" },
            { Key.SecchiObservationsGeodatabase, "SecchiObservationsURL" },
            { Key.SecchiLocationsGeodatabase, "SecchiLocationsURL" },
            { Key.GeoTriggerDistanceMeters, "SecchiGeoTriggerDistance" }
        };
}