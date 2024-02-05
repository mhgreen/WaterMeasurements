using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Esri.ArcGISRuntime.Data;

using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

using Windows.Storage;

using WaterMeasurements.Models;
using WaterMeasurements.Views;
using WaterMeasurements.Services;
using WaterMeasurements.Contracts.Services;
using static WaterMeasurements.Models.SqliteConversion;
using static WaterMeasurements.Models.SqliteConfiguration;

using Ardalis.GuardClauses;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Windows.Media.Core;

namespace WaterMeasurements.Services;

// Message to request the creation of a Sqlite table from a feature table.
public class FeatureToTableMessage(FeatureToTable featureToTable)
    : ValueChangedMessage<FeatureToTable>(featureToTable) { }

// Message to notify modules of the result of a table creation.
public class FeatureToTableResultMessage(FeatureToTableResult featureToTableResult)
    : ValueChangedMessage<FeatureToTableResult>(featureToTableResult) { }

public partial class SqliteService : ISqliteService
{
    private readonly ILogger<SqliteService> logger;

    // Set the event id for the logger.
    private readonly EventId SqliteLog = new(15, "SqliteService");

    private readonly ILocalSettingsService? localSettingsService;

    // Set the name of the sqlite database.
    private const string DbName = "WaterMeasurements.db";

    // Set the name of the sqlite folder which is the default unless there is a setting in Key.SqliteFolder.
    private readonly string sqliteFolderName = "Sqlite";

    public SqliteService(ILogger<SqliteService> logger, ILocalSettingsService? localSettingsService)
    {
        this.logger = logger;
        this.localSettingsService = localSettingsService;

        // Log the service initialization.
        logger.LogInformation(SqliteLog, "SqliteService created.");
        this.localSettingsService = localSettingsService;

        InitializeSqliteService();
    }

