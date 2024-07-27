using Esri.ArcGISRuntime.Geometry;
using WaterMeasurements.Contracts.Templates;

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
    GeoTriggerFenceEntered,
    GeoTriggerFenceExited,
    BeginMeasurement,
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
        SecchiLocationsSqliteLoaded,
        SecchiCollectOutAndBack
    }

    public static Dictionary<Key, string> Item { get; private set; } =
        new()
        {
            { Key.SecchiInitialRun, "SecchiInitialRun" },
            { Key.SecchiObservationsGeodatabase, "SecchiObservationsURL" },
            { Key.SecchiLocationsGeodatabase, "SecchiLocationsURL" },
            { Key.GeoTriggerDistanceMeters, "SecchiGeoTriggerDistance" },
            { Key.SecchiObservationsSqliteLoaded, "SecchiObservationsSqliteLoaded" },
            { Key.SecchiLocationsSqliteLoaded, "SecchiLocationsSqliteLoaded" },
            { Key.SecchiCollectOutAndBack, "SecchiCollectOutAndBack" }
        };
}

// Location class for the SecchiViewModel service.
public class SecchiLocationDisplay(
    string locationName,
    double latitude,
    double longitude,
    LocationType locationType,
    int locationId,
    RecordStatus recordStatus
) : ILocationsDisplay
{
    public string LocationName { get; private set; } = locationName;
    public double Latitude { get; private set; } = latitude;
    public double Longitude { get; private set; } = longitude;
    public LocationType LocationType { get; private set; } = locationType;
    public int LocationId { get; private set; } = locationId;
    public RecordStatus RecordStatus { get; private set; } = recordStatus;
    public string LatLon => $"Lat: {Latitude:F4}, Lon: {Longitude:F4}";
}

public readonly record struct SecchiChannelNumbersMessage(
    uint ObservationChannel,
    uint LocationChannel,
    uint GeoTriggerChannel
);

public readonly record struct LocationNameAndId(string LocationName, int LocationId);

// Source and type of location for adding a new location.
public struct SecchiAddLocation
{
    public LocationType? LocationType { get; set; }
    public LocationSource? LocationSource { get; set; }
    public int? LocationNumber { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? LocationName { get; set; }
    public MapPoint? Location { get; set; }
}

// Secchi location collection class for the SecchiViewModel service.
public class SecchiCollectionDisplay(
    string locationName,
    double latitude,
    double longitude,
    int locationId,
    int obs1,
    int obs2,
    int obs3,
    double secchiDepth,
    DateTimeOffset collectionDate,
    long ObjectId
)
{
    public string LocationName { get; private set; } = locationName;
    public double? Latitude { get; set; } = latitude;
    public double? Longitude { get; set; } = longitude;
    public int LocationId { get; private set; } = locationId;
    public int Obs1 { get; private set; } = obs1;
    public int Obs2 { get; private set; } = obs2;
    public int Obs3 { get; private set; } = obs3;
    public double SecchiDepth { get; private set; } = secchiDepth;
    public DateTimeOffset CollectionDate { get; private set; } = collectionDate;
    public long ObjectId { get; private set; } = ObjectId;
    public string DateLatLon =>
        $"{CollectionDate:MM/dd/yy H:mm} Lat: {Latitude:F4}, Lon: {Longitude:F4}";
    public string Observations =>
        $"Obs1: {Obs1}, Obs2: {Obs2}, Obs3: {Obs3}, Secchi: {SecchiDepth:F0}";
}
