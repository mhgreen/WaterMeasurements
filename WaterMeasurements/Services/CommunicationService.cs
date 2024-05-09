using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Linq;
using Ardalis.GuardClauses;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using FTD2XX_NET;
using Microsoft.Extensions.Logging;
using RecordParser;
using RecordParser.Builders.Reader;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Contracts.Services.Instances;
using WaterMeasurements.Models;
using WaterMeasurements.Services;
using WaterMeasurements.Services.Instances;
using WaterMeasurements.Views;
using Windows.ApplicationModel.VoiceCommands;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using static WaterMeasurements.ViewModels.MainViewModel;

namespace WaterMeasurements.Services;

// Serial port request message.
public class SerialPortRequestMessage(SerialPortAdd serialPortAddMessage)
    : ValueChangedMessage<SerialPortAdd>(serialPortAddMessage) { }

// Serial port hardware state message.
public class SerialPortHardwareStateMessage(SerialPortHardwareState serialPortHardwareState)
    : ValueChangedMessage<SerialPortHardwareState>(serialPortHardwareState) { }

// V3000 observation message.
public class V3000ObservationMessage(V3000Observation v3000Observation)
    : ValueChangedMessage<V3000Observation>(v3000Observation) { }

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

    // serialPortInstanceLogger is used in the SerialPortInstance class.
    // For some reason, an error message is generated even though serialPortInstanceLogger is used in the SerialPortInstance class.
#pragma warning disable IDE0052 // Remove unread private members
    private readonly ILogger<SerialPortInstance> serialPortInstanceLogger;
