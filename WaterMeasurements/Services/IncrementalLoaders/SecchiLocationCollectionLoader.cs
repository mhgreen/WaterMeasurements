using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Ardalis.GuardClauses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Models;
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
            "SecchiLocationCollectionLoader can't work without the Sqlite service."
        );
        var loggerProvider = serviceProvider.GetService<ILogger<SecchiLocationCollectionLoader>>();
        Guard.Against.Null(
            loggerProvider,
            nameof(loggerProvider),
            "SecchiLocationCollectionLoader loggerProvider is null."
        );
        logger = loggerProvider;
        Guard.Against.Null(
            logger,
            nameof(logger),
            "SecchiLocationCollectionLoader can't work without a logger."
        );

        logger.LogDebug(SecchiLocationLoaderLog, "SecchiLocationCollectionLoader created.");
    }

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
    {
        return AsyncInfo.Run((cancellationToken) => LoadMoreItemsAsync(cancellationToken, count));
    }

    private async Task<LoadMoreItemsResult> LoadMoreItemsAsync(
        CancellationToken cancellationToken,
        uint count
    )
    {
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
