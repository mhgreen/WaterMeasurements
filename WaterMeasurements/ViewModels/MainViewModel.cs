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

    private readonly INetworkStatusService? networkStatusService;

    private readonly IGetPreplannedMapService? preplannedMapService;

    private readonly IGeoDatabaseService geoDatabaseService;

    // instanceChannel is used to provide a unique identifier for messages associated with geodatabase and geotrigger instances.
    private uint instanceChannel = 0;

    public MainViewModel(
        INetworkStatusService networkStatusService,
        IGeoDatabaseService geoDatabaseService,
        IGetPreplannedMapService preplannedMapService,
        ILogger<MainViewModel> logger
    )
    {
        this.logger = logger;

        this.geoDatabaseService = geoDatabaseService;
        this.preplannedMapService = preplannedMapService;
        this.networkStatusService = networkStatusService;

        // Message handler for the InstanceChannelRequestMessage.
        WeakReferenceMessenger.Default.Register<MainViewModel, InstanceChannelRequestMessage>(
            this,
            (recipient, message) =>
            {
                logger.LogDebug(
                    MainViewModelLog,
                    "MainViewModel, InstanceChannelRequestMessage: {message}.",
                    message
                );
                // Get the next instance channel and return the value.
                message.Reply(GetNextInstanceChannel());
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
                preplannedMapService,
                nameof(preplannedMapService),
                "MainViewModel, Initialize(): preplannedMapService can not be null"
            );

            // Log that the MainViewModel has been created.
            logger.LogDebug(
                MainViewModelLog,
                "MainViewModel, Initialize(): preplannedMapService checked."
            );

            Guard.Against.Null(
                geoDatabaseService,
                nameof(geoDatabaseService),
                "MainViewModel, Initialize(): geoDatabaseService can not be null"
            );

            // Log that the geoDatabaseService has been checked.
            logger.LogDebug(
                MainViewModelLog,
                "MainViewModel, Initialize(): geoDatabaseService checked."
            );

            Guard.Against.Null(
                networkStatusService,
                nameof(networkStatusService),
                "MainViewModel, Initialize(): networkStatusService can not be null"
            );

            // Log that the networkStatusService has been checked.
            logger.LogDebug(
                MainViewModelLog,
                "MainViewModel, Initialize(): networkStatusService checked."
            );

            // Send a message requesting the next instance channel.
            uint instanceChannelRequestMessage =
                WeakReferenceMessenger.Default.Send<InstanceChannelRequestMessage>();

            logger.LogDebug(
                MainViewModelLog,
                "MainViewModel, Initialize(): next instance channel is: {channel}.",
                instanceChannelRequestMessage
            );

            WeakReferenceMessenger.Default.Register<NetworkChangedMessage>(
                this,
                (recipient, message) =>
                {
                    // Handle the message here, with recipient being the recipient and message being the
                    // input message. Using the recipient passed as input makes it so that
                    // the lambda expression doesn't capture "this", improving performance.

                    var netStat = message.Value;

                    logger.LogDebug(
                        MainViewModelLog,
                        "NetworkChangedMessage IsInternetAvailable: {isInternetAvailable}",
                        netStat.IsInternetAvailable
                    );
                }
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

    // Public method to handle incrementing the instanceChannel in a thread-safe manner.
    public uint GetNextInstanceChannel()
    {
        return (uint)Interlocked.Increment(ref instanceChannel);
    }
}
