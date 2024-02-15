using Ardalis.GuardClauses;
using CommunityToolkit.WinUI.Collections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Models;

namespace WaterMeasurements.Services.IncrementalLoaders;

public class SecchiLocationIncrementalLoader : IIncrementalSource<SecchiLocationDisplay>
{
    private readonly int pageSize = 3;
    private int currentPage = 0;
    private bool hasMoreItems = true;
    private readonly List<SecchiLocationDisplay> secchiLocations;

    // Set the eventId for the logger.
    private readonly EventId SecchiLocationLoaderLog = new(16, "SecchiLocationIncrementalLoader");

    // Set the SqliteService and Logger properties.
    // These properties are set by the dependency injection system.
    // The factory method didn't seem to work in SecchiViewModel.
    // This may be due to how the incremental loader is used in the SecchiViewModel.

    private readonly ILogger<SecchiLocationIncrementalLoader> logger;
    private readonly ISqliteService? sqliteService;

    public bool HasMoreItems => hasMoreItems;

    public SecchiLocationIncrementalLoader()
    {
        var currentApp = Application.Current as App;
        Guard.Against.Null(currentApp, nameof(currentApp), "Application.Current can not be null");
        // var serviceProvider = (Application.Current as App).Host.Services;
        var serviceProvider = currentApp.Host.Services;
        sqliteService = serviceProvider.GetService<ISqliteService>();
        Guard.Against.Null(sqliteService, nameof(sqliteService), "SecchiLocationIncrementalLoader can't work without the Sqlite service.");
        var loggerProvider = serviceProvider.GetService<ILogger<SecchiLocationIncrementalLoader>>();
        Guard.Against.Null(loggerProvider, nameof(loggerProvider), "SecchiLocationIncrementalLoader loggerProvider is null.");
        logger = loggerProvider;
        Guard.Against.Null(logger, nameof(logger), "SecchiLocationIncrementalLoader can't work without a logger.");


        logger.LogDebug(
            SecchiLocationLoaderLog,
            "SecchiLocationIncrementalLoader created."
        );

        secchiLocations = [];
        _ = Initialize();
    }

    private async Task Initialize()
    {
        try
        {

            logger.LogInformation(
                SecchiLocationLoaderLog,
                "Fetching paged items. PageSize: {pageSize}, Page:{currentPage}.",
                pageSize,
                currentPage
            );

            // Make sure the SqliteService is available.
            Guard.Against.Null(sqliteService, nameof(SqliteService), "SecchiLocationIncrementalLoader can't work without the Sqlite service.");

            // Call the GetsecchiLocationsFromSqlite method to retrieve the records.
            var queryResult = await sqliteService.GetSecchiLocationsFromSqlite(pageSize, currentPage);

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
            }

            currentPage++;

            foreach (var item in queryResult)
            {
                secchiLocations.Add(new(latitude: item.Latitude, longitude: item.Longitude, locationId: item.LocationId, locationName: item.Location, locationType: item.LocationType));
            }

            // Iterate through secchiLocations and write the results to the log.
            foreach (var item in secchiLocations)
            {

                logger.LogTrace(
                    SecchiLocationLoaderLog,
                    "GetPagedItemAsync, secchiLocations: {item.LocationName}, {item.Latitude}, {item.Longitude}, {item.LocationType}, {item.LocationId}",
                    item.LocationName,
                    item.Latitude,
                    item.Longitude,
                    item.LocationType,
                    item.LocationId
                );
            }

            // Check if there are more items available.
            var hasMoreItems = secchiLocations.Count == pageSize;

            // Update the hasMoreItems field based on the result.
            this.hasMoreItems = hasMoreItems;

            // Log the result.
            logger.LogInformation(
                SecchiLocationLoaderLog,
                "Fetched {secchiLocations.Count()} items. HasMoreItems: {this.hasMoreItems}",
                secchiLocations.Count,
                this.hasMoreItems
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SecchiLocationLoaderLog,
                "Error initializing SecchiLocationIncrementalLoader: {ex.Message}",
                exception.Message
                );
        }
    }

    public async Task<IEnumerable<SecchiLocationDisplay>> GetPagedItemsAsync(
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        // Make sure the SqliteService is available.
        Guard.Against.Null(sqliteService, nameof(SqliteService), "SecchiLocationIncrementalLoader can't work without the Sqlite service.");

        logger.LogInformation(
                SecchiLocationLoaderLog,
                "GetPagedItemAsync: Fetching paged items. PageSize: {pageSize}, Page:{currentPage}.",
                pageSize,
                currentPage
            );

        // Call the GetsecchiLocationsFromSqlite method to retrieve the records.
        var queryResult = await sqliteService.GetSecchiLocationsFromSqlite(pageSize, currentPage);

        // Iterate through the queryResult and write the results to the log.
        foreach (var item in queryResult)
        {
            logger.LogTrace(
                SecchiLocationLoaderLog,
                "GetPagedItemAsync: {item.LocationName}, {item.Latitude}, {item.Longitude}, {item.LocationType}, {item.LocationId}",
                item.Location,
                item.Latitude,
                item.Longitude,
                item.LocationType,
                item.LocationId
            );
        }

        currentPage++;

        // Add the retrieved records to the secchiLocations list.
        secchiLocations.AddRange(queryResult.Select(item => new SecchiLocationDisplay(latitude: item.Latitude, longitude: item.Longitude, locationId: item.LocationId, locationName: item.Location, locationType: item.LocationType)).ToList());


        // Check if there are more items available.
        var hasMoreItems = secchiLocations.Count == pageSize;

        // Update the hasMoreItems field based on the result.
        this.hasMoreItems = hasMoreItems;

        // Log the result.
        logger.LogInformation(
            SecchiLocationLoaderLog,
            "Fetched {secchiLocations.Count()} items. HasMoreItems: {this.hasMoreItems}",
            secchiLocations.Count,
            this.hasMoreItems
        );

        // Return the retrieved secchiLocations
        return secchiLocations;
    }
}
