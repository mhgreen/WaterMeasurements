using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


using CommunityToolkit.Mvvm.ComponentModel;

using Esri.ArcGISRuntime.Portal;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Tasks;
using Esri.ArcGISRuntime.Tasks.Offline;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Services;
using Windows.Networking.Connectivity;

namespace WaterMeasurements.Models;

// Observable to update the map on the UI.
public partial class GetPreplannedMapModel : ObservableRecipient
{
    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    public Map? map;
}

// Record for Map extent subscription.
public readonly record struct MainMapExtent(Envelope Extent);

// Record for preplaned map configuration.
public readonly record struct PreplannedMapConfiguration
(
    bool ArcGISApiConfigured,
    bool OfflineMapIdConfigured
);

// State of the GetPreplannedMap service.
public enum PreplannedMapState
{
    Undefined,
    Initialization,
    IsInternetAvailable,
    SyncReady,
    UseLocal,
    AppClosing
}

// Triggers for the GetPreplannedMap service.
public enum PreplannedMapTrigger
{
    Startup,
    AppClosing,
    InitializationComplete,
    InternetUnavailableRecieved,
    InternetAvailableRecieved,
    LocalPreplannedMapExists,
    LocalPreplannedMapDoesNotExist,
    Cancel
}