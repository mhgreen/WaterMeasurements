using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;

using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Mapping;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Microsoft.UI.Dispatching;

using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Models;
using WaterMeasurements.Views;
using CommunityToolkit.Mvvm.Messaging;
using Ardalis.GuardClauses;
using WaterMeasurements.Services;
using NLog;

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
    private Map? currentMap;

    [ObservableProperty]
    private double geodatabasePercentDownloaded;

    [ObservableProperty]
    private bool showSecchiCollectionPoint = true;

    [ObservableProperty]
    private string secchiCollectionPointName = "Default Location";

    [ObservableProperty]
    private bool showGeodatabaseDownloadProgress = false;

    // Feature for the current location sent by the GeoTriggerService.
    public ArcGISFeature? feature;

    // DispatcherQueue for the UI thread.
    private DispatcherQueue? uiDispatcherQueue;

    // Observable property for the map border.
    [ObservableProperty]
    private Brush mapBorderColor = new SolidColorBrush(Colors.Transparent);

    private readonly IGetPreplannedMapService? getPreplannedMapService;

    private readonly IGeoDatabaseService geoDatabaseService;

    private readonly INetworkStatusService? networkStatusService;

    private readonly IConfigurationService? configurationService;

    // instanceChannel is used to provide a unique identifier for messages associated with geodatabase and geotrigger instances.
    private uint instanceChannel = 0;

    public MainViewModel(
        INetworkStatusService networkStatusService,
        IConfigurationService configurationService,
        IGetPreplannedMapService getPreplannedMapService,
        IGeoDatabaseService geoDatabaseService,
        ILogger<MainViewModel> logger
    )
    {
        this.logger = logger;

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

    // Public method to handle incrementing the instanceChannel in a thread-safe manner.
    public uint GetNextInstanceChannel()
    {
        return (uint)Interlocked.Increment(ref instanceChannel);
    }
}
