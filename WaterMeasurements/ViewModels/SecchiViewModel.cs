using System.Collections.ObjectModel;
using System.Drawing;
using System.Numerics;
using System.Xml.Linq;
using Ardalis.GuardClauses;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geotriggers;
using Esri.ArcGISRuntime.Location;
using Esri.ArcGISRuntime.UI;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Newtonsoft.Json.Linq;
using Stateless;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Models;
using WaterMeasurements.Services;
using WaterMeasurements.Services.IncrementalLoaders;
using WaterMeasurements.Views;
using WinUIEx.Messaging;
using static WaterMeasurements.Models.SecchiConfiguration;
using static WaterMeasurements.ViewModels.MainViewModel;

namespace WaterMeasurements.ViewModels;

// Message from other modules to request the Secchi channel numbers.
public class SecchiChannelRequestMessage : RequestMessage<SecchiChannelNumbersMessage> { }

// Message to set the value of SecchiSelectView.
public class SetSecchiSelectViewMessage(string value) : ValueChangedMessage<string>(value) { }

public partial class SecchiViewModel : ObservableRecipient
{
    private readonly ILogger<SecchiViewModel> logger;

    // Set the EventId for logging messages.
    internal EventId SecchiViewModelLog = new(7, "SecchiViewModel");

    private readonly ILocalSettingsService? LocalSettingsService;

    [ObservableProperty]
    private string secchiCollectionPointName = "Default Location";

    [ObservableProperty]
    private string locationDisplay = "CurrentLocations";

    // Observable property for the map border.
    [ObservableProperty]
    private Brush mapBorderColor = new SolidColorBrush(Colors.Transparent);

    // Feature for the current location sent by the GeoTriggerService.
    public ArcGISFeature? feature;

    // DispatcherQueue for the UI thread.
    private DispatcherQueue? uiDispatcherQueue;

    // Current observations feature table set by the FeatureTableMessage.
    private FeatureTable? currentObservationsTable = null;

    // Current locations feature table set by the FeatureTableMessage.
    private FeatureTable? currentLocationsTable = null;

    private readonly uint secchiObservationsChannel = 0;
    private readonly uint secchiLocationsChannel = 0;
    private readonly uint secchiGeotriggerChannel = 0;

    private double? geoTriggerDistance = 0;
    private string? observationsURL = string.Empty;
    private string? locationsURL = string.Empty;

    // TODO: add the following to the configuration file.

    // -------------------- Set one or both of the following to true to cause download --------------------

    private readonly bool refreshObservations = false;
    private readonly bool refreshLocations = false;

    // -------------------- Set one or both of above to true to cause download ----------------------------

    private bool haveObservations;
    private bool haveLocations;

    private bool haveLocationsTable = false;
    private bool haveObservationsTable = false;

    // SemaphoreSlim to for returning the next location id.
    private readonly SemaphoreSlim locationIdSemaphore = new(1, 1);

    // The location name for the collection point is provided via geotrigger.
    // Instead of being a global variable, it could be retrieved from the feature table
    // but doing so would require a query to the feature table each time the location name is needed.
    private string? geotriggerLocationName = string.Empty;

    private GraphicsOverlay secchiLocationsOverlay = new() { Id = "SecchiLocations" };

    public GraphicsOverlay SecchiLocationsOverlay
    {
        get => secchiLocationsOverlay;
        set => SetProperty(ref secchiLocationsOverlay, value);
    }

    private SecchiLocationCollectionLoader? secchiLocations;

    public SecchiLocationCollectionLoader SecchiLocations
    {
        get => secchiLocations!;
        set => SetProperty(ref secchiLocations, value);
    }

    private SecchiObservationCollectionLoader? secchiObservations;

    public SecchiObservationCollectionLoader SecchiObservations
    {
        get => secchiObservations!;
        set => SetProperty(ref secchiObservations, value);
    }

    private readonly StateMachine<SecchiServiceState, SecchiServiceTrigger> stateMachine;

    private readonly ISqliteService? sqliteService;

