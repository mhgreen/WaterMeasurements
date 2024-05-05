using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Ardalis.GuardClauses;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using FTD2XX_NET;
using Microsoft.Extensions.Logging;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Contracts.Services.Instances;
using WaterMeasurements.Models;
using WaterMeasurements.Services;
using WaterMeasurements.Services.Instances;
using WaterMeasurements.Views;
using Windows.ApplicationModel.VoiceCommands;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;

namespace WaterMeasurements.Services;

// Serial port request message.
public class SerialPortRequestMessage(SerialPortAdd serialPortAddMessage)
    : ValueChangedMessage<SerialPortAdd>(serialPortAddMessage) { }

// Serial port hardware state message.
public class SerialPortHardwareStateMessage(SerialPortHardwareState serialPortHardwareState)
    : ValueChangedMessage<SerialPortHardwareState>(serialPortHardwareState) { }

public static class FTDINotOkFTDIException
{
#pragma warning disable IDE0060
    public static void FTDINotOk(
        this IGuardClause guardClause,
        FTDI.FT_STATUS status,
        [CallerArgumentExpression(nameof(status))] string? parameterName = null
    )
    {
        if (status != FTDI.FT_STATUS.FT_OK)
        {
            throw new ArgumentException(
                "CommunicationService: FTDI.FT_STATUS is not FT_OK",
                parameterName
            );
        }
    }
#pragma warning restore IDE0060
}

public partial class CommunicationService : ICommunicationService
{
    private readonly ILogger<CommunicationService> logger;
    internal EventId CommunicationServiceLog = new(21, "CommunicationService");

    private readonly ILogger<SerialPortInstance> serialPortInstanceLogger;

    // Regular expression to remove spaces.
    public static readonly Regex RegExRemoveSpace = RemoveSpacesRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex RemoveSpacesRegex();

    // Dictionary to keep track of instances by name.
    private static readonly Dictionary<string, SerialPortInstance> serialPortInstances = [];

    public CommunicationService(
        ILogger<CommunicationService> logger,
        ILogger<SerialPortInstance> serialPortInstanceLogger
    )
    {
        this.logger = logger;
        this.serialPortInstanceLogger = serialPortInstanceLogger;
        logger.LogInformation(CommunicationServiceLog, "CommunicationService has been created.");

        // Register to get MapPageUnloadedMessage messages.
        WeakReferenceMessenger.Default.Register<MapPageUnloaded>(
            this,
            (recipient, message) =>
            {
                logger.LogDebug(
                    CommunicationServiceLog,
                    "CommunicationService, MapPageUnloaded: Stopping the serial port watcher."
                );
                // Unregister all messages.
                WeakReferenceMessenger.Default.UnregisterAll(this);
            }
        );

        // Register to get SerialPortRequestMessage messages.
        WeakReferenceMessenger.Default.Register<CommunicationService, SerialPortRequestMessage>(
            this,
            (recipient, message) =>
            {
                logger.LogDebug(
                    CommunicationServiceLog,
                    "CommunicationService, SerialPortRequestMessage: {message}.",
                    message
                );

                var serialPortIdentifier =
                    RegExRemoveSpace.Replace(message.Value.FtdiCableSerialNumber, "")
                    + RegExRemoveSpace.Replace(message.Value.FtdiCableName, "");

                // Add or replace the instance.
                serialPortInstances[serialPortIdentifier] = new SerialPortInstance(
                    serialPortInstanceLogger,
                    message.Value.FtdiCableSerialNumber,
                    message.Value.FtdiCableName,
                    message.Value.Port,
                    message.Value.SerialMonitorAction,
                    message.Value.HardwareChangeAction,
                    message.Value.NumberoAttempts,
                    message.Value.RetryDelay
                );
                // Log that the instance has been created.
                logger.LogDebug(
                    CommunicationServiceLog,
                    "CommunicationService, SerialPortRequestMessage: Serial port instance created or replaced for FTDI cable with serial number: {SerialNumber}, and name: {Name}.",
                    message.Value.FtdiCableSerialNumber,
                    message.Value.FtdiCableName
                );
                // Iterate through the serialPortInstances dictionary, listing the serial number and name of each instance.
                foreach (var serialPortInstance in serialPortInstances)
                {
                    logger.LogTrace(
                        CommunicationServiceLog,
                        "CommunicationService, SerialPortRequestMessage: Serial port instance: serialPortInstances Key {serialPortInstance.Key}, Serial {serialNumber}, Name {name}.",
                        serialPortInstance.Key,
                        serialPortInstance.Value.FtdiCableSerialNumber,
                        serialPortInstance.Value.FtdiCableName
                    );
                }
            }
        );

        SerialPort V3000SerialPort =
            new()
            {
                BaudRate = 9600,
                DataBits = 8,
                StopBits = StopBits.One,
                Parity = Parity.None,
                Handshake = Handshake.RequestToSend,
                NewLine = "\n",
                ReadTimeout = 40,
                WriteTimeout = 40,
                ReceivedBytesThreshold = 2
            };

        // Send a message to request the serial port instance.
        WeakReferenceMessenger.Default.Send(
            new SerialPortRequestMessage(
                new SerialPortAdd(
                    "20491327",
                    "V3000",
                    V3000SerialPort,
                    V3000DataReceivedHandler,
                    V3000CtsPinChangedHandler,
                    3,
                    4000
                )
            )
        );
    }

