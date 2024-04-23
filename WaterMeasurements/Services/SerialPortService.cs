using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;

namespace WaterMeasurements.Services;

public static class FTDINotOkFTDIException
{
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
}

public partial class SerialPortService : ISerialPortService
{
    private readonly ILogger<SerialPortService> logger;

    // Set the EventId for logging messages.
    internal EventId SerialPortServiceLog = new(21, "SerialPortService");

    private DeviceWatcher? deviceWatcher;

    // Regular expression to remove spaces.
    private static readonly Regex regExRemoveSpace = MyRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex MyRegex();

    private static SerialPort V3000SerialPort;
    private static readonly List<FTDIPort> ports = [];
    private static readonly List<FTDIPort> V3000s = [];
    private static readonly FTDI ftdi = new();
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
                // Close the ftdi handle.
                ftdi.Close();
                // Stop the watcher.
                StopWatcher();
            }
        );
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
            deviceWatcher?.Stop();
        }
        catch (Exception exception)
        {
            logger.LogError(
                SerialPortServiceLog,
                exception,
                "SerialPortService: Error stopping the device watcher."
            );
        }
    }

    private void SerialPort_Added(DeviceWatcher sender, DeviceInformation args)
    {
        uint ftdiCount = 0;

        try
        {
            logger.LogInformation(SerialPortServiceLog, "SerialPortService: Device added.");
            logger.LogInformation(
                SerialPortServiceLog,
                "SerialPortService: Id: {Id} Name: {Name}, Kind: {Kind}, IsEnabled: {IsEnabled}",
                args.Id,
                args.Name,
                args.Kind,
                args.IsEnabled
            );

            /*
            // Iterate through the properties.
            foreach (var property in args.Properties)
            {
                logger.LogInformation(
                    SerialPortServiceLog,
                    "SerialPortService: Property: {Key} Value: {Value}",
                    property.Key,
                    property.Value
                );
            }
            */

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
                var status = ftdi.GetNumberOfDevices(ref ftdiCount);
                Guard.Against.FTDINotOk(status);
                var list = new FTDI.FT_DEVICE_INFO_NODE[ftdiCount];
                status = ftdi.GetDeviceList(list);
                Guard.Against.FTDINotOk(status);
                foreach (var node in list)
                {
                    logger.LogInformation(
                        SerialPortServiceLog,
                        "SerialPortService, SerialPort_Added: V3000 found: {Description}, {SerialNumber}",
                        node.Description,
                        node.SerialNumber
                    );
                    ftdi.OpenBySerialNumber(node.SerialNumber);
                    ftdi.GetCOMPort(out var comport);
                    logger.LogInformation(
                        SerialPortServiceLog,
                        "SerialPortService, SerialPort_Added: V3000 comport: {Comport}",
                        comport
                    );
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

            // Iterate through the properties.
            foreach (var property in args.Properties)
            {
                logger.LogInformation(
                    SerialPortServiceLog,
                    "SerialPortService: Property: {Key} Value: {Value}",
                    property.Key,
                    property.Value
                );
            }
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
}
