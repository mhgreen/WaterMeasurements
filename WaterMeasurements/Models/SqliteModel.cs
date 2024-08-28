using System;
using System.Collections.Generic;
using Esri.ArcGISRuntime.Data;

namespace WaterMeasurements.Models;

public enum DbType
{
    SecchiObservations,
    SecchiLocations,
    SecchiLocationDetail
}

public enum RecordStatus
{
    WorkingSet,
    Comitted
}

public enum LocationSource
{
    CurrentGPS,
    PointOnMap,
    EnteredLatLong
}

public enum LocationType
{
    Ongoing,
    Occasional
}

public enum LocationCollected
{
    NotCollected,
    Collected,
    Error
}

public enum CollectionDirection
{
    Both,
    OneWay
}

public enum CollectOccasional
{
    DontCollect,
    Collect
}

public enum LocationsCollectedStateScope
{
    SingleLocation,
    AllLocations
}

// Record to create a table from a feature table.
public readonly record struct FeatureToTable(FeatureTable FeatureTable, DbType DbType);

// Record with a table name and a location id.
public readonly record struct DbTypeAndLocationId(DbType DbType, int LocationId);

// Record to add a location record to a table.
public readonly record struct AddLocationRecordToTable(
    LocationRecord LocationRecord,
    DbType DbType
);

// Record to delete a location record from a table.
public readonly record struct DeleteLocationRecordFromTable(int LocationId, DbType DbType);

// Record to update a location record in a table.
public readonly record struct UpdateLocationRecordInTable(
    LocationRecord LocationRecord,
    DbType DbType
);

// Record to set a location to collected in a table.
public readonly record struct SetLocationRecordCollectedState(
    int LocationId,
    DbType DbType,
    LocationCollected LocationCollectedState,
    LocationsCollectedStateScope Scope
);

// Data transfer object for location flags.
// This is defined in order to handle the mapping of fields
// to handle explicit enum conversion and to allow for null values
// and the mapping of nulls to default values.
public class LocationFlagsDto
{
    public int LocationId { get; set; }
    public int Status { get; set; }
    public int LocationCollected { get; set; }
    public int? CollectionDirection { get; set; }
    public int? CollectOccasional { get; set; }
}

// Record of location flags.
public readonly record struct LocationFlags(
    int LocationId,
    RecordStatus RecordStatus,
    LocationType LocationType,
    LocationCollected LocationCollected,
    CollectionDirection CollectionDirection,
    CollectOccasional CollectOccasional
);

// Record to update location detail.
public readonly record struct LocationDetail(
    CollectionDirection? CollectionDirection,
    CollectOccasional? CollectOccasional
);

// Record to request a group of records from Sqlite.
public readonly record struct SqliteRecordsGroupRequest(
    DbType DbType,
    int PageSize,
    int PageNumber
);

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

// Record for location.
public readonly record struct LocationRecord(
    double Latitude,
    double Longitude,
    int LocationId,
    string LocationName,
    LocationType LocationType,
    int Status,
    int LocationCollected
);

// Record for Secchi location.
public readonly record struct SecchiLocation(
    double Latitude,
    double Longitude,
    int LocationId,
    string LocationName,
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

public enum CreateLocationDetailStatus
{
    SuccessPreviouslyLoaded,
    SuccessNewCreated,
    Failure
}

public readonly record struct CreateLocationDetailResult(
    DbType TableType,
    CreateLocationDetailStatus Status,
    int ReturnCode,
    string ErrorMessage
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
            { "Date", "TEXT" },
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
        SecchiLocationsLoaded,
        SecchiLocationDetailLoaded
    }

    public static Dictionary<Key, string> Item { get; private set; } =
        new()
        {
            { Key.SqliteInitialRun, "SqliteInitialRun" },
            { Key.SqliteFolder, "SqliteFolder" },
            { Key.SqliteSetToInitialRun, "SqliteSetToInitialRun" },
            { Key.SecchiObservationsLoaded, "SecchiObservationsSqliteLoaded" },
            { Key.SecchiLocationsLoaded, "SecchiLocationsSqliteLoaded" },
            { Key.SecchiLocationDetailLoaded, "SecchiLocationDetailSqliteLoaded" }
        };
}