    private void V3000CtsPinChangedHandler(object sender, SerialPinChangedEventArgs args)
    {
        var currentSerialPort = (SerialPort)sender;
        Guard.Against.Null(
            currentSerialPort,
            nameof(currentSerialPort),
            "In V3000PinChangedHandler, currentSerialPort is null."
        );
        logger.LogDebug(
            CommunicationServiceLog,
            "CommunicationService, V3000PinChangedHandler: V3000PinChangedHandler called."
        );

        var retryCount = 0;
        const int maxRetry = 1; // Maximum number of retries

        while (retryCount <= maxRetry)
        {
            try
            {
                if (args.EventType == System.IO.Ports.SerialPinChange.CtsChanged)
                {
                    if (!currentSerialPort.IsOpen)
                    {
                        logger.LogWarning(
                            CommunicationServiceLog,
                            "CommunicationService, V3000PinChangedHandler: Serial port is not open."
                        );
                        return;
                    }
                    // Attempt to access CtsHolding property
                    if (currentSerialPort.CtsHolding)
                    {
                        logger.LogDebug(
                            CommunicationServiceLog,
                            "CommunicationService, V3000PinChangedHandler: CTS is now ON."
                        );
                    }
                    else
                    {
                        logger.LogDebug(
                            CommunicationServiceLog,
                            "CommunicationService, V3000PinChangedHandler: CTS is now OFF."
                        );
                    }
                }
                // Break the loop if the operation was successful
                break;
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogError(
                    "Access to the serial port was denied. Exception: {message}",
                    exception.Message
                );
                retryCount++;

                if (retryCount > maxRetry)
                {
                    // Log or handle the final failure
                    logger.LogError("Retry limit reached. Unable to access CtsHolding property.");
                    // Exit the loop if the retry limit is reached
                    break;
                }

                // Wait for one second before retrying
                Task.Delay(1000).Wait();
            }
            catch (Exception exception)
            {
                logger.LogError(
                    CommunicationServiceLog,
                    exception,
                    "CommunicationService, V3000PinChangedHandler: Exception occurred in V3000PinChangedHandler."
                );
                // Exit the loop on any other exception
                break;
            }
        }
    }

    private void V3000DataReceivedHandler(object sender, SerialDataReceivedEventArgs args)
    {
        var V3000Observation = string.Empty;
        var charBuffer = new char[4096];
        var charBufferPosition = 0;
        var iterationNumber = 0;
        int bytesToRead;

        // Log that the V3000DataReceivedHandler has been called.
        logger.LogDebug(
            CommunicationServiceLog,
            "CommunicationService, V3000DataReceivedHandler: V3000DataReceivedHandler called."
        );

        var currentSerialPort = (SerialPort)sender;

        Guard.Against.Null(
            currentSerialPort,
            nameof(currentSerialPort),
            "In V3000DataReceivedHandler, currentSerialPort is null"
        );

        do
        {
            // Sleep for 25 milliseconds to allow the buffer to fill.
            Task.Delay(25).Wait();
            iterationNumber++;
            bytesToRead = currentSerialPort.BytesToRead;
            // Console.WriteLine($"<V3000DataReceivedHandler> Bytes available, iteration {iterationNumber}: {bytesToRead}");
            try
            {
                currentSerialPort.Read(charBuffer, charBufferPosition, bytesToRead);
            }
            catch (TimeoutException exception)
            {
                logger.LogError(
                    CommunicationServiceLog,
                    exception,
                    "CommunicationService, V3000DataReceivedHandler: TimeoutException occurred while reading from the V3000."
                );
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    CommunicationServiceLog,
                    exception,
                    "CommunicationService, V3000DataReceivedHandler: Exception occurred while reading from the V3000."
                );
                throw;
            }
            var partialResult = new string(charBuffer, charBufferPosition, bytesToRead);
            // read until crlf is found.
            if (partialResult.EndsWith("\r\n"))
            {
                V3000Observation += partialResult;
                break;
            }
            charBufferPosition += (bytesToRead - 1);
            V3000Observation += partialResult;
            logger.LogTrace(
                CommunicationServiceLog,
                "CommunicationService, V3000DataReceivedHandler, partialResult: {partialResult}",
                partialResult
            );
            logger.LogTrace(
                CommunicationServiceLog,
                "CommunicationService, V3000DataReceivedHandler, V3000 Observation: {V3000Observation}",
                V3000Observation
            );
            logger.LogTrace(
                CommunicationServiceLog,
                "CommunicationService, V3000DataReceivedHandler, Bytes available after Read(), iteration {iterationNumber}: {currentSerialPort.BytesToRead}",
                iterationNumber,
                currentSerialPort.BytesToRead
            );
        } while (currentSerialPort.BytesToRead > 0);
        logger.LogDebug(
            CommunicationServiceLog,
            "CommunicationService, V3000DataReceivedHandler, Final V3000 Observation: {V3000Observation}",
            V3000Observation
        );
    }
}
