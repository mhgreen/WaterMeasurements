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
                "SerialPortService: FTDI.FT_STATUS is not FT_OK",
                parameterName
            );
        }
    }
#pragma warning restore IDE0060
}

public partial class SerialPortService : ISerialPortService
{
    private readonly ILogger<SerialPortService> logger;

    // Set the EventId for logging messages.
    internal EventId SerialPortServiceLog = new(21, "SerialPortService");

    private static SerialPort? currentSerialPort;

    private DeviceWatcher? deviceWatcher;

    // public Action<object, SerialDataReceivedEventArgs> ActionDataReceivedHandler { get; set; }

    // Dictionary to store opened ports
    private readonly Dictionary<string, SerialPort> openedDevices = [];

    private static readonly int numberOfAttempts = 3; // Number of attempts to open the port.
    private static readonly int delayBetweenRetry = 4000; // in milliseconds

    // Regular expression to remove spaces.
    private static readonly Regex regExRemoveSpace = MyRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex MyRegex();

    private static readonly EventWaitHandle waitOnRead = new(false, EventResetMode.AutoReset);

    public SerialPortService(ILogger<SerialPortService> logger)
    {
        this.logger = logger;
        logger.LogInformation(SerialPortServiceLog, "SerialPortService has been created.");
        // Register to get MapPageUnloadedMessage messages.
        WeakReferenceMessenger.Default.Register<MapPageUnloaded>(
            this,
            (recipient, message) =>
            {
                logger.LogDebug(
                    SerialPortServiceLog,
                    "SerialPortService, MapPageUnloaded: Stopping the serial port watcher."
                );
                // Unregister all messages.
                WeakReferenceMessenger.Default.UnregisterAll(this);
                // Stop the watcher.
                StopWatcher();
            }
        );

        currentSerialPort = new()
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

        // ActionDataReceivedHandler = DataReceivedHandler;

        StartWatcher();
    }

    public void StartWatcher()
    {
        try
        {
            var deviceSelector = SerialDevice.GetDeviceSelector();
            deviceWatcher = DeviceInformation.CreateWatcher(deviceSelector);

            deviceWatcher.Added += SerialPort_Added;
            deviceWatcher.Removed += SerialPort_Removed;

            deviceWatcher.Start();
        }
        catch (Exception exception)
        {
            logger.LogError(
                SerialPortServiceLog,
                exception,
                "SerialPortService: Error starting the device watcher."
            );
        }
    }

    public void StopWatcher()
    {
        try
        {
            // Log to debug that the device watcher is stopping.
            logger.LogDebug(
                SerialPortServiceLog,
                "SerialPortService, StopWatcher: Stopping the device watcher."
            );

            // Using the dictionary key in openDevices, call CloseAndRemoveSerialPortBySysId to close and remove the port.
            foreach (var key in openedDevices.Keys)
            {
                CloseAndRemoveSerialPortBySysId(key);
            }

            deviceWatcher?.Stop();
        }
        catch (Exception exception)
        {
            logger.LogError(
                SerialPortServiceLog,
                exception,
                "SerialPortService, StopWatcher: Error stopping the device watcher."
            );
        }
    }

