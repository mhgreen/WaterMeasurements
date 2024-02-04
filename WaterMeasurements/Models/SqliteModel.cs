using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WaterMeasurements.Models;

public static class SqliteConversion
{

    public static Dictionary<string, string> GeodatabaseSqliteTypeConversion
    {
        get; private set;
    }
        = new()
    {
    {"Int16", "INTEGER"},
    {"Int32", "INTEGER"},
    {"Int64", "INTEGER"},
    {"Float32", "REAL"},
    {"Float64", "REAL"},
    {"Date", "NUMERIC"},
    {"Text", "TEXT"},
    {"OID", "NUMERIC"},
    {"GlobalID", "TEXT"},
    {"Guid", "TEXT"},
    {"XML", "TEXT"},
    {"Geometry", "BLOB"},
    {"Raster", "BLOB"},
    {"Blob", "BLOB"}
    };
}

public enum DbType
{
    SecchiObservations,
    SecchiLocations
}

public enum  ObservationStatus
{
    WorkingSet,
    GeodatabaseCommitted,
    LocalOnly
}

// The strings in the dictionary could easily be used instead of the enum.
// This class is used to document the configuration settings and make their use in code obvious.
public static class SqliteConfiguration
{
    public enum Key
    {
        SqliteInitialRun,
        SqliteFolder,
        SecchiObservationsLoaded,
        SecchiLocationsLoaded
    }

    public static Dictionary<Key, string> Item
    {
        get; private set;
    } =
        new()
        {
            { Key.SqliteInitialRun, "SqliteInitialRun" },
            { Key.SqliteFolder, "SqliteFolder" },
            { Key.SecchiObservationsLoaded, "SecchiObservationsSqliteLoaded" },
            { Key.SecchiLocationsLoaded, "SecchiLocationsSqliteLoaded" }
        };
}
