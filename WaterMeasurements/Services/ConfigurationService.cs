using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

using WaterMeasurements.ViewModels;
using WaterMeasurements.Models;
using WaterMeasurements.Services;
using WaterMeasurements.Contracts.Services;

using Ardalis.GuardClauses;
using Windows.Networking.Connectivity;

namespace WaterMeasurements.Services;

public partial class ConfigurationService : IConfigurationService
{
    private readonly ILogger<ConfigurationService> logger;
    internal EventId ConfigurationServiceLog = new(11, "ConfigurationService");
    private readonly ILocalSettingsService? localSettingsService;

    public ConfigurationService(
        ILogger<ConfigurationService> logger,
        ILocalSettingsService localSettingsService
    )
    {
        this.logger = logger;
        logger.LogInformation(ConfigurationServiceLog, "Starting ConfigurationService");
        this.localSettingsService = localSettingsService;

        _ = Initialize();
    }

    private async Task Initialize()
    {
        try
        {
            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "Configuration Service, Initialize(): localSettingsService is null."
            );

            WeakReferenceMessenger.Default.Register<NetworkChangedMessage>(
                this,
                (recipient, message) =>
                {
                    var netStat = message.Value;

                    logger.LogDebug(
                        ConfigurationServiceLog,
                        "ConfigurationService, NetworkChangedMessage IsInternetAvailable: {isInternetAvailable}",
                        netStat.IsInternetAvailable
                    );
                }
            );

            // Get current network status.
            var networkStatus =
                await WeakReferenceMessenger.Default.Send<NetworkStatusRequestMessage>();
            logger.LogDebug(
                ConfigurationServiceLog,
                "ConfigurationService, NetworkStatusRequestMessage, isInternetAvailable: {isInternetAvailable}.",
                networkStatus.IsInternetAvailable
            );
            // Iterate over the network names networkStatus.NetworkNames and log them.
            foreach (var name in networkStatus.NetworkNames)
            {
                logger.LogDebug(
                    ConfigurationServiceLog,
                    "ConfigurationService, NetworkStatusRequestMessage, NetworkName: {networkName}.",
                    name
                );
            }
            // Log the rest of the network status properties.
            logger.LogDebug(
                ConfigurationServiceLog,
                "ConfigurationService, NetworkStatusRequestMessage, ConnectionType: {connectionType}, ConnectivityLevel: {connectivityLevel}, IsInternetOnMeteredConnection: {isInternetOnMeteredConnection}.",
                networkStatus.ConnectionType,
                networkStatus.ConnectivityLevel,
                networkStatus.IsInternetOnMeteredConnection
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                ConfigurationServiceLog,
                "ConfigurationService, NetworkStatusRequestMessage: Exception: {exception}.",
                exception.ToString()
            );
        }
    }
}