#pragma warning restore IDE0052 // Remove unread private members

    // Regular expression to remove spaces.
    public static readonly Regex RegExRemoveSpace = RemoveSpacesRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex RemoveSpacesRegex();

    // Dictionary to keep track of instances by name.
    private static readonly Dictionary<string, SerialPortInstance> serialPortInstances = [];

    // Channel for V3000 serial port messages.
    private readonly uint v3000Channel;

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
                    message.Value.RetryDelay,
                    message.Value.Channel
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

        // Get the next instance channel and use that for the v3000Channel.
        // v3000Channel = WeakReferenceMessenger.Default.Send<InstanceChannelRequestMessage>();

        v3000Channel = 10;

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
                    4000,
                    v3000Channel
                )
            )
        );

        // Register to get SerialPortHardwareStateMessage messages on the v3000Channel.
        WeakReferenceMessenger.Default.Register<SerialPortHardwareStateMessage, uint>(
            this,
            v3000Channel,
            (recipient, message) =>
            {
                logger.LogDebug(
                    CommunicationServiceLog,
                    "CommunicationService, SerialPortHardwareStateMessage: {message}.",
                    message
                );
                if (message.Value.PinState == SerialPortHardwarePinState.CtsOn)
                {
                    logger.LogDebug(
                        CommunicationServiceLog,
                        "CommunicationService, SerialPortHardwareStateMessage: CTS is ON."
                    );
                }
                else if (message.Value.PinState == SerialPortHardwarePinState.CtsOff)
                {
                    logger.LogDebug(
                        CommunicationServiceLog,
                        "CommunicationService, SerialPortHardwareStateMessage: CTS is OFF."
                    );
                }
            }
        );

        // Register to get V3000ObservationMessage messages on the v3000Channel.
        WeakReferenceMessenger.Default.Register<V3000ObservationMessage, uint>(
            this,
            v3000Channel,
            (recipient, message) =>
            {
                logger.LogDebug(
                    CommunicationServiceLog,
                    "CommunicationService, V3000ObservationMessage"
                );
                logger.LogDebug(
                    CommunicationServiceLog,
                    "DataStorageNumber: {DataStorageNumber}, UserAssignedId: {UserAssignedId}",
                    message.Value.DataStorageNumber,
                    message.Value.UserAssignedId
                );
                logger.LogDebug(
                    CommunicationServiceLog,
                    "DataStorageNumber: V3000DateTime: {V3000DateTime}, ProgramNumber: {ProgramNumber}, Citation: {Citation}, BlankValue: {BlankValue}, DilutionFactor: {DilutionFactor}",
                    message.Value.V3000DateTime,
                    message.Value.ProgramNumber,
                    message.Value.Citation,
                    message.Value.BlankValue,
                    message.Value.DilutionFactor
                );
                logger.LogDebug(
                    CommunicationServiceLog,
                    "MeasuredValue: {MeasuredValue}, UnitMeasuredValue: {UnitMeasuredValue}, MeasuredValueStatus: {MeasuredValueStatus}",
                    message.Value.MeasuredValue,
                    message.Value.UnitMeasuredValue,
                    message.Value.MeasuredValueStatus
                );
                logger.LogDebug(
                    CommunicationServiceLog,
                    "SecondaryWavelength: {SecondaryWavelength}, SecondaryUnit: {SecondaryUnit}, SecondaryStatus: {SecondaryStatus}",
                    message.Value.SecondaryWavelength,
                    message.Value.SecondaryUnit,
                    message.Value.SecondaryStatus
                );
            }
        );
    }

    private void V3000CtsPinChangedHandler(object sender, SerialPinChangedEventArgs args)
    {
        var retryCount = 0;
        const int maxRetry = 1; // Maximum number of retries

        var currentSerialPortChannelAndBuffer = (SerialPortChannelAndBuffer)sender;
        var currentSerialPort = currentSerialPortChannelAndBuffer.Port;
        var channel = currentSerialPortChannelAndBuffer.Channel;

        logger.LogDebug(
            CommunicationServiceLog,
            "CommunicationService, V3000PinChangedHandler: V3000PinChangedHandler called."
        );

        Guard.Against.Null(
            currentSerialPort,
            nameof(currentSerialPort),
            "In V3000PinChangedHandler, currentSerialPort is null."
        );

        Guard.Against.Null(channel, nameof(channel), "In V3000PinChangedHandler, channel is null.");

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
                        logger.LogTrace(
                            CommunicationServiceLog,
                            "CommunicationService, V3000PinChangedHandler: CTS is now ON."
                        );
                        // Send a message that the CTS is ON.
                        WeakReferenceMessenger.Default.Send(
                            new SerialPortHardwareStateMessage(
                                new SerialPortHardwareState(SerialPortHardwarePinState.CtsOn)
                            ),
                            channel
                        );
                    }
                    else
                    {
                        logger.LogTrace(
                            CommunicationServiceLog,
                            "CommunicationService, V3000PinChangedHandler: CTS is now OFF."
                        );
                        // Send a message that the CTS is OFF.
                        WeakReferenceMessenger.Default.Send(
                            new SerialPortHardwareStateMessage(
                                new SerialPortHardwareState(SerialPortHardwarePinState.CtsOff)
                            ),
                            channel
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
        var currentSerialPortChannelAndBuffer = (SerialPortChannelAndBuffer)sender;
        var currentSerialPort = currentSerialPortChannelAndBuffer.Port;
        var channel = currentSerialPortChannelAndBuffer.Channel;
        var observationBuffer = currentSerialPortChannelAndBuffer.ObservationBuffer;
        var endOfMessageMarker = "\r\n";
        var reader = new VariableLengthReaderBuilder<(
            int DataStorageNumber,
            DateOnly v3000Date,
            TimeOnly v3000Time,
            DateTime v3000DateTime,
            int UserAssignedId,
            int ProgramNumber,
            string Citation,
            string BlankValue,
            int DilutionFactor,
            double MeasuredValue,
            string UnitMeasuredValue,
            string MeasuredValueStatus,
            int SecondaryWavelength,
            string SecondaryUnit,
            string SecondaryStatus
        )>()
            .Map(x => x.DataStorageNumber, indexColumn: 0)
            .Map(
                x => x.v3000Date,
                1,
                value =>
                    DateOnly.FromDateTime(
                        DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
                    )
            )
            .Map(
                x => x.v3000Time,
                2,
                value =>
                    TimeOnly.FromDateTime(
                        DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
                    )
            )
            .Map(x => x.UserAssignedId, 3)
            .Map(x => x.ProgramNumber, 4)
            .Map(x => x.Citation, 5)
            .Map(x => x.BlankValue, 6)
            .Map(x => x.DilutionFactor, 7, value => value.IsEmpty ? 0 : int.Parse(value))
            .Map(x => x.MeasuredValue, 8)
            .Map(x => x.UnitMeasuredValue, 9)
            .Map(x => x.MeasuredValueStatus, 10)
            .Map(x => x.SecondaryWavelength, 11, value => value.IsEmpty ? 0 : int.Parse(value))
            .Map(x => x.SecondaryUnit, 12)
            .Map(x => x.SecondaryStatus, 13)
            .Build(";");

        // Log that the V3000DataReceivedHandler has been called.
        logger.LogTrace(
            CommunicationServiceLog,
            "CommunicationService, V3000DataReceivedHandler: V3000DataReceivedHandler called."
        );

        Guard.Against.Null(
            currentSerialPort,
            nameof(currentSerialPort),
            "In V3000DataReceivedHandler, currentSerialPort is null"
        );
        Guard.Against.Null(
            channel,
            nameof(channel),
            "In V3000DataReceivedHandler, channel is null."
        );

        try
        {
            // Read all available data from the serial port.
            while (currentSerialPort.BytesToRead > 0)
            {
                var readData = new char[currentSerialPort.BytesToRead];
                var bytesRead = currentSerialPort.Read(readData, 0, readData.Length);
                observationBuffer.Append(readData, 0, bytesRead);

                // Check if the end-of-message marker is in the buffer.
                var bufferContent = observationBuffer.ToString();
                if (bufferContent.Contains(endOfMessageMarker))
                {
                    // Extract the observation up to the end-of-message marker.
                    var endOfMessageIndex =
                        bufferContent.IndexOf(endOfMessageMarker) + endOfMessageMarker.Length;
                    var completeObservation = bufferContent[..endOfMessageIndex];

                    // Process the complete observation.
                    ProcessObservation(completeObservation);

                    // Remove the processed observation from the buffer.
                    observationBuffer.Remove(0, endOfMessageIndex);
                }
            }
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

        void ProcessObservation(string observation)
        {
            // Log and parse the observation.
            logger.LogTrace(
                CommunicationServiceLog,
                "Processing complete observation: {observation}",
                observation
            );
            try
            {
                var parsedResult = reader.Parse(observation);
                logger.LogTrace(
                    CommunicationServiceLog,
                    "Parsed observation: {parsedResult}",
                    parsedResult
                );

                // Now that the observation has been parsed, set V3000DateTime to the parsed date and time and convert to UTC.
                // Combine the date and time into a single DateTime object.
                parsedResult.v3000DateTime = parsedResult.v3000Date.ToDateTime(
                    parsedResult.v3000Time
                );
                // Define the source time zone (Pacific Time in this case)
                var sourceTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                // Convert the localDateTime to UTC
                var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(
                    parsedResult.v3000DateTime,
                    sourceTimeZone
                );
                parsedResult.v3000DateTime = utcDateTime;

                // Create a V3000Observation instance from parsedResult
                var v3000Observation = new V3000Observation
                {
                    DataStorageNumber = parsedResult.DataStorageNumber,
                    V3000DateTime = parsedResult.v3000DateTime,
                    UserAssignedId = parsedResult.UserAssignedId,
                    ProgramNumber = parsedResult.ProgramNumber,
                    Citation = parsedResult.Citation,
                    BlankValue = parsedResult.BlankValue,
                    DilutionFactor = parsedResult.DilutionFactor,
                    MeasuredValue = parsedResult.MeasuredValue,
                    UnitMeasuredValue = parsedResult.UnitMeasuredValue,
                    MeasuredValueStatus = parsedResult.MeasuredValueStatus,
                    SecondaryWavelength = parsedResult.SecondaryWavelength,
                    SecondaryUnit = parsedResult.SecondaryUnit,
                    SecondaryStatus = parsedResult.SecondaryStatus
                };

                // Generate and send the V3000ObservationMessage
                WeakReferenceMessenger.Default.Send(
                    new V3000ObservationMessage(v3000Observation),
                    channel
                );

                // log the parsed result by field.
                logger.LogTrace(
                    CommunicationServiceLog,
                    "DataStorageNumber: {DataStorageNumber}, UserAssignedId: {UserAssignedId}",
                    parsedResult.DataStorageNumber,
                    parsedResult.UserAssignedId
                );
                logger.LogTrace(
                    CommunicationServiceLog,
                    "DataStorageNumber: V3000Date: {V3000Date}, V3000Time: {V3000Time}, V3000DateTime (UTC): {V3000DateTime}",
                    parsedResult.v3000Date,
                    parsedResult.v3000Time,
                    parsedResult.v3000DateTime
                );
                logger.LogTrace(
                    CommunicationServiceLog,
                    "ProgramNumber: {ProgramNumber}, Citation: {Citation}, BlankValue: {BlankValue}, DilutionFactor: {DilutionFactor}",
                    parsedResult.ProgramNumber,
                    parsedResult.Citation,
                    parsedResult.BlankValue,
                    parsedResult.DilutionFactor
                );
                logger.LogTrace(
                    CommunicationServiceLog,
                    "MeasuredValue: {MeasuredValue}, UnitMeasuredValue: {UnitMeasuredValue}, MeasuredValueStatus: {MeasuredValueStatus}",
                    parsedResult.MeasuredValue,
                    parsedResult.UnitMeasuredValue,
                    parsedResult.MeasuredValueStatus
                );
                logger.LogTrace(
                    CommunicationServiceLog,
                    "SecondaryWavelength: {SecondaryWavelength}, SecondaryUnit: {SecondaryUnit}, SecondaryStatus: {SecondaryStatus}",
                    parsedResult.SecondaryWavelength,
                    parsedResult.SecondaryUnit,
                    parsedResult.SecondaryStatus
                );
            }
            catch (Exception exception)
            {
                logger.LogError(
                    CommunicationServiceLog,
                    exception,
                    "Exception occurred while parsing the observation."
                );
            }
        }
    }
}
