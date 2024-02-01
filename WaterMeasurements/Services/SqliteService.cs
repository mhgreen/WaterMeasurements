using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Esri.ArcGISRuntime.Data;

using WaterMeasurements.Models;
using WaterMeasurements.Views;
using WaterMeasurements.Services;
using WaterMeasurements.Contracts.Services;
using static WaterMeasurements.Models.SqliteConversion;

using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace WaterMeasurements.Services;

public partial class SqliteService : ISqliteService
{
    private readonly ILogger<SqliteService> logger;

    // Set the event id for the logger.
    private readonly EventId SqliteLog = new(15, "SqliteService");

    // Set the path for the sqlite database from the configuration service.
    private readonly string sqlitePath = ConfigurationServiceConfiguration.SqliteFolder;

    public SqliteService(ILogger<SqliteService> logger)
    {
        this.logger = logger;

        // Log the service initialization.
        logger.LogInformation(SqliteLog, "SqliteService created.");
    }

    public Task FeaturetableToDatabase(FeatureTable featureTable, DbType dbType)
    {
        try
        {
            // Prepend the prefix to the feature table name.
            var featureTableName = dbType.ToString();
            // Log the feature table name to trace.
            logger.LogTrace(SqliteLog, "Feature table name: {featureTableName}", featureTableName);
            /*
            // Iterate over the fields in the feature table using field.Name to get the field name and GetSqliteType to convert the field type to sqlite.
            var featureTableFields = featureTable.Fields.Select(
                field => $"{field.Name} {GetSqliteType(field)}"
            );
            */
            // Iterate over the fields in the feature table creating a dictionary with the field name as the key and using GetSqliteType for the field value.
            var featureTableFieldsDictionary = featureTable.Fields.ToDictionary(
                field => field.Name,
                field => GetSqliteType(field)
            );
            if (dbType == DbType.SecchiObservations)
            {
                // Add a sequential primary key to the featureTableFieldsDictionary.
                featureTableFieldsDictionary["SecchiObservationsId"] = "INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL";
                // Add a field to track the status of the observation (values are in enum ObservationStatus).
                featureTableFieldsDictionary["Status"] = "INTEGER NOT NULL";
            }
            else if (dbType == DbType.SecchiLocations)
            {

                // Add a sequential primary key to the featureTableFieldsDictionary.
                featureTableFieldsDictionary["SecchiLocationsId"] = "INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL";
                // Add a field to track the status of the observation (values are in enum ObservationStatus).
                featureTableFieldsDictionary["Status"] = "INTEGER NOT NULL";
            }
            // Log the feature table fields dictionary to trace.
            logger.LogTrace(
                SqliteLog,
                "Feature table fields dictionary: {featureTableFieldsDictionary}",
                string.Join(", ", featureTableFieldsDictionary)
            );
            // Convert the featureTableFieldsDictionary to a list of strings.
            var featureTableFields = featureTableFieldsDictionary.Select(
                field => $"{field.Key} {field.Value}"
            );
            // Log the feature table fields to trace.
            logger.LogTrace(
                SqliteLog,
                "Feature table fields: {featureTableFields}",
                string.Join(", ", featureTableFields)
            );
            // Create the feature table create statement.
            var featureTableCreateStatement =
                $"CREATE TABLE IF NOT EXISTS {featureTableName} ({string.Join(", ", featureTableFields)});";
            logger.LogTrace(
                SqliteLog,
                "Feature table create statement: {featureTableCreateStatement}",
                featureTableCreateStatement
            );
        }
        catch (Exception exception)
        {
            logger.LogError(SqliteLog, exception, "Error creating feature table.");
        }
        return Task.CompletedTask;
    }

    private string GetSqliteType(Field field)
    {
        var fieldType = field.FieldType.ToString();
        var sqliteType = GeodatabaseSqliteTypeConversion[fieldType];
        logger.LogTrace(
            SqliteLog,
            "Field type {fieldType} converted to {sqliteType}.",
            fieldType,
            sqliteType
        );
        return sqliteType;
    }
}
