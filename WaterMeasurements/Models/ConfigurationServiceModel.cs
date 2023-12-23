using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WaterMeasurements.Models;

// Record for preplaned map configuration.
// This includes ArcGIS API key as eveything else is triggered by a valid starting map.
public readonly record struct PreplannedMapConfiguration
(
    bool ArcGISApiConfigured,
    bool OfflineMapIdConfigured
);

public class ConfigurationKey
{
    // This is used to make sure that the retrieved map is the correct one.
    public static readonly string PreplannedMapNameKey = "PreplannedMapName";

    // Key for map package path which is stored when the map is downloaded and is used for offline retrieval.
    public static readonly string PackagePathKey = "MapPakagePath";

    // Key for DateTime when map was last checked for updates.
    public static readonly string MapLastCheckedforUpdateKey = "LastMapUpdateChecked";

    // Key for hours between update checks.
    public static readonly string HoursBetweenUpdateChecksKey = "HoursBetweenUpdateChecks";

    // Key to cause deletion of offline map package (true = delete, false = keep).
    public static readonly string DeleteOfflineMapKey = "DeleteOfflineMap";

    // Key to cause download of offline map package (true = cause download, false = regular operation).
    public static readonly string DownloadOfflineMapKey = "DownloadOfflineMap";

    // Key for ArcGIS API key.
    public static readonly string ArcgisApiKey = "ArcGISApiKey";

    // Key for preplanned map identifier.
    public static readonly string OfflineMapIdentifier = "OfflineMapIdentifier";

    // Set the base folder for storing items associated with this application.
    public static readonly string WaterMeasurementsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WaterMeasurements"
    );

    // Set the offline data folder to store preplanned map.
    public static readonly string OfflineDataFolder = Path.Combine(
        WaterMeasurementsFolder,
        "Map",
        "DownloadPreplannedMapAreas"
    );
}
