using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Mapping;

using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Portal;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Tasks;
using Esri.ArcGISRuntime.Tasks.Offline;
using Esri.ArcGISRuntime.UI.Controls;
using Esri.ArcGISRuntime.UI;

using Windows.Security.Cryptography.Core;

using System;
using System.Collections;
using System.Dynamic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Windows.Services.Maps;

using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.Activation;

using Ardalis.GuardClauses;

using WaterMeasurements.Views;
using WaterMeasurements.Services;
using WaterMeasurements.Contracts.Services;

using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using WaterMeasurements.Models;
using Windows.UI.Core;
using Stateless;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Microsoft.Extensions.Hosting;
using Windows.ApplicationModel.Core;
using Esri.ArcGISRuntime.Geotriggers;
using Application = Microsoft.UI.Xaml.Application;
using Microsoft.UI;
using System.Diagnostics.Metrics;
using System.Xml.Linq;
using WaterMeasurements.Services.Instances;
using Windows.ApplicationModel.Store;
using static WaterMeasurements.ViewModels.MainViewModel;
using NLog;
using WaterMeasurements.Helpers;
using System.ComponentModel.DataAnnotations;

namespace WaterMeasurements.ViewModels;

public partial class SecchiViewModel : ObservableRecipient
{
    private readonly ILogger<SecchiViewModel> logger;

    // Set the EventId for logging messages.
    internal EventId SecchiViewModelLog = new(7, "SecchiViewModel");


    [ObservableProperty]
    private bool showSecchiCollectionPoint = true;

    [ObservableProperty]
    private string secchiCollectionPointName = "Default Location";

    // Observable property for the map border.
    [ObservableProperty]
    private Brush mapBorderColor = new SolidColorBrush(Colors.Transparent);

    [ObservableProperty]
    private string selectView = "SecchiCollectionTable";

    // Feature for the current location sent by the GeoTriggerService.
    public ArcGISFeature? feature;

    // DispatcherQueue for the UI thread.
    private DispatcherQueue? uiDispatcherQueue;

    // Current observations feature table set by the FeatureTableMessage.
    private FeatureTable? currentObservationsTable = null;

    private readonly uint secchiObservationsChannel = 0;
    private readonly uint secchiLocationsChannel = 0;
    private readonly uint secchiGeotriggerChannel = 0;

    private bool haveObservations;
    private bool haveLocations;

    private readonly StateMachine<SecchiServiceState, SecchiServiceTrigger> stateMachine;

    public SecchiViewModel(ILogger<SecchiViewModel> logger)
    {
        this.logger = logger;
        logger.LogDebug(SecchiViewModelLog, "SecchiViewModel, Constructor.");

        // Get the next instance channel and use that for the secchiObservationsChannel.
        secchiObservationsChannel =
            WeakReferenceMessenger.Default.Send<InstanceChannelRequestMessage>();
        // Get the next instance channel and use that for the secchiLocationsChannel.
        secchiLocationsChannel =
            WeakReferenceMessenger.Default.Send<InstanceChannelRequestMessage>();
        // Get the next instance channel and use that for the secchiGeotriggerChannel.
        secchiGeotriggerChannel =
            WeakReferenceMessenger.Default.Send<InstanceChannelRequestMessage>();

        // Log to trace secchiObservationsChannel, secchiLocationsChannel, and secchiGeotriggerChannel.
        logger.LogTrace(
            SecchiViewModelLog,
            "SecchiViewModel, Constructor: secchiObservationsChannel: {secchiObservationsChannel}, secchiLocationsChannel: {secchiLocationsChannel}, secchiGeotriggerChannel: {secchiGeotriggerChannel}",
            secchiObservationsChannel,
            secchiLocationsChannel,
            secchiGeotriggerChannel
        );

        // Create the state machine.
        stateMachine = new StateMachine<SecchiServiceState, SecchiServiceTrigger>(
            SecchiServiceState.WaitingForObservations
        );

        Initialize();
    }

