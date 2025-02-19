﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Dapper;
using Dapper.SimpleSqlBuilder;
using Dapper.SimpleSqlBuilder.FluentBuilder;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Location;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Models;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
using Windows.System;
using static WaterMeasurements.Models.SqliteConfiguration;
using static WaterMeasurements.Models.SqliteConversion;
using DbType = WaterMeasurements.Models.DbType;

namespace WaterMeasurements.Services;

// Message to request the creation of a Sqlite table from a feature table.
public class FeatureToTableMessage(FeatureToTable featureToTable)
    : ValueChangedMessage<FeatureToTable>(featureToTable) { }

// Message to notify modules of the result of a table creation.
public class FeatureToTableResultMessage(FeatureToTableResult featureToTableResult)
    : ValueChangedMessage<FeatureToTableResult>(featureToTableResult) { }

// Message to notify modules that a table is available.
public class TableAvailableMessage(DbType dbType) : ValueChangedMessage<DbType>(dbType) { }

// Message to notify modules of the result of a location detail table creation.
public class CreateLocationDetailResultMessage(
    CreateLocationDetailResult createLocationDetailResult
) : ValueChangedMessage<CreateLocationDetailResult>(createLocationDetailResult) { }

// Message to add a location record to a table.
public class AddLocationRecordToTableMessage(AddLocationRecordToTable addLocationRecordToTable)
    : ValueChangedMessage<AddLocationRecordToTable>(addLocationRecordToTable) { }

// Message to notify modules that a location record has been added to a table.
public class LocationRecordAddedToTableMessage(DbTypeAndLocationId dbTypeAndLocationId)
    : ValueChangedMessage<DbTypeAndLocationId>(dbTypeAndLocationId) { }

// Message to delete a location record from a table.
public class DeleteLocationRecordFromTableMessage(
    DeleteLocationRecordFromTable deleteLocationRecordFromTable
) : ValueChangedMessage<DeleteLocationRecordFromTable>(deleteLocationRecordFromTable) { }

// Message to notify modules that a location record has been deleted from a table.
public class LocationRecordDeletedFromTableMessage(DbTypeAndLocationId dbTypeAndLocationId)
    : ValueChangedMessage<DbTypeAndLocationId>(dbTypeAndLocationId) { }

// Message to update a location record in a table.
public class UpdateLocationRecordInTableMessage(
    UpdateLocationRecordInTable updateLocationRecordInTable
) : ValueChangedMessage<UpdateLocationRecordInTable>(updateLocationRecordInTable) { }

// Message to notify modules that a location record has been updated in a table.
public class LocationRecordUpdatedInTableMessage(DbTypeAndLocationId dbTypeAndLocationId)
    : ValueChangedMessage<DbTypeAndLocationId>(dbTypeAndLocationId) { }

// Message to set a location record to collected.
public class SetLocationRecordCollectedStateMessage(
    SetLocationRecordCollectedState setLocationRecordCollected
) : ValueChangedMessage<SetLocationRecordCollectedState>(setLocationRecordCollected) { }

// Message to request a group of records from the Sqlite database.
public class GetSqliteRecordsGroupRequest(SqliteRecordsGroupRequest sqliteRecordsGroupRequest)
    : ValueChangedMessage<SqliteRecordsGroupRequest>(sqliteRecordsGroupRequest) { }

// Message to add a SecchiObservation to the Sqlite database.
public class AddSecchiObservationMessage(SecchiObservation secchiObservation)
    : ValueChangedMessage<SecchiObservation>(secchiObservation) { }

