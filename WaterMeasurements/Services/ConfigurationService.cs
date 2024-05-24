using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Http;
using Esri.ArcGISRuntime.Security;
using Microsoft.Extensions.Logging;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Models;
using WaterMeasurements.Services;
using WaterMeasurements.ViewModels;
using Windows.Networking.Connectivity;
using Windows.System;
using static WaterMeasurements.Models.PrePlannedMapConfiguration;

namespace WaterMeasurements.Services;

// Message to notify modules that ArcGIS Runtime is initialized.
public class ArcGISRuntimeInitializedMessage(bool isInitialized)
    : ValueChangedMessage<bool>(isInitialized) { }

// Message to request runtime initialization.
public class ArcGISRuntimeInitializeRequestMessage() { }

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

        // Register a message handler for the ArcGISRuntimeInitializeRequestMessage.
        WeakReferenceMessenger.Default.Register<
            ConfigurationService,
            ArcGISRuntimeInitializeRequestMessage
        >(
            this,
            async (recipient, message) =>
            {
                logger.LogDebug(
                    ConfigurationServiceLog,
                    "ConfigurationService, ArcGISRuntimeInitializeRequestMessage: {message}.",
                    message
                );
                await ArcGISRuntimeInitialize();
            }
        );

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
                "ConfigurationService, Initialize: Exception: {exception}.",
                exception.ToString()
            );
        }
    }

    public async Task ArcGISRuntimeInitialize()
    {
        try
        {
            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "Configuration Service, Initialize(): localSettingsService is null."
            );

            /* Authentication for ArcGIS location services:
             * Use of ArcGIS location services, including basemaps and geocoding, requires either:
             * 1) ArcGIS identity (formerly "named user"): An account that is a member of an organization in ArcGIS Online or ArcGIS Enterprise
             *    giving your application permission to access the content and location services authorized to an existing ArcGIS user's account.
             *    You'll get an identity by signing into the ArcGIS Portal.
             * 2) API key: A permanent token that grants your application access to ArcGIS location services.
             *    Create a new API key or access existing API keys from your ArcGIS for Developers
             *    dashboard (https://links.esri.com/arcgis-api-keys) then call .UseApiKey("[Your ArcGIS location services API Key]")
             *    in the initialize call below. */

            /* Licensing:
             * Production deployment of applications built with the ArcGIS Maps SDK requires you to license ArcGIS functionality.
             * For more information see https://links.esri.com/arcgis-runtime-license-and-deploy.
             * You can set the license string by calling .UseLicense(licenseString) in the initialize call below
             * or retrieve a license dynamically after signing into a portal:
             * ArcGISRuntimeEnvironment.SetLicense(await myArcGISPortal.GetLicenseInfoAsync()); */

            // Initialize the ArcGIS Maps SDK runtime before any components are created.

            var apiKey = await localSettingsService.ReadSettingAsync<string>(
                Item[Key.ArcgisApiKey]
            );
            Guard.Against.NullOrEmpty(
                apiKey,
                nameof(apiKey),
                "Configuration Service, Initialize(): apiKey is null."
            );
            if (apiKey is not null)
            {
                ArcGISRuntimeEnvironment.Initialize(config =>
                    config
                        // .UseLicense("[Your ArcGIS Maps SDK License key]")
                        .UseApiKey(apiKey)
                        .ConfigureAuthentication(auth =>
                            auth.UseDefaultChallengeHandler() // Use the default authentication dialog
                        // .UseOAuthAuthorizeHandler(myOauthAuthorizationHandler) // Configure a custom OAuth dialog
                        )
                );
            }
            logger.LogInformation(
                ConfigurationServiceLog,
                "ConfigurationService, ArcGISRuntimeInitialize: ArcGISRuntimeEnvironment.IsInitialized: {isInitialized}.",
                ArcGISRuntimeEnvironment.IsInitialized
            );

            // Send a message to notify modules of the state of ArcGIS Runtime initialization.
            WeakReferenceMessenger.Default.Send(
                new ArcGISRuntimeInitializedMessage(ArcGISRuntimeEnvironment.IsInitialized)
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                ConfigurationServiceLog,
                "ConfigurationService, ArcGISRuntimeInitialize: Exception: {exception}.",
                exception.ToString()
            );
        }
    }
}