    private void Initialize()
    {
        try
        {
            // Trigger for feature table received with feature table as a parameter.
            var locationsFeatureTableReceived = stateMachine.SetTriggerParameters<FeatureTable>(
                SecchiServiceTrigger.LocationFeatureTableReceived
            );

            // Trigger for observation feature table received with feature table as a parameter.
            var observationsFeatureTableReceived = stateMachine.SetTriggerParameters<FeatureTable>(
                SecchiServiceTrigger.ObservationFeatureTableReceived
            );

            // Log state transitions.
            stateMachine.OnTransitioned(OnTransition);

            // Start in an undefined state.
            // Wait for a secchi observations feature table message to be returned in response to the request for SecchiObservations.
            stateMachine
                .Configure(SecchiServiceState.WaitingForObservations)
                // Wait for a feature table message to be returned in response to the request for SecchiObservations.
                .OnEntryFrom(
                    observationsFeatureTableReceived,
                    featureTable =>
                    {
                        logger.LogTrace(
                            SecchiViewModelLog,
                            "SecchiViewModel, StateMachine, SecchiServiceState.WaitingForObservations, OnEntryFrom: FeatureTable: {featureTable}.",
                            featureTable.TableName
                        );

                        currentObservationsTable = featureTable;

                        // Log the fields in the feature table.
                        foreach (var field in currentObservationsTable.Fields)
                        {
                            logger.LogTrace(
                                SecchiViewModelLog,
                                "SecchiViewModel, StateMachine, SecchiServiceState.WaitingForObservations, secchiObservationsChannel: {secchiObservationsChannel}: FeatureTable: {featureTable}, Field: {field.Name}, Field type: {field.type}.",
                                secchiObservationsChannel,
                                currentObservationsTable.TableName,
                                field.Name,
                                field.FieldType.ToString()
                            );
                        }

                        // create a where clause to get all the features
                        var queryParameters = new QueryParameters() { WhereClause = "1=1" };

                        // query the feature table
                        var queryResult = currentObservationsTable
                            .QueryFeaturesAsync(queryParameters)
                            .Result;

                        // iterate over the features and log their attributes
                        foreach (var feature in queryResult)
                        {
                            foreach (var attribute in feature.Attributes)
                            {
                                logger.LogTrace(
                                    SecchiViewModelLog,
                                    "SecchiViewModel, StateMachine, SecchiServiceState.WaitingForObservations, secchiObservationsChannel: {secchiObservationsChannel}: FeatureTable: {featureTable}, attribute.Key: {attributeName}, attribute.Value: {attributeValue}.",
                                    secchiObservationsChannel,
                                    currentObservationsTable.TableName,
                                    attribute.Key,
                                    attribute.Value
                                );
                            }
                        }

                        // Observations have been received.
                        haveObservations = true;

                        // If both observations and locations have been received, fire the ObservationAndLocationFeatureTablesReceived trigger.
                        // Otherwise, wiat for locations to be received before moving to HaveObservationsAndLocations state.
                        if (haveObservations && haveLocations)
                        {
                            stateMachine.Fire(
                                SecchiServiceTrigger.ObservationAndLocationFeatureTablesReceived
                            );
                        }
                        else
                        {
                            logger.LogDebug(
                                SecchiViewModelLog,
                                "SecchiViewModel, StateMachine, SecchiServiceState.WaitingForObservations, haveObservations: {haveObservations}, haveLocations: {haveLocations}.",
                                haveObservations,
                                haveLocations
                            );
                        }
                    }
                )
                // Permit the AppClosing trigger.
                .Permit(SecchiServiceTrigger.AppClosing, SecchiServiceState.AppClosing)
                // Once the observations table has been received, wait for the locations table to be received.
                .Permit(
                    SecchiServiceTrigger.LocationFeatureTableReceived,
                    SecchiServiceState.WaitingForLocations
                )
                .Permit(
                    SecchiServiceTrigger.ObservationAndLocationFeatureTablesReceived,
                    SecchiServiceState.HaveObservationsAndLocations
                );

            // Wait for a locations feature table message to be returned in response to the request for SecchiLocations.
            stateMachine
                .Configure(SecchiServiceState.WaitingForLocations)
                // Wait for a feature table message to be returned in response to the request for SecchiLocations.
                .OnEntryFrom(
                    locationsFeatureTableReceived,
                    featureTable =>
                    {
                        logger.LogDebug(
                            SecchiViewModelLog,
                            "SecchiViewModel, StateMachine, SecchiServiceState.WaitingForLocations, OnEntryFrom: FeatureTable: {featureTable}.",
                            featureTable.TableName
                        );

                        // Log the fields in the feature table.
                        foreach (var field in featureTable.Fields)
                        {
                            logger.LogTrace(
                                SecchiViewModelLog,
                                "SecchiViewModel, StateMachine, SecchiServiceState.WaitingForLocations, secchiLocationsChannel: {secchiLocationsChannel}: FeatureTable: {featureTable}, Field: {field.Name}, Field type: {field.type}.",
                                secchiLocationsChannel,
                                featureTable.TableName,
                                field.Name,
                                field.FieldType.ToString()
                            );
                        }

                        // create a where clause to get all the features
                        var queryParameters = new QueryParameters() { WhereClause = "1=1" };

                        // query the feature table
                        var queryResult = featureTable.QueryFeaturesAsync(queryParameters).Result;

                        // iterate over the features and log their attributes
                        foreach (var feature in queryResult)
                        {
                            foreach (var attribute in feature.Attributes)
                            {
                                logger.LogTrace(
                                    SecchiViewModelLog,
                                    "SecchiViewModel, StateMachine, SecchiServiceState.WaitingForLocations, secchiLocationsChannel: {secchiLocationsChannel}: FeatureTable: {featureTable}, attribute.Key: {attributeName}, attribute.Value: {attributeValue}.",
                                    secchiLocationsChannel,
                                    featureTable.TableName,
                                    attribute.Key,
                                    attribute.Value
                                );
                            }
                        }

                        // Locations have been received.
                        haveLocations = true;

                        // Create a geotrigger fence for each location by sending a geotrigger add message to the GeoTriggerService.
                        WeakReferenceMessenger.Default.Send<GeoTriggerAddMessage>(
                            new GeoTriggerAddMessage(
                                new GeoTriggerAdd(
                                    featureTable,
                                    "SecchiLocations2",
                                    secchiGeotriggerChannel,
                                    10
                                )
                            )
                        );

                        // If both observations and locations have been received, fire the ObservationAndLocationFeatureTablesReceived trigger.
                        // Otherwise, wait for observations to be received before moving to HaveObservationsAndLocations state.
                        if (haveObservations && haveLocations)
                        {
                            stateMachine.Fire(
                                SecchiServiceTrigger.ObservationAndLocationFeatureTablesReceived
                            );
                        }
                        else
                        {
                            logger.LogDebug(
                                SecchiViewModelLog,
                                "SecchiViewModel, StateMachine, SecchiServiceState.WaitingForLocations, haveObservations: {haveObservations}, haveLocations: {haveLocations}.",
                                haveObservations,
                                haveLocations
                            );
                        }
                    }
                )
                // Permit the AppClosing trigger.
                .Permit(SecchiServiceTrigger.AppClosing, SecchiServiceState.AppClosing)
                // Once the locations table has been received, permit waiting for the observations table to be received.
                .Permit(
                    SecchiServiceTrigger.ObservationFeatureTableReceived,
                    SecchiServiceState.WaitingForObservations
                )
                .Permit(
                    SecchiServiceTrigger.ObservationAndLocationFeatureTablesReceived,
                    SecchiServiceState.HaveObservationsAndLocations
                );

            // Observations and Locations have been received.
            // Make sure that the UI thread is available before moving to the Running state.
            stateMachine
                .Configure(SecchiServiceState.HaveObservationsAndLocations)
                .OnEntry(() =>
                {
                    // Log the HaveObservationsAndLocations state.
                    logger.LogDebug(
                        SecchiViewModelLog,
                        "SecchiViewModel, stateMachine (SecchiServiceState.HaveObservationsAndLocations): HaveObservationsAndLocations state entered."
                    );

                    // If the UI thread is available, then fire the UiThreadRecievedorPresent trigger.
                    if (uiDispatcherQueue is not null)
                    {
                        // Log that the UI thread is available.
                        logger.LogDebug(
                            SecchiViewModelLog,
                            "SecchiViewModel, stateMachine (SecchiServiceState.HaveObservationsAndLocations): uiDispatcherQueue is not null, firing SecchiServiceTrigger.UiThreadRecievedorPresent."
                        );

                        stateMachine.Fire(SecchiServiceTrigger.UiThreadRecievedorPresent);
                    }
                    else
                    {
                        logger.LogDebug(
                            SecchiViewModelLog,
                            "SecchiViewModel, stateMachine (SecchiServiceState.HaveObservationsAndLocations): uiDispatcherQueue is null, sending a message to request current value."
                        );

                        // Request the current UI Dispatcher Queue.
                        var UiQueueResponse =
                            WeakReferenceMessenger.Default.Send<UIQueueRequestMessage>(
                                new UIQueueRequestMessage()
                            );

                        Guard.Against.Null(
                            UiQueueResponse.Response.UIDispatcherQueue,
                            nameof(UiQueueResponse.Response.UIDispatcherQueue),
                            "SecchiViewModel, stateMachine (SecchiServiceState.HaveObservationsAndLocations): uiDispatcherQueue is null."
                        );

                        uiDispatcherQueue = UiQueueResponse.Response.UIDispatcherQueue;

                        // The UI thread is available, so fire the UiThreadRecievedorPresent trigger.
                        stateMachine.Fire(SecchiServiceTrigger.UiThreadRecievedorPresent);
                    }
                })
                // Permit the AppClosing trigger.
                .Permit(SecchiServiceTrigger.AppClosing, SecchiServiceState.AppClosing)
                .Permit(SecchiServiceTrigger.UiThreadRecievedorPresent, SecchiServiceState.Running);

            // If all tables have been received and the UI thread is available, then move to the Running state.
            stateMachine
                .Configure(SecchiServiceState.Running)
                .OnEntry(() =>
                {
                    // Log the Running state.
                    logger.LogDebug(
                        SecchiViewModelLog,
                        "SecchiViewModel, stateMachine (SecchiServiceState.Running): Running state entered."
                    );
                    // Send a GeodatabaseStateChangeMessage message to the GeoDatabaseService to change the state of the secchi observations geodatabase to BeginTransaction for secchiObservationsChannel.
                    WeakReferenceMessenger.Default.Send<GeodatabaseStateChangeMessage, uint>(
                        new GeodatabaseStateChangeMessage(
                            new GeodatabaseStateChange(GeoDbOperation.BeginTransaction)
                        ),
                        secchiObservationsChannel
                    );
                    StartMonitoringNetwork();
                })
                .InternalTransition(
                    observationsFeatureTableReceived,
                    (featureTable, _) =>
                    {
                        // Log the ObservationFeatureTableReceived trigger.
                        logger.LogDebug(
                            SecchiViewModelLog,
                            "SecchiViewModel, stateMachine (SecchiServiceState.Running): ObservationFeatureTableReceived trigger received."
                        );
                        DataCollectionUpdate(featureTable);
                    }
                )
                .InternalTransition(
                    SecchiServiceTrigger.InternetUnavailableRecieved,
                    _ =>
                    {
                        // Log the InternetUnavailableRecieved trigger.
                        logger.LogDebug(
                            SecchiViewModelLog,
                            "SecchiViewModel, stateMachine (SecchiServiceState.Running): InternetUnavailableRecieved trigger received."
                        );
                    }
                )
                .InternalTransition(
                    SecchiServiceTrigger.InternetAvailableRecieved,
                    _ =>
                    {
                        // Log the InternetAvailableRecieved trigger.
                        logger.LogDebug(
                            SecchiViewModelLog,
                            "SecchiViewModel, stateMachine (SecchiServiceState.Running): InternetAvailableRecieved trigger received."
                        );

                        /*

                        // Send a feature table request message to get the current feature table.
                        WeakReferenceMessenger.Default.Send<FeatureTableRequestMessage, uint>(
                            new FeatureTableRequestMessage("SecchiObservations"),
                            secchiObservationsChannel
                        );

                        */
                    }
                )
                // Permit the AppClosing trigger.
                .Permit(SecchiServiceTrigger.AppClosing, SecchiServiceState.AppClosing);

            // Handle the AppClosing trigger.
            stateMachine
                .Configure(SecchiServiceState.AppClosing)
                .OnEntry(() =>
                {
                    // Log the AppClosing trigger.
                    logger.LogDebug(
                        SecchiViewModelLog,
                        "SecchiViewModel, stateMachine (SecchiServiceState.AppClosing): AppClosing trigger received."
                    );
                    // Unregister all messages.
                    Shutdown();
                })
                .Ignore(SecchiServiceTrigger.InternetAvailableRecieved)
                .Ignore(SecchiServiceTrigger.InternetUnavailableRecieved);

            // Write unhandled trigger to log.
            stateMachine.OnUnhandledTrigger(
                (state, trigger) =>
                {
                    // Log to error.
                    logger.LogError(
                        SecchiViewModelLog,
                        "SecchiViewModel, stateMachine (OnUnhandledTrigger): Unhandled trigger {trigger} in state {state}.",
                        trigger,
                        state
                    );
                }
            );

            // Register to get secchi featuretable messages on the secchiObservationsChannel.
            WeakReferenceMessenger.Default.Register<FeatureTableMessage, uint>(
                this,
                secchiObservationsChannel,
                (recipient, message) =>
                {
                    logger.LogDebug(
                        SecchiViewModelLog,
                        "SecchiViewModel, FeatureTableMessage, secchiObservationsChannel: {secchiObservationsChannel}, FeatureTable: {featureTable}.",
                        secchiObservationsChannel,
                        message.Value.TableName
                    );
                    // Fire the FeatureTableReceived trigger.
                    stateMachine.Fire(observationsFeatureTableReceived, message.Value);
                }
            );

            // Register to get location featuretable messages on the secchiLocationsChannel.
            WeakReferenceMessenger.Default.Register<FeatureTableMessage, uint>(
                this,
                secchiLocationsChannel,
                (recipient, message) =>
                {
                    logger.LogDebug(
                        SecchiViewModelLog,
                        "SecchiViewModel, FeatureTableMessage, secchiLocationsChannel: {secchiLocationsChannel}, FeatureTable: {featureTable}.",
                        secchiLocationsChannel,
                        message.Value.TableName
                    );
                    // Fire the FeatureTableReceived trigger.
                    stateMachine.Fire(locationsFeatureTableReceived, message.Value);
                }
            );

            // Register to get MapPageUnloadedMessage messages.
            WeakReferenceMessenger.Default.Register<MapPageUnloaded>(
                this,
                (recipient, message) =>
                {
                    logger.LogDebug(
                        SecchiViewModelLog,
                        "SecchiViewModel, MapPageUnloaded: Firing the AppClosing trigger."
                    );

                    WeakReferenceMessenger.Default.UnregisterAll(this);
                    // Fire the AppClosing trigger.
                    stateMachine.Fire(SecchiServiceTrigger.AppClosing);
                }
            );

            /*

            // Submit a geodatabase request to the GeoDatabaseService to get SecchObservations.
            WeakReferenceMessenger.Default.Send<GeoDatabaseRequestMessage>(
                new GeoDatabaseRequestMessage(
                    new GeoDatabaseRetrieveRequest(
                        "SecchiObservations",
                        GeoDatabaseType.Observations,
                        secchiObservationsChannel,
                        "https://services2.arcgis.com/iq8zYa0SRsvIFFKz/arcgis/rest/services/SecchiObservations/FeatureServer",
                        false
                    )
                )
            );

            // Submit a geodatabase request to the GeoDatabaseService to get Locations.
            WeakReferenceMessenger.Default.Send<GeoDatabaseRequestMessage>(
                new GeoDatabaseRequestMessage(
                    new GeoDatabaseRetrieveRequest(
                        "SecchiLocations",
                        GeoDatabaseType.Locations,
                        secchiLocationsChannel,
                        "https://services2.arcgis.com/iq8zYa0SRsvIFFKz/arcgis/rest/services/SecchiLocations/FeatureServer",
                        false
                    )
                )
            );

            */
        }
        catch (Exception exception)
        {
            logger.LogError(
                SecchiViewModelLog,
                exception,
                "Exception generated in SecchiViewModel, Initialize(): {message}.",
                exception.Message.ToString()
            );
        }

        WeakReferenceMessenger.Default.Register<UIQueueSetMessage>(
            this,
            (recipient, message) =>
            {
                Guard.Against.Null(
                    message.Value.UIDispatcherQueue,
                    nameof(message.Value.UIDispatcherQueue),
                    "SecchiViewModel, Registering for UIQueueSetMessage: Subscription to uiDispatcherQueue is null."
                );
                uiDispatcherQueue = message.Value.UIDispatcherQueue;

                // Subscribe to geodatabase download progress messages and put those on the UI thread.
                SubscribeGeoDbDownload(uiDispatcherQueue);

                // Subscribe to GeoTriggerMessage.
                WeakReferenceMessenger.Default.Register<GeoTriggerMessage, uint>(
                    this,
                    secchiGeotriggerChannel,
                    (recipient, message) =>
                    {
                        HandleGeotriggerNotification(
                            message.Value.GeotriggerNotificationInfo,
                            uiDispatcherQueue
                        );
                    }
                );
            }
        );
    }