    private async void InitializeSqliteService()
    {
        try
        {
            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "Sqlite Service, InitializeSqliteService(): localSettingsService is null."
            );

            // Retrieve the value of SqliteInitialRun from localSettingsService.
            var sqliteInitialRun = await localSettingsService.ReadSettingAsync<bool?>(
                SqliteConfiguration.Item[Key.SqliteInitialRun]
            );

            // If the boolean is null or true, call InitialRun().
            if (sqliteInitialRun ?? true)
            {
                await InitialRun();
            }

            // Register a message handler for FeatureToTableMessage.
            WeakReferenceMessenger.Default.Register<FeatureToTableMessage>(
                this,
                async (recipient, message) =>
                {
                    // Log the FeatureToTableMessage.
                    logger.LogDebug(
                        SqliteLog,
                        "SqliteService, FeatureToTableMessage: {message}.",
                        message
                    );
                    
                    // Call FeatureToTable with the extracted featureTable and dbType.
                    // await FeatureToTable(message.Value.FeatureTable, message.Value.DbType);

                    // Test the query generation.
                    await TestQueryGeneration(message.Value.FeatureTable, message.Value.DbType);
                }
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Error in InitializeSqliteService: {exception}.",
                exception.ToString()
            );
        }
    }

    private async Task InitialRun()
    {
        try
        {
            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "Sqlite Service, InitialRun(): localSettingsService is null."
            );

            // Check if the sqlite folder has been set in the local settings.
            if (
                string.IsNullOrEmpty(
                    await localSettingsService.ReadSettingAsync<string>(
                        SqliteConfiguration.Item[Key.SqliteFolder]
                    )
                )
            )
            {
                // If not, then set the sqlite folder to the default folder.
                await localSettingsService.SaveSettingAsync(
                    SqliteConfiguration.Item[Key.SqliteFolder],
                    sqliteFolderName
                );
            }

            // Log the sqlite folder to trace.
            logger.LogTrace(SqliteLog, "Sqlite folder: {sqliteFolderName}", sqliteFolderName);

            // Set SecchiObservationsLoaded to false.
            await localSettingsService.SaveSettingAsync(
                SqliteConfiguration.Item[Key.SecchiObservationsLoaded],
                false
            );
            // Set SecchiLocationsLoaded to false.
            await localSettingsService.SaveSettingAsync(
                SqliteConfiguration.Item[Key.SecchiLocationsLoaded],
                false
            );

            // Set sqliteInitialRun to false.
            await localSettingsService.SaveSettingAsync(
                SqliteConfiguration.Item[Key.SqliteInitialRun],
                false
            );

            // Log that the initial run has been completed.
            logger.LogInformation(SqliteLog, "Initial run completed.");
        }
        catch (Exception exception)
        {
            logger.LogError(SqliteLog, "Error in InitialRun: {exception}.", exception.ToString());
        }
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

    private async Task TestQueryGeneration(FeatureTable featureTable, DbType dbType)
    {
        try
        {
            // Log to trace that TestQueryGeneration has been called.
            logger.LogTrace(SqliteLog, "TestQueryGeneration called.");
            
            // Get the feature table name from the enum.
            var featureTableName = dbType.ToString();

            // Log the feature table name to trace.
            logger.LogTrace(SqliteLog, "Feature table name: {featureTableName}", featureTableName);

            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "Sqlite Service, QueryGeneration(): localSettingsService is null."
            );

            // Log the values of SecchiObservationsLoaded and SecchiLocationsLoaded to trace.
            logger.LogTrace(
                SqliteLog,
                "SecchiObservationsLoaded: {SecchiObservationsLoaded}, SecchiLocationsLoaded: {SecchiLocationsLoaded}",
                await localSettingsService.ReadSettingAsync<bool>(
                    SqliteConfiguration.Item[Key.SecchiObservationsLoaded]
                ),
                await localSettingsService.ReadSettingAsync<bool>(
                    SqliteConfiguration.Item[Key.SecchiLocationsLoaded]
                )
            );

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
                // Add a field to track the status of the observation (values are in enum RecordStatus).
                featureTableFieldsDictionary["Status"] = "INTEGER NOT NULL";
                featureTableFieldsDictionary["CONSTRAINT"] =
                    "fk_locations FOREIGN KEY(LocationId) REFERENCES SecchiLocations(LocationId)";
            }
            else if (dbType == DbType.SecchiLocations)
            {
                // Add a sequential primary key to the featureTableFieldsDictionary.
                featureTableFieldsDictionary["LocationId"] += " PRIMARY KEY AUTOINCREMENT";
                // Add a field to track the status of the observation (values are in enum RecordStatus).
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
            using var database = await GetOpenConnectionAsync();

            // Create the Sqlite table using the featureTableCreateStatement.
            using var createTableCommand = database.CreateCommand();
            createTableCommand.CommandText = featureTableCreateStatement;
            var returnCode = createTableCommand.ExecuteNonQuery();

            logger.LogTrace(
                SqliteLog,
                "Sqlite table created from {featureTableName}. Return code: {returnCode}",
                featureTableName,
                returnCode
            );

            // create a where clause to get all the features
            var queryParameters = new QueryParameters() { WhereClause = "1=1" };
            // Get the number of features from the feature table.
            var features = await featureTable.QueryFeaturesAsync(queryParameters);

            // If there are no records in the feature table, then return.
            if (!features.Any())
            {
                logger.LogTrace(
                    SqliteLog,
                    "Number of records in {featureTableName} feature table: {numberOfRecords}.",
                    featureTableName,
                    features.Count()
                );

                // Set the previouslyLoaded setting to true.
                await localSettingsService.SaveSettingAsync(
                    SqliteConfiguration.Item[
                        dbType switch
                        {
                            DbType.SecchiObservations => Key.SecchiObservationsLoaded,
                            DbType.SecchiLocations => Key.SecchiLocationsLoaded,
                            _ => throw new NotImplementedException()
                        }
                    ],
                    true
                );

                // Send a FeatureToTableResultMessage with the number of records inserted and the return code.
                WeakReferenceMessenger.Default.Send(
                    new FeatureToTableResultMessage(
                        new FeatureToTableResult(
                            dbType,
                            features.Count(),
                            returnCode,
                            "",
                            FeatureToTableStatus.SuccessNoRecords
                        )
                    )
                );

                return;
            }

            // Log the number of features in the feature table to trace.
            logger.LogTrace(
                SqliteLog,
                "Inserting {features.Count()} records into table {featureTableName}.",
                features.Count(),
                featureTableName
            );

            // Iterate over the features in the feature table.
            // Insert the features into the sqlite table.
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
                fieldValues["Status"] = (int)RecordStatus.GeodatabaseCommitted;

                // Create the sqlite insert statement.
                var insertStatement =
                    $"INSERT INTO {featureTableName} ({string.Join(", ", fieldValues.Keys)}) VALUES ({string.Join(", ", fieldValues.Values)});";
                logger.LogTrace(
                    SqliteLog,
                    "Sqlite insert statement: {insertStatement}",
                    insertStatement
                );

                // Execute the insert statement.
                using var insertCommand = database.CreateCommand();
                insertCommand.CommandText = insertStatement;
                returnCode = insertCommand.ExecuteNonQuery();
                logger.LogTrace(
                    SqliteLog,
                    "Feature inserted into {featureTableName} with returnCode of {returnCode}.",
                    featureTableName,
                    returnCode
                );


            }
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

    private async Task FeatureToTable(FeatureTable featureTable, DbType dbType)
    {
        try
        {
            // Get the feature table name from the enum.
            var featureTableName = dbType.ToString();

            // Log the feature table name to trace.
            logger.LogTrace(SqliteLog, "Feature table name: {featureTableName}", featureTableName);

            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "Sqlite Service, FeatureToTable(): localSettingsService is null."
            );

            // Log the values of SecchiObservationsLoaded and SecchiLocationsLoaded to trace.
            logger.LogTrace(
                SqliteLog,
                "SecchiObservationsLoaded: {SecchiObservationsLoaded}, SecchiLocationsLoaded: {SecchiLocationsLoaded}",
                await localSettingsService.ReadSettingAsync<bool>(
                    SqliteConfiguration.Item[Key.SecchiObservationsLoaded]
                ),
                await localSettingsService.ReadSettingAsync<bool>(
                    SqliteConfiguration.Item[Key.SecchiLocationsLoaded]
                )
            );
   
            var previouslyLoaded = false;

            // Create a switch statement to check the dbType and set previouslyLoaded to the value of the setting in localSettingsService.
            // If the value is true, then return.
            switch (dbType)
            {
                case DbType.SecchiObservations:
                    previouslyLoaded = await localSettingsService.ReadSettingAsync<bool>(
                        SqliteConfiguration.Item[Key.SecchiObservationsLoaded]
                    );
                    if (previouslyLoaded)
                    {
                        return;
                    }
                    break;
                case DbType.SecchiLocations:
                    previouslyLoaded = await localSettingsService.ReadSettingAsync<bool>(
                        SqliteConfiguration.Item[Key.SecchiLocationsLoaded]
                    );
                    if (previouslyLoaded)
                    {
                        return;
                    }
                    break;
            }
            

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
                // Add a field to track the status of the observation (values are in enum RecordStatus).
                featureTableFieldsDictionary["Status"] = "INTEGER NOT NULL";
                featureTableFieldsDictionary["CONSTRAINT"] =
                    "fk_locations FOREIGN KEY(LocationId) REFERENCES SecchiLocations(LocationId)";
            }
            else if (dbType == DbType.SecchiLocations)
            {
                // Add a sequential primary key to the featureTableFieldsDictionary.
                featureTableFieldsDictionary["LocationId"] += " PRIMARY KEY AUTOINCREMENT";
                // Add a field to track the status of the observation (values are in enum RecordStatus).
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
            using var database = await GetOpenConnectionAsync();

            // Create the Sqlite table using the featureTableCreateStatement.
            using var createTableCommand = database.CreateCommand();
            createTableCommand.CommandText = featureTableCreateStatement;
            var returnCode = createTableCommand.ExecuteNonQuery();

            logger.LogTrace(
                SqliteLog,
                "Sqlite table created from {featureTableName}. Return code: {returnCode}",
                featureTableName,
                returnCode
            );

            // create a where clause to get all the features
            var queryParameters = new QueryParameters() { WhereClause = "1=1" };
            // Get the number of features from the feature table.
            var features = await featureTable.QueryFeaturesAsync(queryParameters);

            // If there are no records in the feature table, then return.
            if (!features.Any())
            {
                logger.LogTrace(
                    SqliteLog,
                    "Number of records in {featureTableName} feature table: {numberOfRecords}.",
                    featureTableName,
                    features.Count()
                );
                // Set the previouslyLoaded setting to true.
                await localSettingsService.SaveSettingAsync(
                    SqliteConfiguration.Item[
                        dbType switch
                        {
                            DbType.SecchiObservations => Key.SecchiObservationsLoaded,
                            DbType.SecchiLocations => Key.SecchiLocationsLoaded,
                            _ => throw new NotImplementedException()
                        }
                    ],
                    true
                );

                // Send a FeatureToTableResultMessage with the number of records inserted and the return code.
                WeakReferenceMessenger.Default.Send(
                    new FeatureToTableResultMessage(
                        new FeatureToTableResult(
                            dbType,
                            features.Count(),
                            returnCode,
                            "",
                            FeatureToTableStatus.SuccessNoRecords
                        )
                    )
                );

                return;
            }

            // Log the number of features in the feature table to trace.
            logger.LogTrace(
                SqliteLog,
                "Inserting {features.Count()} records into table {featureTableName}.",
                features.Count(),
                featureTableName
            );

            // Iterate over the features in the feature table.
            // Insert the features into the sqlite table.
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
                fieldValues["Status"] = (int)RecordStatus.GeodatabaseCommitted;

                // Create the sqlite insert statement.
                var insertStatement =
                    $"INSERT INTO {featureTableName} ({string.Join(", ", fieldValues.Keys)}) VALUES ({string.Join(", ", fieldValues.Values)});";
                logger.LogTrace(
                    SqliteLog,
                    "Sqlite insert statement: {insertStatement}",
                    insertStatement
                );

                // Execute the insert statement.
                using var insertCommand = database.CreateCommand();
                insertCommand.CommandText = insertStatement;
                returnCode = insertCommand.ExecuteNonQuery();
                logger.LogTrace(
                    SqliteLog,
                    "Feature inserted into {featureTableName} with returnCode of {returnCode}.",
                    featureTableName,
                    returnCode
                );

                // Set the previouslyLoaded setting to true.
                await localSettingsService.SaveSettingAsync(
                    SqliteConfiguration.Item[
                        dbType switch
                        {
                            DbType.SecchiObservations => Key.SecchiObservationsLoaded,
                            DbType.SecchiLocations => Key.SecchiLocationsLoaded,
                            _ => throw new NotImplementedException()
                        }
                    ],
                    true
                );
                // Send a FeatureToTableResultMessage with the number of records inserted and the return code.
                WeakReferenceMessenger.Default.Send(
                    new FeatureToTableResultMessage(
                        new FeatureToTableResult(
                            dbType,
                            features.Count(),
                            returnCode,
                            "",
                            FeatureToTableStatus.SuccessWithRecords
                        )
                    )
                );
            }
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "Error creating Sqlite table from DbType {dbType}, Sqlite exception: {sqliteMessage}, with an error code of {SqliteErrorCode}",
                dbType.ToString(),
                sqliteException.Message,
                sqliteException.SqliteErrorCode
            );
            // Send a FeatureToTableResultMessage with the number of records inserted and the return code.
            WeakReferenceMessenger.Default.Send(
                new FeatureToTableResultMessage(
                    new FeatureToTableResult(
                        dbType,
                        0,
                        sqliteException.SqliteErrorCode,
                        sqliteException.Message,
                        FeatureToTableStatus.Failure
                    )
                )
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
