using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Ardalis.GuardClauses;
using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Helpers;
using WaterMeasurements.Models;
using WaterMeasurements.ViewModels;
using Windows.Foundation;
using WinRT;

namespace WaterMeasurements.Services.IncrementalLoaders;

public class SecchiLocationCollectionLoader
    : ObservableCollection<SecchiLocationDisplay>,
        ISupportIncrementalLoading
{
    private int pageIndex = 0;
    private readonly int pageSize = 10;
    private bool hasMoreItems = true;

    // HasMoreItems is a required property of ISupportIncrementalLoading.
    // It is used to determine if there are more items to load.
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

    private int queriedFeatureCount;

    // Initialize helpers.

    private readonly FeatureToType<double?, bool> featureDoubleConverter = new(null, false);
    private readonly FeatureToType<int?, bool> featureIntConverter = new(null, false);
    private readonly FeatureToType<short?, bool> featureShortConverter = new(null, false);
    private readonly FeatureToType<long?, bool> featureLongConverter = new(null, false);
    private readonly FeatureToType<string?, bool> featureStringConverter = new(null, false);
    private readonly FeatureToType<Guid?, bool> featureGuidConverter = new(null, false);

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

            /*

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

                Add(
                    new SecchiLocationDisplay(
                        latitude: item.Latitude,
                        longitude: item.Longitude,
                        locationId: item.LocationId,
                        locationName: item.Location,
                        locationType: item.LocationType
                    )
                );

            }

            // Log then number of records retrieved.
            logger.LogTrace(
                SecchiLocationLoaderLog,
                "GetPagedItemAsync: Fetched {queryResult.Count()} items.",
                queryResult.Count()
            );

            */

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
                hasMoreItems = false;
                return new LoadMoreItemsResult { Count = 0 };
            }
            // Query the secchiLocationFeatures for the next set of features.
            var result = await secchiLocationFeatures.QueryFeaturesAsync(
                queryParameters,
                CancellationToken.None
            );
            queriedFeatureCount = result.Count();
            // If totalFeaturePages is needed, that will have to be a separate query.
            // totalFeaturePages = (int)Math.Ceiling((double)queriedFeatureCount / pageSize);

            // Log the queriedFeatureCount and the totalFeaturePages.
            logger.LogTrace(
                SecchiLocationLoaderLog,
                "GetPagedItemAsync: Queried Feature Count: {queriedFeatureCount}",
                queriedFeatureCount
            );
            // Log the results of the query.
            foreach (var feature in result)
            {
                logger.LogTrace(
                    SecchiLocationLoaderLog,
                    "GetPagedItemAsync: (not sorted) Feature: Id {LocationId} Location Name: {LocationName}, Latitude: {latitude}, Longitude: {longitude}, Location Type: {locationType}  Geometry: {Geometry}",
                    feature.Attributes["LocationId"],
                    feature.Attributes["LocationName"],
                    feature.Attributes["Latitude"],
                    feature.Attributes["Longitude"],
                    feature.Attributes["LocationType"],
                    feature.Geometry
                );

                Guard.Against.Null(
                    feature.Geometry,
                    nameof(feature.Geometry),
                    "SecchiLocationCollectionLoader: feature.Geometry is null."
                );
                // Convert feature.Geometry to Wgs84geometry to add latitude and longitude to the location.
                var Wgs84geometry = feature
                    .Geometry.Project(SpatialReferences.Wgs84)
                    .As<MapPoint>();
                // var latitude = Wgs84geometry.Y;
                // var longitude = Wgs84geometry.X;


                // Convert from the feature table to the SecchiLocationDisplay.
                // Don't convert latitude or longitude, get those from the geometry.

                var conversionSuccess = true;
                List<string> notConverted = [];

                var locationIdConverted = featureIntConverter.ConvertInt32ToInt(
                    "LocationId",
                    feature
                );
                conversionSuccess |= locationIdConverted.Success;
                if (!locationIdConverted.Success)
                {
                    notConverted.Add("LocationId");
                }

                var locationNameConverted = featureStringConverter.ConvertTextToString(
                    "LocationName",
                    feature
                );
                conversionSuccess |= locationNameConverted.Success;
                if (!locationNameConverted.Success)
                {
                    notConverted.Add("LocationName");
                }

                var locationTypeConverted = featureShortConverter.ConvertInt32ToInt(
                    "LocationType",
                    feature
                );
                conversionSuccess |= locationTypeConverted.Success;
                if (!locationTypeConverted.Success)
                {
                    notConverted.Add("LocationType");
                }

                if (conversionSuccess)
                {
                    // Log all of the converted values.
                    logger.LogTrace(
                        SecchiLocationLoaderLog,
                        "GetPagedItemAsync: Features after conversion: Id {LocationId} Location Name: {Location}, Latitude: {latitude}, Longitude: {longitude}, Location Type: {locationType}",
                        locationIdConverted.Value,
                        locationNameConverted.Value,
                        Wgs84geometry.Y,
                        Wgs84geometry.X,
                        locationTypeConverted.Value
                    );

                    if (
                        locationIdConverted.Value is not null
                        && locationNameConverted.Value is not null
                        && locationTypeConverted.Value is not null
                    )
                    {
                        Add(
                            new SecchiLocationDisplay(
                                latitude: Wgs84geometry.Y,
                                longitude: Wgs84geometry.X,
                                locationId: (int)locationIdConverted.Value,
                                locationName: (string)locationNameConverted.Value,
                                locationType: (LocationType)locationTypeConverted.Value
                            )
                        );
                    }
                    else
                    {
                        // Log to error the contents of notConverted.
                        logger.LogError(
                            SecchiLocationLoaderLog,
                            "GetPagedItemAsync: Feature: The following values did not convert: {notConverted}",
                            notConverted
                        );
                    }
                }
                else
                {
                    // Log to error the contents of notConverted.
                    logger.LogError(
                        SecchiLocationLoaderLog,
                        "GetPagedItemAsync: Feature: The following values did not convert: {notConverted}",
                        notConverted
                    );
                }
            }

            // ----------- The following code is for debugging purposes only. -----------

            // The purpose of the code below is to determine the field names and types of the feature table.
            // This may be useful to add additional types to FeatureToType.cs.

            /*

            // Add the field names and types to a dictionary.
            var featureTableDictionary = result.Fields.ToDictionary(
                feature => feature.Name,
                feature => feature.FieldType
            );

            // Log the featureTableDictionary.
            foreach (var (key, value) in featureTableDictionary)
            {
                logger.LogTrace(
                    SecchiLocationLoaderLog,
                    "GetPagedItemAsync: FeatureTableDictionary: {key}, {value}",
                    key,
                    value
                );
            }

            // Get the first feature, its attributes and types.

            var firstFeature = result.FirstOrDefault();

            if (firstFeature is null)
            {
                logger.LogError(
                    SecchiLocationLoaderLog,
                    "GetPagedItemAsync: firstFeature is null."
                );
                return new LoadMoreItemsResult { Count = 0 };
            }

            var firstFeatureAttributes = firstFeature.Attributes.Values;
            var firstFeatureAttributeNames = firstFeatureAttributes.Select(attribute => attribute);
            var firstFeatureAttributeTypes = firstFeatureAttributes.Select(attribute =>
                attribute!.GetType()
            );

            // Log the firstFeatureAttributeNames and firstFeatureAttributeTypes.
            foreach (var (name, type) in firstFeatureAttributeNames.Zip(firstFeatureAttributeTypes))
            {
                logger.LogTrace(
                    SecchiLocationLoaderLog,
                    "GetPagedItemAsync: firstFeatureAttributeNames: {name}, firstFeatureAttributeTypes: {type}",
                    name,
                    type
                );
            }

            foreach (var feature in result)
            {
                logger.LogTrace(
                    SecchiLocationLoaderLog,
                    "GetPagedItemAsync: feature.Attributes: {featureAttributes}",
                    feature.Attributes
                );

                var featureAttributes = feature.Attributes.Values;
                var attribute = featureAttributes.FirstOrDefault();
                if (attribute is null)
                {
                    logger.LogError(
                        SecchiLocationLoaderLog,
                        "GetPagedItemAsync: attribute is null."
                    );
                    continue;
                }
            }

            */

            // ----------- The above code is for debugging purposes only. -----------

            pageIndex++;

            /* For SqliteService, use the following:
            hasMoreItems = queryResult.Count() == pageSize;
            return new LoadMoreItemsResult { Count = (uint)queryResult.Count() };
            */
            hasMoreItems = queriedFeatureCount == pageSize;
            return new LoadMoreItemsResult { Count = (uint)queriedFeatureCount };
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
