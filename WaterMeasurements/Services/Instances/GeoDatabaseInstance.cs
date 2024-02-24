﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Portal;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Tasks;
using Esri.ArcGISRuntime.Tasks.Offline;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Stateless;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Contracts.Services.Instances;
using WaterMeasurements.Models;
using WaterMeasurements.Views;

namespace WaterMeasurements.Services.Instances;

public partial class GeoDatabaseInstance : IGeoDatabaseInstance
{
    private readonly ILogger<GeoDatabaseInstance> logger;
    internal EventId GeoDatabaseLog = new(1, "GeoDatabaseInstance");

    // public read, private write for the name, channel, Url, and causeGeoDatabaseDownload of the GeoDatabaseInstance.
    public string Name { get; private set; }
    public GeoDatabaseType GeoDatabaseType { get; private set; }
    public uint Channel { get; private set; }
    public string Url { get; private set; }
    public bool CauseGeoDatabaseDownload { get; set; }

    private readonly string offlineGeoDatabase = string.Empty;

    // The current map envelope.
    private Envelope? mapEnvelope = null;

    // The current geodatabase.
    private Geodatabase? currentGeodatabase;

    // There is only one feature table supported at a time from the geodatabase, so it is stored here.
    private GeodatabaseFeatureTable? featureTable;

