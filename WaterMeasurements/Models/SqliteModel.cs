using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Esri.ArcGISRuntime.Data;

namespace WaterMeasurements.Models;

public enum DbType
{
    SecchiObservations,
    SecchiLocations
}

public enum RecordStatus
{
    WorkingSet,
    GeodatabaseCommitted
}

public enum LocationType
{
    Permanent,
    OneTime
}

// Record to create a table from a feature table.
public readonly record struct FeatureToTable(FeatureTable FeatureTable, DbType DbType);

// Record to request a group of records from Sqlite.
public readonly record struct SqliteRecordsGroupRequest(DbType DbType, int PageSize, int Offset);

// Record for Secchi observation.
public readonly record struct SecchiObservation(
    int Measurement1,
    int Measurement2,
    int Measurement3,
    double Secchi,
    int LocationId,
    DateTime DateCollected,
    double Latitude,
    double Longitude
);

// Record for Secchi location.
public readonly record struct SecchiLocation(
    double Latitude,
    double Longitude,
    int LocationId,
    string Location,
    LocationType LocationType
);

public enum FeatureToTableStatus
{
    Success,
    SuccessWithPartialRecords,
    SuccessNoRecords,
    Failure
}

// Record with the result of a table creation.
// In the case of a failure, the return code and error are from Sqlite.
// Where the return code is negative, that and the error message are from the application.
public readonly record struct FeatureToTableResult(
    DbType TableType,
    int RecordsInserted,
    int ReturnCode,
    string ErrorMessage,
    FeatureToTableStatus Status
);

public static class SqliteConversion
{
    public static Dictionary<string, string> GeodatabaseSqliteTypeConversion { get; private set; } =
        new()
        {
            { "Int16", "INTEGER" },
            { "Int32", "INTEGER" },
            { "Int64", "INTEGER" },
            { "Float32", "REAL" },
            { "Float64", "REAL" },
            { "Date", "NUMERIC" },
            { "Text", "TEXT" },
            { "OID", "NUMERIC" },
            { "GlobalID", "TEXT" },
            { "Guid", "TEXT" },
            { "XML", "TEXT" },
            { "Geometry", "BLOB" },
            { "Raster", "BLOB" },
            { "Blob", "BLOB" }
        };
}

// The strings in the dictionary could easily be used instead of the enum.
// This class is used to document the configuration settings and make their use in code obvious.
public static class SqliteConfiguration
{
    public enum Key
    {
        SqliteInitialRun,
        SqliteFolder,
        SqliteSetToInitialRun,
        SecchiObservationsLoaded,
        SecchiLocationsLoaded
    }

    public static Dictionary<Key, string> Item { get; private set; } =
        new()
        {
            { Key.SqliteInitialRun, "SqliteInitialRun" },
            { Key.SqliteFolder, "SqliteFolder" },
            { Key.SqliteSetToInitialRun, "SqliteSetToInitialRun" },
            { Key.SecchiObservationsLoaded, "SecchiObservationsSqliteLoaded" },
            { Key.SecchiLocationsLoaded, "SecchiLocationsSqliteLoaded" }
        };
}
