// Almost all of this code is from the Windows Community Tookit.
// The original code may be found here:
// https://github.com/CommunityToolkit/WindowsCommunityToolkit/tree/main/Microsoft.Toolkit.Uwp.Connectivity/Network
// This code was licensed as below:

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Models;
using WaterMeasurements.Views;
using Windows.Networking.Connectivity;

namespace WaterMeasurements.Services;

// Message to notify modules that the status of the network has changed.
public class NetworkChangedMessage(NetworkStatus networkStatus)
    : ValueChangedMessage<NetworkStatus>(networkStatus) { }

// Message for requesting the current network status.
public class NetworkStatusRequestMessage : AsyncRequestMessage<NetworkStatus> { }

public partial class NetworkStatusService : INetworkStatusService
{
    private readonly ILogger<NetworkStatusService> logger;
    internal EventId NetworkStatusEvent = new(1, "NetworkStatusService");

    private readonly List<string> networkNames = [];
    private bool IsInternetAvailable { get; set; }
    private ConnectionType ConnectionType { get; set; }
    private NetworkConnectivityLevel ConnectivityLevel { get; set; }
    private ConnectionCost? ConnectionCost { get; set; }
    private byte? SignalStrength { get; set; }
    private IReadOnlyList<string> NetworkNames => networkNames.AsReadOnly();
    private bool IsInternetOnMeteredConnection =>
        ConnectionCost != null && ConnectionCost.NetworkCostType != NetworkCostType.Unrestricted;

    public NetworkStatusService(ILogger<NetworkStatusService> logger)
    {
        this.logger = logger;
        try
        {
            NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;

            // Respond to requests for the current network status.
            WeakReferenceMessenger.Default.Register<
                NetworkStatusService,
                NetworkStatusRequestMessage
            >(
                this,
                (recipient, message) =>
                {
                    var networkStatus = UpdateConnectionInformation(
                        NetworkInformation.GetInternetConnectionProfile()
                    );
                    message.Reply(networkStatus);
                }
            );

            Initialize();

            // Register to get MapPageUnloadedMessage messages.
            WeakReferenceMessenger.Default.Register<MapPageUnloaded>(
                this,
                (recipient, message) =>
                {
                    logger.LogDebug(
                        NetworkStatusEvent,
                        "NetworkStatusEvent, MapPageUnloaded: stopping processing of network status changes, resetting network information and unregistering all message subscriptions."
                    );
                    // Stop processing network status changes.
                    NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;

                    // Clear network status information.
                    Reset();

                    // Unregister all messages.
                    WeakReferenceMessenger.Default.UnregisterAll(this);
                }
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                NetworkStatusEvent,
                exception,
                "Exception generated in NetworkStatusService, NetworkStatusService(ILogger<NetworkStatusService> logger)."
            );
        }
    }

    ~NetworkStatusService()
    {
        NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
        NLog.LogManager.Shutdown();
    }

    internal NetworkStatus Reset()
    {
        networkNames.Clear();
        ConnectionType = ConnectionType.Unknown;
        ConnectivityLevel = NetworkConnectivityLevel.None;
        IsInternetAvailable = false;
        ConnectionCost = null;
        SignalStrength = null;

        var currentNetworkStatus = new NetworkStatus(
            IsInternetAvailable,
            ConnectionType,
            ConnectivityLevel,
            SignalStrength,
            NetworkNames,
            IsInternetOnMeteredConnection
        );
        return currentNetworkStatus;
    }

    public NetworkStatus UpdateConnectionInformation(ConnectionProfile profile)
    {
        try
        {
            if (profile == null)
            {
                logger.LogDebug(
                    NetworkStatusEvent,
                    "NetworkStatusService, UpdateConnectionInformation(ConnectionProfile profile). ConnectionProfile is null."
                );
                return Reset();
            }

            networkNames.Clear();

            var ianaInterfaceType = profile.NetworkAdapter?.IanaInterfaceType ?? 0;

            ConnectionType = ianaInterfaceType switch
            {
                6 => ConnectionType.Ethernet,
                71 => ConnectionType.WiFi,
                243 or 244 => ConnectionType.Data,
                _ => ConnectionType.Unknown,
            };
            var names = profile.GetNetworkNames();
            if (names?.Count > 0)
            {
                networkNames.AddRange(names);
            }

            ConnectivityLevel = profile.GetNetworkConnectivityLevel();

            // In most cases InternetAcces will indicate internet availability.
            // If internet availability should be otherwise defined, IsInternetAvailable may be configured.
            IsInternetAvailable = ConnectivityLevel switch
            {
                NetworkConnectivityLevel.None => false,
                NetworkConnectivityLevel.LocalAccess => false,
                NetworkConnectivityLevel.ConstrainedInternetAccess => false,
                NetworkConnectivityLevel.InternetAccess => true,
                _ => false,
            };

            ConnectionCost = profile.GetConnectionCost();
            SignalStrength = profile.GetSignalBars();

            var networkStatus = new NetworkStatus(
                IsInternetAvailable,
                ConnectionType,
                ConnectivityLevel,
                SignalStrength,
                NetworkNames,
                IsInternetOnMeteredConnection
            );
            logger.LogTrace(
                NetworkStatusEvent,
                "UpdateConnectionInformation, networkStatus: {networkStatus}.",
                networkStatus
            );
            return networkStatus;
        }
        catch (Exception exception)
        {
            logger.LogError(
                NetworkStatusEvent,
                exception,
                "Exception generated in NetworkStatusService, UpdateConnectionInformation(ConnectionProfile profile)."
            );
            return Reset();
        }
    }

    public void Initialize()
    {
        try
        {
            var networkStatus = UpdateConnectionInformation(
                NetworkInformation.GetInternetConnectionProfile()
            );
            WeakReferenceMessenger.Default.Send(new NetworkChangedMessage(networkStatus));
            logger.LogDebug(
                NetworkStatusEvent,
                "NetworkStatusService, Initialize networkStatus: {networkStatus}.",
                networkStatus
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                NetworkStatusEvent,
                exception,
                "Exception generated in NetworkStatusService, Initialize()."
            );
        }
    }

    private void OnNetworkStatusChanged(object? sender)
    {
        try
        {
            var networkStatus = UpdateConnectionInformation(
                NetworkInformation.GetInternetConnectionProfile()
            );
            WeakReferenceMessenger.Default.Send(new NetworkChangedMessage(networkStatus));
            logger.LogTrace(
                NetworkStatusEvent,
                "OnNetworkStatusChanged, networkStatus: {networkStatus}",
                networkStatus
            );
            logger.LogDebug(
                NetworkStatusEvent,
                "OnNetworkStatusChanged, isInternetAvailable: {isInternetAvailable}.",
                networkStatus.IsInternetAvailable
            );
            // Iterate over the network names networkStatus.NetworkNames and log them.
            foreach (var name in networkStatus.NetworkNames)
            {
                logger.LogDebug(
                    NetworkStatusEvent,
                    "OnNetworkStatusChanged, NetworkName: {networkName}.",
                    name
                );
            }
            // Log the rest of the network status properties.
            logger.LogDebug(
                NetworkStatusEvent,
                "OnNetworkStatusChanged, ConnectionType: {connectionType}, ConnectivityLevel: {connectivityLevel}, IsInternetOnMeteredConnection: {isInternetOnMeteredConnection}.",
                networkStatus.ConnectionType,
                networkStatus.ConnectivityLevel,
                networkStatus.IsInternetOnMeteredConnection
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                NetworkStatusEvent,
                exception,
                "Exception generated in NetworkStatusService, OnNetworkStatusChanged(object? sender)."
            );
        }
    }
}