    // Configure the location of the offline geodatabase folder.
    private static readonly string offlineGeoDatabasesFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WaterMeasurements",
        "DownloadedGeoDatabases"
    );

    private readonly StateMachine<GeoDbServiceState, GeoDbServiceTrigger> stateMachine;

    // Constructor.
    public GeoDatabaseInstance(
        ILogger<GeoDatabaseInstance> logger,
        string name,
        GeoDatabaseType geoDatabaseType,
        uint channel,
        string url,
        bool causeGeoDatabaseDownload
    )
    {
        this.logger = logger;

        // Name = name;
        Name = Guard.Against.NullOrWhiteSpace(
            name,
            nameof(name),
            "GeoDatabaseInstance, constructor: Name is null or blank."
        );
        // GeoDatabaseType = geoDatabaseType;
        GeoDatabaseType = Guard.Against.EnumOutOfRange(
            geoDatabaseType,
            nameof(geoDatabaseType),
            "GeoDatabaseInstance, constructor: GeoDatabaseType enum is out of range."
        );
        // Channel = channel;
        Channel = (uint)
            Guard.Against.NegativeOrZero(
                channel,
                nameof(channel),
                "GeoDatabaseInstance, constructor: Channel is negative or zero."
            );
        // Url = url;
        Url = Guard.Against.NullOrWhiteSpace(
            url,
            nameof(url),
            "GeoDatabaseInstance, constructor: Url is null or blank."
        );
        // CauseGeoDatabaseDownload = causeGeoDatabaseDownload;
        CauseGeoDatabaseDownload = Guard.Against.Null(
            causeGeoDatabaseDownload,
            nameof(CauseGeoDatabaseDownload),
            "GeoDatabaseInstance, constructor: CauseGeoDatabaseDownload is null."
        );

        // Create a state machine to manage the state of the geodatabase service.
        stateMachine = new StateMachine<GeoDbServiceState, GeoDbServiceTrigger>(
            GeoDbServiceState.Undefined
        );

        // Log that the GeoDatabaseInstance has been created.
        logger.LogInformation(GeoDatabaseLog, "GeoDatabaseInstance: GeoDatabaseInstance started.");

        // Log the name, channel, and Url of the GeoDatabaseInstance.
        logger.LogInformation(
            GeoDatabaseLog,
            "GeoDatabaseInstance: Name: {name}, Type: {GeoDatabaseType} Channel: {channel}, LocationsUrl: {LocationsUrl}, CauseGeoDatabaseDownload: {CauseGeoDatabaseDownload}.",
            Name,
            GeoDatabaseType,
            Channel,
            Url,
            CauseGeoDatabaseDownload
        );

        // The name of the offline geodatabase.
        offlineGeoDatabase = Path.Combine(offlineGeoDatabasesFolder, Name + ".geodatabase");

        // Log the location where the geodatabase will be stored.
        logger.LogInformation(
            GeoDatabaseLog,
            "GeoDatabaseInstance: offlineGeoDatabase: Path where offline geodatabase will be stored: {offlineGeoDatabase}.",
            offlineGeoDatabase
        );

        // Trigger for map envelope received with envelope as a parameter.
        var mapEnvelopeReceived = stateMachine.SetTriggerParameters<Envelope>(
            GeoDbServiceTrigger.MapEnvelopeReceived
        );

        // Trigger for GeoDatabaseStateChange.
        var geoDatabaseStateChange = stateMachine.SetTriggerParameters<GeodatabaseStateChange>(
            GeoDbServiceTrigger.GeoDatabaseStateChange
        );

        // Trigger for AddFeatureMessage.
        var featureMessage = stateMachine.SetTriggerParameters<FeatureMessage>(
            GeoDbServiceTrigger.GeoDatabaseFeatureReceived
        );

        // Trigger for FeatureTableRequestMessage.
        var featureTableRequest = stateMachine.SetTriggerParameters<string>(
            GeoDbServiceTrigger.FeatureTableRequestReceived
        );

        // For testing, delete the local geodatabase.
        // This will force a download of the geodatabase.
        if (CauseGeoDatabaseDownload)
        {
            DeleteOfflineGeodatabase();
        }

        try
        {
            // Log state transitions.
            stateMachine.OnTransitioned(OnTransition);

            // Start in an undefined state.
            // Wait for a map envelope as the envelope is needed to determine the extent of the geodatabase.
            // Upon Exit, get the current network status.
            stateMachine
                .Configure(GeoDbServiceState.Undefined)
                // See if the currentMapEnvelope in GeoDatabaseService has been set.
                .OnEntry(() => CheckForMapEnvelope())
                // Upon Exit, get the current network status.
                .OnExit(() => GetInternetStatus())
                .Permit(
                    GeoDbServiceTrigger.MapEnvelopeHasBeenSet,
                    GeoDbServiceState.IsInternetAvailable
                )
                .Permit(
                    GeoDbServiceTrigger.MapEnvelopeReceived,
                    GeoDbServiceState.IsInternetAvailable
                );

            // When a config envelope or map envelope is received, wait for internet status.
            // If internet is available, then move to SyncReady, otherwise move to UseLocal.
            // Upon Exit, check for a local geodatabase.
            // The check for a local geodatabase will generate a LocalGeoDatabaseExists or LocalGeoDatabaseDoesNotExist trigger.
            stateMachine
                .Configure(GeoDbServiceState.IsInternetAvailable)
                .OnExit(() => CheckForLocalGeodatabase())
                .OnEntryFrom(mapEnvelopeReceived, envelope => mapEnvelope = envelope)
                .Permit(GeoDbServiceTrigger.InternetAvailableRecieved, GeoDbServiceState.SyncReady)
                .Permit(
                    GeoDbServiceTrigger.InternetUnavailableRecieved,
                    GeoDbServiceState.UseLocal
                );

            // Internet is available, and the existence of a local geodatabase has been determined.
            // If a local geodatabase exists, then move to UseLocal, otherwise move to DownloadGeodatabase.
            stateMachine
                .Configure(GeoDbServiceState.SyncReady)
                .Permit(GeoDbServiceTrigger.LocalGeoDatabaseExists, GeoDbServiceState.UseLocal)
                //.Permit(GeoDbServiceTrigger.LocalGeoDatabaseExists, GeoDbServiceState.DownloadGeodatabase)
                .Permit(
                    GeoDbServiceTrigger.LocalGeoDatabaseDoesNotExist,
                    GeoDbServiceState.DownloadGeodatabase
                );

            // Get the local geodatabase.
            stateMachine
                .Configure(GeoDbServiceState.UseLocal)
                .OnEntry(async () =>
                {
                    Guard.Against.Null(
                        mapEnvelope,
                        nameof(mapEnvelope),
                        "GeoDatabaseInstance, stateMachine (GeoDbServiceState.UseLocal): mapEnvelope is null."
                    );

                    // Use GetDownloadedGeodatabase() to get the local geodatabase and store it in a CurrentGeodatabase record.
                    currentGeodatabase = await GetDownloadedGeodatabase();

                    // Make sure that currentGeodatabase is not null.
                    Guard.Against.Null(
                        currentGeodatabase,
                        nameof(currentGeodatabase),
                        "GeoDatabaseInstance, stateMachine (GeoDbServiceState.UseLocal): currentGeodatabase is null."
                    );

                    // Send the feature table.
                    SendFeatureTableZero(currentGeodatabase);
                })
                // Handle the GeoDatabaseStateChange trigger.
                // Don't check for currentGeodatabase being null as it is checked after GetDownloadedGeodatabase() is called.
                .InternalTransition(
                    geoDatabaseStateChange,
                    (GeoDbOperation, _) => HandleGeodatabaseStateChange(GeoDbOperation.StateRequest)
                )
                // Handle the GeoDatabaseFeatureReceived trigger by calling AddFeatureToGeodatabase.
                .InternalTransition(
                    featureMessage,
                    (featureMessage, _) => AddFeatureToGeodatabase(featureMessage)
                )
                // Handle the FeatureTableRequestReceived trigger by sending the feature table.
                .InternalTransition(
                    featureTableRequest,
                    (featureTableRequest, _) =>
                    {
                        // Log the name of the table requested.
                        logger.LogDebug(
                            GeoDatabaseLog,
                            "GeoDatabaseInstance {name}, stateMachine (GeoDbServiceState.UseLocal): featureTableRequest.FeatureTable: {tableName}.",
                            Name,
                            featureTableRequest
                        );

                        // Make sure that currentGeodatabase is not null.
                        Guard.Against.Null(
                            currentGeodatabase,
                            nameof(currentGeodatabase),
                            "GeoDatabaseInstance, stateMachine (GeoDbServiceState.UseLocal): currentGeodatabase is null."
                        );

                        // Make sure that featureTable is not null.
                        Guard.Against.Null(
                            featureTable,
                            nameof(featureTable),
                            "GeoDatabaseInstance, stateMachine (GeoDbServiceState.UseLocal): featureTable is null."
                        );

                        if (featureTableRequest != Name)
                        {
                            // Log that the requested table is not the current geodatabase and show the name of the requested table and the current geodatabase name.
                            logger.LogDebug(
                                GeoDatabaseLog,
                                "GeoDatabaseInstance stateMachine (GeoDbServiceState.UseLocal): the name requested: {requestedValue}, does not match the current geodatabase's name: {name}.",
                                featureTableRequest,
                                Name
                            );
                        }

                        // Send the feature table.
                        SendFeatureTableZero(currentGeodatabase);
                    }
                )
                .Ignore(GeoDbServiceTrigger.InternetAvailableRecieved)
                .Permit(GeoDbServiceTrigger.AppClosing, GeoDbServiceState.AppClosing);

            // There was not a local geodatabase, so download one.
            // After a successful download, a local geodatabase trigger will be sent from within DownloadGeodatabase.
            stateMachine
                .Configure(GeoDbServiceState.DownloadGeodatabase)
                .OnEntry(async () =>
                {
                    Guard.Against.Null(
                        mapEnvelope,
                        nameof(mapEnvelope),
                        "GeoDatabaseInstance, stateMachine (GeoDbServiceState.DownloadGeodatabase): mapEnvelope is null."
                    );

                    // Download the geodatabase.
                    await DownloadGeodatabase(mapEnvelope);
                })
                .Permit(GeoDbServiceTrigger.LocalGeoDatabaseExists, GeoDbServiceState.UseLocal);

            // Handle the AppClosing trigger.
            stateMachine
                .Configure(GeoDbServiceState.AppClosing)
                .OnEntry(() =>
                {
                    // Log the AppClosing trigger.
                    logger.LogDebug(
                        GeoDatabaseLog,
                        "GeoDatabaseInstance {name}, stateMachine (GeoDbServiceState.AppClosing): AppClosing trigger received.",
                        Name
                    );
                    // Close the geodatabase and unregister all messages.
                    Cleanup();
                })
                .Ignore(GeoDbServiceTrigger.InternetAvailableRecieved)
                .Ignore(GeoDbServiceTrigger.InternetUnavailableRecieved);

            // Write unhandled trigger to log.
            stateMachine.OnUnhandledTrigger(
                (state, trigger) =>
                {
                    // Log to error.
                    logger.LogError(
                        GeoDatabaseLog,
                        "GeoDatabaseInstance {name}, stateMachine (OnUnhandledTrigger): Unhandled trigger {trigger} in state {state}.",
                        Name,
                        trigger,
                        state
                    );
                }
            );

            // Register to get MapExtentChangedMessage and use that to trigger MapEnvelopeReceived.
            WeakReferenceMessenger.Default.Register<MapExtentChangedMessage>(
                this,
                (recipient, message) =>
                {
                    logger.LogDebug(
                        GeoDatabaseLog,
                        "GeoDatabaseInstance {name}, MapExtentChangedMessage: {envelope}.",
                        Name,
                        message.Value.Extent.Project(SpatialReferences.Wgs84).ToString()
                    );
                    stateMachine.Fire(mapEnvelopeReceived, message.Value.Extent);
                }
            );

            // Register to get MapPageUnloaded message and use that to trigger AppClosing.
            WeakReferenceMessenger.Default.Register<MapPageUnloaded>(
                this,
                (recipient, message) =>
                {
                    logger.LogDebug(
                        GeoDatabaseLog,
                        "GeoDatabaseInstance {name}, MapPageUnloaded.",
                        Name
                    );
                    stateMachine.Fire(GeoDbServiceTrigger.AppClosing);
                }
            );

            // Register to get GeodatabaseStateChangeMessage and use that to trigger GeoDatabaseStateChange.
            WeakReferenceMessenger.Default.Register<GeodatabaseStateChangeMessage, uint>(
                this,
                Channel,
                (recipient, message) =>
                {
                    logger.LogDebug(
                        GeoDatabaseLog,
                        "GeoDatabaseInstance {name}, GeodatabaseStateChangeMessage: {message}.",
                        Name,
                        message.Value
                    );
                    stateMachine.Fire(geoDatabaseStateChange, message.Value);
                }
            );

            // Register to get FeatureTableRequestMessage and use that to trigger FeatureTableRequestReceived.
            WeakReferenceMessenger.Default.Register<FeatureTableRequestMessage, uint>(
                this,
                Channel,
                (recipient, message) =>
                {
                    logger.LogDebug(
                        GeoDatabaseLog,
                        "GeoDatabaseInstance {name}, FeatureTableRequestMessage: {message}.",
                        Name,
                        message.Value
                    );
                    stateMachine.Fire(featureTableRequest, message.Value);
                }
            );

            // Register to get AddFeatureMessage and use that to trigger GeoDatabaseFeatureReceived.
            WeakReferenceMessenger.Default.Register<AddFeatureMessage, uint>(
                this,
                Channel,
                (recipient, message) =>
                {
                    Guard.Against.Null(
                        message.Value,
                        nameof(message.Value),
                        "GeoDatabaseInstance, AddFeatureMessage: message.Value is null."
                    );
                    logger.LogDebug(
                        GeoDatabaseLog,
                        "GeoDatabaseInstance {name}, AddFeatureMessage {message}.",
                        Name,
                        message.Value
                    );
                    stateMachine.Fire(featureMessage, message.Value);
                }
            );

            /*
            // Register to get NetworkChangedMessage and use that to trigger InternetAvailableRecieved and InternetUnavailableRecieved.
            WeakReferenceMessenger.Default.Register<NetworkChangedMessage>(
                this,
                (recipient, message) =>
                {
                    logger.LogDebug(
                        GeoDatabaseLog,
                        "GeoDatabaseInstance {name}, NetworkChangedMessage IsInternetAvailable = {isInternetAvailable}.",
                        message.Value.IsInternetAvailable
                    );
                    if (message.Value.IsInternetAvailable)
                    {
                        stateMachine.Fire(GeoDbServiceTrigger.InternetAvailableRecieved);
                    }
                    else
                    {
                        stateMachine.Fire(GeoDbServiceTrigger.InternetUnavailableRecieved);
                    }
                }
            );
            */
        }
        catch (Exception exception)
        {
            logger.LogError(
                GeoDatabaseLog,
                exception,
                "GeoDatabaseInstance {name}: Exception: {exception}.",
                Name,
                exception.Message
            );
        }
    }

    ~GeoDatabaseInstance()
    {
        // Log that the GeoDatabaseInstance has been destroyed.
        logger.LogInformation(
            GeoDatabaseLog,
            "GeoDatabaseInstance: GeoDatabaseInstance destroyed."
        );
        // Close the geodatabase and unregister all messages.
        Cleanup();
    }

    // Get the current network status and use that to trigger InternetAvailableRecieved and InternetUnavailableRecieved.
    private async void GetInternetStatus()
    {
        // Send a NetworkStatusRequestMessage to get the current network status and use that to trigger InternetAvailableRecieved and InternetUnavailableRecieved.
        var networkStatus =
            await WeakReferenceMessenger.Default.Send<NetworkStatusRequestMessage>();
        logger.LogDebug(
            GeoDatabaseLog,
            "GeoDatabaseInstance {name}, NetworkStatusRequestMessage: IsInternetAvailable: {isInternetAvailable}.",
            Name,
            networkStatus.IsInternetAvailable
        );
        if (networkStatus.IsInternetAvailable)
        {
            stateMachine.Fire(GeoDbServiceTrigger.InternetAvailableRecieved);
        }
        else
        {
            stateMachine.Fire(GeoDbServiceTrigger.InternetUnavailableRecieved);
        }
    }

    // Log state transitions.
    private void OnTransition(
        StateMachine<GeoDbServiceState, GeoDbServiceTrigger>.Transition transition
    )
    {
        logger.LogDebug(
            GeoDatabaseLog,
            "GeoDatabaseInstance {name}, OnTransition: Transitioned from {transition.Source} to {transition.Destination} via {transition.Trigger}.",
            Name,
            transition.Source,
            transition.Destination,
            transition.Trigger
        );
    }

    // Send a FeatureTableMessage with the featureTable.
    private async void SendFeatureTableZero(Geodatabase geodatabase)
    {
        try
        {
            Guard.Against.Null(
                geodatabase,
                nameof(geodatabase),
                "GeoDatabaseInstance, SendFeatureTableZero(Geodatabase geodatabase): geodatabase is null."
            );

            // Select the first table from the geodatabase.
            featureTable = geodatabase.GetGeodatabaseFeatureTable(0);
            // Make sure that featureTable is not null.
            Guard.Against.Null(
                featureTable,
                nameof(featureTable),
                "GeoDatabaseInstance, SendFeatureTableZero(Geodatabase geodatabase): featureTable is null."
            );
            // Load the table so the TableName can be read.
            await featureTable.LoadAsync();

            // Log the name of the table.
            logger.LogDebug(
                GeoDatabaseLog,
                "GeoDatabaseInstance {name}, SendFeatureTableZero: featureTable.TableName: {featureTable.TableName}, sending message containing featureTable to channel {channel}.",
                Name,
                featureTable.TableName,
                Channel
            );

            // Send a FeatureTableMessage with the featureTable.
            WeakReferenceMessenger.Default.Send(new FeatureTableMessage(featureTable), Channel);

            // Log the fields in the featureTable.
            foreach (var field in featureTable.Fields)
            {
                logger.LogTrace(
                    GeoDatabaseLog,
                    "GeoDatabaseInstance {name}, SendFeatureTableZero: featureTable.TableName: {featureTable.TableName}, field.Name: {field.Name}, field.FieldType: {field.FieldType}.",
                    Name,
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
                        GeoDatabaseLog,
                        "GeoDatabaseInstance {name}, SendFeatureTableZero: feature.Attributes: attribute.Key: {attributeName}, attribute.Value: {attributeValue}.",
                        Name,
                        attribute.Key,
                        attribute.Value
                    );
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                GeoDatabaseLog,
                "GeoDatabaseInstance {name}, SendFeatureTableZero: Exception: {exception}.",
                Name,
                exception.ToString()
            );
        }
    }

    private async void ListGeodatabaseContents(Geodatabase geodatabase)
    {
        foreach (var table in geodatabase.GeodatabaseFeatureTables)
        {
            try
            {
                // Load the table so the TableName can be read.
                await table.LoadAsync();

                logger.LogDebug(GeoDatabaseLog, "TableName: {table.TableName}.", table.TableName);

                foreach (var field in table.Fields)
                {
                    logger.LogDebug(
                        GeoDatabaseLog,
                        "GeoDatabaseInstance {name}, ListGeodatabaseContents: table.TableName: {table.TableName}, field.Name: {field}.",
                        Name,
                        table.TableName,
                        field.Name
                    );
                }

                // create a where clause to get all the features.
                var queryParameters = new QueryParameters() { WhereClause = "1=1" };

                // query the feature table
                var queryResult = table.QueryFeaturesAsync(queryParameters).Result;

                // iterate over the features and log their attributes
                foreach (var feature in queryResult)
                {
                    foreach (var attribute in feature.Attributes)
                    {
                        logger.LogDebug(
                            GeoDatabaseLog,
                            "GeoDatabaseInstance {name}, ListGeodatabaseContents: feature.Attributes: attribute.Key: {attributeName}, attribute.Value: {attributeValue}.",
                            Name,
                            attribute.Key,
                            attribute.Value
                        );
                    }
                }
            }
            catch (Exception exception)
            {
                logger.LogError(
                    GeoDatabaseLog,
                    "GeoDatabaseInstance {name}, ListGeodatabaseContents: Exception: {exception}.",
                    Name,
                    exception.ToString()
                );
            }
        }
    }

    // Checks for the existence of a previously downloaded geodatabase.
    // If one exists, then the LocalGeoDatabaseExists trigger is fired.
    // If it does not exist, then the LocalGeoDatabaseDoesNotExist trigger is fired.
    private void CheckForLocalGeodatabase()
    {
        if (File.Exists(offlineGeoDatabase))
        {
            logger.LogDebug(
                GeoDatabaseLog,
                "GeoDatabaseInstance {name}, DoesLocalGeodatabaseExist: Local geodatabase exists.",
                Name
            );
            stateMachine.Fire(GeoDbServiceTrigger.LocalGeoDatabaseExists);
        }
        else
        {
            logger.LogDebug(
                GeoDatabaseLog,
                "GeoDatabaseInstance {name}, DoesLocalGeodatabaseExist: Local geodatabase does not exist.",
                Name
            );
            stateMachine.Fire(GeoDbServiceTrigger.LocalGeoDatabaseDoesNotExist);
        }
    }

    // Get the locally stored geodatabase.
    private async Task<Geodatabase?> GetDownloadedGeodatabase()
    {
        // If the local geodatabase exists, open it.
        if (File.Exists(offlineGeoDatabase))
        {
            logger.LogDebug(
                GeoDatabaseLog,
                "GeoDatabaseInstance {name}, GetDownloadedGeodatabase: Opening local geodatabase.",
                Name
            );
            return await Geodatabase.OpenAsync(offlineGeoDatabase);
        }
        else
        {
            logger.LogDebug(
                GeoDatabaseLog,
                "GeoDatabaseInstance {name}, GetDownloadedGeodatabase: Local geodatabase does not exist.",
                Name
            );
            return null;
        }
    }

    // Handle Geodatabase state changes.
    private void HandleGeodatabaseStateChange(GeoDbOperation GeoDbOperation)
    {
        try
        {
            if (currentGeodatabase is not null)
            {
                switch (GeoDbOperation)
                {
                    case GeoDbOperation.BeginTransaction:
                        logger.LogDebug(
                            GeoDatabaseLog,
                            "GeoDatabaseInstance {name}, HandleGeodatabaseStateChange: BeginTransaction.",
                            Name
                        );
                        if (currentGeodatabase.IsInTransaction)
                        {
                            logger.LogDebug(
                                GeoDatabaseLog,
                                "GeoDatabaseInstance {name}, HandleGeodatabaseStateChange: Already in a transaction, no operation.",
                                Name
                            );
                        }
                        else
                        {
                            logger.LogDebug(
                                GeoDatabaseLog,
                                "GeoDatabaseInstance {name}, HandleGeodatabaseStateChange: Not in a transaction, beginning transaction.",
                                Name
                            );
                            currentGeodatabase.BeginTransaction();
                        }
                        break;
                    case GeoDbOperation.Commit:
                        logger.LogDebug(
                            GeoDatabaseLog,
                            "GeoDatabaseInstance {name}, HandleGeodatabaseStateChange: Commit.",
                            Name
                        );
                        if (currentGeodatabase.IsInTransaction)
                        {
                            logger.LogDebug(
                                GeoDatabaseLog,
                                "GeoDatabaseInstance {name}, HandleGeodatabaseStateChange: In a transaction, committing.",
                                Name
                            );
                            currentGeodatabase.CommitTransaction();
                        }
                        else
                        {
                            logger.LogDebug(
                                GeoDatabaseLog,
                                "GeoDatabaseInstance {name}, HandleGeodatabaseStateChange: Not in a transaction, no operation.",
                                Name
                            );
                        }
                        break;
                    case GeoDbOperation.Rollback:
                        logger.LogDebug(
                            GeoDatabaseLog,
                            "GeoDatabaseInstance {name}, HandleGeodatabaseStateChange: Rollback.",
                            Name
                        );
                        if (currentGeodatabase.IsInTransaction)
                        {
                            logger.LogDebug(
                                GeoDatabaseLog,
                                "GeoDatabaseInstance {name}, HandleGeodatabaseStateChange: In a transaction, rolling back.",
                                Name
                            );
                            currentGeodatabase.RollbackTransaction();
                        }
                        else
                        {
                            logger.LogDebug(
                                GeoDatabaseLog,
                                "GeoDatabaseInstance {name}, HandleGeodatabaseStateChange: Not in a transaction, no operation.",
                                Name
                            );
                        }
                        break;
                    default:
                        logger.LogError(
                            GeoDatabaseLog,
                            "GeoDatabaseInstance {name}, HandleGeodatabaseStateChange: Unknown operation.",
                            Name
                        );
                        break;
                }
            }
            else
            {
                logger.LogError(
                    GeoDatabaseLog,
                    "GeoDatabaseInstance {name}, HandleGeodatabaseStateChange: currentGeodatabase is null.",
                    Name
                );
                throw new Exception("currentGeodatabase is null.");
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                GeoDatabaseLog,
                exception,
                "Exception generated in GeoDatabaseInstance {name}, HandleGeodatabaseStateChange: {exception}.",
                Name,
                exception.Message
            );
        }
    }

    // Handle the GeoDatabaseStateChange trigger, add the feature to the geodatabase.
    private async void AddFeatureToGeodatabase(FeatureMessage featureMessage)
    {
        try
        {
            // Make sure that currentGeodatabase is not null.
            Guard.Against.Null(
                currentGeodatabase,
                nameof(currentGeodatabase),
                "GeoDatabaseInstance, AddFeatureToGeodatabase: currentGeodatabase is null."
            );
            // Make sure that featureMessage is not null.
            Guard.Against.Null(
                featureMessage,
                nameof(featureMessage),
                "GeoDatabaseInstance, AddFeatureToGeodatabase: featureMessage is null."
            );

            logger.LogDebug(
                GeoDatabaseLog,
                "GeoDatabaseInstance {name}, AddFeatureToGeodatabase: Adding feature to geodatabase.",
                Name
            );

            // Log the contents of the featureMessage.
            foreach (var attribute in featureMessage.FeatureToAdd.Attributes)
            {
                logger.LogDebug(
                    GeoDatabaseLog,
                    "GeoDatabaseInstance {name}, AddFeatureToGeodatabase: featureMessage.FeatureToAdd.Attributes: attribute.Key: {attributeName}, attribute.Value: {attributeValue}.",
                    Name,
                    attribute.Key,
                    attribute.Value
                );
            }

            // Make sure that featureTable is not null.
            Guard.Against.Null(
                featureTable,
                nameof(featureTable),
                "GeoDatabaseInstance, AddFeatureToGeodatabase: featureTable is null."
            );

            await featureTable.AddFeatureAsync(featureMessage.FeatureToAdd);

            ListGeodatabaseContents(currentGeodatabase);
        }
        catch (Exception exception)
        {
            logger.LogError(
                GeoDatabaseLog,
                exception,
                "GeoDatabaseInstance {name}, AddFeatureToGeodatabase: Exception: {exception}.",
                Name,
                exception.Message
            );
        }
    }

    // Download the geodatabase.
    private async Task DownloadGeodatabase(Envelope envelope)
    {
        try
        {
            // If the directory where the offline map is stored does not exist, create it.
            if (Directory.Exists(offlineGeoDatabasesFolder) != true)
            {
                logger.LogInformation(
                    GeoDatabaseLog,
                    "GeoDatabaseInstance {name}, DownloadGeodatabase: Directory {mapPackagePath} does not exist, creating it.",
                    Name,
                    offlineGeoDatabasesFolder
                );
                Directory.CreateDirectory(offlineGeoDatabasesFolder);
            }

            Geodatabase localGeodatabase;
            // Create a new GeodatabaseSyncTask with the uri of the feature server to pull from.
            var uri = new Uri(Url);
            var gdbTask = await GeodatabaseSyncTask.CreateAsync(uri);

            // Create parameters for the task: layers and extent to include, out spatial reference, and sync model.
            var gdbParams = await gdbTask.CreateDefaultGenerateGeodatabaseParametersAsync(envelope);
            gdbParams.SyncModel = SyncModel.Layer;
            gdbParams.LayerOptions.Clear();
            gdbParams.LayerOptions.Add(new GenerateLayerOption(0));

            // Create a geodatabase job that generates the geodatabase.
            var generateGdbJob = gdbTask.GenerateGeodatabase(gdbParams, offlineGeoDatabase);

            // Add a handler for progress changes on the job.
            generateGdbJob.ProgressChanged += GdbPercentChanged;

            // Handle the job changed event and check the status of the job; store the geodatabase when it's ready.
            generateGdbJob.StatusChanged += (s, e) =>
            {
                // See if the job succeeded.
                if (generateGdbJob.Status == JobStatus.Succeeded)
                {
                    logger.LogDebug(
                        GeoDatabaseLog,
                        "GeoDatabaseInstance {name}, DownloadGeodatabase: Created local geodatabase.",
                        Name
                    );
                }
                else if (generateGdbJob.Status == JobStatus.Failed)
                {
                    // If generateGdbJob.Status is Failed, see if there is an error with the job.
                    if (generateGdbJob.Error != null)
                    {
                        logger.LogDebug(
                            GeoDatabaseLog,
                            "GeoDatabaseInstance {name}, DownloadGeodatabase: Unable to create local geodatabase: {generateGdbJob.Error}.",
                            Name,
                            generateGdbJob.Error.Message
                        );
                    }
                    else
                    {
                        logger.LogDebug(
                            GeoDatabaseLog,
                            "GeoDatabaseInstance {name}, DownloadGeodatabase: Unable to create local geodatabase, but no error returned.",
                            Name
                        );
                    }
                }
            };

            // Handle the progress changed event and send the percent complete.
            void GdbPercentChanged(object? sender, EventArgs progressChanged)
            {
                logger.LogDebug(
                    GeoDatabaseLog,
                    "GeoDatabaseInstance {name}, DownloadGeodatabase: Sending percent downloaded: {generateGdbJob.Progress}.",
                    Name,
                    generateGdbJob.Progress
                );

                // Send a GeodatabaseDownloadMessage with the percent downloaded.
                WeakReferenceMessenger.Default.Send<GeoDatabaseDownloadProgressMessage, uint>(
                    new GeoDatabaseDownloadProgressMessage(
                        new GeoDatabaseDownloadInstanceProgress(generateGdbJob.Progress)
                    ),
                    Channel
                );
            }

            // Start the generate geodatabase job.
            localGeodatabase = await generateGdbJob.GetResultAsync();
            // The local geodatabase has been created, so send the LocalGeoDatabaseExists trigger.
            stateMachine.Fire(GeoDbServiceTrigger.LocalGeoDatabaseExists);
        }
        catch (Exception exception)
        {
            logger.LogError(
                GeoDatabaseLog,
                exception,
                "GeoDatabaseInstance {name}, DownloadGeodatabase: Exception: {exception}.",
                Name,
                exception.Message
            );
        }
    }

    // Check if the currentMapEnvelope in GeoDatabaseService has been set. If so, then send the MapEnvelopeHasBeenSet trigger.
    private void CheckForMapEnvelope()
    {
        var geoDatabaseServiceMapEnvelope =
            WeakReferenceMessenger.Default.Send<MapEnvelopeRequestMessage>();

        if (geoDatabaseServiceMapEnvelope is not null)
        {
            logger.LogDebug(
                GeoDatabaseLog,
                "GeoDatabaseInstance {name}, CheckForMapEnvelope: GeoDatabaseService currentMapEnvelope is not null and contains: {geoDatabaseServiceMapEnvelope}",
                Name,
                geoDatabaseServiceMapEnvelope.ToString()
            );
            mapEnvelope = geoDatabaseServiceMapEnvelope;
            stateMachine.Fire(GeoDbServiceTrigger.MapEnvelopeHasBeenSet);
        }
        else
        {
            logger.LogDebug(
                GeoDatabaseLog,
                "GeoDatabaseInstance {name}, CheckForMapEnvelope: GeoDatabaseService currentMapEnvelope is null.",
                Name
            );
        }
    }

    private void DeleteOfflineGeodatabase()
    {
        try
        {
            // If the local geodatabase exists, delete it.
            if (File.Exists(offlineGeoDatabase))
            {
                logger.LogDebug(
                    GeoDatabaseLog,
                    "GeoDatabaseInstance {name}, DeleteOfflineGeodatabase: Deleting local geodatabase.",
                    Name
                );
                File.Delete(offlineGeoDatabase);
            }
            else
            {
                logger.LogDebug(
                    GeoDatabaseLog,
                    "GeoDatabaseInstance {name},DeleteOfflineGeodatabase: Local geodatabase does not exist.",
                    Name
                );
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                GeoDatabaseLog,
                exception,
                "GeoDatabaseInstance {name},DeleteOfflineGeodatabase: Exception: {exception}.",
                Name,
                exception.Message
            );
        }
    }

    private void Cleanup()
    {
        // If the geodatabase exists, close it.
        currentGeodatabase?.Close();
        // Unregister all messages.
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
