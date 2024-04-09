using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Geotriggers;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Portal;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Tasks;
using Esri.ArcGISRuntime.Tasks.Offline;
using Esri.ArcGISRuntime.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stateless;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Contracts.Services.Instances;
using WaterMeasurements.Models;
using WaterMeasurements.Services;
using WaterMeasurements.Services.Instances;
using WaterMeasurements.Views;
using static System.Net.Mime.MediaTypeNames;

namespace WaterMeasurements.Services;

// GeoDatabase retrieve message.
public class GeoDatabaseRequestMessage(GeoDatabaseRetrieveRequest geoDatabaseRetrieveRequest)
    : ValueChangedMessage<GeoDatabaseRetrieveRequest>(geoDatabaseRetrieveRequest) { }

public class FeatureTableRequestMessage(string featureTable)
    : ValueChangedMessage<string>(featureTable) { }

// GeoDatabase delete message.
public class GeoDatabaseDeleteMessage(GeoDatabaseDeleteRequest geoDatabaseDeleteRequest)
    : ValueChangedMessage<GeoDatabaseDeleteRequest>(geoDatabaseDeleteRequest) { }

// Message for geodatabase download progress.
public class GeoDatabaseDownloadProgressMessage(
    GeoDatabaseDownloadInstanceProgress geoDatabaseDownloadProgress
) : ValueChangedMessage<GeoDatabaseDownloadInstanceProgress>(geoDatabaseDownloadProgress) { }

// Notification that the feature table has changed.
public class FeatureTableMessage(FeatureTable featureTable)
    : ValueChangedMessage<FeatureTable>(featureTable) { }

// Message to request a Geodatabase state change.
public class GeodatabaseStateChangeMessage(GeodatabaseStateChange geodatabaseStateChange)
    : ValueChangedMessage<GeodatabaseStateChange>(geodatabaseStateChange) { }

// Message to add a feature to the geodatabase service.
public class AddFeatureMessage(FeatureAddMessage featureAddMessage)
    : ValueChangedMessage<FeatureAddMessage>(featureAddMessage) { }

// Message to delete a feature from the geodatabase service.
public class DeleteFeatureMessage(FeatureDeleteMessage featureDeleteMessage)
    : ValueChangedMessage<FeatureDeleteMessage>(featureDeleteMessage) { }

// Message to update a feature in the geodatabase service.
public class UpdateFeatureMessage(FeatureUpdateMessage featureUpdateMessage)
    : ValueChangedMessage<FeatureUpdateMessage>(featureUpdateMessage) { }

// Message to update modules of a feature change.
public class ChangedFeatureMessage(FeatureChangedMessage featureChangedMessage)
    : ValueChangedMessage<FeatureChangedMessage>(featureChangedMessage) { }

public partial class GeoDatabaseService : IGeoDatabaseService
{
    private readonly ILogger<GeoDatabaseService> logger;

    // Set the EventId for logging messages.
    internal EventId GeoDatabaseLog = new(4, "GeoDatabaseService");
    private readonly ILogger<GeoDatabaseInstance> geoDatabaseInstanceLogger;

    // Dictionary to keep track of instances by name.
    private static readonly Dictionary<string, GeoDatabaseInstance> geoDatabaseInstances = [];

    public GeoDatabaseService(
        ILogger<GeoDatabaseService> logger,
        ILogger<GeoDatabaseInstance> geoDatabaseInstanceLogger
    )
    {
        this.logger = logger;
        this.geoDatabaseInstanceLogger = geoDatabaseInstanceLogger;

        // Log that the GeoDatabaseService has been created.
        logger.LogInformation(GeoDatabaseLog, "GeoDatabaseService: GeoDatabaseService created.");

        Initialize();
    }

    private void Initialize()
    {
        // Register a message handler for the GeoDatabaseRequestMessage.
        WeakReferenceMessenger.Default.Register<GeoDatabaseService, GeoDatabaseRequestMessage>(
            this,
            (recipient, message) =>
            {
                logger.LogDebug(
                    GeoDatabaseLog,
                    "GeoDatabaseService, GeoDatabaseRequestMessage: {message}.",
                    message
                );

                // Check to see if the instance already exists.
                if (geoDatabaseInstances.ContainsKey(message.Value.Name))
                {
                    // Log that the instance already exists.
                    logger.LogDebug(
                        GeoDatabaseLog,
                        "GeoDatabaseService, GeoDatabaseRequestMessage: Instance already exists. Deleting the existing one and replacing it with the one requested."
                    );
                    // If the instance exists, then delete it and create a new one.
                    geoDatabaseInstances.Remove(message.Value.Name);
                    geoDatabaseInstances.Add(
                        message.Value.Name,
                        new GeoDatabaseInstance(
                            geoDatabaseInstanceLogger,
                            message.Value.Name,
                            message.Value.GeoDatabaseType,
                            message.Value.Channel,
                            message.Value.Url,
                            message.Value.CauseGeoDatabaseDownload
                        )
                    );
                }
                else
                {
                    // If the instance does not exist, then add it to the dictionary.
                    geoDatabaseInstances.Add(
                        message.Value.Name,
                        new GeoDatabaseInstance(
                            geoDatabaseInstanceLogger,
                            message.Value.Name,
                            message.Value.GeoDatabaseType,
                            message.Value.Channel,
                            message.Value.Url,
                            message.Value.CauseGeoDatabaseDownload
                        )
                    );
                }

                // Log to debug all the geodatabase instances and their details.
                foreach (var geoDatabaseInstance in geoDatabaseInstances)
                {
                    logger.LogDebug(
                        GeoDatabaseLog,
                        "GeoDatabaseService, GeoDatabaseRequestMessage: Name: {name}, Type: {GeoDatabaseType} Channel: {channel}, LocationsUrl: {LocationsUrl}, CauseGeoDatabaseDownload: {CauseGeoDatabaseDownload}.",
                        geoDatabaseInstance.Value.Name,
                        geoDatabaseInstance.Value.GeoDatabaseType,
                        geoDatabaseInstance.Value.Channel,
                        geoDatabaseInstance.Value.Url,
                        geoDatabaseInstance.Value.CauseGeoDatabaseDownload
                    );
                }
            }
        );

        // Log to debug all the geodatabase instances and their details.
        foreach (var geoDatabaseInstance in geoDatabaseInstances)
        {
            logger.LogDebug(
                GeoDatabaseLog,
                "GeoDatabaseService, GeoDatabaseService: Name: {name}, Type: {GeoDatabaseType} Channel: {channel}, LocationsUrl: {LocationsUrl}, CauseGeoDatabaseDownload: {CauseGeoDatabaseDownload}.",
                geoDatabaseInstance.Value.Name,
                geoDatabaseInstance.Value.GeoDatabaseType,
                geoDatabaseInstance.Value.Channel,
                geoDatabaseInstance.Value.Url,
                geoDatabaseInstance.Value.CauseGeoDatabaseDownload
            );
        }
    }
}
