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

// The strings in the dictionary could easily be used instead of the enum.
// // This class is used to document the configuration settings and make their use in code obvious.
public static class PrePlannedMapConfiguration
{
    public enum Key
    {
        PreplannedMapInitialRun,
        PreplannedMapName,
        PackagePath,
        MapLastCheckedforUpdate,
        HoursBetweenUpdateChecks,
        DeleteOfflineMap,
        DownloadOfflineMap,
        ArcgisApiKey,
        OfflineMapIdentifier
    }

    public static Dictionary<Key, string> Item
    {
        get; private set;
    } =
        new()
        {
            { Key.PreplannedMapInitialRun, "PreplannedMapInitialRun" },
            { Key.PreplannedMapName, "PreplannedMapName" },
            { Key.PackagePath, "MapPakagePath" },
            { Key.MapLastCheckedforUpdate, "MapLastCheckedforUpdate" },
            { Key.HoursBetweenUpdateChecks, "HoursBetweenUpdateChecks" },
            { Key.DeleteOfflineMap, "DeleteOfflineMap" },
            { Key.DownloadOfflineMap, "DownloadOfflineMap" },
            { Key.ArcgisApiKey, "ArcGISApiKey" },
            { Key.OfflineMapIdentifier, "OfflineMapIdentifier" }
        };
}
