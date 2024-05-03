using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using FTD2XX_NET;
using Windows.Devices.Sms;

namespace WaterMeasurements.Models;

public readonly record struct SerialMonitorMessage(
    string FtdiCableSerialNumber,
    string FtdiCableName,
    SerialPort Port,
    Action<string> SerialMonitorAction,
    Action<object, SerialPinChangedEventArgs>? HardwareChangeAction,
    int NumberoAttempts,
    int RetryDelay
);

public readonly record struct FtdiInstance(
    FTDI FtdiPort,
    string NodeComportName,
    string NodeDescription,
    string NodeSerialNumber
);

public class FtdiPort
{
    private readonly string nodeComportName;
    private readonly string nodeDescription;
    private readonly string nodeSerialNumber;

    // Constructor
    public FtdiPort()
    {
        nodeComportName = string.Empty;
        nodeDescription = string.Empty;
        nodeSerialNumber = string.Empty;
    }

    // Constructor

    public FtdiPort(string ComportName, string Description, string SerialNumber)
    {
        nodeComportName = ComportName;
        nodeDescription = Description;
        nodeSerialNumber = SerialNumber;
    }

    public string NodeComportName => nodeComportName;

    public string NodeDescription => nodeDescription;

    public string NodeSerialNumber => nodeSerialNumber;
}