// Message to provide modules with a SecchiLocation from the Sqlite database.
public class SecchiLocationsSqliteRecordGroup(SecchiLocation secchiLocation)
    : ValueChangedMessage<SecchiLocation>(secchiLocation) { }

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

            // ---------- Set to initial run manually ----------

            // This is mostly for testing, but could also be used to reset the app to initial run state.
            await localSettingsService.SaveSettingAsync(
                SqliteConfiguration.Item[Key.SqliteSetToInitialRun],
                false
            );
            // ---------- Set to initial run manually ----------

            // Retrieve the value of SqliteSetToInitialRun from localSettingsService.
            var sqliteSetToInitialRun = await localSettingsService.ReadSettingAsync<bool?>(
                SqliteConfiguration.Item[Key.SqliteSetToInitialRun]
            );

            // If the boolean is null or true, call SetToInitialRun().
            if (sqliteSetToInitialRun ?? true)
            {
                SetToInitialRun();
            }
            else
            {
                // Retrieve the value of SqliteInitialRun from localSettingsService.
                var sqliteInitialRun = await localSettingsService.ReadSettingAsync<bool?>(
                    SqliteConfiguration.Item[Key.SqliteInitialRun]
                );

                // If the boolean is null or true, call InitialRun().
                if (sqliteInitialRun ?? true)
                {
                    await InitialRun();
                }
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
                    await FeatureToTable(message.Value.FeatureTable, message.Value.DbType);
                }
            );

            // Register a message handler for AddLocationRecordToTableMessage.
            WeakReferenceMessenger.Default.Register<AddLocationRecordToTableMessage>(
                this,
                async (recipient, message) =>
                {
                    // Log the AddLocationRecordToTableMessage.
                    logger.LogDebug(
                        SqliteLog,
                        "SqliteService, AddLocationRecordToTableMessage: {message}.",
                        message
                    );

                    // Call AddLocationRecordToTable with the extracted location and dbType.
                    await AddLocationRecordToTable(
                        message.Value.LocationRecord,
                        message.Value.DbType
                    );
                }
            );

            // Register a message handler for DeleteLocationRecordFromTableMessage.
            WeakReferenceMessenger.Default.Register<DeleteLocationRecordFromTableMessage>(
                this,
                async (recipient, message) =>
                {
                    // Log the DeleteLocationRecordFromTableMessage.
                    logger.LogDebug(
                        SqliteLog,
                        "SqliteService, DeleteLocationRecordFromTableMessage: {message}.",
                        message
                    );

                    // Call DeleteLocationRecordFromTable with the extracted locationId and dbType.
                    await DeleteLocationRecordFromTable(
                        message.Value.LocationId,
                        message.Value.DbType
                    );
                }
            );

            // Register a message handler for UpdateLocationRecordInTableMessage.
            WeakReferenceMessenger.Default.Register<UpdateLocationRecordInTableMessage>(
                this,
                async (recipient, message) =>
                {
                    // Log the UpdateLocationRecordInTableMessage.
                    logger.LogDebug(
                        SqliteLog,
                        "SqliteService, UpdateLocationRecordInTableMessage: {message}.",
                        message
                    );

                    // Call UpdateLocationRecordInTable with the extracted location and dbType.
                    await UpdadateLocationRecordInTable(
                        message.Value.LocationRecord,
                        message.Value.DbType
                    );
                }
            );

            // Register a message handler for GetSqliteRecordsGroupRequest.
            WeakReferenceMessenger.Default.Register<GetSqliteRecordsGroupRequest>(
                this,
                async (recipient, message) =>
                {
                    // Log the GetSqliteRecordsGroupRequest.
                    logger.LogDebug(
                        SqliteLog,
                        "SqliteService, GetSqliteRecordsGroupRequest: {message}.",
                        message.Value.ToString()
                    );

                    // Call GetRecordGroupFromSqlite with the extracted dbType, pageSize, and pageNumber.
                    await GetRecordGroupFromSqlite(
                        message.Value.DbType,
                        message.Value.PageSize,
                        message.Value.PageNumber
                    );
                }
            );

            // Register a message handler for SetLocationRecordCollectedStateMessage.
            WeakReferenceMessenger.Default.Register<SetLocationRecordCollectedStateMessage>(
                this,
                async (recipient, message) =>
                {
                    // Log the SetLocationRecordCollectedStateMessage.
                    logger.LogDebug(
                        SqliteLog,
                        "SqliteService, SetLocationRecordCollectedStateMessage: {message}.",
                        message
                    );

                    // Call SetLocationRecordtoCollectedState with the extracted locationId, dbType, and LocationCollectedState.
                    await SetLocationRecordtoCollectedState(
                        message.Value.LocationId,
                        message.Value.DbType,
                        message.Value.LocationCollectedState,
                        message.Value.Scope
                    );
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

    private async Task FeatureToTable(FeatureTable featureTable, DbType dbType)
    {
        var sumOfInsertedRecords = 0;

        try
        {
            // Get the feature table name from the enum.
            var featureTableName = dbType.ToString();

            // Log the feature table name to trace.
            logger.LogTrace(SqliteLog, "Feature table name: {featureTableName}", featureTableName);

            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "Sqlite Service, FeatureToTable: localSettingsService is null."
            );

            // Check whether or not the feature table has been previously loaded.
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
                        // Log to trace that the SecchiObservations table has already been loaded.
                        logger.LogTrace(
                            SqliteLog,
                            "Feature table {SecchiObservations} has already been loaded, returning from FeatureToTable().",
                            DbType.SecchiObservations.ToString()
                        );
                        SendTableAvailableMessage(DbType.SecchiObservations);
                        return;
                    }
                    else
                    {
                        // Log to trace that the SecchiObservations table has not been loaded.
                        logger.LogTrace(
                            SqliteLog,
                            "Feature table {SecchiObservations} has not been loaded.",
                            DbType.SecchiObservations.ToString()
                        );
                    }
                    break;
                case DbType.SecchiLocations:
                    previouslyLoaded = await localSettingsService.ReadSettingAsync<bool>(
                        SqliteConfiguration.Item[Key.SecchiLocationsLoaded]
                    );

                    if (previouslyLoaded)
                    {
                        // Log to trace that the SecchiLocations table has already been loaded.
                        logger.LogTrace(
                            SqliteLog,
                            "Feature table {SecchiLocations} has already been loaded, returning from FeatureToTable().",
                            DbType.SecchiLocations.ToString()
                        );
                        SendTableAvailableMessage(DbType.SecchiLocations);
                        return;
                    }
                    else
                    {
                        // Log to trace that the SecchiLocations table has not been loaded.
                        logger.LogTrace(
                            SqliteLog,
                            "Feature table {SecchiLocations} has not been loaded.",
                            DbType.SecchiLocations.ToString()
                        );
                    }
                    break;
                default:
                    // Log a warning that the dbType is not implemented.
                    logger.LogError(
                        SqliteLog,
                        "Sqlite Service, FeatureToTable: dbType {dbType} is not implemented.",
                        dbType
                    );
                    return;
            }

            // Iterate over the fields in the feature table creating a dictionary with the field name as the key and using GetSqliteType for the field value.
            var featureTableFieldsDictionary = featureTable.Fields.ToDictionary(
                field => field.Name,
                field => GetSqliteType(field)
            );
            if (dbType == DbType.SecchiObservations)
            {
                // Add a sequential primary key to the featureTableFieldsDictionary.
                // featureTableFieldsDictionary["ObservationFID"] = "INTEGER PRIMARY KEY NOT NULL";
                // Add a field to track the status of the observation (values are in enum RecordStatus).
                featureTableFieldsDictionary["Status"] = "INTEGER NOT NULL";
                featureTableFieldsDictionary["CONSTRAINT"] =
                    "fk_locations FOREIGN KEY(LocationId) REFERENCES SecchiLocations(LocationId)";
            }
            else if (dbType == DbType.SecchiLocations)
            {
                // Add a sequential primary key to the featureTableFieldsDictionary.
                featureTableFieldsDictionary["LocationId"] += " PRIMARY KEY";
                // featureTableFieldsDictionary["LocationId"] += " PRIMARY KEY AUTOINCREMENT";
                // Add a field to track the status of the observation (values are in enum RecordStatus).
                featureTableFieldsDictionary["Status"] = "INTEGER NOT NULL";
                // Add a field to track whether the location has been collected (values are in enum LocationCollected).
                featureTableFieldsDictionary["LocationCollected"] = "INTEGER NOT NULL";
            }
            // Log the feature table fields dictionary to trace.
            logger.LogTrace(
                SqliteLog,
                "Feature table fields dictionary: {featureTableFieldsDictionary}",
                string.Join(", ", featureTableFieldsDictionary)
            );
            // Convert the featureTableFieldsDictionary to a list of strings.
            var featureTableFields = featureTableFieldsDictionary.Select(field =>
                $"{field.Key} {field.Value}"
            );
            // Log the feature table fields to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, FeatureToTable: Feature table fields: {featureTableFields}",
                string.Join(", ", featureTableFields)
            );
            // Create the feature table create statement.
            var featureTableCreateStatement =
                $"CREATE TABLE IF NOT EXISTS {featureTableName} ({string.Join(", ", featureTableFields)});";
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, FeatureToTable: Feature table create statement: {featureTableCreateStatement}",
                featureTableCreateStatement
            );

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            // Create the Sqlite table using the featureTableCreateStatement.
            using var createTableCommand = database.CreateCommand();
            createTableCommand.CommandText = featureTableCreateStatement;
            var createReturnCode = createTableCommand.ExecuteNonQuery();

            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, FeatureToTable: Sqlite table created from {featureTableName}. Return code: {returnCode}",
                featureTableName,
                createReturnCode
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
                    "Sqlite Service, FeatureToTable: Number of records in {featureTableName} feature table: {numberOfRecords}.",
                    featureTableName,
                    features.Count()
                );

                // Set the previouslyLoaded setting to true.
                await SetPreviouslyLoadedState(dbType, true);

                // Send a message that the table is available.
                SendTableAvailableMessage(dbType);

                // Send a FeatureToTableResultMessage with the number of records inserted and the return code.
                WeakReferenceMessenger.Default.Send(
                    new FeatureToTableResultMessage(
                        new FeatureToTableResult(
                            dbType,
                            features.Count(),
                            createReturnCode,
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
                "Sqlite Service, FeatureToTable: Inserting {features.Count()} records into table {featureTableName}.",
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
                fieldValues["Status"] = (int)RecordStatus.Comitted;

                // For table types that have location records, set flags to indicate
                // that the location has not been collected and the collection direction is one way.

                // If the dbType is SecchiLocations, then set appropriate flags.
                if (dbType == DbType.SecchiLocations)
                {
                    // Initialize the feature to a state of NotCollected.
                    fieldValues["LocationCollected"] = (int)LocationCollected.NotCollected;
                }

                // Create the sqlite insert statement.
                var insertStatement =
                    $"INSERT INTO {featureTableName} ({string.Join(", ", fieldValues.Keys)}) VALUES ({string.Join(", ", fieldValues.Values)});";
                logger.LogTrace(
                    SqliteLog,
                    "Sqlite Service, FeatureToTable: Sqlite insert statement: {insertStatement}",
                    insertStatement
                );

                // Execute the insert statement.
                using var insertCommand = database.CreateCommand();
                insertCommand.CommandText = insertStatement;
                var insertedRecords = insertCommand.ExecuteNonQuery();

                // Keep a running total of the number of inserted records.
                // In the end, this should equal the number of features.
                sumOfInsertedRecords += insertedRecords;

                // log the sumOfInsertedRecords to trace.
                logger.LogDebug(
                    SqliteLog,
                    "Sqlite Service, FeatureToTable: For the table {featureTableName}: sumOfInsertedRecords is {sumOfInsertedRecords}.",
                    featureTableName,
                    sumOfInsertedRecords
                );
            }

            // Send a message that the table is available.
            SendTableAvailableMessage(dbType);

            // Set the previouslyLoaded setting to true.
            await SetPreviouslyLoadedState(dbType, true);

            if (sumOfInsertedRecords == features.Count())
            {
                // Send a FeatureToTableResultMessage with the number of records inserted and the return code.
                WeakReferenceMessenger.Default.Send(
                    new FeatureToTableResultMessage(
                        new FeatureToTableResult(
                            dbType,
                            features.Count(),
                            0,
                            "",
                            FeatureToTableStatus.Success
                        )
                    )
                );
            }
            else
            {
                // Log a warning that not all records from the featuretable were written to Sqlite.
                logger.LogWarning(
                    SqliteLog,
                    "Sqlite Service, FeatureToTable: Expected to insert {expectedInserts} but only {actualInserts} were inserted.",
                    features.Count(),
                    sumOfInsertedRecords
                );
                // Send a FeatureToTableResultMessage with the number of records inserted and the return code.
                WeakReferenceMessenger.Default.Send(
                    new FeatureToTableResultMessage(
                        new FeatureToTableResult(
                            dbType,
                            sumOfInsertedRecords,
                            -1,
                            "Sqlite Service, FeatureToTable: Not all records from the featuretable were written to Sqlite.",
                            FeatureToTableStatus.SuccessWithPartialRecords
                        )
                    )
                );
            }

            // Create the corresponding location detail table.
            switch (dbType)
            {
                case DbType.SecchiLocations:
                    var createLocationDetailResult = await CreateLocationDetailTable(
                        DbType.SecchiLocationDetail
                    );
                    if (
                        createLocationDetailResult.Status
                        == CreateLocationDetailStatus.SuccessNewCreated
                    )
                    {
                        // Log to trace that the location detail table was created.
                        logger.LogTrace(
                            SqliteLog,
                            "Sqlite Service, FeatureToTable, create location detail table: Location detail table created for {DbType}.",
                            DbType.SecchiLocationDetail
                        );
                        // Send a LocationDetailTableCreatedMessage with the return code and error message.
                        WeakReferenceMessenger.Default.Send(
                            new CreateLocationDetailResultMessage(createLocationDetailResult)
                        );
                    }
                    else
                    {
                        // Log to trace that the location detail table was not created.
                        logger.LogError(
                            SqliteLog,
                            "Sqlite Service, FeatureToTable, create location detail table: Location detail table not created for {DbType}.",
                            DbType.SecchiLocationDetail
                        );
                        // Send a LocationDetailTableCreatedMessage with the return code and error message.
                        WeakReferenceMessenger.Default.Send(
                            new CreateLocationDetailResultMessage(createLocationDetailResult)
                        );
                    }
                    break;
                // SecchiObservations does not have a location detail table.
                case DbType.SecchiObservations:
                    break;
                default:
                    // Log to trace that the dbType is not implemented.
                    logger.LogError(
                        SqliteLog,
                        "Sqlite Service, FeatureToTable, create location detail table: dbType {dbType} is not implemented.",
                        dbType
                    );
                    break;
            }
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, FeatureToTable: Error creating Sqlite table from DbType {dbType}, Sqlite exception: {sqliteMessage}, with an error code of {SqliteErrorCode}",
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
                "Sqlite Service, FeatureToTable: Error creating Sqlite table from DbType {dbType}: {exception}.",
                dbType.ToString(),
                exception.ToString()
            );
        }
    }

    public async Task AddLocationRecordToTable(LocationRecord LocationRecord, DbType DbType)
    {
        IInsertValueBuilder fluentBuilder;

        try
        {
            logger.LogTrace(SqliteLog, "AddLocationRecordToTable called.");

            // Log the LocationRecord to trace.
            logger.LogTrace(SqliteLog, "LocationRecord: {LocationRecord}", LocationRecord);

            switch (DbType)
            {
                case DbType.SecchiLocations:

                    // Create a fluent builder to add a location record to the SecchiLocations table.
                    fluentBuilder = SimpleBuilder
                        .CreateFluent()
                        .InsertInto($"SecchiLocations")
                        .Columns(
                            $"Latitude, Longitude, LocationId, LocationName, LocationType, Status, LocationCollected"
                        )
                        .Values(
                            $@"{LocationRecord.Latitude}, {LocationRecord.Longitude}, {LocationRecord.LocationId},
                            {LocationRecord.LocationName}, {(int)LocationRecord.LocationType}, {(int)RecordStatus.WorkingSet},
                            {(int)LocationCollected.NotCollected}"
                        );

                    // Log builder.sql to trace.
                    logger.LogTrace(
                        SqliteLog,
                        "Sqlite Service, AddLocationRecordToTable: Sqlite insert statement (fluentBuilder): {builder.Sql}",
                        fluentBuilder.Sql
                    );
                    break;

                default:
                    logger.LogError(
                        SqliteLog,
                        "Sqlite Service, AddLocationRecordToTable: dbType {DbType} is not implemented.",
                        DbType
                    );
                    return;
            }

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();
            // Execute the insert statement.
            var insertedRecords = database.Execute(fluentBuilder.Sql, fluentBuilder.Parameters);
            // Log the number of inserted records to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, AddLocationRecordToTable: Inserted {insertedRecords} records into {DbType} table.",
                insertedRecords,
                DbType
            );

            // Send a message that the location record has been added to the table.
            WeakReferenceMessenger.Default.Send(
                new LocationRecordAddedToTableMessage(
                    new DbTypeAndLocationId(DbType, LocationRecord.LocationId)
                )
            );
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, AddLocationRecordToTable: Error adding location record to table: {sqliteException.Message}, with an error code of {sqliteException.SqliteErrorCode}",
                sqliteException.Message,
                sqliteException.SqliteErrorCode
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, AddLocationRecordToTable: Error adding location record to table: {exception}.",
                exception.ToString()
            );
        }
    }

    public async Task<LocationRecord> GetLocationRecordFromTable(int LocationId, DbType DbType)
    {
        IWhereBuilder fluentBuilder;

        try
        {
            logger.LogTrace(SqliteLog, "GetLocationRecordFromTable called.");
            // Log the LocationId to trace.
            logger.LogTrace(SqliteLog, "LocationId: {LocationId}", LocationId);
            // Log the DbType to trace.
            logger.LogTrace(SqliteLog, "DbType: {DbType}", DbType);

            /*
            // Create the table portion of the sqlite select statement.
            var selectSqlTablePortion = $@"SELECT * FROM {DbType}";
            // Log selectLocationRecordSql to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite select statement (table portion): {selectSqlTablePortion}",
                selectSqlTablePortion
            );
            */

            Guard.Against.Null(
                LocationId,
                nameof(LocationId),
                "Sqlite Service, GetLocationRecordCollectionState: Location number is null."
            );

            Guard.Against.Null(
                DbType,
                nameof(DbType),
                "SqliteService, GetLocationRecordCollectionState: Database type is null"
            );

            switch (DbType)
            {
                case DbType.SecchiLocations:

                    fluentBuilder = SimpleBuilder
                        .CreateFluent()
                        .Select($"*")
                        .From($"SecchiLocations")
                        .Where($"LocationId = {LocationId}");

                    // Log builder.sql to trace.
                    logger.LogTrace(
                        SqliteLog,
                        "Sqlite Service, GetLocationRecordFromTable: Sqlite select statement (fluentBuilder): {builder.Sql}",
                        fluentBuilder.Sql
                    );

                    break;
                default:
                    logger.LogError(
                        SqliteLog,
                        "Sqlite Service, GetLocationRecordFromTable: dbType {DbType} is not implemented.",
                        DbType
                    );
                    return new LocationRecord();
            }

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            var locations = database.Query<LocationRecord>(
                fluentBuilder.Sql,
                fluentBuilder.Parameters
            );

            // Log the number of location records to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, GetLocationRecordFromTable: Retrieved {locationRecord} records from {DbType} table.",
                locations.Count(),
                DbType
            );

            // log locations to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, GetLocationRecordFromTable: LocationRecord: {locations}",
                locations
            );

            if (locations.Count() == 1)
            {
                return locations.First();
            }
            else
            {
                // Log an error indicating that multiple locations were found.
                logger.LogError(
                    SqliteLog,
                    "Sqlite Service, GetLocationRecordFromTable: multiple records({numberLocations}) with LocationId {LocationId} were found in {DbType} table.",
                    locations.Count(),
                    LocationId,
                    DbType
                );

                // Return an empty location record.
                return new LocationRecord();
            }
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, GetLocationRecordFromTable: Error retrieving location record from table: {sqliteException.Message}, with an error code of {sqliteException.SqliteErrorCode}",
                sqliteException.Message,
                sqliteException.SqliteErrorCode
            );
            return new LocationRecord();
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, GetLocationRecordFromTable: Error retrieving location record from table: {exception}",
                exception.ToString()
            );
            return new LocationRecord();
        }
    }

    public async Task DeleteLocationRecordFromTable(int locationId, DbType DbType)
    {
        int deletedRecords;

        try
        {
            logger.LogTrace(SqliteLog, "DeleteLocationRecordFromTable called.");
            // Log the locationId to trace.
            logger.LogTrace(SqliteLog, "LocationId: {locationId}", locationId);

            // Create the sqlite delete statement.
            var deleteLocationRecordSql = $"DELETE FROM {DbType} WHERE LocationId = {locationId}";
            // Log builder.sql to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite delete statement: {deleteLocationRecordSql}",
                deleteLocationRecordSql
            );

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            // Execute the delete statement.
            using var deleteCommand = database.CreateCommand();
            deleteCommand.CommandText = deleteLocationRecordSql;
            deletedRecords = deleteCommand.ExecuteNonQuery();

            // Log the number of deleted records to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, DeleteLocationRecordFromTable: Deleted {deletedRecords} records from {DbType} table.",
                deletedRecords,
                DbType
            );

            // Send a message that the location record has been deleted from the table.
            WeakReferenceMessenger.Default.Send(
                new LocationRecordDeletedFromTableMessage(
                    new DbTypeAndLocationId(DbType, locationId)
                )
            );
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, DeleteLocationRecordFromTable: Error deleting location record from table: {sqliteException.Message}, with an error code of {sqliteException.SqliteErrorCode}",
                sqliteException.Message,
                sqliteException.SqliteErrorCode
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, DeleteLocationRecordFromTable: Error deleting location record from table: {exception}.",
                exception.ToString()
            );
        }
    }

    public async Task UpdadateLocationRecordInTable(LocationRecord LocationRecord, DbType DbType)
    {
        try
        {
            logger.LogTrace(SqliteLog, "UpdateLocationRecordInTable called.");
            // Log the LocationRecord to trace.
            logger.LogTrace(SqliteLog, "LocationRecord: {LocationRecord}", LocationRecord);

            var updateSqlTablePortion =
                $@"
                UPDATE {DbType}
                ";

            // Create the sqlite update statement.
            var builder = SimpleBuilder.Create(
                $@" SET Latitude = {LocationRecord.Latitude}, Longitude = {LocationRecord.Longitude}, LocationId = {LocationRecord.LocationId}, LocationName = \""{LocationRecord.LocationName}\"", LocationType = {(int)LocationRecord.LocationType}, Status = {(int)RecordStatus.WorkingSet}, LocationCollected = {(int)LocationCollected.NotCollected} WHERE LocationId = {LocationRecord.LocationId}"
            );

            var sqlStatement = updateSqlTablePortion + builder.Sql;

            // Log builder.sql to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, UpdadateLocationRecordInTable: Sqlite update statement: {builder.Sql}",
                sqlStatement
            );

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            // Get the number of updated records from the update statement.
            var updatedRecords = database.Execute(sqlStatement, builder.Parameters);

            // Log the number of updated records to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, UpdadateLocationRecordInTable: Updated {updatedRecords} records in {DbType} table.",
                updatedRecords,
                DbType
            );

            // Send a message that the location record has been updated in the table.
            WeakReferenceMessenger.Default.Send(
                new LocationRecordUpdatedInTableMessage(
                    new DbTypeAndLocationId(DbType, LocationRecord.LocationId)
                )
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, UpdadateLocationRecordInTable: Error updating location record in table: {exception}.",
                exception.ToString()
            );
        }
    }

    public async Task SetLocationRecordtoCollectedState(
        int LocationId,
        DbType DbType,
        LocationCollected LocationCollectedState,
        LocationsCollectedStateScope Scope
    )
    {
        IWhereBuilder fluentBuilder;

        try
        {
            logger.LogDebug(SqliteLog, "SetLocationRecordtoCollected called.");
            // Log the DbType to trace.
            logger.LogDebug(SqliteLog, "DbType: {DbType}", DbType);
            // Log the locationId to trace.
            logger.LogDebug(SqliteLog, "LocationId: {locationId}", LocationId);
            // Log the LocationCollectedState to trace.
            logger.LogDebug(
                SqliteLog,
                "LocationCollectedState: {LocationCollectedState}",
                LocationCollectedState
            );

            Guard.Against.Null(
                LocationId,
                nameof(LocationId),
                "Sqlite Service, SetLocationRecordtoCollectedState: Location number is null."
            );

            Guard.Against.Null(
                DbType,
                nameof(DbType),
                "SqliteService, SetLocationRecordtoCollectedState: Database type is null"
            );

            Guard.Against.Null(
                LocationCollectedState,
                nameof(LocationCollectedState),
                "SqliteService, SetLocationRecordtoCollectedState: LocationCollectedState is null"
            );

            switch (DbType)
            {
                case DbType.SecchiLocations:

                    fluentBuilder = SimpleBuilder
                        .CreateFluent()
                        .Update($"SecchiLocations")
                        .Set($"LocationCollected = {(int)LocationCollectedState}")
                        .Where($"LocationId = {LocationId}");

                    if (Scope == LocationsCollectedStateScope.AllLocations)
                    {
                        logger.LogInformation(
                            SqliteLog,
                            "Sqlite Service, SetLocationRecordtoCollected: Setting all locations in SecchiLocations to collected state {LocationCollectedState}.",
                            LocationCollectedState
                        );
                        // Update all locations
                        fluentBuilder = SimpleBuilder
                            .CreateFluent()
                            .Update($"SecchiLocations")
                            .Set($"LocationCollected = {(int)LocationCollectedState}");
                    }
                    else
                    {
                        logger.LogDebug(
                            SqliteLog,
                            "Sqlite Service, SetLocationRecordtoCollected: Setting location {LocationId} in SecchiLocations to collected state {LocationCollectedState}.",
                            LocationId,
                            LocationCollectedState
                        );
                        // Update a specific location
                        fluentBuilder = SimpleBuilder
                            .CreateFluent()
                            .Update($"SecchiLocations")
                            .Set($"LocationCollected = {(int)LocationCollectedState}")
                            .Where($"LocationId = {LocationId}");
                    }

                    break;

                default:
                    logger.LogError(
                        SqliteLog,
                        "Sqlite Service, GetLocationRecordCollectionState: dbType {DbType} is not implemented.",
                        DbType
                    );
                    return;
            }

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            // Log builder.sql to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, SetLocationRecordtoCollected: Sqlite update statement (fluentBuilder): {builder.Sql}",
                fluentBuilder.Sql
            );

            // Execute the update statement.
            var updatedRecords = database.Execute(fluentBuilder.Sql, fluentBuilder.Parameters);

            // Log the number of updated records to trace.
            logger.LogDebug(
                SqliteLog,
                "Sqlite Service, SetLocationRecordtoCollected: Updated {updatedRecords} records in {DbType} table.",
                updatedRecords,
                DbType
            );
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, SetLocationRecordtoCollected: Error updating location record collection state: {sqliteException.Message}, with an error code of {sqliteException.SqliteErrorCode}",
                sqliteException.Message,
                sqliteException.SqliteErrorCode
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, SetLocationRecordtoCollected: Error setting location record to collected: {exception}.",
                exception.ToString()
            );
        }
    }

    public async Task<LocationFlags> GetLocationFlags(int LocationId, DbType DbType)
    {
        IWhereBuilder fluentBuilder;

        try
        {
            logger.LogDebug(SqliteLog, "Sqlite Service, GetLocationFlags called.");
            // Log the DbType to trace.
            logger.LogDebug(SqliteLog, "DbType: {DbType}", DbType);
            // Log the locationId to trace.
            logger.LogDebug(SqliteLog, "LocationId: {locationId}", LocationId);

            Guard.Against.Null(
                LocationId,
                nameof(LocationId),
                "Sqlite Service, GetLocationFlags: Location number is null."
            );

            Guard.Against.Null(
                DbType,
                nameof(DbType),
                "SqliteService, GetLocationFlags: Database type is null"
            );

            switch (DbType)
            {
                case DbType.SecchiLocations:

                    fluentBuilder = SimpleBuilder
                        .CreateFluent()
                        .Select(
                            $"location.LocationId, location.Status, location.LocationCollected, detail.CollectionDirection, detail.CollectOccasional"
                        )
                        .From($"SecchiLocations location")
                        .LeftJoin(
                            $"SecchiLocationDetail detail ON location.LocationId = detail.LocationId"
                        )
                        .Where($"location.LocationId = {LocationId}");

                    break;

                default:
                    logger.LogError(
                        SqliteLog,
                        "Sqlite Service, GetLocationFlags: dbType {DbType} is not implemented.",
                        DbType
                    );
                    return new LocationFlags();
            }

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            // Log builder.sql to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, GetLocationFlags: Sqlite select statement (fluentBuilder): {builder.Sql}",
                fluentBuilder.Sql
            );

            var locationFlagsQueryResult = database.Query<LocationFlagsDto>(
                fluentBuilder.Sql,
                fluentBuilder.Parameters
            );

            // Log the number of location flags to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, GetLocationFlags: Retrieved {locationFlagsQueryResult} records from {DbType} table.",
                locationFlagsQueryResult.Count(),
                DbType
            );

            if (locationFlagsQueryResult.Count() == 1)
            {
                // Load the location flags from the query result.
                var dto = locationFlagsQueryResult.First();

                // Create a new location flags object from the query result.
                // For the CollectionDirection and CollectOccasional fields, use the enum value if it is not null,
                // otherwise use the default value.
                var locationFlags = new LocationFlags
                {
                    LocationId = dto.LocationId,
                    RecordStatus = (RecordStatus)dto.Status,
                    LocationCollected = (LocationCollected)dto.LocationCollected,
                    CollectionDirection = dto.CollectionDirection.HasValue
                        ? (CollectionDirection)dto.CollectionDirection.Value
                        : CollectionDirection.OneWay,
                    CollectOccasional = dto.CollectOccasional.HasValue
                        ? (CollectOccasional)dto.CollectOccasional.Value
                        : CollectOccasional.Collect
                };
                return locationFlags;
            }
            else
            {
                // Log an error indicating that multiple locations were found.
                logger.LogError(
                    SqliteLog,
                    "Sqlite Service, GetLocationFlags: multiple records({numberLocations}) with LocationId {LocationId} were found in {DbType} table.",
                    locationFlagsQueryResult.Count(),
                    LocationId,
                    DbType
                );

                // Return an empty location record.
                return new LocationFlags();
            }
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, GetLocationFlags: Error retrieving location flags from table: {sqliteException.Message}, with an error code of {sqliteException.SqliteErrorCode}",
                sqliteException.Message,
                sqliteException.SqliteErrorCode
            );
            return new LocationFlags();
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, GetLocationFlags: Error retrieving location flags from table: {exception}",
                exception.ToString()
            );
            return new LocationFlags();
        }
    }

    public async Task<LocationCollected> GetLocationRecordCollectionState(
        int LocationId,
        DbType DbType
    )
    {
        IWhereBuilder fluentBuilder;

        try
        {
            logger.LogDebug(SqliteLog, "Sqlite Service, GetLocationRecordCollectionState called.");
            // Log the DbType to trace.
            logger.LogDebug(
                SqliteLog,
                "Sqlite Service, GetLocationRecordCollectionState, DbType: {DbType}",
                DbType
            );
            // Log the locationId to trace.
            logger.LogDebug(
                SqliteLog,
                "Sqlite Service, GetLocationRecordCollectionState, LocationId: {locationId}",
                LocationId
            );

            Guard.Against.Null(
                LocationId,
                nameof(LocationId),
                "Sqlite Service, GetLocationRecordCollectionState: Location number is null."
            );

            Guard.Against.Null(
                DbType,
                nameof(DbType),
                "SqliteService, GetLocationRecordCollectionState: Database type is null"
            );

            switch (DbType)
            {
                case DbType.SecchiLocations:

                    fluentBuilder = SimpleBuilder
                        .CreateFluent()
                        .Select($"LocationCollected")
                        .From($"SecchiLocations")
                        .Where($"LocationId = {LocationId}");
                    break;

                default:
                    logger.LogError(
                        SqliteLog,
                        "Sqlite Service, GetLocationRecordCollectionState: dbType {DbType} is not implemented.",
                        DbType
                    );
                    return LocationCollected.Error;
            }

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            // Log builder.sql to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, GetLocationRecordCollectionState: Sqlite select statement (fluentBuilder): {builder.Sql}",
                fluentBuilder.Sql
            );

            var locationCollected = database.Query<LocationCollected>(
                fluentBuilder.Sql,
                fluentBuilder.Parameters
            );

            // Log the number of location collected records to trace.
            logger.LogDebug(
                SqliteLog,
                "Sqlite Service, GetLocationRecordCollectionState: Retrieved {collectedCount} records from {DbType} table.",
                locationCollected.Count(),
                DbType
            );

            // Make sure that only one record was returned and return an enum.
            if (locationCollected.Count() == 1)
            {
                var locationCollectedEnum = (LocationCollected)locationCollected.First();
                // Log the locationCollected value to debug.
                logger.LogDebug(
                    SqliteLog,
                    "Sqlite Service, GetLocationRecordCollectionState: LocationCollected: {locationCollected}",
                    locationCollectedEnum
                );
                return locationCollectedEnum;
            }
            else
            {
                // Log an error indicating that multiple locations were found.
                logger.LogError(
                    SqliteLog,
                    "Sqlite Service, GetLocationRecordCollectionState, Error: ({numberLocations}) records with LocationId {LocationId} were found in {DbType} table.",
                    locationCollected.Count(),
                    LocationId,
                    DbType
                );
                return LocationCollected.Error;
            }
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, GetLocationRecordCollectionState: Error getting location record collection state: {sqliteException.Message}, with an error code of {sqliteException.SqliteErrorCode}",
                sqliteException.Message,
                sqliteException.SqliteErrorCode
            );
            return LocationCollected.Error;
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, GetLocationRecordCollectionState: Error getting location record collection state: {exception}.",
                exception.ToString()
            );
            return LocationCollected.Error;
        }
    }

    public async Task<CreateLocationDetailResult> CreateLocationDetailTable(DbType dbType)
    {
        var locationDetailTableName = dbType.ToString();
        string locationTableName;

        try
        {
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, CreateLocationDetailTable: CreateLocationDetailTable called."
            );

            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, CreateLocationDetailTable: locationDetailTableName is {locationDetailTableName}.",
                locationDetailTableName
            );

            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "Sqlite Service, CreateLocationDetailTable: localSettingsService is null."
            );

            switch (dbType)
            {
                case DbType.SecchiLocationDetail:

                    logger.LogTrace(
                        SqliteLog,
                        "Sqlite Service, CreateLocationDetailTable: dbType is SecchiLocationDetail."
                    );

                    locationTableName = DbType.SecchiLocations.ToString();

                    logger.LogTrace(
                        SqliteLog,
                        "Sqlite Service, CreateLocationDetailTable: locationTableName is {locationTableName}.",
                        locationTableName
                    );

                    var detailPreviouslyLoaded = await localSettingsService.ReadSettingAsync<bool>(
                        SqliteConfiguration.Item[Key.SecchiLocationDetailLoaded]
                    );
                    if (detailPreviouslyLoaded)
                    {
                        // Log to trace that the SecchiLocationDetail table has already been loaded.
                        logger.LogTrace(
                            SqliteLog,
                            "Sqlite Service, CreateLocationDetailTable: Feature table {SecchiLocationDetail} has already been loaded, returning from CreateLocationDetailTable().",
                            locationDetailTableName
                        );
                        SendTableAvailableMessage(DbType.SecchiLocationDetail);
                        // Return a CreateLocationDetailResult with a status of Success.
                        return new CreateLocationDetailResult(
                            dbType,
                            CreateLocationDetailStatus.SuccessPreviouslyLoaded,
                            0,
                            string.Empty
                        );
                    }
                    else
                    {
                        // Log to trace that the SecchiLocationDetail table has not been loaded.
                        logger.LogTrace(
                            SqliteLog,
                            "Sqlite Service, CreateLocationDetailTable: Feature table {SecchiLocationDetail} has not been loaded.",
                            locationDetailTableName
                        );
                    }

                    var locationsPreviouslyLoaded =
                        await localSettingsService.ReadSettingAsync<bool>(
                            SqliteConfiguration.Item[Key.SecchiLocationsLoaded]
                        );

                    if (locationsPreviouslyLoaded is false)
                    {
                        // Log to trace that the SecchiLocations table has not been loaded.
                        logger.LogError(
                            SqliteLog,
                            "Sqlite Service, CreateLocationDetailTable: Feature table {SecchiLocations} has not been loaded. Location detail may not be created prior to locations existing.",
                            locationTableName
                        );
                        return new CreateLocationDetailResult(
                            dbType,
                            CreateLocationDetailStatus.Failure,
                            -1,
                            $@"Feature table {locationTableName} has not been loaded, unable to create detail table."
                        );
                    }
                    break;
                default:
                    // Log a warning that the dbType is not implemented.
                    logger.LogWarning(
                        SqliteLog,
                        "Sqlite Service, CreateLocationDetailTable: dbType {dbType} is not implemented.",
                        dbType
                    );
                    return new CreateLocationDetailResult(
                        dbType,
                        CreateLocationDetailStatus.Failure,
                        -1,
                        $@"The DbType {dbType} is not implemented."
                    );
            }

            var featureDetailTableCreateStatement =
                $@"
                    CREATE TABLE IF NOT EXISTS {locationDetailTableName} (
                    LocationId INTEGER PRIMARY KEY,
                    CollectionDirection INTEGER NOT NULL,
                    CollectOccasional INTEGER NOT NULL,
                    CONSTRAINT fk_locations FOREIGN KEY(LocationId) REFERENCES {locationTableName}(LocationId)
                    );
                ";

            // Log to trace the builder.sql.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, CreateLocationDetailTable: Create SQL: {featureDetailTableCreateStatement}",
                featureDetailTableCreateStatement
            );

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            // Create the Sqlite table using the featureTableCreateStatement.
            using var createTableCommand = database.CreateCommand();
            createTableCommand.CommandText = featureDetailTableCreateStatement;
            var createReturnCode = createTableCommand.ExecuteNonQuery();

            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, CreateLocationDetailTable: Sqlite detail table created for {locationTableName}. Return code: {returnCode}",
                locationTableName,
                createReturnCode
            );

            if (createReturnCode == 0)
            {
                // Log to trace that the detail table was created.
                logger.LogTrace(
                    SqliteLog,
                    "Sqlite Service, CreateLocationDetailTable: Detail table created for {locationTableName}.",
                    locationTableName
                );
                // Set the previouslyLoaded setting to true.
                await SetPreviouslyLoadedState(dbType, true);
                return new CreateLocationDetailResult(
                    dbType,
                    CreateLocationDetailStatus.SuccessNewCreated,
                    createReturnCode,
                    string.Empty
                );
            }
            else
            {
                // Log to trace that the detail table was not created.
                logger.LogError(
                    SqliteLog,
                    "Sqlite Service, CreateLocationDetailTable: Detail table not created for {locationTableName}.",
                    locationTableName
                );
                return new CreateLocationDetailResult(
                    dbType,
                    CreateLocationDetailStatus.Failure,
                    createReturnCode,
                    $@"Detail table not created for {locationTableName}."
                );
            }
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, CreateLocationDetailTable: Error creating Sqlite table from DbType {dbType}, Sqlite exception: {sqliteMessage}, with an error code of {SqliteErrorCode}",
                dbType.ToString(),
                sqliteException.Message,
                sqliteException.SqliteErrorCode
            );
            return new CreateLocationDetailResult(
                dbType,
                CreateLocationDetailStatus.Failure,
                sqliteException.SqliteErrorCode,
                sqliteException.Message
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, CreateLocationDetailTable: Error creating location detail table: {exception}.",
                exception.ToString()
            );
            return new CreateLocationDetailResult(
                dbType,
                CreateLocationDetailStatus.Failure,
                -1,
                exception.Message
            );
        }
    }

    public async Task UpdateLocationDetailRecordInDetailTable(
        int locationId,
        LocationDetail locationDetail,
        DbType dbType
    )
    {
        Builder builder;

        try
        {
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, UpdateLocationDetailRecordInDetailTable: UpdateLocationDetailRecordInDetailTable called."
            );

            Guard.Against.Null(
                locationDetail.CollectionDirection,
                nameof(locationDetail.CollectionDirection),
                "Collected direction is null."
            );
            Guard.Against.Null(
                locationDetail.CollectOccasional,
                nameof(locationDetail.CollectOccasional),
                "Collect occassional is null"
            );

            switch (dbType)
            {
                case DbType.SecchiLocationDetail:
                    // Log to trace that the dbType is SecchiLocationDetail.
                    logger.LogTrace(
                        SqliteLog,
                        "Sqlite Service, UpdateLocationDetailRecordInDetailTable: dbType is SecchiLocationDetail."
                    );
                    // Create the sqlite insert statement.
                    builder = SimpleBuilder.Create(
                        $@"
                            UPDATE {dbType} SET CollectionDirection = 
                            {(int)locationDetail.CollectionDirection},
                            CollectOccasional = {(int)locationDetail.CollectOccasional}
                            WHERE LocationId = {locationId}
                        "
                    );

                    // Log to trace the builder.sql.
                    logger.LogTrace(
                        SqliteLog,
                        "Sqlite Service, UpdateLocationDetailRecordInDetailTable: Update SQL: {builder.sql}",
                        builder.Sql
                    );
                    break;
                default:
                    // Log a warning that the dbType is not implemented.
                    logger.LogWarning(
                        SqliteLog,
                        "Sqlite Service, UpdateLocationDetailRecordInDetailTable: dbType {dbType} is not implemented.",
                        dbType
                    );
                    return;
            }

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            // Execute the insert statement.
            using var insertCommand = database.CreateCommand();
            insertCommand.CommandText = builder.Sql;
            var insertedRecords = insertCommand.ExecuteNonQuery();

            // Log the number of inserted records to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, UpdateLocationDetailRecordInDetailTable: Inserted {insertedRecords} records into {dbType} table.",
                insertedRecords,
                dbType
            );
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, UpdateLocationDetailRecordInDetailTable: Error creating Sqlite table from DbType {dbType}, Sqlite exception: {sqliteMessage}, with an error code of {SqliteErrorCode}",
                dbType.ToString(),
                sqliteException.Message,
                sqliteException.SqliteErrorCode
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, UpdateLocationDetailRecordInDetailTable: Error updating location detail table: {exception}.",
                exception.ToString()
            );
        }
    }

    public async Task AddLocationDetailRecordToDetailTable(
        int locationId,
        LocationDetail locationDetail,
        DbType dbType
    )
    {
        Builder builder;

        try
        {
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, AddLocationDetailRecordToDetailTable: AddLocationDetailRecord called."
            );

            Guard.Against.Null(
                locationDetail.CollectionDirection,
                nameof(locationDetail.CollectionDirection),
                "Sqlite Service, AddLocationDetailRecordToDetailTable: Collected direction is null."
            );
            Guard.Against.Null(
                locationDetail.CollectOccasional,
                nameof(locationDetail.CollectOccasional),
                "Sqlite Service, AddLocationDetailRecordToDetailTable: Collect occassional is null"
            );

            switch (dbType)
            {
                case DbType.SecchiLocationDetail:
                    // Log to trace that the dbType is SecchiLocationDetail.
                    logger.LogTrace(SqliteLog, "dbType is SecchiLocationDetail.");
                    // Create the sqlite insert statement.
                    builder = SimpleBuilder.Create(
                        $@"
                            INSERT INTO {dbType} (LocationId, CollectionDirection, CollectOccasional) VALUES (
                            {locationId},
                            {(int)locationDetail.CollectionDirection},
                            {(int)locationDetail.CollectOccasional})
                        "
                    );

                    // Log to trace the builder.sql.
                    logger.LogTrace(
                        SqliteLog,
                        "Sqlite Service, AddLocationDetailRecordToDetailTable: Sqlite insert statement: {builder.sql}",
                        builder.Sql
                    );
                    break;
                default:
                    // Log a warning that the dbType is not implemented.
                    logger.LogWarning(
                        SqliteLog,
                        "Sqlite Service, AddLocationDetailRecordToDetailTable: dbType {dbType} is not implemented.",
                        dbType
                    );
                    return;
            }

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            // Execute the insert statement.
            using var insertCommand = database.CreateCommand();
            insertCommand.CommandText = builder.Sql;
            var insertedRecords = insertCommand.ExecuteNonQuery();

            // Log the number of inserted records to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, AddLocationDetailRecordToDetailTable: Inserted {insertedRecords} records into {dbType} table.",
                insertedRecords,
                dbType
            );
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, AddLocationDetailRecordToDetailTable: Error creating Sqlite table from DbType {dbType}, Sqlite exception: {sqliteMessage}, with an error code of {SqliteErrorCode}",
                dbType.ToString(),
                sqliteException.Message,
                sqliteException.SqliteErrorCode
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, AddLocationDetailRecordToDetailTable: Error adding location detail record to table: {exception}.",
                exception.ToString()
            );
        }
    }

    public async Task<LocationDetail> GetLocationDetailRecordFromDetailTable(
        int locationId,
        DbType dbType
    )
    {
        Builder builder;

        try
        {
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, GetLocationDetailRecordFromDetailTable: GetLocationDetailRecord called."
            );

            switch (dbType)
            {
                case DbType.SecchiLocationDetail:
                    // Log to trace that the dbType is SecchiLocationDetail.
                    logger.LogTrace(
                        SqliteLog,
                        "Sqlite Service, GetLocationDetailRecordFromDetailTable: dbType is SecchiLocationDetail."
                    );
                    // Create the sqlite select statement.
                    builder = SimpleBuilder.Create(
                        $@"SELECT FROM {dbType} WHERE LocationId = {locationId}"
                    );

                    // Log to trace the builder.sql.
                    logger.LogTrace(
                        SqliteLog,
                        "Sqlite Service, GetLocationDetailRecordFromDetailTable: Sqlite select statement: {builder.sql}",
                        builder.Sql
                    );
                    break;
                default:
                    // Log a warning that the dbType is not implemented.
                    logger.LogWarning(
                        SqliteLog,
                        "Sqlite Service, GetLocationDetailRecordFromDetailTable: dbType {dbType} is not implemented.",
                        dbType
                    );
                    // return a new LocationDetail with values set to null.
                    return new LocationDetail();
            }

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            // Execute the select statement.
            using var selectCommand = database.CreateCommand();
            selectCommand.CommandText = builder.Sql;

            // Read the data using the selectCommand
            using var reader = selectCommand.ExecuteReader();

            // Log the number of location records to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, GetLocationDetailRecordFromDetailTable: Retrieved {locationRecord} records from {dbType} table.",
                reader.RecordsAffected,
                dbType
            );

            // If there are records, read the first one
            if (reader.Read())
            {
                // Set the properties of the LocationDetail object based on the selected record
                var collectionDirectionStr = reader["CollectionDirection"] as string;
                var collectOccasionalStr = reader["CollectOccasional"] as string;

                // If the values are not null, then parse them to the enum values.
                if (collectionDirectionStr is not null && collectOccasionalStr is not null)
                {
                    var collectionDirection = Enum.Parse<CollectionDirection>(
                        collectionDirectionStr
                    );
                    var collectOccasional = Enum.Parse<CollectOccasional>(collectOccasionalStr);

                    // Initialize a new LocationDetail object, setting the properties to the values read from the database.
                    var locationDetail = new LocationDetail
                    {
                        CollectionDirection = collectionDirection,
                        CollectOccasional = collectOccasional
                    };

                    // Return the LocationDetail object
                    return locationDetail;
                }
                else
                {
                    // If the values are null, then set the enum values to the default values.
                    return new LocationDetail();
                }
            }
            else
            {
                // If there are no records, then return a new LocationDetail object with the properties set to the default values.
                return new LocationDetail();
            }
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, GetLocationDetailRecordFromDetailTable: Error getting detail record from Sqlite table with DbType {dbType}, Sqlite exception: {sqliteMessage}, with an error code of {SqliteErrorCode}",
                dbType.ToString(),
                sqliteException.Message,
                sqliteException.SqliteErrorCode
            );
            // Return a new LocationDetail object with the properties set to the default values.
            return new LocationDetail();
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, GetLocationDetailRecordFromDetailTable: Error getting location detail record from table: {exception}.",
                exception.ToString()
            );
            // Return a new LocationDetail object with the properties set to the default values.
            return new LocationDetail();
        }
    }

    public async Task DeleteLocationDetailRecordFromDetailTable(int locationId, DbType dbType)
    {
        Builder builder;

        try
        {
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, DeleteLocationDetailRecordFromDetailTable: DeleteLocationDetailRecord called."
            );

            switch (dbType)
            {
                case DbType.SecchiLocationDetail:
                    // Log to trace that the dbType is SecchiLocationDetail.
                    logger.LogTrace(SqliteLog, "dbType is SecchiLocationDetail.");
                    // Create the sqlite insert statement.
                    builder = SimpleBuilder.Create(
                        $@"
                            DELETE FROM {dbType} WHERE LocationId = {locationId}
                        "
                    );

                    // Log to trace the builder.sql.
                    logger.LogTrace(
                        SqliteLog,
                        "Sqlite Service, DeleteLocationDetailRecordFromDetailTable: Sqlite delete statement: {builder.sql}",
                        builder.Sql
                    );
                    break;
                default:
                    // Log a warning that the dbType is not implemented.
                    logger.LogWarning(
                        SqliteLog,
                        "Sqlite Service, DeleteLocationDetailRecordFromDetailTable: dbType {dbType} is not implemented.",
                        dbType
                    );
                    return;
            }

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            // Execute the insert statement.
            using var deleteCommand = database.CreateCommand();
            deleteCommand.CommandText = builder.Sql;
            var deletedRecords = deleteCommand.ExecuteNonQuery();

            // Log the number of deleted records to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, DeleteLocationDetailRecordFromDetailTable: Deleted {deletedRecords} records from {dbType} table.",
                deletedRecords,
                dbType
            );
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, DeleteLocationDetailRecordFromDetailTable: Error deleting location detail record from DbType {dbType}, Sqlite exception: {sqliteMessage}, with an error code of {SqliteErrorCode}",
                dbType.ToString(),
                sqliteException.Message,
                sqliteException.SqliteErrorCode
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, DeleteLocationDetailRecordFromDetailTable: Error deleting location detail record from table: {exception}.",
                exception.ToString()
            );
        }
    }

    private async void SetToInitialRun()
    {
        try
        {
            logger.LogTrace(SqliteLog, "SetToInitialRun called.");

            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "Sqlite Service, SetToInitialRun(): localSettingsService is null."
            );

            // Get the current folder.
            var sqliteFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                sqliteFolderName,
                CreationCollisionOption.OpenIfExists
            );

            // Log the sqlite folder to trace.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, SetToInitialRun(): Sqlite folder: {sqliteFolder}",
                sqliteFolder
            );

            // Log that deleting files with DbName is starting.
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, SetToInitialRun(): Starting deleting files named {DbName}.",
                DbName
            );

            // Delete any files with DbName.
            var files = await sqliteFolder.GetFilesAsync();
            foreach (var file in files)
            {
                // Log the name of the current file to trace.
                logger.LogTrace(
                    SqliteLog,
                    "Sqlite Service, SetToInitialRun(): Checking file name: {file.Name}",
                    file.Name
                );

                if (file.Name == DbName)
                {
                    // Log that the file is being deleted to trace.
                    logger.LogTrace(
                        SqliteLog,
                        "Sqlite Service, SetToInitialRun(): Deleting file: {file.Name}",
                        file.Name
                    );
                    try
                    {
                        await file.DeleteAsync();
                    }
                    catch (System.Runtime.InteropServices.COMException ex)
                    {
                        logger.LogError(
                            SqliteLog,
                            ex,
                            "Sqlite Service, SetToInitialRun(): Error deleting file: {file.Name}. Check to see if the file is open by another application.",
                            file.Name
                        );
                    }
                }
            }

            // Set SqliteFolder to string.Empty.
            await localSettingsService.SaveSettingAsync(
                SqliteConfiguration.Item[Key.SqliteFolder],
                string.Empty
            );

            // Set sqliteInitialRun to false.
            await localSettingsService.SaveSettingAsync(
                SqliteConfiguration.Item[Key.SqliteInitialRun],
                false
            );
            await InitialRun();
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, SetToInitialRun(): Error in SetToInitialRun: {exception}.",
                exception.ToString()
            );
        }
    }

    private async Task InitialRun()
    {
        try
        {
            logger.LogTrace(SqliteLog, "InitialRun called.");

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
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, InitialRun(): Sqlite folder: {sqliteFolderName}",
                sqliteFolderName
            );

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

            // Set SecchiLocationDetailsLoaded to false.
            await localSettingsService.SaveSettingAsync(
                SqliteConfiguration.Item[Key.SecchiLocationDetailLoaded],
                false
            );

            // Set sqliteInitialRun to false.
            await localSettingsService.SaveSettingAsync(
                SqliteConfiguration.Item[Key.SqliteInitialRun],
                false
            );

            // Log that the initial run has been completed.
            logger.LogInformation(SqliteLog, "Sqlite Service, InitialRun: Initial run completed.");
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, InitialRun: Error in InitialRun: {exception}.",
                exception.ToString()
            );
        }
    }

    private void SendTableAvailableMessage(DbType dbType)
    {
        // Log the table available message to trace.
        logger.LogTrace(
            SqliteLog,
            "Sqlite Service, SendTableAvailableMessage: Sending TableAvailableMessage for {dbType}.",
            dbType.ToString()
        );

        // Send a TableAvailableMessage with the dbType.
        WeakReferenceMessenger.Default.Send(new TableAvailableMessage(dbType));
    }

    public async Task<IEnumerable<LocationRecord>> GetRecordGroupFromSqlite(
        DbType dbType,
        int pageSize,
        int pageNumber
    )
    {
        try
        {
            logger.LogTrace(
                SqliteLog,
                "Sqlite Service, GetRecordGroupFromSqlite: called with dbType of {dbType}.",
                dbType
            );

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            switch (dbType)
            {
                case DbType.SecchiLocations:
                    //Log to trace that the dbType is SecchiLocations.
                    logger.LogTrace(
                        SqliteLog,
                        "Sqlite Service, GetRecordGroupFromSqlite: dbType is SecchiLocations."
                    );

                    // Calculate the offset so that the correct records for a page are retrieved.
                    var offset = pageSize * pageNumber;

                    //Dapper.SimpleSqlBuilder to build the SQL query.
                    var builder = SimpleBuilder
                        .CreateFluent()
                        .Select($"*")
                        .From($"SecchiLocations")
                        .OrderBy($"LocationId")
                        .Limit(pageSize)
                        .Offset(offset);

                    // Log builder.sql to trace.
                    logger.LogTrace(
                        SqliteLog,
                        "Sqlite Service, GetRecordGroupFromSqlite: Sqlite query: {builder.Sql}",
                        builder.Sql
                    );

                    // Execute the query and retrieve the results.
                    var locations = await database.QueryAsync<LocationRecord>(
                        builder.Sql,
                        builder.Parameters
                    );

                    if (locations.Count() < pageSize)
                    {
                        // Log a warning that the number of records returned is less than the page size.
                        logger.LogWarning(
                            SqliteLog,
                            "Sqlite Service, GetRecordGroupFromSqlite: Number of records returned is less than the page size, so a message can be sent."
                        );
                    }

                    // Iterate over the locations and log them to trace.
                    foreach (var location in locations)
                    {
                        logger.LogTrace(
                            SqliteLog,
                            "Sqlite Service, GetRecordGroupFromSqlite: Location: {location}",
                            location
                        );
                    }

                    return locations;

                default:
                    // Log a warning that the dbType is not implemented.
                    logger.LogWarning(
                        SqliteLog,
                        "Sqlite Service, GetRecordGroupFromSqlite: dbType {dbType} is not implemented.",
                        dbType
                    );
                    return [];
            }
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, GetRecordGroupFromSqlite: Error creating Sqlite table from DbType {dbType}, Sqlite exception: {sqliteMessage}, with an error code of {SqliteErrorCode}",
                dbType.ToString(),
                sqliteException.Message,
                sqliteException.SqliteErrorCode
            );
            return [];
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Sqlite Service, GetRecordGroupFromSqlite: Error getting SecchiLocations: {exception}.",
                exception.ToString()
            );
            return [];
        }
    }

    public async Task<IEnumerable<SecchiLocation>> GetSecchiLocationsFromSqlite(
        int pageSize,
        int pageNumber
    )
    {
        try
        {
            logger.LogTrace(
                SqliteLog,
                "GetSecchiLocationsFromSqlite called with a pageSize of {pageSize} and a pageNumber of {pageNumber}.",
                pageSize,
                pageNumber
            );

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            // Calculate the offset so that the correct records for a page are retrieved.
            var offset = pageSize * pageNumber;

            //Dapper.SimpleSqlBuilder to build the SQL query.
            var builder = SimpleBuilder
                .CreateFluent()
                .Select($"*")
                .From($"SecchiLocations")
                .OrderBy($"LocationId DESC")
                .Limit(pageSize)
                .Offset(offset);

            // Log builder.sql to trace.
            logger.LogTrace(SqliteLog, "Sqlite query: {builder.Sql}", builder.Sql);

            // Execute the query and retrieve the results.
            var locations = await database.QueryAsync<SecchiLocation>(
                builder.Sql,
                builder.Parameters
            );

            return locations;
        }
        catch (SqliteException sqliteException)
        {
            logger.LogError(
                SqliteLog,
                "SqliteService, GetSecchiLocationsFromSqlite, Sqlite exception: {sqliteMessage}, with an error code of {SqliteErrorCode}",
                sqliteException.Message,
                sqliteException.SqliteErrorCode
            );

            // Return an empty list of SecchiLocation.
            return [];
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Error getting SecchiLocations: {exception}.",
                exception.ToString()
            );

            // Return an empty list of SecchiLocation.
            return [];
        }
    }

    private async Task SetPreviouslyLoadedState(DbType dbType, bool state)
    {
        Guard.Against.Null(
            localSettingsService,
            nameof(localSettingsService),
            "Sqlite Service, SetPreviouslyLoadedState: localSettingsService is null."
        );

        // Set the previouslyLoaded setting to true.
        await localSettingsService.SaveSettingAsync(
            SqliteConfiguration.Item[
                dbType switch
                {
                    DbType.SecchiObservations => Key.SecchiObservationsLoaded,
                    DbType.SecchiLocations => Key.SecchiLocationsLoaded,
                    DbType.SecchiLocationDetail => Key.SecchiLocationDetailLoaded,
                    _ => throw new NotImplementedException()
                }
            ],
            state
        );
        // Log that the previouslyLoaded state has been set to true.
        logger.LogTrace(
            SqliteLog,
            "Sqlite Service, SetPreviouslyLoadedState: Set {dbType} previouslyLoaded state to true.",
            dbType.ToString()
        );
    }

    private static string GetSqliteType(Field field)
    {
        var fieldType = field.FieldType.ToString();
        var sqliteType = GeodatabaseSqliteTypeConversion[fieldType];
        /*
        logger.LogTrace(
            SqliteLog,
            "Sqlite Service, GetSqliteType: Field type {fieldType} converted to {sqliteType}.",
            fieldType,
            sqliteType
        );
        */
        return sqliteType;
    }
}
