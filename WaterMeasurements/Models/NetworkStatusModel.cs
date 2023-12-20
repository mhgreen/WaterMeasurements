using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.Networking.Connectivity;

namespace WaterMeasurements.Models;

// Record for NetworkStatus subscription.
public readonly record struct NetworkStatus(
    bool IsInternetAvailable,
    ConnectionType ConnectionType,
    NetworkConnectivityLevel ConnectivityLevel,
    byte? SignalStrength,
    IReadOnlyList<string> NetworkNames,
    bool IsInternetOnMeteredConnection
);

public enum ConnectionType
{
    Ethernet,
    WiFi,
    Data,
    Unknown,
}
