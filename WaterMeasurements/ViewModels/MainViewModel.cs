using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows.Input;
using Ardalis.GuardClauses;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Newtonsoft.Json.Linq;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Models;
using WaterMeasurements.Services;
using WaterMeasurements.Views;
using static WaterMeasurements.Models.PrePlannedMapConfiguration;

namespace WaterMeasurements.ViewModels;

// Message for requesting the current UIQueue.
public class UIQueueRequestMessage : RequestMessage<UIQueue> { }

public partial class MainViewModel : ObservableRecipient
{
    private readonly ILogger<MainViewModel> logger;

    // Set the EventId for logging messages.
    internal EventId MainViewModelLog = new(3, "MainViewModel");

    // Message to request the next instance channel.
    public class InstanceChannelRequestMessage : RequestMessage<uint> { }

    [ObservableProperty]
    private ElementTheme elementTheme = ElementTheme.Default;

    [ObservableProperty]
    private bool lightThemeSelected = false;

    [ObservableProperty]
    private bool darkThemeSelected = false;

    [ObservableProperty]
    private bool systemThemeSelected = false;

    [ObservableProperty]
    private Map? currentMap;

    [ObservableProperty]
    private double geodatabasePercentDownloaded;

    [ObservableProperty]
    private bool showGeodatabaseDownloadProgress = false;

    [ObservableProperty]
    private string uiSelected = "Map";

    // Observable property for the map border.
    [ObservableProperty]
    private Brush mapBorderColor = new SolidColorBrush(Colors.Transparent);

    // Observable property for the selected location in the list of locations for a measurement type.
    // This is used to deselect the location when the user moves to a different measurement type.
    [ObservableProperty]
    private object? selectedLocation;

    // Observable property for the map center and auto-pan options.
    // If the location is selected from the list, the map will pan to that location.
    // Center and auto-pan should then be set to null until selected.
    [ObservableProperty]
    private object? mapCenterAutoPan;

    public ICommand SwitchThemeCommand { get; }

    // Feature for the current location sent by the GeoTriggerService.
    public ArcGISFeature? feature;

    // DispatcherQueue for the UI thread.
    private DispatcherQueue? uiDispatcherQueue;

    private readonly IGetPreplannedMapService? getPreplannedMapService;

    private readonly IGeoDatabaseService geoDatabaseService;

    private readonly INetworkStatusService? networkStatusService;

    private readonly IConfigurationService? configurationService;

    private readonly ILocalSettingsService? localSettingsService;

    private readonly IThemeSelectorService? themeSelectorService;

    private readonly IGeoTriggerService? geoTriggerService;

    private readonly IWaterQualityService? waterQualityService;

    // instanceChannel is used to provide a unique identifier for messages associated with geodatabase and geotrigger instances.
    private uint instanceChannel = 0;

    public MainViewModel(
        INetworkStatusService networkStatusService,
        IConfigurationService configurationService,
        IGetPreplannedMapService getPreplannedMapService,
        IGeoDatabaseService geoDatabaseService,
        ILocalSettingsService? localSettingsService,
        ILogger<MainViewModel> logger,
        IThemeSelectorService themeSelectorService,
        IGeoTriggerService geoTriggerService,
        IWaterQualityService waterQualityService
    )
    {
        this.logger = logger;

        UiSelected = "Map";

        // Message handler for the InstanceChannelRequestMessage.
        WeakReferenceMessenger.Default.Register<MainViewModel, InstanceChannelRequestMessage>(
            this,
            (recipient, message) =>
            {
                logger.LogTrace(
                    MainViewModelLog,
                    "MainViewModel, InstanceChannelRequestMessage received."
                );
                // Get the next instance channel and return the value.
                message.Reply(GetNextInstanceChannel());
            }
        );

        this.networkStatusService = networkStatusService;
        this.configurationService = configurationService;
        this.getPreplannedMapService = getPreplannedMapService;
        this.geoDatabaseService = geoDatabaseService;
        this.localSettingsService = localSettingsService;
        this.themeSelectorService = themeSelectorService;
        this.geoTriggerService = geoTriggerService;
        this.waterQualityService = waterQualityService;

        SwitchThemeCommand = new RelayCommand<ElementTheme>(
            async (param) =>
            {
                // Log the theme change.
                logger.LogTrace(
                    MainViewModelLog,
                    "MainViewModel, SwitchThemeCommand: Theme changed to {param}.",
                    param
                );

                Guard.Against.Null(
                    themeSelectorService,
                    nameof(themeSelectorService),
                    "MainViewModel, Initialize(): themeSelectorService is null"
                );

                if (ElementTheme != param)
                {
                    ElementTheme = param;
                    await themeSelectorService.SetThemeAsync(param);
                }
            }
        );

        _ = Initialize();
    }

