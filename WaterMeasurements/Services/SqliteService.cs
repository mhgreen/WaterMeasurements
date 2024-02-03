using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Esri.ArcGISRuntime.Data;

using Windows.Storage;

using WaterMeasurements.Models;
using WaterMeasurements.Views;
using WaterMeasurements.Services;
using WaterMeasurements.Contracts.Services;
using static WaterMeasurements.Models.SqliteConversion;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace WaterMeasurements.Services;

public partial class SqliteService : ISqliteService
{
    private readonly ILogger<SqliteService> logger;

    // Set the event id for the logger.
    private readonly EventId SqliteLog = new(15, "SqliteService");

    // Set the name of the sqlite database.
    private const string DbName = "WaterMeasurements.db";

    // Set the path for the sqlite database from the configuration service.
    private readonly string sqliteFolderName = ConfigurationServiceConfiguration.SqliteFolder;

    public SqliteService(ILogger<SqliteService> logger)
    {
        this.logger = logger;

        // Log the service initialization.
        logger.LogInformation(SqliteLog, "SqliteService created.");
    }

    // Open the connection to the sqlite database.
    private async Task<SqliteConnection> GetOpenConnectionAsync()
    {
        try
        {
            var sqliteFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                sqliteFolderName,
                CreationCollisionOption.OpenIfExists
            );
            await sqliteFolder
                .CreateFileAsync(DbName, CreationCollisionOption.OpenIfExists)
                .AsTask()
                .ConfigureAwait(false);

            var dbPath = Path.Combine(sqliteFolder.Path, DbName);
            // Log to trace the sqlite database path.
            logger.LogTrace(SqliteLog, "Sqlite database path: {dbPath}", dbPath);
            var connection = new SqliteConnection($"Filename={dbPath}");
            connection.Open();
            return connection;
        }
        catch (Exception exception)
        {
            logger.LogError(SqliteLog, exception, "Error opening connection to sqlite database.");
            throw;
        }
    }

    public async Task FeatureToTable(FeatureTable featureTable, DbType dbType)
    {
        try
        {
            // Prepend the prefix to the feature table name.
            var featureTableName = dbType.ToString();

            // Log the feature table name to trace.
            logger.LogTrace(SqliteLog, "Feature table name: {featureTableName}", featureTableName);

            // Iterate over the fields in the feature table creating a dictionary with the field name as the key and using GetSqliteType for the field value.
            var featureTableFieldsDictionary = featureTable.Fields.ToDictionary(
                field => field.Name,
                field => GetSqliteType(field)
            );
            if (dbType == DbType.SecchiObservations)
            {
                // Add a sequential primary key to the featureTableFieldsDictionary.
                featureTableFieldsDictionary["SecchiObservationsId"] =
                    "INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL";
                // Add a field to track the status of the observation (values are in enum ObservationStatus).
                featureTableFieldsDictionary["Status"] = "INTEGER NOT NULL";
                featureTableFieldsDictionary["CONSTRAINT"] =
                    "fk_locations FOREIGN KEY(LocationId) REFERENCES SecchiLocations(LocationId)";
            }
            else if (dbType == DbType.SecchiLocations)
            {
                // Add a sequential primary key to the featureTableFieldsDictionary.
                featureTableFieldsDictionary["LocationId"] += " PRIMARY KEY AUTOINCREMENT";
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

            // Open the connection to the sqlite database.
            using var db = await GetOpenConnectionAsync();
            // Create the Sqlite table using the featureTableCreateStatement.
            using var createTableCommand = db.CreateCommand();
            createTableCommand.CommandText = featureTableCreateStatement;
            createTableCommand.ExecuteNonQuery();
            logger.LogTrace(
                SqliteLog,
                "Sqlite table created from {featureTableName}.",
                featureTableName
            );
            // create a where clause to get all the features
            var queryParameters = new QueryParameters() { WhereClause = "1=1" };
            // Get the number of features from the feature table.
            var features = await featureTable.QueryFeaturesAsync(queryParameters);
            logger.LogTrace(
                SqliteLog,
                "Number of records in {featureTableName} feature table: {numberOfRecords}.",
                featureTableName,
                features.Count()
            );
            // If there are no records in the feature table, then return.
            if (!features.Any())
            {
                return;
            }

            // Log the number of features in the feature table to trace.
            logger.LogTrace(
                SqliteLog,
                "Inserting {features.Count()} records into table {featureTableName}.",
                features.Count(),
                featureTableName
            );

            // Insert the features into the sqlite table.
            // Iterate over the features in the feature table.
            foreach (var feature in features)
            {
                // Create a dictionary to store the field values for the feature.
                var fieldValues = new Dictionary<string, object>();

                // Iterate over the fields in the feature table.
                foreach (var field in featureTable.Fields)
                {
                    // Get the field name and value for the current field.
                    var fieldName = field.Name;
                    var fieldValue = feature.GetAttributeValue(field);

                    // Add the field name and value to the fieldValues dictionary.
                    if (fieldValue != null)
                    {
                        // In the featureTableFieldsDictionary, Check if the field value contains "TEXT".
                        if (featureTableFieldsDictionary[fieldName].Contains("TEXT"))
                        {
                            // If it does, then add double quotes around the field value.
                            fieldValue = $"\"{fieldValue}\"";
                        }
                        // Add the field name and value to the fieldValues dictionary.
                        fieldValues[fieldName] = fieldValue;
                    }
                }
                // Indicate that the feature is from the geodatabase and has been committed.
                fieldValues["Status"] = (int)ObservationStatus.GeodatabaseCommitted;

                // Create the sqlite insert statement.
                var insertStatement =
                    $"INSERT INTO {featureTableName} ({string.Join(", ", fieldValues.Keys)}) VALUES ({string.Join(", ", fieldValues.Values)});";
                logger.LogTrace(
                    SqliteLog,
                    "Sqlite insert statement: {insertStatement}",
                    insertStatement
                );

                // Execute the insert statement.
                using var insertCommand = db.CreateCommand();
                insertCommand.CommandText = insertStatement;
                insertCommand.ExecuteNonQuery();
                logger.LogTrace(
                    SqliteLog,
                    "Feature inserted into {featureTableName}.",
                    featureTableName
                );
            }
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "Error creating Sqlite table from DbType {dbType}, Sqlite exception: {sqliteException}, with an error code of {SqliteErrorCode}",
                dbType.ToString(),
                sqliteException.ToString(),
                sqliteException.SqliteErrorCode
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Error creating Sqlite table from DbType {dbType}: {exception}.",
                dbType.ToString(),
                exception.ToString()
            );
        }
    }

    private string GetSqliteType(Field field)
    {
        var fieldType = field.FieldType.ToString();
        var sqliteType = GeodatabaseSqliteTypeConversion[fieldType];
        /*
        logger.LogTrace(
            SqliteLog,
            "Field type {fieldType} converted to {sqliteType}.",
            fieldType,
            sqliteType
        );
        */
        return sqliteType;
    }
}
