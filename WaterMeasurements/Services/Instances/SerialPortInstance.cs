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

                var ftdi = new FTDI();
                FTDI.FT_STATUS status;
                var attempt = 0;

                // Use the Regular Expression to remove spaces from args.Name and write it to the log.
                var name = CommunicationService.RegExRemoveSpace.Replace(args.Name, "");

                if (args.Name.Contains(ftdiCableName, StringComparison.CurrentCultureIgnoreCase))
                {
                    logger.LogInformation(
                        SerialPortLog,
                        "SerialPortInstance, SerialPort_Added: Cable with name {CableName} found.",
                        ftdiCableName
                    );
                    if (openedDevices.ContainsKey(args.Id))
                    {
                        logger.LogInformation(
                            SerialPortLog,
                            "SerialPortInstance, SerialPort_Added: Device with id {Id} already opened, closing device and removing from openedDevices.",
                            args.Id
                        );
                        CloseAndRemoveSerialPortBySysId(args.Id);
                    }
                    do
                    {
                        status = ftdi.OpenBySerialNumber(ftdiCableSerialNumber);
                        if (status != FTDI.FT_STATUS.FT_OK)
                        {
                            logger.LogError(
                                SerialPortLog,
                                "SerialPortInstance, SerialPort_Added: Error opening device by serial number {SerialNumber}, attempt {AttemptNumber}, error: {Status}.",
                                FtdiCableSerialNumber,
                                attempt + 1,
                                status
                            );
                            // Delay before retrying
                            Task.Delay(RetryDelay).Wait();
                        }
                        attempt++;
                    } while (status != FTDI.FT_STATUS.FT_OK && attempt < retryAttempts);

                    if (status != FTDI.FT_STATUS.FT_OK)
                    {
                        logger.LogError(
                            SerialPortLog,
                            "SerialPortInstance, SerialPort_Added: Failed to open device with serial number {SerialNumber} after {Attempt} attempts.",
                            FtdiCableSerialNumber,
                            RetryAttempts
                        );
                    }
                    Guard.Against.FTDINotOk(
                        status,
                        "SerialPortInstance, SerialPort_Added: Failed to open device, so not proceeding with search for cable serial number."
                    );

                    logger.LogInformation(
                        SerialPortLog,
                        "SerialPortInstance, SerialPort_Added: Cable with serial number {SerialNumber} found.",
                        FtdiCableSerialNumber
                    );

                    var getComPortStatus = ftdi.GetCOMPort(out var comport);
                    Guard.Against.FTDINotOk(
                        getComPortStatus,
                        "Cable with serial number found, but does not have an associated COM port."
                    );

                    // The COM port has been identified, so close the FTDI device.
                    ftdi.Close();

                    logger.LogInformation(
                        SerialPortLog,
                        "SerialPortInstance, SerialPort_Added: Cable {CableName} is on comport {Comport}.",
                        FtdiCableName,
                        comport
                    );

                    // ftdi.Close() has been called, so we can now use the comport to setup the serial port.
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

                    if (hardwareChangeAction != null)
                    {
                        if (currentSerialPort.CtsHolding)
                        {
                            logger.LogDebug(
                                SerialPortLog,
                                "SerialPortInstance, SerialPort_Added: Upon opening the serial port, CTS is ON."
                            );
                            // Send a message that the CTS is ON.
                            WeakReferenceMessenger.Default.Send(
                                new SerialPortHardwareStateMessage(
                                    new SerialPortHardwareState(SerialPortHardwarePinState.CtsOn)
                                )
                            );
                        }
                        else
                        {
                            logger.LogDebug(
                                SerialPortLog,
                                "SerialPortInstance, SerialPort_Added: Upon opening the serial port, CTS is OFF."
                            );
                            // Send a message that the CTS is OFF.
                            WeakReferenceMessenger.Default.Send(
                                new SerialPortHardwareStateMessage(
                                    new SerialPortHardwareState(SerialPortHardwarePinState.CtsOff)
                                )
                            );
                        }
                    }

                    // Add to openedDevices dictionary if not already present
                    openedDevices.TryAdd(args.Id, currentSerialPort);
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
