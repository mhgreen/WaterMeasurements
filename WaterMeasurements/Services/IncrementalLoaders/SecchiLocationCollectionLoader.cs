using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Ardalis.GuardClauses;
using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Portal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using NLog;
using NLog.Fluent;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Models;
using WaterMeasurements.ViewModels;
using Windows.Foundation;

namespace WaterMeasurements.Services.IncrementalLoaders;

public class SecchiLocationCollectionLoader
    : ObservableCollection<SecchiLocationDisplay>,
        ISupportIncrementalLoading
{
    private int pageIndex = 0;
    private readonly int pageSize = 10;
    private bool hasMoreItems = true;

    public bool HasMoreItems => hasMoreItems;

    // Set the eventId for the logger.
    private readonly EventId SecchiLocationLoaderLog = new(17, "SecchiLocationCollectionLoader");

    // Set the SqliteService and Logger properties.
    // These properties are set by the dependency injection system.
    // The factory method didn't seem to work in SecchiViewModel.
    // This may be due to how the incremental loader is used in the SecchiViewModel.

    private readonly ILogger<SecchiLocationCollectionLoader> logger;
    private readonly ISqliteService? sqliteService;

    private struct SecchiChannelNumbers
    {
        public uint ObservationChannel { get; set; }
        public uint LocationChannel { get; set; }
        public uint GeoTriggerChannel { get; set; }
    }

    // create secchiLocationsFeatures as a FeatureTable.
    private FeatureTable? secchiLocationFeatures;

    private QueryParameters queryParameters = new() { WhereClause = "1=1" };

    private int totalFeatureCount;
    private int totalFeaturePages;

    public SecchiLocationCollectionLoader()
    {
        var currentApp = Application.Current as App;
        Guard.Against.Null(currentApp, nameof(currentApp), "Application.Current can not be null");
        // var serviceProvider = (Application.Current as App).Host.Services;
        var serviceProvider = currentApp.Host.Services;
        sqliteService = serviceProvider.GetService<ISqliteService>();
        Guard.Against.Null(
            sqliteService,
            nameof(sqliteService),
            "SecchiLocationCollectionLoader: SecchiLocationCollectionLoader can't work without the Sqlite service."
        );
        var loggerProvider = serviceProvider.GetService<ILogger<SecchiLocationCollectionLoader>>();
        Guard.Against.Null(
            loggerProvider,
            nameof(loggerProvider),
            "SecchiLocationCollectionLoader: SecchiLocationCollectionLoader loggerProvider is null."
        );
        logger = loggerProvider;
        Guard.Against.Null(
            logger,
            nameof(logger),
            "SecchiLocationCollectionLoader: SecchiLocationCollectionLoader can't work without a logger."
        );

        logger.LogDebug(SecchiLocationLoaderLog, "SecchiLocationCollectionLoader created.");

        SecchiChannelNumbers secchiChannelNumbers = new();

        // Request the Secchi channel numbers from SecchiViewModel.
        var secchiChannelMessageResult =
            WeakReferenceMessenger.Default.Send<SecchiChannelRequestMessage>();
        if (secchiChannelMessageResult.Response.LocationChannel is not 0)
        {
            secchiChannelNumbers.LocationChannel = secchiChannelMessageResult
                .Response
                .LocationChannel;
            secchiChannelNumbers.ObservationChannel = secchiChannelMessageResult
                .Response
                .ObservationChannel;
            secchiChannelNumbers.GeoTriggerChannel = secchiChannelMessageResult
                .Response
                .GeoTriggerChannel;

            // Log to trace the individual channel numbers.
            logger.LogTrace(
                SecchiLocationLoaderLog,
                "SecchiLocationCollectionLoader: SecchiChannelRequestMessage, ObservationChannel: {ObservationChannel}, LocationChannel: {LocationChannel}, GeoTriggerChannel: {GeoTriggerChannel}",
                secchiChannelNumbers.ObservationChannel,
                secchiChannelNumbers.LocationChannel,
                secchiChannelNumbers.GeoTriggerChannel
            );
        }

        // Register to get location featuretable messages on the secchiLocationsChannel.

        if (secchiChannelNumbers.LocationChannel is not 0)
        {
            // Register to get location featuretable messages on the secchiLocationsChannel.
            WeakReferenceMessenger.Default.Register<FeatureTableMessage, uint>(
                this,
                secchiChannelNumbers.LocationChannel,
                (recipient, message) =>
                {
                    logger.LogTrace(
                        SecchiLocationLoaderLog,
                        "SecchiLocationCollectionLoader: FeatureTableMessage, secchiLocationsChannel: {secchiLocationsChannel}, FeatureTable: {featureTable}.",
                        secchiChannelNumbers.LocationChannel,
                        message.Value.TableName
                    );

                    secchiLocationFeatures = message.Value;
                }
            );
        }
        else
        {
            // Log to trace that the secchiLocationsChannel is not set.
            logger.LogError(
                SecchiLocationLoaderLog,
                "SecchiLocationCollectionLoader: secchiLocationsChannel is not set."
            );
        }
    }

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
    {
        return AsyncInfo.Run((cancellationToken) => LoadMoreItemsAsync(count, cancellationToken));
    }

    private async Task<LoadMoreItemsResult> LoadMoreItemsAsync(
        uint count,
        CancellationToken cancellationToken
    )
    {
        _ = cancellationToken;
        try
        {
            // Make sure the SqliteService is available.
            Guard.Against.Null(
                sqliteService,
                nameof(SqliteService),
                "SecchiLocationCollectionLoader can't work without the Sqlite service."
            );

            // Write to the log that LoadMoreItemsAsync is called along with the count.
            logger.LogDebug(
                SecchiLocationLoaderLog,
                "LoadMoreItemsAsync called with count: {Count}",
                count
            );

            // Call the GetsecchiLocationsFromSqlite method to retrieve the records.
            var queryResult = await sqliteService.GetSecchiLocationsFromSqlite(pageSize, pageIndex);

            // secchiLocations.AddRange(queryResult.Select(item => new SecchiLocationDisplay(latitude: item.Latitude, longitude: item.Longitude, locationId: item.LocationId, locationName: item.Location, locationType: item.LocationType)).ToList());

            // Iterate through the queryResult and write the results to the log.
            foreach (var item in queryResult)
            {
                logger.LogTrace(
                    SecchiLocationLoaderLog,
                    "GetPagedItemAsync, queryResult: {item.LocationName}, {item.Latitude}, {item.Longitude}, {item.LocationType}, {item.LocationId}",
                    item.Location,
                    item.Latitude,
                    item.Longitude,
                    item.LocationType,
                    item.LocationId
                );
                // Add the retrieved item to the secchiLocations list.
                /*
                 *
                Add(
                    new SecchiLocationDisplay(
                        latitude: item.Latitude,
                        longitude: item.Longitude,
                        locationId: item.LocationId,
                        locationName: item.Location,
                        locationType: item.LocationType
                    )
                );
                */
            }

            // Log then number of records retrieved.
            logger.LogTrace(
                SecchiLocationLoaderLog,
                "GetPagedItemAsync: Fetched {queryResult.Count()} items.",
                queryResult.Count()
            );

            queryParameters = new QueryParameters
            {
                WhereClause = "1=1",
                ResultOffset = (pageIndex * pageSize),
                MaxFeatures = pageSize,
                ReturnGeometry = true,
            };

            if (secchiLocationFeatures is null)
            {
                logger.LogError(
                    SecchiLocationLoaderLog,
                    "SecchiLocationCollectionLoader: secchiLocationFeatures is null."
                );
                return new LoadMoreItemsResult { Count = 0 };
            }
            // Query the secchiLocationFeatures for the next set of features.
            var result = await secchiLocationFeatures.QueryFeaturesAsync(
                queryParameters,
                CancellationToken.None
            );
            totalFeatureCount = result.Count();
            totalFeaturePages = (int)Math.Ceiling((double)totalFeatureCount / pageSize);

            // Log the totalFeatureCount and the totalFeaturePages.
            logger.LogTrace(
                SecchiLocationLoaderLog,
                "GetPagedItemAsync: TotalFeatureCount: {totalFeatureCount}, TotalFeaturePages: {totalFeaturePages}",
                totalFeatureCount,
                totalFeaturePages
            );
            // Log the results of the query.
            foreach (var feature in result)
            {
                logger.LogTrace(
                    SecchiLocationLoaderLog,
                    "GetPagedItemAsync: (not sorted) Feature: Id {LocationId} Location Name: {Location}, Latitude: {latitude}, Longitude: {longitude}, Location Type: {locationType}  Geometry: {Geometry}",
                    feature.Attributes["LocationId"],
                    feature.Attributes["Location"],
                    feature.Attributes["Latitude"],
                    feature.Attributes["Longitude"],
                    feature.Attributes["LocationType"],
                    feature.Geometry
                );

                Add(
                    new SecchiLocationDisplay(
                        latitude: (double)feature.Attributes["Latitude"],
                        longitude: (double)feature.Attributes["Longitude"],
                        locationId: (int)feature.Attributes["LocationId"],
                        locationName: (string)feature.Attributes["Location"],
                        locationType: (LocationType)feature.Attributes["LocationType"]
                    )
                );
            }

            foreach (var field in result)
            {
                if (field.Attributes.TryGetValue("LocationId", out var locationId))
                {
                    if (locationId is null)
                    {
                        logger.LogError(
                            SecchiLocationLoaderLog,
                            "GetPagedItemAsync: LocationId is null."
                        );
                        continue;
                    }
                    var locationIdInt = (int)locationId;
                    // Log the locationIdResult.
                    logger.LogTrace(
                        SecchiLocationLoaderLog,
                        "GetPagedItemAsync: LocationId after verification: {locationIdResult}",
                        locationIdInt
                    );
                }
                else
                {
                    logger.LogError(
                        SecchiLocationLoaderLog,
                        "GetPagedItemAsync: LocationId is not a field in the Secchi locations geodatabase."
                    );
                }
            }

            pageIndex++;

            hasMoreItems = queryResult.Count() == pageSize;
            // hasMoreItems = false;

            return new LoadMoreItemsResult { Count = (uint)queryResult.Count() };
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Error in LoadMoreItemsAsync: {ErrorMessage}",
                exception.Message
            );
            throw;
        }
    }
}
