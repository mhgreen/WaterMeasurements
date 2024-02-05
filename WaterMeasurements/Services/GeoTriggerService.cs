using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geotriggers;
using Esri.ArcGISRuntime.Location;

using Microsoft.Extensions.Logging;

using WaterMeasurements.Models;
using WaterMeasurements.Views;
using WaterMeasurements.Services;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Contracts.Services.Instances;
using WaterMeasurements.Services.Instances;

using Windows.Networking.Connectivity;

namespace WaterMeasurements.Services;

// Message to notify modules of a GeoTrigger event.
public class GeoTriggerMessage(GeoTriggerNotification geoTriggerNotification) : ValueChangedMessage<GeoTriggerNotification>(geoTriggerNotification)
{
}

// Message to request the addition of a GeoTrigger.
public class GeoTriggerAddMessage(GeoTriggerAdd geoTriggerAdd) : ValueChangedMessage<GeoTriggerAdd>(geoTriggerAdd)
{
}

// Message to request the deletion of a GeoTrigger by name.
public class GeoTriggerDeleteMessage(string name) : ValueChangedMessage<string>(name)
{
}

public partial class GeoTriggerService : IGeoTriggerService
{
    private readonly ILogger<GeoTriggerService> logger;
    internal EventId GeoTriggerLog = new(5, "GeoTriggerService");
    private readonly ILogger<GeoTriggerInstance> geoTriggerInstanceLogger;

    // Static dictionary to keep track of instances by name.
    private static readonly Dictionary<string, GeoTriggerInstance> geoTriggerInstances = [];

    public GeoTriggerService(
        ILogger<GeoTriggerService> logger,
        ILogger<GeoTriggerInstance> geoTriggerInstanceLogger
    )
    {
        this.logger = logger;
        this.geoTriggerInstanceLogger = geoTriggerInstanceLogger;

        // Log the service initialization.
        logger.LogInformation(GeoTriggerLog, "GeoTriggerService created.");

        Initialize();        
    }

    private void Initialize()
    {
        try
        {
            // Register a message handler for GeoTriggerAddMessage.
            WeakReferenceMessenger.Default.Register<GeoTriggerAddMessage>(
                this,
                (recipient, message) =>
                {
                    // Log the GeoTriggerAddMessage.
                    logger.LogDebug(
                        GeoTriggerLog,
                        "GeoTriggerService, GeoTriggerAddMessage: {message}.",
                        message.ToString()
                    );

                    // Check to see if the instance exists.
                    if (geoTriggerInstances.ContainsKey(message.Value.Name))
                    {
                        // Log that the instance already exists.
                        logger.LogDebug(
                            GeoTriggerLog,
                            "GeoTriggerService, GeoTriggerAddMessage: GeoTrigger instance already exists: {name}. Deleting the existing one and replacing it with the one requested.",
                            message.Value.Name
                        );

                        // If the instance exists, then delete it.
                        geoTriggerInstances.Remove(message.Value.Name);
                    }

                    // Add the instance to the dictionary.
                    geoTriggerInstances.Add(
                        message.Value.Name,
                        new GeoTriggerInstance(
                            geoTriggerInstanceLogger,
                            message.Value.Name,
                            message.Value.Channel,
                            message.Value.FeatureTable,
                            message.Value.TriggerDistance
                        )
                    );

                    // Log that the instance was added.
                    logger.LogDebug(
                        GeoTriggerLog,
                        "GeoTriggerService, GeoTriggerAddMessage: GeoTrigger instance added: {name}.",
                        message.Value.Name
                    );
                }
            );

            // Register a message handler for GeoTriggerDeleteMessage.
            WeakReferenceMessenger.Default.Register<GeoTriggerDeleteMessage>(
                this,
                (recipient, message) =>
                {
                    // Log the GeoTriggerDeleteMessage.
                    logger.LogDebug(
                        GeoTriggerLog,
                        "GeoTriggerService, GeoTriggerDeleteMessage: {message}.",
                        message.ToString()
                    );

                    // Check to see if the instance exists.
                    if (geoTriggerInstances.ContainsKey(message.Value))
                    {
                        // Log that the instance exists.
                        logger.LogDebug(
                            GeoTriggerLog,
                            "GeoTriggerService, GeoTriggerDeleteMessage: GeoTrigger instance exists: {name}. Stopping the geotrigger and deleting the dictionary entry.",
                            message.Value
                        );
                        // Stop monitoring the geotrigger.
                        geoTriggerInstances[message.Value].StopGeotrigger();

                        // If the instance exists, then delete it.
                        geoTriggerInstances.Remove(message.Value);
                    }
                    else
                    {
                        // Log that the instance does not exist.
                        logger.LogDebug(
                            GeoTriggerLog,
                            "GeoTriggerService, GeoTriggerDeleteMessage: GeoTrigger instance does not exist: {name}.",
                            message.Value
                        );
                    }
                }
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                GeoTriggerLog,
                exception,
                "GeoTriggerService, Initialize: Exception: {message}.",
                exception.Message.ToString()
            );
        }
    }
}
