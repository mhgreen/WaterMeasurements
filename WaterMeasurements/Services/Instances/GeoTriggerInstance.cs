using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Geotriggers;
using Esri.ArcGISRuntime.Location;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Portal;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Tasks;
using Esri.ArcGISRuntime.Tasks.Offline;
using Microsoft.Extensions.Logging;
using WaterMeasurements.Contracts.Services.Instances;
using WaterMeasurements.Models;
using WaterMeasurements.Views;

namespace WaterMeasurements.Services.Instances;

public partial class GeoTriggerInstance : IGeoTriggerInstance
{
    private readonly ILogger<GeoTriggerInstance> logger;
    internal EventId GeoTriggerLog = new(6, "GeoTriggerInstance");

    public string Name { get; private set; }
    public uint Channel { get; private set; }
    public FeatureTable FeatureTable { get; private set; }
    public double TriggerDistance { get; private set; }

    private readonly SystemLocationDataSource? systemLocation = new();

    private GeotriggerMonitor? locationsMonitor;

    private LocationGeotriggerFeed? geotriggerFeed;

    // Constructor.
    public GeoTriggerInstance(
        ILogger<GeoTriggerInstance> logger,
        string name,
        uint channel,
        FeatureTable featureTable,
        double triggerDistance
    )
    {
        this.logger = logger;
        Name = name;
        Channel = channel;
        FeatureTable = featureTable;
        TriggerDistance = triggerDistance;

        _ = Initialize();
    }