    private void SerialPort_Added(DeviceWatcher sender, DeviceInformation args)
    {
        uint ftdiCount = 0;

        try
        {
            logger.LogInformation(SerialPortServiceLog, "SerialPortService: SerialPort_Added.");
            Guard.Against.Null(
                currentSerialPort,
                nameof(currentSerialPort),
                "In SerialPort_Added, currentSerialPort is null"
            );
            logger.LogInformation(
                SerialPortServiceLog,
                "SerialPortService: Id: {Id} Name: {Name}, Kind: {Kind}, IsEnabled: {IsEnabled}",
                args.Id,
                args.Name,
                args.Kind,
                args.IsEnabled
            );

            // Use the Regular Expression to remove spaces from args.Name and write it to the log.
            var name = regExRemoveSpace.Replace(args.Name, "");
            logger.LogInformation(
                SerialPortServiceLog,
                "SerialPortService: args.Name: {Name}",
                name
            );

            if (args.Name.Contains("V3000", StringComparison.CurrentCultureIgnoreCase))
            {
                logger.LogInformation(
                    SerialPortServiceLog,
                    "SerialPortService, SerialPort_Added: V3000 found."
                );

                FTDI ftdi = new();
                var getNumberDevicesStatus = ftdi.GetNumberOfDevices(ref ftdiCount);
                if (getNumberDevicesStatus != FTDI.FT_STATUS.FT_OK)
                {
                    logger.LogError(
                        SerialPortServiceLog,
                        "SerialPortService, SerialPort_Added: Error getting number of devices, error: {getNumberDevicesStatus}.",
                        getNumberDevicesStatus
                    );
                }

                Guard.Against.FTDINotOk(getNumberDevicesStatus);
                var list = new FTDI.FT_DEVICE_INFO_NODE[ftdiCount];
                var getDeviceListStatus = ftdi.GetDeviceList(list);
                if (getDeviceListStatus != FTDI.FT_STATUS.FT_OK)
                {
                    logger.LogError(
                        SerialPortServiceLog,
                        "SerialPortService, SerialPort_Added: Error getting device list, error: {getDeviceListStatus}.",
                        getDeviceListStatus
                    );
                }
                Guard.Against.FTDINotOk(getDeviceListStatus);

                // Log the number of devices found.
                logger.LogInformation(
                    SerialPortServiceLog,
                    "SerialPortService, SerialPort_Added: Number of FTDI devices found: {ftdiCount}",
                    ftdiCount
                );

                foreach (var node in list)
                {
                    logger.LogInformation(
                        SerialPortServiceLog,
                        "SerialPortService, SerialPort_Added: V3000 found: {Description}, {SerialNumber}",
                        node.Description,
                        node.SerialNumber
                    );

                    // Log all detail to debug.
                    logger.LogDebug(
                        SerialPortServiceLog,
                        "SerialPortService, SerialPort_Added: V3000 found: {Description}, {SerialNumber}, {LocId}, {Flags}, {Type}, {ID}",
                        node.Description,
                        node.SerialNumber,
                        node.LocId,
                        node.Flags,
                        node.Type,
                        node.ID
                    );

                    CloseAndRemoveSerialPortBySysId(args.Id);

                    FTDI.FT_STATUS openByIndexStatus;
                    var attempt = 0;

                    do
                    {
                        openByIndexStatus = ftdi.OpenByIndex(0);
                        if (openByIndexStatus != FTDI.FT_STATUS.FT_OK)
                        {
                            logger.LogError(
                                SerialPortServiceLog,
                                "SerialPortService, SerialPort_Added: Error opening device by index, attempt {AttemptNumber}, error: {openByIndexStatus}.",
                                attempt + 1,
                                openByIndexStatus
                            );
                            // Delay before retrying.
                            Task.Delay(delayBetweenRetry).Wait();
                        }
                        attempt++;
                    } while (
                        openByIndexStatus != FTDI.FT_STATUS.FT_OK
                        && attempt < numberOfAttempts
                        && numberOfAttempts > 0
                    );

                    Guard.Against.FTDINotOk(openByIndexStatus);

                    var getComPortStatus = ftdi.GetCOMPort(out var comport);
                    if (getComPortStatus != FTDI.FT_STATUS.FT_OK)
                    {
                        logger.LogError(
                            SerialPortServiceLog,
                            "SerialPortService, SerialPort_Added: Error getting COM port, error: {getComPortStatus}.",
                            getComPortStatus
                        );
                    }
                    Guard.Against.FTDINotOk(getComPortStatus);
                    logger.LogInformation(
                        SerialPortServiceLog,
                        "SerialPortService, SerialPort_Added: V3000 comport: {Comport}",
                        comport
                    );

                    ftdi.Close();

                    /*
                    // Use this for the instance version.
                    var receivedHandler = new SerialDataReceivedEventHandler(DataReceivedHandler);
                    currentSerialPort.DataReceived += (sender, args) =>
                        ActionDataReceivedHandler(currentSerialPort, args);
                    */

                    currentSerialPort.DataReceived += new SerialDataReceivedEventHandler(
                        DataReceivedHandler
                    );

                    currentSerialPort.PortName = comport;
                    currentSerialPort.Open();

                    openedDevices.Add(args.Id, currentSerialPort);
                }
            }
            else
            {
                logger.LogInformation(SerialPortServiceLog, "SerialPortService: Port found.");
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                SerialPortServiceLog,
                exception,
                "SerialPortService: Error processing the added device."
            );
        }
    }

