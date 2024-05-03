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
    internal EventId SerialPortServiceLog = new(21, "SerialPortService");

    private readonly ILogger<SerialPortInstance> serialPortInstanceLogger;
    internal EventId SerialPortLog = new(22, "SerialPortInstance");

    private static SerialPort? currentSerialPort;

    public SerialPortService(
        ILogger<SerialPortService> logger,
        ILogger<SerialPortInstance> serialPortInstanceLogger
    )
    {
        this.logger = logger;
        this.serialPortInstanceLogger = serialPortInstanceLogger;
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

        _ = new SerialPortInstance(
            serialPortInstanceLogger,
            "20491327",
            "V3000",
            currentSerialPort,
            V3000DataReceivedHandler,
            null,
            3,
            4000
        );
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
            SerialPortLog,
            "SerialPortService, V3000DataReceivedHandler: V3000DataReceivedHandler called."
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
                    SerialPortLog,
                    exception,
                    "SerialPortService, V3000DataReceivedHandler: TimeoutException occurred while reading from the V3000."
                );
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    SerialPortLog,
                    exception,
                    "SerialPortService, V3000DataReceivedHandler: Exception occurred while reading from the V3000."
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
                SerialPortLog,
                "SerialPortService, V3000DataReceivedHandler, partialResult: {partialResult}",
                partialResult
            );
            logger.LogTrace(
                SerialPortLog,
                "SerialPortService, V3000DataReceivedHandler, V3000O bservation: {V3000Observation}",
                V3000Observation
            );
            logger.LogTrace(
                SerialPortLog,
                "SerialPortService, V3000DataReceivedHandler, Bytes available after Read(), iteration {iterationNumber}: {currentSerialPort.BytesToRead}",
                iterationNumber,
                currentSerialPort.BytesToRead
            );
        } while (currentSerialPort.BytesToRead > 0);
        logger.LogDebug(
            SerialPortLog,
            "SerialPortService, V3000DataReceivedHandler, Final V3000 Observation: {V3000Observation}",
            V3000Observation
        );
    }
}
