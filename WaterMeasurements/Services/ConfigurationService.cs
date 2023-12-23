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

namespace WaterMeasurements.Services;

// Message to notify modules of the status of the preplanned map configuation.
public class MapConfigurationMessage(PreplannedMapConfiguration configurationValid)
    : ValueChangedMessage<PreplannedMapConfiguration>(configurationValid) { }

public partial class ConfigurationService : IConfigurationService
{
    private readonly ILogger<ConfigurationService> logger;
    internal EventId ConfigurationServiceLog = new(11, "ConfigurationService");

    private readonly ILocalSettingsService? localSettingsService;

    private readonly INetworkStatusService? networkStatusService;

    public ConfigurationService(
        ILogger<ConfigurationService> logger,
        ILocalSettingsService localSettingsService,
        INetworkStatusService networkStatusService
    )
    {
        this.logger = logger;
        logger.LogInformation(ConfigurationServiceLog, "Starting ConfigurationService");
        this.networkStatusService = networkStatusService;
        this.localSettingsService = localSettingsService;

        _ = Initialize();
        _ = CheckForArcgisKeyAndMapId();
    }

    private async Task Initialize()
    {
        try
        {
            WeakReferenceMessenger.Default.Register<NetworkChangedMessage>(
                this,
                (recipient, message) =>
                {
                    // Handle the message here, with recipient being the recipient and message being the
                    // input message. Using the recipient passed as input makes it so that
                    // the lambda expression doesn't capture "this", improving performance.

                    var netStat = message.Value;

                    logger.LogDebug(
                        ConfigurationServiceLog,
                        "NetworkChangedMessage IsInternetAvailable: {isInternetAvailable}",
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

    private async Task CheckForArcgisKeyAndMapId()
    {
        try
        {
            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "ConfigurationService, CheckForArcgisKeyAndMapId: localSettingsService can not be null."
            );

            Guard.Against.Null(
                networkStatusService,
                nameof(networkStatusService),
                "ConfigurationService, CheckForArcgisKeyAndMapId: networkStatusService can not be null."
            );

            var arcgisKey = await localSettingsService.ReadSettingAsync<string>(
                ConfigurationKey.ArcgisApiKey
            );
            var offlineMapId = await localSettingsService.ReadSettingAsync<string>(
                ConfigurationKey.OfflineMapIdentifier
            );
            var arcgisKeyPresent = false;
            var offlineMapPresent = false;

            // If the arcgisKey is not null or blank, then set arcgisKeyPresent to true.
            if (arcgisKey is not null && arcgisKey != "")
            {
                logger.LogInformation(
                    ConfigurationServiceLog,
                    "ConfigurationService, CheckForArcgisKeyAndMapId: arcgisKey is not null or blank."
                );
                // log the arcgisKey to debug.
                logger.LogInformation(
                    ConfigurationServiceLog,
                    "ConfigurationService, CheckForArcgisKeyAndMapId: arcgisKey: {arcgisKey}.",
                    arcgisKey
                );

                arcgisKeyPresent = true;
            }
            else
            {
                logger.LogInformation(
                    ConfigurationServiceLog,
                    "ConfigurationService, CheckForArcgisKeyAndMapId: arcgisKey is null or blank."
                );
                arcgisKeyPresent = false;
            }

            // If the offlineMapId is not null or blank, then set offlineMapPresent to true.
            if (offlineMapId is not null && offlineMapId != "")
            {
                logger.LogInformation(
                    ConfigurationServiceLog,
                    "ConfigurationService, CheckForArcgisKeyAndMapId: offlineMapId is not null or blank."
                );
                // log the offlineMapId to debug.
                logger.LogInformation(
                    ConfigurationServiceLog,
                    "ConfigurationService, CheckForArcgisKeyAndMapId: offlineMapId: {offlineMapId}.",
                    offlineMapId
                );

                offlineMapPresent = true;
            }
            else
            {
                logger.LogInformation(
                    ConfigurationServiceLog,
                    "ConfigurationService, CheckForArcgisKeyAndMapId: offlineMapId is null or blank."
                );
                offlineMapPresent = false;
            }

            // Send a message using false to indicate arcgisKey and offlineMapId are null or blank.
            // One may be vaild and the other invalid indicated by true or false.
            // This is used by the UI and the state machine to determine if the map can be displayed or if the user needs to be prompted to enter the arcgisKey or offlineMapId.

            WeakReferenceMessenger.Default.Send(
                new MapConfigurationMessage(
                    new PreplannedMapConfiguration(arcgisKeyPresent, offlineMapPresent)
                )
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                ConfigurationServiceLog,
                "ConfigurationService, CheckForArcgisKeyAndMapId: Exception: {exception}.",
                exception.ToString()
            );
        }
    }
}
