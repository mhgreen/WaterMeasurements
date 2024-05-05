using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using FTD2XX_NET;
using Windows.Devices.Sms;

namespace WaterMeasurements.Models;

public readonly record struct SerialPortAdd(
    string FtdiCableSerialNumber,
    string FtdiCableName,
    SerialPort Port,
    Action<object, SerialDataReceivedEventArgs> SerialMonitorAction,
    Action<object, SerialPinChangedEventArgs>? HardwareChangeAction,
    int NumberoAttempts,
    int RetryDelay
);

public readonly record struct SerialPortHardwareState(SerialPortHardwarePinState PinState);

public enum SerialPortHardwarePinState
{
    CtsOn,
    CtsOff,
    DsrOn,
    DsrOff
}