    private void SerialPort_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        try
        {
            logger.LogInformation(SerialPortServiceLog, "SerialPortService: Device removed.");
            logger.LogInformation(
                SerialPortServiceLog,
                "SerialPortService: Id: {Id}, Kind: {Kind}",
                args.Id,
                args.Kind
            );
            CloseAndRemoveSerialPortBySysId(args.Id);
        }
        catch (Exception exception)
        {
            logger.LogError(
                SerialPortServiceLog,
                exception,
                "SerialPortService: Error processing the removed device."
            );
        }
    }

    private void CloseAndRemoveSerialPortBySysId(string id)
    {
        try
        {
            if (openedDevices.TryGetValue(id, value: out var port))
            {
                logger.LogInformation(
                    SerialPortServiceLog,
                    "SerialPortService, CloseAndRemoveSerialPort: {PortName} already opened, closing and removing from dictionary of open devices.",
                    port.PortName
                );
                // Remove the DataReceived event handler.
                port.DataReceived -= new SerialDataReceivedEventHandler(DataReceivedHandler);
                // Close the port and remove it from the dictionary.
                port.Close();
                openedDevices.Remove(id);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                SerialPortServiceLog,
                exception,
                "SerialPortService, CloseAndRemoveSerialPortBySysId: Error closing and removing the serial port."
            );
        }
    }

    private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs args)
    {
        var V3000Observation = string.Empty;
        var charBuffer = new char[4096];
        var charBufferPosition = 0;
        var iterationNumber = 0;
        int bytesToRead;

        // Log that the DataReceivedHandler has been called.
        logger.LogDebug(
            SerialPortServiceLog,
            "SerialPortService, DataReceivedHandler: DataReceivedHandler called."
        );

        Guard.Against.Null(
            currentSerialPort,
            nameof(currentSerialPort),
            "In DataReceivedHandler, currentSerialPort is null"
        );

        do
        {
            // Sleep for 25 milliseconds to allow the buffer to fill.
            Task.Delay(25).Wait();
            iterationNumber++;
            bytesToRead = currentSerialPort.BytesToRead;
            // Console.WriteLine($"<DataReceivedHandler> Bytes available, iteration {iterationNumber}: {bytesToRead}");
            try
            {
                currentSerialPort.Read(charBuffer, charBufferPosition, bytesToRead);
            }
            catch (TimeoutException exception)
            {
                logger.LogError(
                    SerialPortServiceLog,
                    exception,
                    "SerialPortService, DataReceivedHandler: TimeoutException occurred while reading from the V3000."
                );
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    SerialPortServiceLog,
                    exception,
                    "SerialPortService, DataReceivedHandler: Exception occurred while reading from the V3000."
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
                SerialPortServiceLog,
                "SerialPortService, DataReceivedHandler, partialResult: {partialResult}",
                partialResult
            );
            logger.LogTrace(
                SerialPortServiceLog,
                "SerialPortService, DataReceivedHandler, V3000Observation: {V3000Observation}",
                V3000Observation
            );
            logger.LogTrace(
                SerialPortServiceLog,
                "SerialPortService, DataReceivedHandler, Bytes available after Read(), iteration {iterationNumber}: {currentSerialPort.BytesToRead}",
                iterationNumber,
                currentSerialPort.BytesToRead
            );
        } while (currentSerialPort.BytesToRead > 0);
        logger.LogDebug(
            SerialPortServiceLog,
            "SerialPortService, DataReceivedHandler, Final V3000Observation: {V3000Observation}",
            V3000Observation
        );
        logger.LogDebug(
            SerialPortServiceLog,
            "SerialPortService, DataReceivedHandler: Setting waitOnRead."
        );
        waitOnRead.Set();
    }
}