    private async Task Initialize()
    {
        try
        {
            // Log that a GeoTrigger was added.
            logger.LogDebug(
                GeoTriggerLog,
                "GeoTriggerInstance, geotrigger instance created with a name of {name}, on channel {channel}, and a trigger distance of {triggerDistance}.",
                Name,
                Channel,
                TriggerDistance
            );

            // Query all of the records in the feature table.
            QueryParameters queryParameters = new() { WhereClause = "1=1" };
            var featureQueryResult = FeatureTable.QueryFeaturesAsync(queryParameters).Result;

            // Iterate over the results and log the feature attributes.
            foreach (var feature in featureQueryResult)
            {
                logger.LogTrace(
                    GeoTriggerLog,
                    "GeoTriggerInstance, feature attributes: {featureAttributes}.",
                    feature.Attributes
                );
            }

            // Log the start of the geotrigger.
            logger.LogDebug(
                GeoTriggerLog,
                "GeoTriggerInstance, starting geotrigger with a name of {name}, on channel {channel}, and a trigger distance of {triggerDistance}.",
                Name,
                Channel,
                TriggerDistance
            );

            // Check to see if the system location is available.
            if (systemLocation is null)
            {
                // Log that the system location is not available.
                logger.LogError(
                    GeoTriggerLog,
                    "GeoTriggerInstance, name {name} on channel {channel}: system location is not available, so geotriggering is not possible.",
                    Name,
                    Channel
                );
                logger.LogError(
                    GeoTriggerLog,
                    "Make sure that the GPS unit is attached and working."
                );

                // Throw an exception.
                throw new Exception(
                    "System location is not available, so geotriggering is not possible"
                );
            }

            // Use the system location data source to get the current location.
            geotriggerFeed = new LocationGeotriggerFeed(systemLocation);

            if (geotriggerFeed is null)
            {
                // Log that the geotrigger feed is not available.
                logger.LogError(
                    GeoTriggerLog,
                    "GeoTriggerInstance, name {name} on channel {channel}: geotrigger feed is not available, so geotriggering is not possible.",
                    Name,
                    Channel
                );
                logger.LogError(
                    GeoTriggerLog,
                    "Make sure that the GPS unit is attached and working."
                );

                // Throw an exception.
                throw new Exception(
                    "Geotrigger feed is not available, so geotriggering is not possible"
                );
            }

            if (geotriggerFeed.LocationDataSource is null)
            {
                // Log that the location data source is not available.
                logger.LogError(
                    GeoTriggerLog,
                    "GeoTriggerInstance, name {name} on channel {channel}: location data source is not available, so geotriggering is not possible.",
                    Name,
                    Channel
                );
                logger.LogError(
                    GeoTriggerLog,
                    "Make sure that the GPS unit is attached and working."
                );

                // Throw an exception.
                throw new Exception(
                    "Location data source is not available, so geotriggering is not possible"
                );
            }

            // Start the location data source.
            await geotriggerFeed.LocationDataSource.StartAsync();

            // Create parameters that define the geofence features and a buffer distance (meters).
            var fenceParameters = new FeatureFenceParameters(FeatureTable, TriggerDistance);

            if (fenceParameters is null)
            {
                // Log that the fence parameters are not available.
                logger.LogError(
                    GeoTriggerLog,
                    "GeoTriggerInstance, name {name} on channel {channel}: fence parameters are null, so geotriggering is not possible.",
                    Name,
                    Channel
                );

                // Throw an exception.
                throw new Exception("Fence parameters are null.");
            }

            if (fenceParameters.FeatureTable is null)
            {
                logger.LogError(
                    GeoTriggerLog,
                    "GeoTriggerInstance, name {name} on channel {channel}: fenceParameters.FeatureTable is null.",
                    Name,
                    Channel
                );
                throw new Exception("fenceParameters.FeatureTable is null.");
            }

            // Log to trace the fence parameters.
            logger.LogTrace(
                GeoTriggerLog,
                "GeoTriggerInstance, name {name} on channel {channel}: fenceParameters.FeatureTable: {featureTable}.",
                Name,
                Channel,
                fenceParameters.FeatureTable.ToString()
            );

            // Iterate through the features in the feature table.
            foreach (var field in fenceParameters.FeatureTable.Fields)
            {
                // Log to trace the feature.
                logger.LogDebug(
                    GeoTriggerLog,
                    "GeoTriggerInstance, name {name} on channel {channel}: fenceParameters.FeatureTable.Fields field: {field}.",
                    Name,
                    Channel,
                    field
                );
            }

            // create a where clause to get all the features
            queryParameters = new QueryParameters() { WhereClause = "1=1" };

            // query the feature table
            var queryResult = fenceParameters
                .FeatureTable.QueryFeaturesAsync(queryParameters)
                .Result;

            foreach (var feature in queryResult)
            {
                foreach (var attribute in feature.Attributes)
                {
                    logger.LogDebug(
                        GeoTriggerLog,
                        "GeoTriggerInstance, name {name} on channel {channel}: feature.Attributes: {attributeName}, Value: {attributeValue}.",
                        Name,
                        Channel,
                        attribute.Key,
                        attribute.Value
                    );
                }
            }

            // Create a geotrigger with the location feed, enter or exit rule type, and the fence parameters.
            var fenceGeotrigger = new FenceGeotrigger(
                geotriggerFeed,
                FenceRuleType.EnterOrExit,
                fenceParameters
            );

            // Create a GeotriggerMonitor to monitor the FenceGeotrigger created previously.
            locationsMonitor = new GeotriggerMonitor(fenceGeotrigger);

            // Add a notification handler for the GeotriggerMonitor.
            locationsMonitor.Notification += HandleGeotriggerNotification;

            // Start Geotrigger monitor.
            if (locationsMonitor is not null)
            {
                // Log the start of the monitor.
                logger.LogDebug(
                    GeoTriggerLog,
                    "GeoTriggerInstance, name {name} on channel {channel}: Starting Geotrigger monitor: {monitorName}.",
                    Name,
                    Channel,
                    locationsMonitor.ToString()
                );
                await locationsMonitor.StartAsync();
            }
            else
            {
                // Log that the monitor is null.
                logger.LogError(
                    GeoTriggerLog,
                    "GeoTriggerInstance, name {name} on channel {channel}: Geotrigger monitor is null.",
                    Name,
                    Channel
                );
                // Throw an exception.
                throw new Exception("Geotrigger monitor is null.");
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                GeoTriggerLog,
                exception,
                "GeoTriggerInstance, name {name} on channel {channel}: Exception: {message}.",
                Name,
                Channel,
                exception.Message.ToString()
            );
        }
    }

