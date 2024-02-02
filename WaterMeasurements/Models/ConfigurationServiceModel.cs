using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WaterMeasurements.Models;

public class ConfigurationServiceConfiguration
{
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

    // Set the folder for sqlite databases.
    public static readonly string SqliteFolder = "Sqlite";
}
