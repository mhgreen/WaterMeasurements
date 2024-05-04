using System;
using System.Collections.Generic;
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
using WaterMeasurements.Views;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;

namespace WaterMeasurements.Services.Instances;

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
                "SerialPortInstance: FTDI.FT_STATUS is not FT_OK",
                parameterName
            );
        }
    }
#pragma warning restore IDE0060
}

public partial class SerialPortInstance : ISerialPortInstance
{
    private readonly ILogger<SerialPortInstance> logger;
    internal EventId SerialPortLog = new(22, "SerialPortInstance");
    public string? FtdiCableSerialNumber { get; private set; }
    public string? FtdiCableName { get; private set; }

    // public string? FtdiCableIdentifier { get; private set; }
    public SerialPort? CurrentSerialPort { get; private set; }
    public Action<object, SerialDataReceivedEventArgs>? DataReceivedAction { get; private set; }
    public Action<object, SerialPinChangedEventArgs>? HardwareChangeAction { get; private set; }
    public int RetryAttempts { get; private set; }
    public int RetryDelay { get; private set; }

    private SerialDataReceivedEventHandler? dataReceivedHandler;
    private SerialPinChangedEventHandler? pinChangedHandler;

    private DeviceWatcher? deviceWatcher;

    // Dictionary to store opened ports
    private readonly Dictionary<string, SerialPort> openedDevices = [];

    // private static readonly EventWaitHandle waitOnRead = new(false, EventResetMode.AutoReset);

