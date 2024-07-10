using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Core.Contracts.Services;
using WaterMeasurements.Core.Helpers;
using WaterMeasurements.Helpers;
using WaterMeasurements.Models;

namespace WaterMeasurements.Services;

// Message from modules requesting that a measurement request be added to the queue.
public class AddMeasurementRequestMessage(MeasurementType measurementType)
    : ValueChangedMessage<MeasurementType>(measurementType) { }

// Message to notify modules that a measurement should be done.
public class BeginMeasurementMessage(MeasurementType measurementType)
    : ValueChangedMessage<MeasurementType>(measurementType) { }

// Message from modules that a measurement has been completed.
public class MeasurementCompleteMessage(MeasurementType measurementType)
    : ValueChangedMessage<MeasurementType>(measurementType) { }

public partial class MeasurementQueueService : IMeasurementQueueService
{
    private readonly ILogger<MeasurementQueueService> logger;

    // Construct a ConcurrentQueue.
    private readonly ConcurrentQueue<MeasurementType> measurementQueue = new();

    // Lock object for the queue.
    private readonly object requestQueueLock = new();

    // Set the EventId for logging messages.
    internal EventId MeasurementQueueServiceLog = new(23, "MeasurementQueueService");

    public MeasurementQueueService(ILogger<MeasurementQueueService> logger)
    {
        this.logger = logger;

        // Log that the MeasurementQueueService has been created.
        logger.LogInformation(
            MeasurementQueueServiceLog,
            "MeasurementQueueService: MeasurementQueueService created."
        );

        // Initialize the MeasurementQueueService.
        Initialize();
    }

    private void Initialize()
    {
        WeakReferenceMessenger.Default.Register<AddMeasurementRequestMessage>(
            this,
            (recipient, message) =>
            {
                lock (requestQueueLock)
                {
                    // If there is nothing in the queue, then after adding the measurement request, send a BeginMeasurementMessage.
                    // Otherwise, just add the measurement request to the queue.
                    // This is done because, if there are multiple measurement requests, then the next queue element will be retrieved
                    // after the current measurement is completed as indicated by getting a MeasurementCompleteMessage.
                    // If there is nothing in the queue, then there is no outstanding measurement taking place and there would be no
                    // MeasurementCompleteMessage to trigger the next measurement.
                    if (measurementQueue.IsEmpty)
                    {
                        measurementQueue.Enqueue(message.Value);
                        SendMessageFromQueue();
                    }
                    else
                    {
                        // Add the measurement request to the queue.
                        measurementQueue.Enqueue(message.Value);
                    }
                }

                // Log that a measurement request has been added to the queue.
                logger.LogDebug(
                    MeasurementQueueServiceLog,
                    "MeasurementQueueService: {measurementType} Measurement request added to queue.",
                    message.Value
                );
            }
        );

        // Once a measurement is complete. A message is sent back to this service.
        // The service will call SendMessageFromQueue to send a BeginMeasurementMessage if there are more measurements in the queue.
        WeakReferenceMessenger.Default.Register<MeasurementCompleteMessage>(
            this,
            (recipient, message) =>
            {
                lock (requestQueueLock)
                {
                    // If the queue is not empty, send a BeginMeasurementMessage.
                    SendMessageFromQueue();
                }

                // Log that a measurement has been completed.
                logger.LogDebug(
                    MeasurementQueueServiceLog,
                    "MeasurementQueueService: {measurementType} Measurement completed.",
                    message.Value
                );
            }
        );

        // Log that the MeasurementQueueService has been initialized.
        logger.LogDebug(
            MeasurementQueueServiceLog,
            "MeasurementQueueService: MeasurementQueueService initialized."
        );
    }

    // Send a BeginMeasurementMessage if the queue is not empty.
    private void SendMessageFromQueue()
    {
        // If the queue is not empty, send a BeginMeasurementMessage.
        if (measurementQueue.TryDequeue(out var measurementType))
        {
            WeakReferenceMessenger.Default.Send(new BeginMeasurementMessage(measurementType));

            // Log that a measurement has been started.
            logger.LogDebug(
                MeasurementQueueServiceLog,
                "MeasurementQueueService: BeginMeasurementMessage for {measurementType} sent.",
                measurementType
            );
        }
    }
}