    private async Task Initialize()
    {
        // Log that the MainViewModel has been created.
        logger.LogInformation(
            MainViewModelLog,
            "MainViewModel, Initialize(): MainViewModel created."
        );

        try
        {
            Guard.Against.Null(
                networkStatusService,
                nameof(networkStatusService),
                "MainViewModel, Initialize(): networkStatusService is null"
            );

            Guard.Against.Null(
                getPreplannedMapService,
                nameof(getPreplannedMapService),
                "MainViewModel, Initialize(): getPreplannedMapService is null"
            );

            Guard.Against.Null(
                geoDatabaseService,
                nameof(geoDatabaseService),
                "MainViewModel, Initialize(): geoDatabaseService is null"
            );

            Guard.Against.Null(
                configurationService,
                nameof(configurationService),
                "MainViewModel, Initialize(): configurationService is null"
            );

            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "MainViewModel, Initialize(): localSettingsService is null"
            );

            Guard.Against.Null(
                themeSelectorService,
                nameof(themeSelectorService),
                "MainViewModel, Initialize(): themeSelectorService is null"
            );

            Guard.Against.Null(
                geoTriggerService,
                nameof(geoTriggerService),
                "MainViewModel, Initialize(): geoTriggerService is null"
            );

            await IndicateCurrentTheme();

            WeakReferenceMessenger.Default.Register<NetworkChangedMessage>(
                this,
                (recipient, message) =>
                {
                    var netStat = message.Value;

                    logger.LogDebug(
                        MainViewModelLog,
                        "MainViewModel, NetworkChangedMessage IsInternetAvailable: {isInternetAvailable}",
                        netStat.IsInternetAvailable
                    );
                }
            );

            // Get current network status.
            var networkStatus =
                await WeakReferenceMessenger.Default.Send<NetworkStatusRequestMessage>();
            logger.LogDebug(
                MainViewModelLog,
                "MainViewModel, NetworkStatusRequestMessage, isInternetAvailable: {isInternetAvailable}.",
                networkStatus.IsInternetAvailable
            );
            // Iterate over the network names networkStatus.NetworkNames and log them.
            foreach (var name in networkStatus.NetworkNames)
            {
                logger.LogDebug(
                    MainViewModelLog,
                    "MainViewModel, NetworkStatusRequestMessage, NetworkName: {networkName}.",
                    name
                );
            }
            // Log the rest of the network status properties.
            logger.LogDebug(
                MainViewModelLog,
                "MainViewModel, NetworkStatusRequestMessage, ConnectionType: {connectionType}, ConnectivityLevel: {connectivityLevel}, IsInternetOnMeteredConnection: {isInternetOnMeteredConnection}.",
                networkStatus.ConnectionType,
                networkStatus.ConnectivityLevel,
                networkStatus.IsInternetOnMeteredConnection
            );

            // TODO: Determine if this is needed.

            // Send a message requesting the next instance channel.
            uint instanceChannelRequestMessage =
                WeakReferenceMessenger.Default.Send<InstanceChannelRequestMessage>();

            logger.LogDebug(
                MainViewModelLog,
                "MainViewModel, Initialize(): next instance channel is: {channel}.",
                instanceChannelRequestMessage
            );

            // Register to get map messages.
            WeakReferenceMessenger.Default.Register<PropertyChangedMessage<Map>>(
                this,
                (recipient, message) =>
                {
                    CurrentMap = message.NewValue;
                }
            );

            WeakReferenceMessenger.Default.Register<UIQueueSetMessage>(
                this,
                (recipient, message) =>
                {
                    Guard.Against.Null(
                        message.Value.UIDispatcherQueue,
                        nameof(message.Value.UIDispatcherQueue),
                        "MainViewModel, Registering for UIQueueSetMessage: Subscription to uiDispatcherQueue is null."
                    );
                    uiDispatcherQueue = message.Value.UIDispatcherQueue;

                    // Subscribe to geodatabase download progress messages and put those on the UI thread.
                    SubscribeGeoDbDownload(uiDispatcherQueue);
                }
            );

            // Respond to requests for the current UI Dispatcher Queue which should have been set by a UIQueueSetMessage.
            WeakReferenceMessenger.Default.Register<MainViewModel, UIQueueRequestMessage>(
                this,
                (request, message) =>
                {
                    Guard.Against.Null(
                        uiDispatcherQueue,
                        nameof(uiDispatcherQueue),
                        "MainViewModel, UIQueueRequestMessage: uiDispatcherQueue is null."
                    );

                    message.Reply(new UIQueue(uiDispatcherQueue));
                }
            );

            // Register to get MapPageUnloadedMessage messages.
            WeakReferenceMessenger.Default.Register<MapPageUnloaded>(
                this,
                (recipient, message) =>
                {
                    logger.LogDebug(
                        MainViewModelLog,
                        "MainViewModel, MapPageUnloaded: unregistering all message subscriptions."
                    );

                    // Unregister all messages.
                    WeakReferenceMessenger.Default.UnregisterAll(this);
                }
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                MainViewModelLog,
                exception,
                "Exception generated MainViewModel, Initialize() {exception}.",
                exception.Message.ToString()
            );
        }
    }

    public async Task<dynamic> RetrieveSettingByKeyAsync<T>(string settingKey)
    {
        logger.LogTrace(
            MainViewModelLog,
            "MainViewModel, RetrieveSettingByKey: Requesting setting by key: {settingsKey}.",
            settingKey
        );
        try
        {
            Guard.Against.NullOrWhiteSpace(
                settingKey,
                nameof(settingKey),
                "MainViewModel, RetrieveSettingByKey: settingKey is null or whitespace."
            );
            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "MainViewModel, RetrieveSettingByKey: localSettingsService is null."
            );
            var setting = await localSettingsService.ReadSettingAsync<T>(settingKey);

            return setting!;
        }
        catch (Exception exception)
        {
            logger.LogError(
                MainViewModelLog,
                exception,
                "Exception generated in MainViewModel, RetrieveSettingByKey(): {exception}",
                exception.Message.ToString()
            );

            return null!;
        }
    }

    public async Task StoreSettingByKeyAsync(string settingsKey, dynamic value)
    {
        if (value is string stringValue)
        {
            logger.LogDebug(
                MainViewModelLog,
                "MainViewModel, StoreSettingByKey: configuration with key: {settingsKey} set to value {value}.",
                settingsKey,
                stringValue
            );
        }
        else if (value is int intValue)
        {
            logger.LogDebug(
                MainViewModelLog,
                "MainViewModel, StoreSettingByKey: configuration with key: {settingsKey} set to value {value}.",
                settingsKey,
                intValue
            );
        }
        else if (value is bool boolValue)
        {
            logger.LogDebug(
                MainViewModelLog,
                "MainViewModel, StoreSettingByKey: configuration with key: {settingsKey} set to value {value}.",
                settingsKey,
                boolValue
            );
        }
        else if (value is double doubleValue)
        {
            logger.LogDebug(
                MainViewModelLog,
                "MainViewModel, StoreSettingByKey: configuration with key: {settingsKey} set to value {value}.",
                settingsKey,
                doubleValue
            );
        }
        else
        {
            logger.LogDebug(
                MainViewModelLog,
                "MainViewModel, StoreSettingByKey: configuration with key: {settingsKey} is not of type string, integer, boolean, or double. Value will be set, but not confirmed here.",
                settingsKey
            );
        }

        try
        {
            Guard.Against.NullOrWhiteSpace(
                settingsKey,
                nameof(settingsKey),
                "MainViewModel, StoreSettingByKey: settingsKey is null or whitespace."
            );

            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "MainViewModel, StoreSettingByKey: localSettingsService is null."
            );

            await localSettingsService.SaveSettingAsync(settingsKey, value);
        }
        catch (Exception exception)
        {
            logger.LogError(
                MainViewModelLog,
                exception,
                "Exception generated in MainViewModel, StoreSettingByKey(): {exception}",
                exception.Message.ToString()
            );
        }
    }

    public async Task InitializeArcGISRuntimeAsync()
    {
        try
        {
            Guard.Against.Null(
                configurationService,
                nameof(configurationService),
                "MainViewModel, InitializeArcGISRuntime(): configurationService is null."
            );

            await configurationService.ArcGISRuntimeInitialize();
        }
        catch (Exception exception)
        {
            logger.LogError(
                MainViewModelLog,
                exception,
                "Exception generated in MainViewModel, InitializeArcGISRuntime(): {exception}",
                exception.Message.ToString()
            );
        }
    }

    private void SubscribeGeoDbDownload(DispatcherQueue uiDispatcherQueue)
    {
        try
        {
            WeakReferenceMessenger.Default.Register<
                PropertyChangedMessage<GeoDatabaseDownloadProgress>
            >(
                this,
                (recipient, message) =>
                {
                    Guard.Against.Null(
                        uiDispatcherQueue,
                        nameof(uiDispatcherQueue),
                        "MainViewModel, SubscribeGeoDbDownload(DispatcherQueue uiDispatcherQueue): uiDispatcherQueue can not be null."
                    );
                    uiDispatcherQueue.TryEnqueue(() =>
                    {
                        GeodatabasePercentDownloaded = message.NewValue.PercentDownloaded;

                        if (message.NewValue.PercentDownloaded == 100)
                        {
                            ShowGeodatabaseDownloadProgress = false;
                        }
                        else
                        {
                            ShowGeodatabaseDownloadProgress = true;
                        }

                        logger.LogDebug(
                            MainViewModelLog,
                            "MainViewModel, PropertyChangedMessage, GeoDatabaseDownloadProgress: Geodatabase percent downloaded: {percentDownloaded}, Thread: {thread}",
                            message.NewValue.PercentDownloaded,
                            Environment.CurrentManagedThreadId
                        );
                    });
                }
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                MainViewModelLog,
                exception,
                "Exception generated in MainViewModel, SubscribeGeoDbDownload(). {exception}",
                exception.Message.ToString()
            );
        }
    }

    public async Task RequestPreplannedMapConfigurationMessage()
    {
        logger.LogDebug(
            MainViewModelLog,
            "MainViewModel, RequestPreplannedMapConfigurationMessage: Requesting PreplannedMapConfigurationStatusMessage."
        );
        try
        {
            Guard.Against.Null(
                getPreplannedMapService,
                nameof(getPreplannedMapService),
                "MainViewModel, RequestPreplannedMapConfigurationMessage: getPreplannedMapService is null."
            );
            await getPreplannedMapService.PreplannedMapConfigurationStatusMessage();
        }
        catch (Exception exception)
        {
            logger.LogError(
                MainViewModelLog,
                exception,
                "Exception generated in MainViewModel, RequestPreplannedMapConfigurationMessage(). {exception}",
                exception.Message.ToString()
            );
        }
    }

    public async Task RequestArcGISRuntimeInitializeMessage()
    {
        logger.LogDebug(
            MainViewModelLog,
            "MainViewModel, RequestArcGISRuntimeInitialize: Requesting ArcGISRuntimeInitialize."
        );
        try
        {
            Guard.Against.Null(
                configurationService,
                nameof(configurationService),
                "MainViewModel, RequestArcGISRuntimeInitialize: configurationService is null."
            );
            await configurationService.ArcGISRuntimeInitialize();
        }
        catch (Exception exception)
        {
            logger.LogError(
                MainViewModelLog,
                exception,
                "Exception generated in MainViewModel, RequestArcGISRuntimeInitialize(). {exception}",
                exception.Message.ToString()
            );
        }
    }

    // Set the theme dropdown one the main page to the current theme.
    private async Task IndicateCurrentTheme()
    {
        logger.LogTrace(
            MainViewModelLog,
            "MainViewModel, IndicateCurrentTheme: Indicating current theme."
        );
        try
        {
            Guard.Against.Null(
                themeSelectorService,
                nameof(themeSelectorService),
                "MainViewModel, IndicateCurrentTheme: themeSelectorService is null."
            );
            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "MainViewModel, IndicateCurrentTheme: localSettingsService is null."
            );
            var theme = await localSettingsService.ReadSettingAsync<string>(
                ThemeSelectorService.SettingsKey
            );
            // Log the current theme.
            logger.LogTrace(
                MainViewModelLog,
                "MainViewModel, IndicateCurrentTheme: Current theme is {theme}.",
                theme
            );
            // Set the theme dropdown to the current theme.
            switch (theme)
            {
                case "Light":
                    LightThemeSelected = true;
                    break;
                case "Dark":
                    DarkThemeSelected = true;
                    break;
                case "Default":
                    SystemThemeSelected = true;
                    break;
                default:
                    SystemThemeSelected = true;
                    break;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                MainViewModelLog,
                exception,
                "Exception generated in MainViewModel, IndicateCurrentTheme(). {exception}",
                exception.Message.ToString()
            );
        }
    }

    // Public method to handle incrementing the instanceChannel in a thread-safe manner.
    public uint GetNextInstanceChannel()
    {
        return (uint)Interlocked.Increment(ref instanceChannel);
    }
}