    private void DataCollectionUpdate(FeatureTable featureTable)
    {
        // Log the fields in the feature table.
        foreach (var field in featureTable.Fields)
        {
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, StateMachine, SecchiServiceState.WaitingForLocations, secchiLocationsChannel: {secchiLocationsChannel}: FeatureTable: {featureTable}, Field: {field.Name}, Field type: {field.type}.",
                secchiLocationsChannel,
                featureTable.TableName,
                field.Name,
                field.FieldType.ToString()
            );
        }

        // create a where clause to get all the features
        var queryParameters = new QueryParameters() { WhereClause = "1=1" };

        // query the feature table
        var queryResult = featureTable.QueryFeaturesAsync(queryParameters).Result;

        // iterate over the features and log their attributes
        foreach (var feature in queryResult)
        {
            foreach (var attribute in feature.Attributes)
            {
                logger.LogDebug(
                    SecchiViewModelLog,
                    "SecchiViewModel, StateMachine, SecchiServiceState.WaitingForLocations, secchiLocationsChannel: {secchiLocationsChannel}: FeatureTable: {featureTable}, attribute.Key: {attributeName}, attribute.Value: {attributeValue}.",
                    secchiLocationsChannel,
                    featureTable.TableName,
                    attribute.Key,
                    attribute.Value
                );
            }
        }
    }

    // Get the current network status and use that to trigger InternetAvailableRecieved and InternetUnavailableRecieved.
    private async void StartMonitoringNetwork()
    {
        // Send a NetworkStatusRequestMessage to get the current network status and use that to trigger InternetAvailableRecieved and InternetUnavailableRecieved.
        var networkStatus =
            await WeakReferenceMessenger.Default.Send<NetworkStatusRequestMessage>();
        logger.LogDebug(
            SecchiViewModelLog,
            "SecchiViewModel, NetworkStatusRequestMessage: IsInternetAvailable: {isInternetAvailable}.",
            networkStatus.IsInternetAvailable
        );
        if (networkStatus.IsInternetAvailable)
        {
            stateMachine.Fire(SecchiServiceTrigger.InternetAvailableRecieved);
        }
        else
        {
            stateMachine.Fire(SecchiServiceTrigger.InternetUnavailableRecieved);
        }

        // Register to get NetworkChangedMessage messages.
        WeakReferenceMessenger.Default.Register<NetworkChangedMessage>(
            this,
            (recipient, message) =>
            {
                var networkStatus = message.Value;

                logger.LogDebug(
                    SecchiViewModelLog,
                    "NetworkChangedMessage IsInternetAvailable: {isInternetAvailable}",
                    networkStatus.IsInternetAvailable
                );
                if (networkStatus.IsInternetAvailable)
                {
                    stateMachine.Fire(SecchiServiceTrigger.InternetAvailableRecieved);
                }
                else
                {
                    stateMachine.Fire(SecchiServiceTrigger.InternetUnavailableRecieved);
                }
            }
        );
    }

    // Log state transitions.
    private void OnTransition(
        StateMachine<SecchiServiceState, SecchiServiceTrigger>.Transition transition
    )
    {
        logger.LogDebug(
            SecchiViewModelLog,
            "SecchiViewModel, OnTransition: Transitioned from {transition.Source} to {transition.Destination} via {transition.Trigger}.",
            transition.Source,
            transition.Destination,
            transition.Trigger
        );
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
                        "SecchiViewModel, SubscribeGeoDbDownload(DispatcherQueue uiDispatcherQueue): uiDispatcherQueue can not be null."
                    );
                    uiDispatcherQueue.TryEnqueue(() =>
                    {
                        // GeodatabasePercentDownloaded = message.NewValue.PercentDownloaded;

                        if (message.NewValue.PercentDownloaded == 100)
                        {
                            // ShowGeodatabaseDownloadProgress = false;
                        }
                        else
                        {
                            // ShowGeodatabaseDownloadProgress = true;
                        }

                        logger.LogDebug(
                            SecchiViewModelLog,
                            "SecchiViewModel, PropertyChangedMessage, GeoDatabaseDownloadProgress: Geodatabase percent downloaded: {percentDownloaded}, Thread: {thread}",
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
                SecchiViewModelLog,
                exception,
                "Exception generated in SecchiViewModel, SubscribeGeoDbDownload(). {exception}",
                exception.Message.ToString()
            );
        }
    }

    private void HandleGeotriggerNotification(
        GeotriggerNotificationInfo info,
        DispatcherQueue uiDispatcherQueue
    )
    {
        // Log to debug the type of notification.
        logger.LogDebug(
            SecchiViewModelLog,
            "SecchiViewModel, HandleGeotriggerNotification, GeotriggerNotification: {notificationType} received.",
            info.GeotriggerMonitor.ToString()
        );

        if (info is FenceGeotriggerNotificationInfo fenceInfo)
        {
            try
            {
                if (uiDispatcherQueue is not null)
                {
                    switch (fenceInfo.FenceNotificationType)
                    {
                        case FenceNotificationType.Entered:
                            logger.LogInformation(
                                SecchiViewModelLog,
                                "SecchiViewModel, HandleGeotriggerNotification, FenceNotification: Entered. {fenceInfo}",
                                fenceInfo.Message
                            );
                            uiDispatcherQueue.TryEnqueue(() =>
                            {
                                MapBorderColor = new SolidColorBrush(Colors.SeaGreen);
                            });
                            break;
                        case FenceNotificationType.Exited:
                            logger.LogInformation(
                                SecchiViewModelLog,
                                "SecchiViewModel, HandleGeotriggerNotification, FenceNotification: Exited. {fenceInfo}",
                                fenceInfo.Message
                            );
                            uiDispatcherQueue.TryEnqueue(() =>
                            {
                                MapBorderColor = new SolidColorBrush(Colors.Transparent);
                            });
                            break;
                        default:
                            logger.LogInformation(
                                SecchiViewModelLog,
                                "SecchiViewModel, HandleGeotriggerNotification, FenceNotification: Unknown. {fenceInfo}",
                                fenceInfo.Message
                            );
                            break;
                    }
                    if (fenceInfo.FenceGeoElement is null)
                    {
                        logger.LogError(
                            SecchiViewModelLog,
                            "SecchiViewModel, HandleGeotriggerNotification: fenceInfo.FenceGeoElement is null."
                        );
                        throw new Exception("fenceInfo.FenceGeoElement is null.");
                    }
                    feature = fenceInfo.FenceGeoElement as ArcGISFeature;
                    if (feature is not null)
                    {
                        if (fenceInfo.FenceNotificationType == FenceNotificationType.Entered)
                        {
                            logger.LogDebug(
                                SecchiViewModelLog,
                                "SecchiViewModel, HandleGeotriggerNotification: ArcGIS Feature Geometry: {featureGeometry}",
                                feature.Geometry?.ToString()
                            );

                            var locationId = feature.Attributes["LocationId"];
                            var locationName = feature.Attributes["Location"];
                            if (locationId is not null && locationName is not null)
                            {
                                logger.LogDebug(
                                    SecchiViewModelLog,
                                    "SecchiViewModel, HandleGeotriggerNotification: FenceNotification: Entered. {locationId}, {locationName}",
                                    locationId,
                                    locationName
                                );
                                uiDispatcherQueue.TryEnqueue(() =>
                                {
                                    ShowSecchiCollectionPoint = true;
                                    SecchiCollectionPointName = locationName.ToString()!;
                                });
                            }
                        }
                        if (fenceInfo.FenceNotificationType == FenceNotificationType.Exited)
                        {
                            logger.LogDebug(
                                SecchiViewModelLog,
                                "SecchiViewModel, HandleGeotriggerNotification: ArcGIS Feature Geometry: {featureGeometry}",
                                feature.Geometry?.ToString()
                            );

                            var locationId = feature.Attributes["LocationId"];
                            var locationName = feature.Attributes["Location"];
                            if (locationId is not null && locationName is not null)
                            {
                                logger.LogDebug(
                                    SecchiViewModelLog,
                                    "SecchiViewModel, HandleGeotriggerNotification: FenceNotification: Exited. {locationId}, {locationName}",
                                    locationId,
                                    locationName
                                );
                            }
                        }
                    }
                }
                else
                {
                    logger.LogError(
                        SecchiViewModelLog,
                        "SecchiViewModel, HandleGeotriggerNotification: Unable to dispatch to the UI, uiDispatcherQueue is null."
                    );
                    throw new ArgumentNullException(
                        paramName: nameof(uiDispatcherQueue),
                        message: "SecchiViewModel, HandleGeotriggerNotification: uiDispatcherQueue can not be null."
                    );
                }
            }
            catch (Exception exception)
            {
                logger.LogError(
                    SecchiViewModelLog,
                    exception,
                    "Exception generated in SecchiViewModel, HandleGeotriggerNotification: {message}.",
                    exception.Message.ToString()
                );
            }
        }
    }

    public void ProcessSecchiMeasurements(SecchiMeasurements secchiMeasurements)
    {
        try
        {
            Guard.Against.Null(
                secchiMeasurements,
                nameof(secchiMeasurements),
                "SecchiViewModel, ProcessSecchiMeasurements: secchiMeasurements can not be null."
            );

            // Once the locations have been collected, hide the secchi collection point.
            ShowSecchiCollectionPoint = true;

            // Log to debug the type of notification.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, ProcessSecchiMeasurements: SecchiSaveButton clicked."
            );

            // Log to debug sender the contents of secchiMeasurements.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, ProcessSecchiMeasurements: SecchiMeasurements: {secchiMeasurements}.",
                secchiMeasurements.ToString()
            );

            Guard.Against.Null(
                currentObservationsTable,
                nameof(currentObservationsTable),
                "SecchiViewModel, ProcessSecchiMeasurements: currentObservationsTable can not be null."
            );

            // Log to debug the name of the current feature table.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, ProcessSecchiMeasurements: CurrentFeatureTable: {currentObservationsTable}.",
                currentObservationsTable.TableName
            );

            var secchiObservation = currentObservationsTable.CreateFeature();
            secchiObservation.SetAttributeValue("measurement1", secchiMeasurements.Measurement1);
            secchiObservation.SetAttributeValue("measurement2", secchiMeasurements.Measurement2);
            secchiObservation.SetAttributeValue("measurement3", secchiMeasurements.Measurement3);

            // Calculate the secchi value by averaging the three measurements.
            var secchiValue = Math.Round(
                (double)(
                    secchiMeasurements.Measurement1
                    + secchiMeasurements.Measurement2
                    + secchiMeasurements.Measurement3
                ) / 3
            );
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, ProcessSecchiMeasurements: Secchi value rounded (double): {secchiValue}.",
                secchiValue
            );
            secchiObservation.SetAttributeValue("secchi", secchiValue);

            // TODO: Configure a state machine to make sure that everything is in the correct state before committing the transaction.
            // secchiObservation.SetAttributeValue("locationId", feature.Attributes["location_id"]);
            // For testing, set the locationId to 55.
            secchiObservation.SetAttributeValue("locationId", 55);

            // Get the current time and assign that to the secchiObservation.
            secchiObservation.SetAttributeValue("dateCollected", DateTime.UtcNow);

            // Get the current latitude and longitude and assign that to the secchiObservation.
            secchiObservation.SetAttributeValue(
                "CollectedLongitude",
                secchiMeasurements.Location.X
            );
            secchiObservation.SetAttributeValue("CollectedLatitude", secchiMeasurements.Location.Y);

            // Log to debug the secchiObservation attributes.
            foreach (var attribute in secchiObservation.Attributes)
            {
                logger.LogTrace(
                    SecchiViewModelLog,
                    "SecchiViewModel, ProcessSecchiMeasurements: Attribute: {attributeName}, Value: {attributeValue}",
                    attribute.Key,
                    attribute.Value
                );
            }

            // Send the feature via an AddFeatureMessage to the GeoDatabaseService.
            WeakReferenceMessenger.Default.Send<AddFeatureMessage, uint>(
                new AddFeatureMessage(new FeatureMessage("SecchiObservations", secchiObservation)),
                secchiObservationsChannel
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SecchiViewModelLog,
                exception,
                "Exception generated in SecchiViewModel, ProcessSecchiMeasurements: {message}.",
                exception.Message.ToString()
            );
        }
    }

    public void SecchiNavView_ItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args
    )
    {
        // Log to debug that the MapNavView_ItemInvoked event was fired.
        logger.LogDebug(
            SecchiViewModelLog,
            "SecchiViewModel, CollectionNavView_ItemInvoked(): CollectionNavView_ItemInvoked event fired."
        );

        // Log the name of the invoked item.
        logger.LogDebug(
            SecchiViewModelLog,
            "SecchiViewModel, CollectionNavView_ItemInvoked(): Invoked item name: {invokedItemName}.",
            args.InvokedItemContainer.Name
        );

        // Log the sender name.
        logger.LogDebug(
            SecchiViewModelLog,
            "SecchiViewModel, CollectionNavView_ItemInvoked(): Sender name: {senderName}.",
            sender.Name
        );

        switch (args.InvokedItemContainer.Name)
        {
            case "SecchiNavAdd":
                // Log that upload was selected.
                logger.LogDebug(
                    SecchiViewModelLog,
                    "SecchiViewModel, CollectionNavView_ItemInvoked(): Add selected."
                );
                SelectView = "SecchiDataEntry";
                break;
            case "SecchiNavCollected":
                // Log that upload was selected.
                logger.LogDebug(
                    SecchiViewModelLog,
                    "SecchiViewModel, CollectionNavView_ItemInvoked(): Collected selected."
                );
                SelectView = "SecchiCollectionTable";
                break;
            case "SecchiNavDiscard":
                // Log that discard was selected.
                logger.LogDebug(
                    SecchiViewModelLog,
                    "SecchiViewModel, CollectionNavView_ItemInvoked(): Discard item selected."
                );
                break;
            case "SecchiNavUpload":
                // Log that upload was selected.
                logger.LogDebug(
                    SecchiViewModelLog,
                    "SecchiViewModel, CollectionNavView_ItemInvoked(): Upload selected."
                );
                break;
            case "SecchiNavInfo":
                // Log that discard was selected.
                logger.LogDebug(
                    SecchiViewModelLog,
                    "SecchiViewModel, CollectionNavView_ItemInvoked(): Info item selected."
                );
                break;


            case "SettingsItem":
                // Log that settings was selected.
                logger.LogDebug(
                    SecchiViewModelLog,
                    "SecchiViewModel, CollectionNavView_ItemInvoked(): Settings selected."
                );
                SelectView = "SecchiSettings";
                break;
            default:
                break;
        }
    }

    private void Shutdown()
    {
        // Unregister all messages.
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
