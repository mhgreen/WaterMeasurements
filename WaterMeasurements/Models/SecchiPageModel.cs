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

// The strings in the dictionary could easily be used instead of the enum.
// This class is used to document the configuration settings and make their use in code obvious.
public static class SecchiConfiguration
{
    public enum Key
    {
        SecchiInitialRun,
        SecchiObservationsGeodatabase,
        SecchiLocationsGeodatabase,
        GeoTriggerDistanceMeters,
        SecchiObservationsSqliteLoaded,
        SecchiLocationsSqliteLoaded
    }

    public static Dictionary<Key, string> Item
    {
        get; private set;
    } =
        new()
        {
            { Key.SecchiInitialRun, "SecchiInitialRun" },
            { Key.SecchiObservationsGeodatabase, "SecchiObservationsURL" },
            { Key.SecchiLocationsGeodatabase, "SecchiLocationsURL" },
            { Key.GeoTriggerDistanceMeters, "SecchiGeoTriggerDistance" },
            { Key.SecchiObservationsSqliteLoaded, "SecchiObservationsSqliteLoaded" },
            { Key.SecchiLocationsSqliteLoaded, "SecchiLocationsSqliteLoaded" }
        };
}