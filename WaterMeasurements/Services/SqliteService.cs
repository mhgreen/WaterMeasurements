using System.Data;
using Ardalis.GuardClauses;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Dapper;
using Dapper.SimpleSqlBuilder;
using Esri.ArcGISRuntime.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Models;
using Windows.Storage;
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

// Message to nofify modules that a table is available.
public class TableAvailableMessage(DbType dbType) : ValueChangedMessage<DbType>(dbType) { }

// Message to add a SecchiObservation to the Sqlite database.
public class AddSecchiObservationMessage(SecchiObservation secchiObservation)
    : ValueChangedMessage<SecchiObservation>(secchiObservation) { }

// Message to add a SecchiLocation to the Sqlite database.
public class AddSecchiLocationMessage(SecchiLocation secchiLocation)
    : ValueChangedMessage<SecchiLocation>(secchiLocation) { }

// Message to request a group of records from the Sqlite database.
public class GetSqliteRecordsGroupRequest(SqliteRecordsGroupRequest sqliteRecordsGroupRequest)
    : ValueChangedMessage<SqliteRecordsGroupRequest>(sqliteRecordsGroupRequest) { }

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

            // Register a message handler for AddSecchiLocationMessage.
            WeakReferenceMessenger.Default.Register<AddSecchiLocationMessage>(
                this,
                (recipient, message) =>
                {
                    // Log the AddSecchiLocationMessage.
                    logger.LogDebug(
                        SqliteLog,
                        "SqliteService, AddSecchiLocationMessage: {message}.",
                        message
                    );

                    // Call AddSecchiLocation with the extracted secchiLocation.
                    AddSecchiLocation(message.Value);
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
                "Sqlite Service, FeatureToTable(): localSettingsService is null."
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
            var featureTableFields = featureTableFieldsDictionary.Select(field =>
                $"{field.Key} {field.Value}"
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
            var createReturnCode = createTableCommand.ExecuteNonQuery();

            logger.LogTrace(
                SqliteLog,
                "Sqlite table created from {featureTableName}. Return code: {returnCode}",
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
                    "Number of records in {featureTableName} feature table: {numberOfRecords}.",
                    featureTableName,
                    features.Count()
                );

                // Set the previouslyLoaded setting to true.
                await SetPreviouslyLoadedState(dbType);

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
                fieldValues["Status"] = (int)RecordStatus.Comitted;

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
                var insertedRecords = insertCommand.ExecuteNonQuery();

                // Keep a running total of the number of inserted records.
                // In the end, this should equal the number of features.
                sumOfInsertedRecords += insertedRecords;

                // log the sumOfInsertedRecords to trace.
                logger.LogDebug(
                    SqliteLog,
                    "For the table {featureTableName}: sumOfInsertedRecords is {sumOfInsertedRecords}.",
                    featureTableName,
                    sumOfInsertedRecords
                );
            }

            // Send a message that the table is available.
            SendTableAvailableMessage(dbType);

            // Set the previouslyLoaded setting to true.
            await SetPreviouslyLoadedState(dbType);

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
                    "Expected to insert {expectedInserts} but only {actualInserts} were inserted.",
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
                            "Not all records from the featuretable were written to Sqlite.",
                            FeatureToTableStatus.SuccessWithPartialRecords
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

    private void AddSecchiLocation(SecchiLocation secchiLocation)
    {
        try
        {
            logger.LogTrace(SqliteLog, "AddSecchiLocation called.");

            // Log the SecchiLocation to trace.
            logger.LogTrace(SqliteLog, "SecchiLocation: {secchiLocation}", secchiLocation);

            // Create the sqlite insert statement.
            var insertStatement =
                $"INSERT INTO SecchiLocations (Latitude, Longitude, LocationId, Location, LocationType) VALUES ({secchiLocation.Latitude}, {secchiLocation.Longitude}, {secchiLocation.LocationId}, \"{secchiLocation.Location}\", {(int)secchiLocation.LocationType});";
            logger.LogTrace(
                SqliteLog,
                "Sqlite insert statement: {insertStatement}",
                insertStatement
            );
            var builder = SimpleBuilder.Create(
                $"""
                    INSERT INTO SecchiLocations (Latitude, Longitude, LocationId, Location, LocationType)
                    VALUES ({secchiLocation.Latitude}, {secchiLocation.Longitude}, {secchiLocation.LocationId}, \"{secchiLocation.Location}\", {(int)
                    secchiLocation.LocationType})
                """
            );

            // Log builder.sql to trace.
            logger.LogTrace(SqliteLog, "Sqlite insert statement: {builder.Sql}", builder.Sql);

            builder = SimpleBuilder.Create(
                $@"
                    INSERT INTO SecchiLocations (Latitude, Longitude, LocationId, Location, LocationType)
                    VALUES ({secchiLocation.Latitude}, {secchiLocation.Longitude}, {secchiLocation.LocationId}, \""{secchiLocation.Location}\"", {(int)secchiLocation.LocationType})
                "
            );

            // Log builder.sql to trace.
            logger.LogTrace(SqliteLog, "Sqlite insert statement: {builder.Sql}", builder.Sql);

            /*
            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            // Execute the insert statement.
            using var insertCommand = database.CreateCommand();
            insertCommand.CommandText = insertStatement;
            var insertedRecords = insertCommand.ExecuteNonQuery();

            // Log the number of inserted records to trace.
            logger.LogTrace(
                SqliteLog,
                "Inserted {insertedRecords} records into SecchiLocations table.",
                insertedRecords
            );

            // Send a message that the table is available.
            SendTableAvailableMessage(DbType.SecchiLocations);
            */
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Error adding SecchiLocation: {exception}.",
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

            // Delete any files with DbName.
            var files = await sqliteFolder.GetFilesAsync();
            foreach (var file in files)
            {
                if (file.Name == DbName)
                {
                    await file.DeleteAsync();
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
                "Error in SetToInitialRun: {exception}.",
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

    private void SendTableAvailableMessage(DbType dbType)
    {
        // Log the table available message to trace.
        logger.LogTrace(
            SqliteLog,
            "Sending TableAvailableMessage for {dbType}.",
            dbType.ToString()
        );

        // Send a TableAvailableMessage with the dbType.
        WeakReferenceMessenger.Default.Send(new TableAvailableMessage(dbType));
    }

    private async Task GetRecordGroupFromSqlite(DbType dbType, int pageSize, int pageNumber)
    {
        try
        {
            logger.LogTrace(
                SqliteLog,
                "GetObservableCollection called with dbType of {dbType}.",
                dbType
            );

            // Open the connection to the sqlite database.
            using var database = await GetOpenConnectionAsync();

            switch (dbType)
            {
                case DbType.SecchiLocations:
                    //Log to trace that the dbType is SecchiLocations.
                    logger.LogTrace(SqliteLog, "dbType is SecchiLocations.");

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
                    logger.LogTrace(SqliteLog, "Sqlite query: {builder.Sql}", builder.Sql);

                    // Execute the query and retrieve the results.
                    var locations = await database.QueryAsync<SecchiLocation>(
                        builder.Sql,
                        builder.Parameters
                    );

                    if (locations.Count() < pageSize)
                    {
                        // Log a warning that the number of records returned is less than the page size.
                        logger.LogWarning(
                            SqliteLog,
                            "Number of records returned is less than the page size, so a message can be sent."
                        );
                    }

                    // Iterate over the locations and log them to trace.
                    foreach (var location in locations)
                    {
                        WeakReferenceMessenger.Default.Send(
                            new SecchiLocationsSqliteRecordGroup(location)
                        );
                    }
                    break;

                default:
                    // Log a warning that the dbType is not implemented.
                    logger.LogWarning(SqliteLog, "dbType {dbType} is not implemented.", dbType);
                    break;
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
        }
        catch (Exception exception)
        {
            logger.LogError(
                SqliteLog,
                "Error getting SecchiLocations: {exception}.",
                exception.ToString()
            );
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
                .OrderBy($"LocationId")
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

    private async Task SetPreviouslyLoadedState(DbType dbType)
    {
        Guard.Against.Null(
            localSettingsService,
            nameof(localSettingsService),
            "Sqlite Service, SetPreviouslyLoadedState(): localSettingsService is null."
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
        // Log that the previouslyLoaded state has been set to true.
        logger.LogTrace(
            SqliteLog,
            "Set {dbType} previouslyLoaded state to true.",
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
            "Field type {fieldType} converted to {sqliteType}.",
            fieldType,
            sqliteType
        );
        */
        return sqliteType;
    }
}