    public SerialPortInstance(
        ILogger<SerialPortInstance> logger,
        string ftdiCableSerialNumber,
        string ftdiCableName,
        SerialPort currentSerialPort,
        Action<object, SerialDataReceivedEventArgs> dataReceivedAction,
        Action<object, SerialPinChangedEventArgs>? hardwareChangeAction = null,
        int retryAttempts = 3,
        int retryDelay = 4000
    )
    {
        this.logger = logger;
        try
        {
            Guard.Against.Null(logger, nameof(logger));

            // FtdiCableSerialNumber = ftdiCableSerialNumber;
            FtdiCableSerialNumber = Guard.Against.NullOrWhiteSpace(
                ftdiCableSerialNumber,
                nameof(ftdiCableSerialNumber),
                "SerialPortInstance, constructor: FtdiCableSerialNumber is null or blank."
            );
            // FtdiCableName = ftdiCableName;
            FtdiCableName = Guard.Against.NullOrWhiteSpace(
                ftdiCableName,
                nameof(ftdiCableName),
                "SerialPortInstance, constructor: FtdiCableName is null or blank."
            );
            // CurrentSerialPort = currentSerialPort;
            Guard.Against.Null(
                currentSerialPort,
                nameof(currentSerialPort),
                "SerialPortInstance, constructor: CurrentSerialPort is null."
            );
            // DataReceivedAction = dataReceivedAction;
            DataReceivedAction = Guard.Against.Null(
                dataReceivedAction,
                nameof(dataReceivedAction),
                "SerialPortInstance, constructor: DataReceivedAction is null."
            );
            // HardwareChangeAction = hardwareChangeAction;
            HardwareChangeAction = hardwareChangeAction;
            // RetryAttempts = retryAttempts;
            RetryAttempts = Guard.Against.NegativeOrZero(
                retryAttempts,
                nameof(retryAttempts),
                "SerialPortInstance, constructor: RetryAttempts is negative or zero."
            );
            // RetryDelay = retryDelay;
            RetryDelay = Guard.Against.NegativeOrZero(
                retryDelay,
                nameof(retryDelay),
                "SerialPortInstance, constructor: RetryDelay is negative or zero."
            );

            // Log that the SerialPortInstance has been created.
            logger.LogInformation(
                SerialPortLog,
                "SerialPortInstance: SerialPortInstance created for FTDI cable name {CableName} with serial {CableSerial}.",
                ftdiCableName,
                ftdiCableSerialNumber
            );

            // Register to get MapPageUnloadedMessage messages.
            WeakReferenceMessenger.Default.Register<MapPageUnloaded>(
                this,
                (recipient, message) =>
                {
                    logger.LogDebug(
                        SerialPortLog,
                        "SerialPortInstance, MapPageUnloadedMessage: Stopping serial port watcher for cable name {CableName} with serial {CableSerial}.",
                        ftdiCableName,
                        ftdiCableSerialNumber
                    );
                    // Stop the watcher.
                    StopWatcher();
                }
            );

            StartWatcher();
        }
        catch (Exception exception)
        {
            logger.LogError(SerialPortLog, exception, "SerialPortInstance: Error in constructor");
        }

        void StartWatcher()
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
                    SerialPortLog,
                    exception,
                    "SerialPortInstance: Error starting the device watcher."
                );
            }
        }

        void StopWatcher()
        {
            try
            {
                // Log to debug that the device watcher is stopping.
                logger.LogDebug(
                    SerialPortLog,
                    "SerialPortInstance, StopWatcher: Stopping the device watcher."
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
                    SerialPortLog,
                    exception,
                    "SerialPortInstance, StopWatcher: Error stopping the device watcher."
                );
            }
        }

        void SerialPort_Added(DeviceWatcher sender, DeviceInformation args)
        {
            uint ftdiCount = 0;

            try
            {
                logger.LogInformation(SerialPortLog, "SerialPortInstance: SerialPort_Added.");
                Guard.Against.Null(
                    currentSerialPort,
                    nameof(currentSerialPort),
                    "SerialPortInstance, SerialPort_Added: CurrentSerialPort is null"
                );
                Guard.Against.Null(
                    ftdiCableName,
                    nameof(ftdiCableName),
                    "SerialPortInstance, SerialPort_Added: FtdiCableName is null"
                );
                logger.LogInformation(
                    SerialPortLog,
                    "SerialPortInstance: Id: {Id} Name: {Name}, Kind: {Kind}, IsEnabled: {IsEnabled}",
                    args.Id,
                    args.Name,
                    args.Kind,
                    args.IsEnabled
                );

                // Use the Regular Expression to remove spaces from args.Name and write it to the log.
                var name = CommunicationService.RegExRemoveSpace.Replace(args.Name, "");
                logger.LogInformation(SerialPortLog, "SerialPortInstance: args.Name: {Name}", name);

                if (args.Name.Contains(ftdiCableName, StringComparison.CurrentCultureIgnoreCase))
                {
                    logger.LogInformation(
                        SerialPortLog,
                        "SerialPortInstance, SerialPort_Added: {CableName} found.",
                        ftdiCableName
                    );

                    FTDI ftdi = new();
                    var getNumberDevicesStatus = ftdi.GetNumberOfDevices(ref ftdiCount);
                    if (getNumberDevicesStatus != FTDI.FT_STATUS.FT_OK)
                    {
                        logger.LogError(
                            SerialPortLog,
                            "SerialPortInstance, SerialPort_Added: Error getting number of devices, error: {getNumberDevicesStatus}.",
                            getNumberDevicesStatus
                        );
                    }

                    Guard.Against.FTDINotOk(getNumberDevicesStatus);
                    var list = new FTDI.FT_DEVICE_INFO_NODE[ftdiCount];
                    var getDeviceListStatus = ftdi.GetDeviceList(list);
                    if (getDeviceListStatus != FTDI.FT_STATUS.FT_OK)
                    {
                        logger.LogError(
                            SerialPortLog,
                            "SerialPortInstance, SerialPort_Added: Error getting device list, error: {getDeviceListStatus}.",
                            getDeviceListStatus
                        );
                    }
                    Guard.Against.FTDINotOk(getDeviceListStatus);

                    // Log the number of devices found.
                    logger.LogInformation(
                        SerialPortLog,
                        "SerialPortInstance, SerialPort_Added: Number of FTDI devices found: {ftdiCount}",
                        ftdiCount
                    );

                    foreach (var node in list)
                    {
                        logger.LogInformation(
                            SerialPortLog,
                            "SerialPortInstance, SerialPort_Added: {CableName} found: {Description}, {SerialNumber}",
                            ftdiCableName,
                            node.Description,
                            node.SerialNumber
                        );

                        // Log all detail to debug.
                        logger.LogDebug(
                            SerialPortLog,
                            "SerialPortInstance, SerialPort_Added: {CableName} found: {Description}, {SerialNumber}, {LocId}, {Flags}, {Type}, {ID}",
                            ftdiCableName,
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
                                    SerialPortLog,
                                    "SerialPortInstance, SerialPort_Added: Error opening device by index, attempt {AttemptNumber}, error: {openByIndexStatus}.",
                                    attempt + 1,
                                    openByIndexStatus
                                );
                                // Delay before retrying.
                                Task.Delay(retryDelay).Wait();
                            }
                            attempt++;
                        } while (
                            openByIndexStatus != FTDI.FT_STATUS.FT_OK
                            && attempt < retryAttempts
                            && retryAttempts > 0
                        );

                        Guard.Against.FTDINotOk(openByIndexStatus);

                        var getComPortStatus = ftdi.GetCOMPort(out var comport);
                        if (getComPortStatus != FTDI.FT_STATUS.FT_OK)
                        {
                            logger.LogError(
                                SerialPortLog,
                                "SerialPortInstance, SerialPort_Added: Error getting COM port, error: {getComPortStatus}.",
                                getComPortStatus
                            );
                        }
                        Guard.Against.FTDINotOk(getComPortStatus);
                        logger.LogInformation(
                            SerialPortLog,
                            "SerialPortInstance, SerialPort_Added: {CableName} comport: {Comport}",
                            ftdiCableName,
                            comport
                        );

                        ftdi.Close();

                        Guard.Against.Null(
                            dataReceivedAction,
                            nameof(dataReceivedAction),
                            "SerialPortInstance: Serial instance must have an action to handle received data."
                        );

                        currentSerialPort.PortName = comport;
                        dataReceivedHandler = (sender, args) =>
                            dataReceivedAction(currentSerialPort, args);

                        currentSerialPort.DataReceived += dataReceivedHandler;

                        if (hardwareChangeAction != null)
                        {
                            pinChangedHandler = (sender, args) =>
                                hardwareChangeAction(currentSerialPort, args);

                            currentSerialPort.PinChanged += pinChangedHandler;
                        }

                        currentSerialPort.Open();

                        openedDevices.Add(args.Id, currentSerialPort);
                    }
                }
                else
                {
                    logger.LogInformation(SerialPortLog, "SerialPortInstance: Port found.");
                }
            }
            catch (Exception exception)
            {
                logger.LogError(
                    SerialPortLog,
                    exception,
                    "SerialPortInstance: Error processing the added device."
                );
            }
        }

        void SerialPort_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            try
            {
                logger.LogInformation(SerialPortLog, "SerialPortInstance: Device removed.");
                logger.LogInformation(
                    SerialPortLog,
                    "SerialPortInstance: Id: {Id}, Kind: {Kind}",
                    args.Id,
                    args.Kind
                );
                CloseAndRemoveSerialPortBySysId(args.Id);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    SerialPortLog,
                    exception,
                    "SerialPortInstance: Error processing the removed device."
                );
            }
        }

        void CloseAndRemoveSerialPortBySysId(string id)
        {
            try
            {
                if (openedDevices.TryGetValue(id, value: out var port))
                {
                    logger.LogInformation(
                        SerialPortLog,
                        "{PortName} previously opened, closing and removing from dictionary of open devices.",
                        port.PortName
                    );
                    // Remove the DataReceived event handler.
                    Guard.Against.Null(
                        dataReceivedAction,
                        nameof(dataReceivedAction),
                        "SerialPortInstance, CloseAndRemoveSerialPort: Serial instance must have an action to handle received data."
                    );
                    Guard.Against.Null(
                        currentSerialPort,
                        nameof(currentSerialPort),
                        "SerialPortInstance, CloseAndRemoveSerialPort: Serial instance must have a port in order to interact with an instrument."
                    );
                    // Unregister the event handlers from the port object.
                    if (DataReceivedAction != null)
                    {
                        logger.LogTrace(
                            SerialPortLog,
                            "SerialPortInstance, CloseAndRemoveSerialPort: Unregistering DataReceived event handler."
                        );
                        port.DataReceived -= dataReceivedHandler;
                    }
                    if (HardwareChangeAction != null)
                    {
                        logger.LogTrace(
                            SerialPortLog,
                            "SerialPortInstance, CloseAndRemoveSerialPort: Unregistering PinChanged event handler."
                        );
                        port.PinChanged -= pinChangedHandler;
                    }

                    port.Close();
                    openedDevices.Remove(id);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(
                    SerialPortLog,
                    exception,
                    "SerialPortInstance, CloseAndRemoveSerialPortBySysId: Error closing and removing the serial port."
                );
            }
        }
    }
}
