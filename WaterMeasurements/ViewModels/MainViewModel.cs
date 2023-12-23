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

    // private readonly IConfigurationService? configurationService;

    private readonly IGetPreplannedMapService? preplannedMapService;

    private readonly IGeoDatabaseService geoDatabaseService;

    // instanceChannel is used to provide a unique identifier for messages associated with geodatabase and geotrigger instances.
    private uint instanceChannel = 0;

    public MainViewModel(
        // IConfigurationService configurationService,
        IGetPreplannedMapService preplannedMapService,
        IGeoDatabaseService geoDatabaseService,
        ILogger<MainViewModel> logger
    )
    {
        this.logger = logger;

        // Register for the MapConfigurationMessage message and print the two values to the debug console.
        WeakReferenceMessenger.Default.Register<MapConfigurationMessage>(
            this,
            (recipient, message) =>
            {
                logger.LogDebug(
                    MainViewModelLog,
                    "MainViewModel, MapConfiguredMessage, ArcGISApiConfigured: {arcGISApiConfigured}, OfflineMapIdConfigured: {offlineMapIdConfigured}",
                    message.Value.ArcGISApiConfigured,
                    message.Value.OfflineMapIdConfigured
                );
            }
        );

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

        // this.configurationService = configurationService;
        this.preplannedMapService = preplannedMapService;
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
            /*
            
            Guard.Against.Null(
                configurationService,
                nameof(configurationService),
                "MainViewModel, Initialize(): configurationService can not be null"
            );

            */

            Guard.Against.Null(
                preplannedMapService,
                nameof(preplannedMapService),
                "MainViewModel, Initialize(): preplannedMapService can not be null"
            );

            Guard.Against.Null(
                geoDatabaseService,
                nameof(geoDatabaseService),
                "MainViewModel, Initialize(): geoDatabaseService can not be null"
            );

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

    // Public method to handle incrementing the instanceChannel in a thread-safe manner.
    public uint GetNextInstanceChannel()
    {
        return (uint)Interlocked.Increment(ref instanceChannel);
    }
}