    public SecchiViewModel(
        ILogger<SecchiViewModel> logger,
        ILocalSettingsService? localSettingsService,
        ISqliteService? sqliteService
    )
    {
        this.logger = logger;
        logger.LogDebug(SecchiViewModelLog, "SecchiViewModel, Constructor.");

        LocalSettingsService = localSettingsService;

        this.sqliteService = sqliteService;

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

        // Now that the channels have been set, register to get the SecchiChannelRequestMessage.
        // Register to get SecchiChannelRequestMessage, return the Secchi channel numbers.
        WeakReferenceMessenger.Default.Register<SecchiChannelRequestMessage>(
            this,
            (recipient, message) =>
            {
                logger.LogDebug(
                    SecchiViewModelLog,
                    "SecchiViewModel, SecchiChannelRequestMessage: SecchiChannelRequestMessage received."
                );

                // Return the Secchi channel numbers.
                message.Reply(
                    new SecchiChannelNumbersMessage(
                        secchiObservationsChannel,
                        secchiLocationsChannel,
                        secchiGeotriggerChannel
                    )
                );
            }
        );

        // Create the state machine.
        stateMachine = new StateMachine<SecchiServiceState, SecchiServiceTrigger>(
            SecchiServiceState.WaitingForObservations
        );

        var isConfigured = CheckConfiguration();
        if (isConfigured)
        {
            // Log that the SecchiViewModel is configured.
            logger.LogDebug(SecchiViewModelLog, "SecchiViewModel, Constructor: Configured.");
            // Log the value of observationsURL.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, Constructor: ObservationsURL: {observationsURL}.",
                observationsURL
            );
            // Log the value of locationsURL.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, Constructor: LocationsURL: {locationsURL}.",
                locationsURL
            );
            // Log the value of geoTriggerDistance.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, Constructor: GeoTriggerDistance: {geoTriggerDistance}.",
                geoTriggerDistance
            );

            SecchiLocations = [];
            SecchiObservations = [];

            Initialize();
            StartMonitoringSqlite();
        }
        else
        {
            logger.LogDebug(SecchiViewModelLog, "SecchiViewModel, Constructor: Not configured.");
        }
    }

    private void Initialize()
    {
        try
        {
            Guard.Against.Null(
                sqliteService,
                nameof(sqliteService),
                "SecchiViewModel, Initialize(): sqliteService can not be null."
            );

            // Trigger for location feature table received with feature table as a parameter.
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

                        /*
                        // Send the FeatureToTableMessage to the SqliteService to convert the feature table to a table.
                        WeakReferenceMessenger.Default.Send<FeatureToTableMessage>(
                            new FeatureToTableMessage(
                                new FeatureToTable(featureTable, DbType.SecchiObservations)
                            )
                        );
                        */

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
                )
                .PermitReentry(SecchiServiceTrigger.ObservationFeatureTableReceived);

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

                        currentLocationsTable = featureTable;

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

                        // Iterate over the features and create a graphic for each feature.
                        // This is used by the map to display the collection locations (MainPage.xaml.cs).
                        foreach (var feature in queryResult)
                        {
                            // Create a new graphic using the feature's geometry and the collection location symbol
                            var graphic = new Graphic(
                                feature.Geometry,
                                MapSymbols.CollectionLocationSymbol
                            );

                            // Add the LocationId from the feature's attributes to the graphic's attributes
                            graphic.Attributes.Add("LocationId", feature.Attributes["LocationId"]);

                            // Add the graphic to the graphics overlay
                            secchiLocationsOverlay.Graphics.Add(graphic);
                        }

                        // Send the FeatureToTableMessage to the SqliteService to convert the feature table to a table.
                        WeakReferenceMessenger.Default.Send<FeatureToTableMessage>(
                            new FeatureToTableMessage(
                                new FeatureToTable(featureTable, DbType.SecchiLocations)
                            )
                        );

                        // Locations have been received.
                        haveLocations = true;

                        Guard.Against.Null(
                            geoTriggerDistance,
                            nameof(geoTriggerDistance),
                            "SecchiViewModel, StateMachine, SecchiServiceState.WaitingForLocations: geoTriggerDistance can not be null."
                        );

                        // Create a geotrigger fence for each location by sending a geotrigger add message to the GeoTriggerService.
                        WeakReferenceMessenger.Default.Send<GeoTriggerAddMessage>(
                            new GeoTriggerAddMessage(
                                new GeoTriggerAdd(
                                    featureTable,
                                    "SecchiGeotrigger",
                                    secchiGeotriggerChannel,
                                    (double)geoTriggerDistance
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
                )
                .PermitReentry(SecchiServiceTrigger.LocationFeatureTableReceived);

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

                    // Observations and locations have been received, so set the SecchiSelectView to the SecchiCollectionTable.
                    WeakReferenceMessenger.Default.Send<SetSecchiSelectViewMessage>(
                        new SetSecchiSelectViewMessage("SecchiCollectionTable")
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

            // Register to get changed feature messages on the secchiLocationsChannel.
            WeakReferenceMessenger.Default.Register<ChangedFeatureMessage, uint>(
                this,
                secchiLocationsChannel,
                (recipient, message) =>
                {
                    logger.LogDebug(
                        SecchiViewModelLog,
                        "SecchiViewModel, ChangedFeatureMessage, secchiLocationsChannel: {secchiLocationsChannel}, FeatureChangedMessage: {featureChangedMessage}.",
                        secchiLocationsChannel,
                        message.Value
                    );

                    if (message.Value.FeatureTableAction == FeatureTableAction.Added)
                    {
                        // Log that a feature has been added.
                        logger.LogDebug(
                            SecchiViewModelLog,
                            "SecchiViewModel, ChangedFeatureMessage, Added, secchiLocationsChannel: {secchiLocationsChannel}, FeatureChangedMessage: {featureChangedMessage}.",
                            secchiLocationsChannel,
                            message.Value
                        );

                        // Log the fields in the feature table.
                        foreach (var attribute in message.Value.FeatureChanged.Attributes)
                        {
                            logger.LogTrace(
                                SecchiViewModelLog,
                                "SecchiViewModel, ChangedFeatureMessage, {name}: attribute.Key: {attributeName}, attribute.Value: {attributeValue}.",
                                message.Value.FeatureTable,
                                attribute.Key,
                                attribute.Value
                            );
                        }
                    }
                    else if (message.Value.FeatureTableAction == FeatureTableAction.Deleted)
                    {
                        // Log that a feature has been deleted.
                        logger.LogDebug(
                            SecchiViewModelLog,
                            "SecchiViewModel, ChangedFeatureMessage, Deleted, secchiLocationsChannel: {secchiLocationsChannel}, FeatureChangedMessage: {featureChangedMessage}.",
                            secchiLocationsChannel,
                            message.Value
                        );
                    }
                    else if (message.Value.FeatureTableAction == FeatureTableAction.Updated)
                    {
                        // Log that a feature has been updated.
                        logger.LogDebug(
                            SecchiViewModelLog,
                            "SecchiViewModel, ChangedFeatureMessage, Updated, secchiLocationsChannel: {secchiLocationsChannel}, FeatureChangedMessage: {featureChangedMessage}.",
                            secchiLocationsChannel,
                            message.Value
                        );
                    }
                }
            );

            // Register to get changed feature messages on the secchiObservationsChannel.
            WeakReferenceMessenger.Default.Register<ChangedFeatureMessage, uint>(
                this,
                secchiObservationsChannel,
                (recipient, message) =>
                {
                    logger.LogDebug(
                        SecchiViewModelLog,
                        "SecchiViewModel, ChangedFeatureMessage, secchiObservationsChannel: {secchiObservationsChannel}, FeatureChangedMessage: {featureChangedMessage}.",
                        secchiObservationsChannel,
                        message.Value
                    );

                    if (message.Value.FeatureTableAction == FeatureTableAction.Added)
                    {
                        // Log that a feature has been added.
                        logger.LogDebug(
                            SecchiViewModelLog,
                            "SecchiViewModel, ChangedFeatureMessage, Added, secchiObservationsChannel: {secchiObservationsChannel}, FeatureChangedMessage: {featureChangedMessage}.",
                            secchiObservationsChannel,
                            message.Value
                        );

                        // Log the fields in the feature table.
                        foreach (var attribute in message.Value.FeatureChanged.Attributes)
                        {
                            logger.LogTrace(
                                SecchiViewModelLog,
                                "SecchiViewModel, ChangedFeatureMessage, {name}: attribute.Key: {attributeName}, attribute.Value: {attributeValue}.",
                                message.Value.FeatureTable,
                                attribute.Key,
                                attribute.Value
                            );
                        }

                        // Add the new observation to the SecchiObservations collection.
                        // This updates the list of observations in the UI.
                        SecchiObservations.Add(
                            new SecchiCollectionDisplay(
                                geotriggerLocationName!,
                                (double)
                                    message.Value.FeatureChanged.Attributes["CollectedLatitude"]!,
                                (double)
                                    message.Value.FeatureChanged.Attributes["CollectedLongitude"]!,
                                (int)message.Value.FeatureChanged.Attributes["LocationId"]!,
                                (int)message.Value.FeatureChanged.Attributes["Measurement1"]!,
                                (int)message.Value.FeatureChanged.Attributes["Measurement2"]!,
                                (int)message.Value.FeatureChanged.Attributes["Measurement3"]!,
                                (double)message.Value.FeatureChanged.Attributes["Secchi"]!,
                                (DateTimeOffset)
                                    message.Value.FeatureChanged.Attributes["DateCollected"]!,
                                (long)message.Value.FeatureChanged.Attributes["OBJECTID"]!
                            )
                        );
                    }
                    else if (message.Value.FeatureTableAction == FeatureTableAction.Deleted)
                    {
                        // Log that a feature has been deleted.
                        logger.LogDebug(
                            SecchiViewModelLog,
                            "SecchiViewModel, ChangedFeatureMessage, Deleted, secchiObservationsChannel: {secchiObservationsChannel}, FeatureChangedMessage: {featureChangedMessage}.",
                            secchiObservationsChannel,
                            message.Value
                        );

                        var objectId = (long)message.Value.FeatureChanged.Attributes["OBJECTID"]!;

                        // Remove the observation from the SecchiObservations collection.
                        // This updates the list of observations in the UI.
                        SecchiObservations.Remove(
                            SecchiObservations.FirstOrDefault(x => x.ObjectId == objectId)!
                        );
                    }
                    else if (message.Value.FeatureTableAction == FeatureTableAction.Updated)
                    {
                        // Log that a feature has been updated.
                        logger.LogDebug(
                            SecchiViewModelLog,
                            "SecchiViewModel, ChangedFeatureMessage, Updated, secchiObservationsChannel: {secchiObservationsChannel}, FeatureChangedMessage: {featureChangedMessage}.",
                            secchiObservationsChannel,
                            message.Value
                        );
                    }
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

            Guard.Against.Null(
                observationsURL,
                nameof(observationsURL),
                "SecchiViewModel, Initialize(): observationsURL can not be null."
            );

            // Submit a geodatabase request to the GeoDatabaseService to get SecchObservations.
            WeakReferenceMessenger.Default.Send<GeoDatabaseRequestMessage>(
                new GeoDatabaseRequestMessage(
                    new GeoDatabaseRetrieveRequest(
                        "SecchiObservations",
                        GeoDatabaseType.Observations,
                        secchiObservationsChannel,
                        observationsURL,
                        refreshObservations
                    )
                )
            );

            Guard.Against.Null(
                locationsURL,
                nameof(locationsURL),
                "SecchiViewModel, Initialize(): locationsURL can not be null."
            );

            // Submit a geodatabase request to the GeoDatabaseService to get Locations.
            WeakReferenceMessenger.Default.Send<GeoDatabaseRequestMessage>(
                new GeoDatabaseRequestMessage(
                    new GeoDatabaseRetrieveRequest(
                        "SecchiLocations",
                        GeoDatabaseType.Locations,
                        secchiLocationsChannel,
                        locationsURL,
                        refreshLocations
                    )
                )
            );
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

    // Monitor messages generated by the SqliteService.
    private void StartMonitoringSqlite()
    {
        WeakReferenceMessenger.Default.Register<FeatureToTableResultMessage>(
            this,
            (recipient, message) =>
            {
                logger.LogDebug(
                    SecchiViewModelLog,
                    "SecchiViewModel, FeatureToTableResultMessage: FeatureToTableResult: {featureToTableResult}.",
                    message.Value
                );
            }
        );

        WeakReferenceMessenger.Default.Register<TableAvailableMessage>(
            this,
            (recipient, message) =>
            {
                logger.LogDebug(
                    SecchiViewModelLog,
                    "SecchiViewModel, TableAvailableMessage: TableAvailable: {tableAvailable}.",
                    message.Value.ToString()
                );
                WaitForObservationsAndLocations(message.Value);
            }
        );
    }

    private void WaitForObservationsAndLocations(DbType dbType)
    {
        try
        {
            if (dbType == DbType.SecchiLocations)
            {
                haveLocationsTable = true;
                // Log that the SecchiViewModel has locations.
                logger.LogDebug(
                    SecchiViewModelLog,
                    "SecchiViewModel, WaitForObservationsAndLocations: SecchiViewModel has locations."
                );
            }
            else if (dbType == DbType.SecchiObservations)
            {
                haveObservationsTable = true;
                // Log that the SecchiViewModel has observations.
                logger.LogDebug(
                    SecchiViewModelLog,
                    "SecchiViewModel, WaitForObservationsAndLocations: SecchiViewModel has observations."
                );
            }

            if (haveLocationsTable && haveObservationsTable)
            {
                // Log that the SecchiViewModel has both locations and observations.
                logger.LogDebug(
                    SecchiViewModelLog,
                    "SecchiViewModel, WaitForObservationsAndLocations: SecchiViewModel has both locations and observations."
                );
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                SecchiViewModelLog,
                exception,
                "Exception generated in SecchiViewModel, WaitForObservationsAndLocations(DbType dbType): {message}.",
                exception.Message.ToString()
            );
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
                                MapBorderColor = (SolidColorBrush)
                                    Application.Current.Resources["AccentFillColorDefaultBrush"];
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
                            var locationName = feature.Attributes["LocationName"];
                            if (locationId is not null && locationName is not null)
                            {
                                logger.LogDebug(
                                    SecchiViewModelLog,
                                    "SecchiViewModel, HandleGeotriggerNotification: FenceNotification: Entered. {locationId}, {locationName}",
                                    locationId,
                                    locationName
                                );

                                // Send a SetSecchiSelectViewMessage with the value of "SecchiDataEntry".
                                WeakReferenceMessenger.Default.Send<SetSecchiSelectViewMessage>(
                                    new SetSecchiSelectViewMessage("SecchiDataEntry")
                                );

                                uiDispatcherQueue.TryEnqueue(() =>
                                {
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
                            var locationName = feature.Attributes["LocationName"];
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

    public void ProcessSecchiMeasurements(SecchiMeasurement secchiMeasurements)
    {
        int locationId;
        // string? locationName;

        try
        {
            Guard.Against.Null(
                secchiMeasurements,
                nameof(secchiMeasurements),
                "SecchiViewModel, ProcessSecchiMeasurements: secchiMeasurements can not be null."
            );

            // Make sure that the feature from the geotrigger notification is not null.
            Guard.Against.Null(
                feature,
                nameof(feature),
                "SecchiViewModel, ProcessSecchiMeasurements: feature can not be null."
            );

            Guard.Against.Null(
                feature.Attributes["LocationId"],
                nameof(feature),
                "SecchiViewModel, ProcessSecchiMeasurements: feature.Attributes[LocationId] can not be null."
            );

            locationId = (int)feature.Attributes["LocationId"]!;

            Guard.Against.NegativeOrZero(
                locationId,
                nameof(locationId),
                "SecchiViewModel, ProcessSecchiMeasurements: locationId can not be negative or zero."
            );

            Guard.Against.NullOrEmpty(
                feature.Attributes["LocationName"]!.ToString(),
                nameof(feature),
                "SecchiViewModel, ProcessSecchiMeasurements: feature.Attributes[Location] can not be null or empty."
            );

            geotriggerLocationName = feature.Attributes["LocationName"]!.ToString();

            // Once the location have been collected, move to the results panel.
            // Send a SetSecchiSelectViewMessage with the value of "SecchiCollectionTable".
            WeakReferenceMessenger.Default.Send<SetSecchiSelectViewMessage>(
                new SetSecchiSelectViewMessage("SecchiCollectionTable")
            );

            // Log to debug the type of notification.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, ProcessSecchiMeasurements: SecchiSaveButton clicked."
            );

            // Log to debug sender the contents of secchiMeasurements.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, ProcessSecchiMeasurements: SecchiMeasurement: {secchiMeasurements}.",
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

            var secchiObservation = (ArcGISFeature)currentObservationsTable.CreateFeature();

            secchiObservation.Geometry = secchiMeasurements.Location;

            secchiObservation.SetAttributeValue("Measurement1", secchiMeasurements.Measurement1);
            secchiObservation.SetAttributeValue("Measurement2", secchiMeasurements.Measurement2);
            secchiObservation.SetAttributeValue("Measurement3", secchiMeasurements.Measurement3);

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
            secchiObservation.SetAttributeValue("Secchi", secchiValue);

            secchiObservation.SetAttributeValue("LocationId", locationId);

            // Get the current time and assign that to the secchiObservation.
            secchiObservation.SetAttributeValue("DateCollected", DateTime.UtcNow);

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
                new AddFeatureMessage(
                    new FeatureAddMessage("SecchiObservations", "OBJECTID", secchiObservation)
                ),
                secchiObservationsChannel
            );

            /*
            // Add the new observation record to Sqlite.
            WeakReferenceMessenger.Default.Send<AddSecchiObservationMessage>(
                new AddSecchiObservationMessage(
                    new SecchiObservation(
                        secchiMeasurements.Measurement1,
                        secchiMeasurements.Measurement2,
                        secchiMeasurements.Measurement3,
                        secchiValue,
                        locationId,
                        DateTime.UtcNow,
                        secchiMeasurements.Location.Y,
                        secchiMeasurements.Location.X
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
                "Exception generated in SecchiViewModel, ProcessSecchiMeasurements: {message}.",
                exception.Message.ToString()
            );
        }
    }

    public void DeleteObservation(long objectId)
    {
        try
        {
            Guard.Against.Null(
                SecchiObservations,
                nameof(SecchiObservations),
                "SecchiViewModel, DeleteObservation(): SecchiObservations can not be null."
            );

            Guard.Against.Null(
                currentObservationsTable,
                nameof(currentObservationsTable),
                "SecchiViewModel, DeleteObservation(): currentObservationsTable can not be null."
            );

            // Log to debug the objectId.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, DeleteObservation(): ObjectId: {objectId}.",
                objectId
            );

            // Create a query to get the feature to delete.
            var queryParameters = new QueryParameters { WhereClause = $"OBJECTID = {objectId}" };

            // Query the feature table.
            var queryResult = currentObservationsTable.QueryFeaturesAsync(queryParameters).Result;

            // Log to debug the queryResult.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, DeleteObservation(): QueryResult: {queryResult}.",
                queryResult.Count()
            );

            // Get the first feature in the queryResult.
            var featureToDelete = queryResult.FirstOrDefault();

            // Log to debug the featureToDelete.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, DeleteObservation(): FeatureToDelete: {featureToDelete}.",
                featureToDelete
            );

            Guard.Against.Null(
                featureToDelete,
                nameof(featureToDelete),
                "SecchiViewModel, DeleteObservation(): featureToDelete can not be null."
            );

            // Send the feature via a DeleteFeatureMessage to the GeoDatabaseService.
            WeakReferenceMessenger.Default.Send<DeleteFeatureMessage, uint>(
                new DeleteFeatureMessage(
                    new FeatureDeleteMessage("SecchiObservations", "OBJECTID", featureToDelete)
                ),
                secchiObservationsChannel
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SecchiViewModelLog,
                exception,
                "Exception generated in SecchiViewModel, DeleteObservation: {message}.",
                exception.Message.ToString()
            );
        }
    }

    public async Task<int> NextLocationId()
    {
        await locationIdSemaphore.WaitAsync();
        try
        {
            Guard.Against.Null(
                currentLocationsTable,
                nameof(currentLocationsTable),
                "SecchiViewModel, NextLocationId(): currentLocationsTable can not be null."
            );

            // create a where clause to get all the features
            var queryParameters = new QueryParameters() { WhereClause = "1=1" };

            // query the feature table
            var queryResult = await currentLocationsTable.QueryFeaturesAsync(queryParameters);

            // find the maximum LocationId
            var maxLocationId = queryResult
                .Select(feature => Convert.ToInt32(feature.Attributes["LocationId"]))
                .Max();

            // the next location number is one more than the maximum
            var nextLocationNumber = maxLocationId + 1;

            // Log to debug the nextLocationId.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, NextLocationId(): NextLocationId: {nextLocationNumber}.",
                nextLocationNumber
            );

            return nextLocationNumber;
        }
        catch (Exception exception)
        {
            logger.LogError(
                SecchiViewModelLog,
                exception,
                "Exception generated in SecchiViewModel, NextLocationId: {message}.",
                exception.Message.ToString()
            );
            return 0;
        }
        finally
        {
            locationIdSemaphore.Release();
        }
    }

    // Send a message to the GeoDatabaseService adding a feature to the SecchiLocations feature table.
    public void AddNewLocation(SecchiAddLocation secchiAddLocation)
    {
        // Log to debug that AddNewLocation was called.
        logger.LogDebug(
            SecchiViewModelLog,
            "SecchiViewModel, AddNewLocation(): AddNewLocation called."
        );

        try
        {
            Guard.Against.Null(
                secchiAddLocation,
                nameof(secchiAddLocation),
                "SecchiViewModel, AddNewLocation(): secchiAddLocation can not be null."
            );

            Guard.Against.Null(
                secchiAddLocation.Location,
                nameof(secchiAddLocation.Location),
                "SecchiViewModel, AddNewLocation(): secchiAddLocation.Location can not be null."
            );

            Guard.Against.Null(
                secchiAddLocation.Latitude,
                nameof(secchiAddLocation.Latitude),
                "SecchiViewModel, AddNewLocation(): secchiAddLocation.Latitude can not be null."
            );

            Guard.Against.Null(
                secchiAddLocation.Longitude,
                nameof(secchiAddLocation.Longitude),
                "SecchiViewModel, AddNewLocation(): secchiAddLocation.Longitude can not be null."
            );

            Guard.Against.Null(
                secchiAddLocation.LocationName,
                nameof(secchiAddLocation.LocationName),
                "SecchiViewModel, AddNewLocation(): secchiAddLocation.LocationName can not be null."
            );

            Guard.Against.Null(
                secchiAddLocation.LocationType,
                nameof(secchiAddLocation.LocationType),
                "SecchiViewModel, AddNewLocation(): secchiAddLocation.Location can not be null."
            );

            Guard.Against.Null(
                secchiAddLocation.LocationNumber,
                nameof(secchiAddLocation.LocationNumber),
                "SecchiViewModel, AddNewLocation(): secchiAddLocation.LocationNumber can not be null."
            );

            Guard.Against.Null(
                currentLocationsTable,
                nameof(currentLocationsTable),
                "SecchiViewModel, AddNewLocation(): currentLocationsTable can not be null."
            );

            var newFeature = (ArcGISFeature)currentLocationsTable.CreateFeature();
            newFeature.Geometry = secchiAddLocation.Location;
            newFeature.Attributes["Latitude"] = secchiAddLocation.Latitude;
            newFeature.Attributes["Longitude"] = secchiAddLocation.Longitude;
            newFeature.Attributes["LocationName"] = secchiAddLocation.LocationName;
            newFeature.Attributes["LocationId"] = secchiAddLocation.LocationNumber;
            newFeature.Attributes["LocationType"] = (int)secchiAddLocation.LocationType;

            // Send the feature via an AddFeatureMessage to the GeoDatabaseService.
            WeakReferenceMessenger.Default.Send<AddFeatureMessage, uint>(
                new AddFeatureMessage(
                    new FeatureAddMessage("SecchiLocations", "LocationId", newFeature)
                ),
                secchiLocationsChannel
            );

            // Add the new location record to Sqlite.
            WeakReferenceMessenger.Default.Send<AddLocationRecordToTableMessage>(
                new AddLocationRecordToTableMessage(
                    new AddLocationRecordToTable(
                        new LocationRecord(
                            (double)secchiAddLocation.Latitude,
                            (double)secchiAddLocation.Longitude,
                            (int)secchiAddLocation.LocationNumber,
                            secchiAddLocation.LocationName,
                            (LocationType)secchiAddLocation.LocationType,
                            (int)RecordStatus.WorkingSet,
                            (int)LocationCollected.NotCollected
                        ),
                        DbType.SecchiLocations
                    )
                )
            );

            // Add the new location to the SecchiLocations collection.
            SecchiLocations.Add(
                new SecchiLocationDisplay(
                    latitude: (double)secchiAddLocation.Latitude,
                    longitude: (double)secchiAddLocation.Longitude,
                    locationId: (int)secchiAddLocation.LocationNumber,
                    locationName: secchiAddLocation.LocationName,
                    locationType: (LocationType)secchiAddLocation.LocationType
                )
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SecchiViewModelLog,
                exception,
                "Exception generated in SecchiViewModel, AddNewLocation: {message}.",
                exception.Message.ToString()
            );
        }
    }

    // Send a message to the GeoDatabaseService and SqliteService to delete a feature from the SecchiLocations feature table.
    public void DeleteLocation(int locationId)
    {
        // Log to debug that DeleteLocation was called.
        logger.LogDebug(
            SecchiViewModelLog,
            "SecchiViewModel, DeleteLocation(): DeleteLocation called."
        );

        try
        {
            Guard.Against.Null(
                SecchiLocations,
                nameof(SecchiLocations),
                "SecchiViewModel, DeleteLocation(): SecchiLocations can not be null."
            );

            Guard.Against.Null(
                currentLocationsTable,
                nameof(currentLocationsTable),
                "SecchiViewModel, DeleteLocation(): currentLocationsTable can not be null."
            );

            // Log to debug the locationId.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, DeleteLocation(): LocationId: {locationId}.",
                locationId
            );

            // Create a query to get the feature to delete.
            var queryParameters = new QueryParameters
            {
                WhereClause = $"LocationId = {locationId}"
            };

            // Query the feature table.
            var queryResult = currentLocationsTable.QueryFeaturesAsync(queryParameters).Result;

            // Log to debug the number of features returned by the query.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, DeleteLocation(): Number of features returned by the query: {queryResult.Count()}.",
                queryResult.Count()
            );

            // Iterate over the features and log their attributes.
            foreach (var feature in queryResult)
            {
                foreach (var attribute in feature.Attributes)
                {
                    logger.LogTrace(
                        SecchiViewModelLog,
                        "SecchiViewModel, DeleteLocation(): FeatureTable: {currentLocationsTable.TableName}, attribute.Key: {attributeName}, attribute.Value: {attributeValue}.",
                        currentLocationsTable.TableName,
                        attribute.Key,
                        attribute.Value
                    );
                }
            }

            // Send a DeleteFeatureMessage to the GeoDatabaseService.
            WeakReferenceMessenger.Default.Send<DeleteFeatureMessage, uint>(
                new DeleteFeatureMessage(
                    new FeatureDeleteMessage("SecchiLocations", "LocationId", queryResult.First())
                ),
                secchiLocationsChannel
            );

            // Find the item in SecchiLocations that matches locationID and remove that item.
            var secchiLocationDisplay = SecchiLocations.FirstOrDefault(x =>
                x.LocationId == locationId
            );
            if (secchiLocationDisplay != null)
            {
                SecchiLocations.Remove(secchiLocationDisplay);
            }
            else
            {
                logger.LogError(
                    SecchiViewModelLog,
                    "SecchiViewModel, DeleteLocation(): LocationId: {locationId} not found in SecchiLocations.",
                    locationId
                );
            }

            // Send a message to the SqliteService to delete the location from the table.
            WeakReferenceMessenger.Default.Send<DeleteLocationRecordFromTableMessage>(
                new DeleteLocationRecordFromTableMessage(
                    new DeleteLocationRecordFromTable(locationId, DbType.SecchiLocations)
                )
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SecchiViewModelLog,
                exception,
                "Exception generated in SecchiViewModel, DeleteLocation: {message}.",
                exception.Message.ToString()
            );
        }
    }

    // Send a message to the GeoDatabaseService and SqliteService to update a feature from the SecchiLocations feature table.
    public void UpdateLocation(int locationId)
    {
        // Log to debug that DeleteLocation was called.
        logger.LogDebug(
            SecchiViewModelLog,
            "SecchiViewModel, UpdateLocation: UpdateLocation called."
        );

        try
        {
            Guard.Against.Null(
                SecchiLocations,
                nameof(SecchiLocations),
                "SecchiViewModel, UpdateLocation: SecchiLocations can not be null."
            );

            Guard.Against.Null(
                currentLocationsTable,
                nameof(currentLocationsTable),
                "SecchiViewModel, UpdateLocation: currentLocationsTable can not be null."
            );

            // Log to debug the locationId.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, UpdateLocation: LocationId: {locationId}.",
                locationId
            );

            // Create a query to get the feature to update.
            var queryParameters = new QueryParameters
            {
                WhereClause = $"LocationId = {locationId}"
            };

            // Query the feature table.
            var queryResult = currentLocationsTable.QueryFeaturesAsync(queryParameters).Result;

            // Log to debug the number of features returned by the query.
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, UpdateLocation: Number of features returned by the query: {queryResult.Count()}.",
                queryResult.Count()
            );

            // Iterate over the features and log their attributes.
            foreach (var feature in queryResult)
            {
                foreach (var attribute in feature.Attributes)
                {
                    logger.LogTrace(
                        SecchiViewModelLog,
                        "SecchiViewModel, UpdateLocation: FeatureTable: {currentLocationsTable.TableName}, attribute.Key: {attributeName}, attribute.Value: {attributeValue}.",
                        currentLocationsTable.TableName,
                        attribute.Key,
                        attribute.Value
                    );
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                SecchiViewModelLog,
                exception,
                "Exception generated in SecchiViewModel, DeleteLocation: {message}.",
                exception.Message.ToString()
            );
        }
    }

    private bool CheckConfiguration()
    {
        // Log to debug that CheckConfiguraation was called.
        logger.LogDebug(
            SecchiViewModelLog,
            "SecchiViewModel, CheckConfiguration(): CheckConfiguration called."
        );

        Guard.Against.Null(
            LocalSettingsService,
            nameof(LocalSettingsService),
            "SecchiViewModel, CheckConfiguration(): LocalSettingsService can not be null."
        );

        // Get the URL for Secchi observations from local settings.
        Task.Run(async () =>
            {
                observationsURL = await LocalSettingsService.ReadSettingAsync<string>(
                    SecchiConfiguration.Item[Key.SecchiObservationsGeodatabase]
                );
            })
            .Wait();

        // Get the URL for Secchi locations from local settings.
        Task.Run(async () =>
            {
                locationsURL = await LocalSettingsService.ReadSettingAsync<string>(
                    SecchiConfiguration.Item[Key.SecchiLocationsGeodatabase]
                );
            })
            .Wait();

        // Get the GeoTriggerDistance from local settings.
        Task.Run(async () =>
            {
                geoTriggerDistance = await LocalSettingsService.ReadSettingAsync<double>(
                    SecchiConfiguration.Item[Key.GeoTriggerDistanceMeters]
                );
            })
            .Wait();

        if (
            string.IsNullOrEmpty(observationsURL)
            || string.IsNullOrEmpty(locationsURL)
            || geoTriggerDistance == 0
        )
        {
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, CheckConfiguration(): observationsURL is null or empty or locationsURL is null or empty or geoTriggerDistance is 0."
            );
            return false;
        }
        else
        {
            logger.LogDebug(
                SecchiViewModelLog,
                "SecchiViewModel, CheckConfiguration(): ObservationsURL, LocationsURL, and geoTriggerDistance have been configured."
            );
            return true;
        }
    }

    private void Shutdown()
    {
        // Unregister all messages.
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