    private void HandleGeotriggerNotification(object? sender, GeotriggerNotificationInfo info)
    {
        ArcGISFeature? feature;

        // Log to debug the type of notification.
        logger.LogDebug(
            GeoTriggerLog,
            "GeoTriggerInstance, name {name} on channel {channel}, HandleGeotriggerNotification: {notificationType}, sending GeoTriggerNotification.",
            Name,
            Channel,
            info.GeotriggerMonitor.ToString()
        );

        // Send a message to notify modules of the GeoTrigger event and feature.
        WeakReferenceMessenger.Default.Send<GeoTriggerMessage, uint>(
            new GeoTriggerMessage(new GeoTriggerNotification(info)),
            Channel
        );

        if (info is FenceGeotriggerNotificationInfo fenceInfo)
        {
            try
            {
                switch (fenceInfo.FenceNotificationType)
                {
                    case FenceNotificationType.Entered:
                        logger.LogDebug(
                            GeoTriggerLog,
                            "GeoTriggerInstance, name {name} on channel {channel}, HandleGeotriggerNotification: fenceInfo.FenceNotificationType: Entered. {fenceInfo}.",
                            Name,
                            Channel,
                            fenceInfo.Message
                        );
                        break;
                    case FenceNotificationType.Exited:
                        logger.LogDebug(
                            GeoTriggerLog,
                            "GeoTriggerInstance, name {name} on channel {channel}, HandleGeotriggerNotification: fenceInfo.FenceNotificationType: Exited. {fenceInfo}.",
                            Name,
                            Channel,
                            fenceInfo.Message
                        );
                        break;
                    default:
                        logger.LogDebug(
                            GeoTriggerLog,
                            "GeoTriggerInstance, name {name} on channel {channel}, HandleGeotriggerNotification: fenceInfo.FenceNotificationType: Unknown. {fenceInfo}.",
                            Name,
                            Channel,
                            fenceInfo.Message
                        );
                        break;
                }
                if (fenceInfo.FenceGeoElement is null)
                {
                    logger.LogError(
                        GeoTriggerLog,
                        "GeoTriggerInstance, name {name} on channel {channel}, HandleGeotriggerNotification: fenceInfo.FenceGeoElement is null.",
                        Name,
                        Channel
                    );
                    throw new Exception("fenceInfo.FenceGeoElement is null.");
                }
                feature = fenceInfo.FenceGeoElement as ArcGISFeature;
                if (feature is not null)
                {
                    if (fenceInfo.FenceNotificationType == FenceNotificationType.Entered)
                    {
                        logger.LogDebug(
                            GeoTriggerLog,
                            "GeoTriggerInstance, name {name} on channel {channel}, HandleGeotriggerNotification: fenceInfo.FenceNotificationType == Entered: ArcGISFeature, feature.Geometry: {featureGeometry}.",
                            Name,
                            Channel,
                            feature.Geometry?.ToString()
                        );

                        var locationId = feature.Attributes["LocationId"];
                        var locationName = feature.Attributes["LocationName"];
                        if (
                            info.GeotriggerMonitor == locationsMonitor
                            && locationId is not null
                            && locationName is not null
                        )
                        {
                            logger.LogDebug(
                                GeoTriggerLog,
                                "GeoTriggerInstance, name {name} on channel {channel}, HandleGeotriggerNotification: fenceInfo.FenceNotificationType == Entered: ArcGISFeature, feature.Attributes: {locationId}, {locationName}.",
                                Name,
                                Channel,
                                locationId,
                                locationName
                            );
                        }
                    }
                    if (fenceInfo.FenceNotificationType == FenceNotificationType.Exited)
                    {
                        logger.LogDebug(
                            GeoTriggerLog,
                            "GeoTriggerInstance, name {name} on channel {channel}, HandleGeotriggerNotification: fenceInfo.FenceNotificationType == Exited: ArcGISFeature, feature.Geometry: {featureGeometry}.",
                            Name,
                            Channel,
                            feature.Geometry?.ToString()
                        );

                        var locationId = feature.Attributes["LocationId"];
                        var locationName = feature.Attributes["LocationName"];
                        if (
                            info.GeotriggerMonitor == locationsMonitor
                            && locationId is not null
                            && locationName is not null
                        )
                        {
                            logger.LogDebug(
                                GeoTriggerLog,
                                "GeoTriggerInstance, name {name} on channel {channel}, HandleGeotriggerNotification: fenceInfo.FenceNotificationType == Exited: ArcGISFeature, feature.Attributes: {locationId}, {locationName}.",
                                Name,
                                Channel,
                                locationId,
                                locationName
                            );
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                logger.LogError(
                    GeoTriggerLog,
                    exception,
                    "GeoTriggerInstance, name {name} on channel {channel}, HandleGeotriggerNotification: Exception: {message}.",
                    Name,
                    Channel,
                    exception.Message.ToString()
                );
            }
        }
    }

    public void StopGeotrigger()
    {
        // Log that a GeoTrigger was removed.
        logger.LogDebug(
            GeoTriggerLog,
            "GeoTriggerInstance, stopping geotrigger with a name of {name}, on channel {channel}, and a trigger distance of {triggerDistance}.",
            Name,
            Channel,
            TriggerDistance
        );
        locationsMonitor?.Stop();
    }

    // Destructor.
    ~GeoTriggerInstance()
    {
        // Log that a GeoTrigger was removed.
        logger.LogDebug(
            GeoTriggerLog,
            "GeoTriggerInstance, geotrigger instance removed with a name of {name}, on channel {channel}, and a trigger distance of {triggerDistance}.",
            Name,
            Channel,
            TriggerDistance
        );
    }
}
