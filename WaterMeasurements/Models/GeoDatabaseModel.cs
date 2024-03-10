using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Tasks;
using Esri.ArcGISRuntime.Tasks.Offline;
using Esri.ArcGISRuntime.UI.Editing;
using Stateless;

namespace WaterMeasurements.Models;

// Observable to update the geomap download progress on the UI.
public partial class GeodatabaseDownload : ObservableRecipient
{
    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    public GeoDatabaseDownloadProgress progress;
}

// Record for the request to download a geodatabase.
public readonly record struct GeoDatabaseRetrieveRequest(
    string Name,
    GeoDatabaseType GeoDatabaseType,
    uint Channel,
    string Url,
    bool CauseGeoDatabaseDownload
);

// Record for the request to delete a geodatabase.
public readonly record struct GeoDatabaseDeleteRequest(
    string Name,
    GeoDatabaseType GeoDatabaseType,
    string Url
);

/*
// Record to request the currentMapEnvelope from the GeodatabaseService.
public readonly record struct MapEnvelopeRequest(Envelope MapEnvelope);
*/

// Record for observation of the geodatabase download progress.
// TODO: Remove this record and use the GeoDatabaseDownloadProgressMessage instead.
public readonly record struct GeoDatabaseDownloadProgress(double PercentDownloaded);

// Message for geodatabase download progress.
public readonly record struct GeoDatabaseDownloadInstanceProgress(double PercentDownloaded);

// Message to request a Geodatabase state change.
public readonly record struct GeodatabaseStateChange(GeoDbOperation StateRequest);

// Record to add a feature to a feature table.
public readonly record struct FeatureAddMessage(string FeatureTable, Feature FeatureToAdd);

// Record to delete a feature from a feature table.
public readonly record struct FeatureDeleteMessage(string FeatureTable, Feature FeatureToDelete);

// Record to update a feature in a feature table.
public readonly record struct FeatureUpdateMessage(string FeatureTable, Feature FeatureToUpdate);

// Geodatabase type.
public enum GeoDatabaseType
{
    Observations,
    Locations
}

// Geodatabase operations.
public enum GeoDbOperation
{
    BeginTransaction,
    Commit,
    Rollback
}

// State of the geodatabase service.
public enum GeoDbServiceState
{
    Undefined,
    IsInternetAvailable,
    SyncReady,
    UseLocal,
    DownloadGeodatabase,
    SyncGeodatabase,
    AppClosing
}

// Triggers for the geodatabase service.
public enum GeoDbServiceTrigger
{
    Startup,
    AppClosing,
    InternetUnavailableRecieved,
    InternetAvailableRecieved,
    MapEnvelopeReceived,
    MapEnvelopeHasBeenSet,
    LocalGeoDatabaseExists,
    LocalGeoDatabaseDoesNotExist,
    GeoDatabaseStateChange,
    GeoDatabaseAddFeature,
    GeoDatabaseDeleteFeature,
    GeoDatabaseUpdateFeature,
    GeoDatabaseSecchiMeasurementReceived,
    FeatureTableRequestReceived,
    Cancel
}
