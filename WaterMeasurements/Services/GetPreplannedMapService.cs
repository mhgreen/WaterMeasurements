using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Esri.ArcGISRuntime.Portal;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Tasks;
using Esri.ArcGISRuntime.Tasks.Offline;
using Esri.ArcGISRuntime.Data;

using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

using Microsoft.Extensions.Logging;

using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Models;

using Windows.ApplicationModel.Store;
using Ardalis.GuardClauses;
using Stateless;

// using static WaterMeasurements.Models.GetPreplannedMapModel;

namespace WaterMeasurements.Services;

// Message to notify modules that the extent of the main map has changed.
public class MapExtentChangedMessage : ValueChangedMessage<MainMapExtent>
{
    public MapExtentChangedMessage(MainMapExtent mapEnvelope)
        : base(mapEnvelope) { }
}

// Request message to get the map envelope.
public class MapEnvelopeRequestMessage : RequestMessage<Envelope> { }

// Message to notify modules of the status of the preplanned map configuation.
public class MapConfigurationMessage(PreplannedMapConfiguration configurationValid)
    : ValueChangedMessage<PreplannedMapConfiguration>(configurationValid) { }

public partial class GetPreplannedMapService : IGetPreplannedMapService
{
    private readonly ILogger<GetPreplannedMapService> logger;

    // Set the EventId for logging messages.
    internal EventId DownloadPreplannedEvent = new(2, "GetPreplannedMapService");

    private readonly ILocalSettingsService? localSettingsService;

    private readonly GetPreplannedMapModel preplannedMapModel;

    // --------------------------------------------------------------------
    // To cause the preplanned map to be downloaded, set the following to true (used for testing).
    // This will cause the routine CauseMapDownload that manually configures the
    // download request setting in GetPreplannedMapService to be called.
    // Set the value to be overridden in the method call below.
    private readonly bool useOverriddenDownloadSetting = false;

    // These value set by the routine is presisted in the localSettingsService.
    // This is a one-shot operation and is reset to false after the operation is complete.
    // --------------------------------------------------------------------

    // Get the base folder for storing items associated with this application.
    private static readonly string WaterMeasurementsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WaterMeasurements"
    );

    // TODO: Add this to the configuration screen.
    // The ID for a web map item hosted on the server.
    // This is used to make sure that the retrieved map is the correct one.
    private const string PreplannedMapName = "WaterMeasurements_MapArea";

    // Key for map package path which is stored when the map is downloaded and is used for offline retrieval.
    private const string packagePathKey = "MapPakagePath";

    // Key for DateTime when map was last checked for updates.
    private const string mapLastCheckedforUpdateKey = "LastMapUpdateChecked";

    // Key for hours between update checks.
    private const string hoursBetweenUpdateChecksKey = "HoursBetweenUpdateChecks";

    // Default value for hours between update checks.
    private const int defaultHoursBetweenUpdateChecks = 2;

    // Key to cause deletion of offline map package (true = delete, false = keep).
    private const string deleteOfflineMapKey = "DeleteOfflineMap";

    // Key to cause download of offline map package (true = cause download, false = regular operation).
    private const string downloadOfflineMapKey = "DownloadOfflineMap";

    // Configure the offline data folder to store secchi map.
    private static readonly string offlineDataFolder = Path.Combine(
        WaterMeasurementsFolder,
        "Map",
        "DownloadPreplannedMapAreas"
    );

    // Key for ArcGIS API key.
    private const string arcgisApiKey = "ArcGISApiKey";

    // Key for preplanned map identifier.
    private const string offlineMapIdentifier = "OfflineMapIdentifier";

    // Most recently opened map package.
    private MobileMapPackage? mobileMapPackage;

    // Current map envelope.
    private Envelope? currentMapEnvelope = null;

    // Retrieve the local app data store.
    private readonly Windows.Storage.ApplicationDataContainer? localSettings = Windows
        .Storage
        .ApplicationData
        .Current
        .LocalSettings;

    // State machine for the GetPreplannedMapService.
    private readonly StateMachine<PreplannedMapState, PreplannedMapTrigger> stateMachine;

    public GetPreplannedMapService(
        ILogger<GetPreplannedMapService> logger,
        ILocalSettingsService localSettingsService
    )
    {
        this.logger = logger;
        this.localSettingsService = localSettingsService;

        // For testing, set useOverriddenDownloadSetting above to true to cause CauseMapDownload
        // to be called which will cause a download of the preplanned map. This is a one-shot operation
        // and is reset to false after the operation is complete. Of course, if useOverriddenDownloadSetting
        // is left set to true, then this will be called every time the app is run.
        if (useOverriddenDownloadSetting)
        {
            CauseMapDownload(true);
        }

        preplannedMapModel = new GetPreplannedMapModel { Map = null };

        // Register a message handler for the MapEnvelopeRequestMessage returning the currentMapEnvelope.
        WeakReferenceMessenger.Default.Register<GetPreplannedMapService, MapEnvelopeRequestMessage>(
            this,
            (recipient, message) =>
            {
                logger.LogDebug(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, MapEnvelopeRequestMessage: {message}.",
                    message
                );

                message.Reply(currentMapEnvelope!);
            }
        );

        // Create a state machine for the GetPreplannedMapService.
        stateMachine = new StateMachine<PreplannedMapState, PreplannedMapTrigger>(
            PreplannedMapState.Undefined
        );
        InitializeStateMachine();
        _ = CheckForArcgisKey();
    }

    // State Machine for GetPreplannedMapService.
    private async void InitializeStateMachine()
    {
        var mapPackagePath = string.Empty;

        try
        {
            // Log that the GetPreplannedMapService has been created.
            logger.LogInformation(
                DownloadPreplannedEvent,
                "GetPreplannedMapService: Initializing state machine."
            );

            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "GetPreplannedMapService, StateMachine: localSettingsService can not be null."
            );

            // Get the current setting for mapPackagePath.
            mapPackagePath = await GetMapPackagePath(packagePathKey);

            // If mapPackagePath is null, then set it to default.
            if (mapPackagePath is null || mapPackagePath == "")
            {
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, Initialization: mapPackagePath is null or empty, setting mapPackagePath to default location."
                );

                // Set the mapPackagePath to the default value.
                mapPackagePath = Path.Combine(offlineDataFolder, PreplannedMapName);
                // Log mapPackagePath.
                logger.LogDebug(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, Initialization: mapPackagePath: {mapPackagePath}.",
                    mapPackagePath
                );
                // Save the mapPackagePath for future runs.
                await SetMapPackagePath(packagePathKey, mapPackagePath);
            }

            logger.LogDebug(
                "GetPreplannedMapService, StateMachine: mapPackagePath has a value of {mapPackagePath} ",
                mapPackagePath
            );

            // Log state transitions.
            stateMachine.OnTransitioned(OnTransition);

            // Start in an undefined state and wait for the Startup trigger to begin.
            stateMachine
                .Configure(PreplannedMapState.Undefined)
                .Permit(PreplannedMapTrigger.Startup, PreplannedMapState.Initialization);

            // Perform initialization. First check to see if this is the first time the app has been run.
            // If it is the first time, then set the mapPackagePath and trigger InitializationComplete.
            stateMachine
                .Configure(PreplannedMapState.Initialization)
                .OnEntry(async () => await Initialization())
                .OnExit(async () => await GetNetworkStatusAsync())
                .Permit(
                    PreplannedMapTrigger.InitializationComplete,
                    PreplannedMapState.IsInternetAvailable
                );

            // Whait for internet status.
            // If internet is available, then move to SyncReady, otherwise move to UseLocal.
            stateMachine
                .Configure(PreplannedMapState.IsInternetAvailable)
                .Permit(
                    PreplannedMapTrigger.InternetAvailableRecieved,
                    PreplannedMapState.SyncReady
                )
                .Permit(
                    PreplannedMapTrigger.InternetUnavailableRecieved,
                    PreplannedMapState.UseLocal
                );

            // If internet is available, then the state is SyncReady.
            stateMachine
                .Configure(PreplannedMapState.SyncReady)
                .OnEntry(async () =>
                {
                    // TODO: Items in local settings should be moved to the settings service.

                    Guard.Against.Null(
                        localSettings,
                        nameof(localSettings),
                        "GetPreplannedMapService, StateMachine (SyncReady): localSettings can not be null or empty."
                    );

                    var WebMapId = (string?)localSettings.Values["secchiWebMapIdKey"];
                    if (WebMapId == null || WebMapId == "")
                    {
                        // TODO: If WebMapId is null or empty, send a message to the UI.
                        logger.LogError(
                            DownloadPreplannedEvent,
                            "GetPreplannedMapService, StateMachine (SyncReady): Downloading a preplanned map area is not possible without a PortalItemId. Configure a valid web map identifier in settings."
                        );
                        Guard.Against.NullOrEmpty(
                            WebMapId,
                            nameof(WebMapId),
                            "GetPreplannedMapService, StateMachine (SyncReady): Downloading a preplanned map area is not possible without a PortalItemId. Configure a valid web map identifier in settings."
                        );
                    }

                    // Set to true to cause InitializeOnlineAsync to be called at every run instead of waiting
                    // for a specified amount of time to pass between checks for updates.
                    var checkAnyway = false;

                    // Determine if a preplanned map should be downloaded
                    // by getting the setting via the downloadOfflineMapKey.
                    var downloadOfflineMap = await localSettingsService.ReadSettingAsync<bool>(
                        downloadOfflineMapKey
                    );
                    // When downloadOfflineMap is true, the current offline map
                    // package path contents will be deleted. This will trigger a
                    // download of a new offline map package.
                    // Delete the offline map is a one-shot operation. Once the
                    // offline map is deleted, the deleteOfflineMapKey is set to false.
                    if (downloadOfflineMap)
                    {
                        if (downloadOfflineMap)
                        {
                            logger.LogInformation(
                                DownloadPreplannedEvent,
                                "GetPreplannedMapService, InitializeOnlineAsync: A download of the offline map has been requested, deleting previous downloaded maps."
                            );
                        }

                        DeleteDownloadedMaps(mapPackagePath);

                        // Preplanned download request is one-shot, so set the request indicator
                        // back to false using the downloadOfflineMapKey.
                        await localSettingsService.SaveSettingAsync(downloadOfflineMapKey, false);
                    }
                    var dateMapLastUpdated = await GetLastMapUpdateAsync(
                        mapLastCheckedforUpdateKey
                    );
                    var hoursBetweenUpdateChecks = await localSettingsService.ReadSettingAsync<int>(
                        hoursBetweenUpdateChecksKey
                    );

                    var elapsedSinceLastUpdate = DateTime.UtcNow - dateMapLastUpdated;
                    if (
                        (elapsedSinceLastUpdate.TotalHours >= hoursBetweenUpdateChecks)
                        || checkAnyway
                        || downloadOfflineMap
                    )
                    {
                        if (checkAnyway || downloadOfflineMap)
                        {
                            logger.LogInformation(
                                DownloadPreplannedEvent,
                                "GetPreplannedMapService, StateMachine (SyncReady): checkAnyway or downloadOfflineMap has been set, InitializeOnlineAsync will be called irrespective of when last checked."
                            );
                        }
                        else
                        {
                            logger.LogInformation(
                                DownloadPreplannedEvent,
                                "GetPreplannedMapService, StateMachine (SyncReady): Map was last checked for updates {elapsedSinceLastUpdate} hour(s) ago, checking for update.",
                                elapsedSinceLastUpdate.TotalHours
                            );
                        }
                        await InitializeOnlineAsync(PreplannedMapName, WebMapId, offlineDataFolder);
                    }
                    else
                    {
                        logger.LogInformation(
                            DownloadPreplannedEvent,
                            "GetPreplannedMapService, StateMachine (SyncReady): Update check set to {hoursBetweenUpdateChecks:f1} hour(s) which has not elapsed, using stored map. ",
                            hoursBetweenUpdateChecks
                        );
                        logger.LogInformation(
                            DownloadPreplannedEvent,
                            "GetPreplannedMapService, StateMachine (SyncReady): Map was last checked for updates {elapsedSinceLastUpdate:f1} hour(s) ago.",
                            elapsedSinceLastUpdate.TotalHours
                        );
                        await GetOfflineMap(mapPackagePath);
                    }
                });

            // If internet is not available, then the state is UseLocal.
            stateMachine
                .Configure(PreplannedMapState.UseLocal)
                .OnEntry(async () =>
                {
                    logger.LogInformation(
                        DownloadPreplannedEvent,
                        "GetPreplannedMapService, StateMachine (SyncReady): Internet access is not available."
                    );
                    await GetOfflineMap(mapPackagePath);
                });

            // Write unhandled trigger to log.
            stateMachine.OnUnhandledTrigger(
                (state, trigger) =>
                {
                    // Log to error.
                    logger.LogError(
                        DownloadPreplannedEvent,
                        "GetPreplannedMapService, StateMachinee (UseLocal): Unhandled trigger {trigger} in state {state}.",
                        trigger,
                        state
                    );
                }
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, Exception: {exception}.",
                exception.ToString()
            );
        }

        // Start the state machine.
        // stateMachine.Fire(PreplannedMapTrigger.Startup);
    }

    // Cause map download by calling this with a true value.
    private async void CauseMapDownload(bool setDelete)
    {
        Guard.Against.Null(
            localSettingsService,
            nameof(localSettingsService),
            "GetPreplannedMapService, CauseMapDownload: localSettingsService can not be null."
        );

        // Change the download setting.
        await localSettingsService.SaveSettingAsync(downloadOfflineMapKey, setDelete);
    }

    // Get the current network status and use that to trigger InternetAvailableRecieved and InternetUnavailableRecieved.
    private async Task GetNetworkStatusAsync()
    {
        try
        {
            var networkStatus =
                await WeakReferenceMessenger.Default.Send<NetworkStatusRequestMessage>();
            logger.LogDebug(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, GetNetworkStatusAsync: NetworkStatusRequestMessage, isInternetAvailable: {isInternetAvailable}.",
                networkStatus.IsInternetAvailable
            );

            if (networkStatus.IsInternetAvailable == true)
            {
                stateMachine.Fire(PreplannedMapTrigger.InternetAvailableRecieved);
            }
            else
            {
                stateMachine.Fire(PreplannedMapTrigger.InternetUnavailableRecieved);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, GetNetworkStatusAsync: Exception: {exception}.",
                exception.ToString()
            );
        }
    }

    private async Task InitializeOnlineAsync(
        string portalItemTitle,
        string webMapId,
        string offlineDataFolder
    )
    {
        // var mapPackagePath = string.Empty;
        try
        {
            // Close the current mobile package if mobileMapPackage is not null.
            mobileMapPackage?.Close();

            Guard.Against.NullOrEmpty(
                portalItemTitle,
                nameof(portalItemTitle),
                "GetPreplannedMapService, InitializeOnlineAsync: portalItemTitle can not be null or empty."
            );
            Guard.Against.NullOrEmpty(
                webMapId,
                nameof(webMapId),
                "GetPreplannedMapService, InitializeOnlineAsync: webMapId can not be null or empty."
            );
            Guard.Against.NullOrEmpty(
                offlineDataFolder,
                nameof(offlineDataFolder),
                "GetPreplannedMapService, InitializeOnlineAsync: offlineDataFolder can not be null or empty."
            );

            // mapPackagePath = Path.Combine(offlineDataFolder, portalItemTitle);
            // Get the current value of mapPackagePath.
            var mapPackagePath = await GetMapPackagePath(packagePathKey);
            Guard.Against.NullOrEmpty(
                mapPackagePath,
                nameof(mapPackagePath),
                "GetPreplannedMapService, InitializeOnlineAsync: mapPackagePath can not be null or empty."
            );

            logger.LogDebug(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, InitializeOnlineAsync: mapPackagePath: {mapPackagePath}.",
                mapPackagePath
            );

            // Create the ArcGIS Online portal.
            var portal = await ArcGISPortal.CreateAsync();

            // Get the Cullaby secchi web map item using its ID.
            var webmapItem = await PortalItem.CreateAsync(portal, webMapId);

            // Create an offline map task for the web map item.
            var offlineMapTask = await OfflineMapTask.CreateAsync(webmapItem);

            // Create a list of preplanned map areas from the current web map.
            var preplannedAreas = await offlineMapTask.GetPreplannedMapAreasAsync();

            // Log the number of preplanned areas.
            logger.LogDebug(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, InitializeOnlineAsync: Number of preplanned areas: {preplannedAreas}.",
                preplannedAreas.Count
            );

            // List the preplanned areas.
            foreach (var area in preplannedAreas)
            {
                logger.LogDebug(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, InitializeOnlineAsync: Preplanned area: {area}.",
                    area.PortalItem!.Title
                );
            }

            // Find the preplanned area and store it.
            var selectedArea = preplannedAreas.Where(
                area => area.PortalItem!.Title == portalItemTitle
            );

            Guard.Against.Null(
                selectedArea,
                nameof(selectedArea),
                "GetPreplannedMapService, InitializeOnlineAsync: selectedArea can not be null."
            );

            // Get the preplanned area (should be the only item in the list).
            var offlineMapArea = selectedArea.First();

            if (offlineMapArea.PortalItem!.Title != portalItemTitle)
            {
                // TODO: Notify the user that the map that is being downloaded is not the one in the settings.

                logger.LogError(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, InitializeOnlineAsync: offlineMapArea.PortalItem.Title ({offlineMapArea.PortalItem.Title}) does not equal to portalItemTitle ({portalItemTitle}). This will result in unpredictable behavior.",
                    offlineMapArea.PortalItem!.Title,
                    portalItemTitle
                );
            }

            // await SetMapPackagePath(packagePathKey, mapPackagePath);

            logger.LogDebug(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, InitializeOnlineAsync: Planning to store {PortalItem} at {offlineDataFolder}.",
                offlineMapArea.PortalItem.Title,
                offlineDataFolder
            );
            // If an area has been downloaded, get that area.
            if (Directory.Exists(mapPackagePath))
            {
                try
                {
                    logger.LogDebug(
                        DownloadPreplannedEvent,
                        "GetPreplannedMapService, InitializeOnlineAsync: {PortalItem} at {offlineDataFolder} exists, getting locally stored map.",
                        offlineMapArea.PortalItem.Title,
                        offlineDataFolder
                    );

                    // Open local offline map package.
                    mobileMapPackage = await MobileMapPackage.OpenAsync(mapPackagePath);

                    // Get the first map in mobileMapPackage throwing an exception if null.
                    var map = mobileMapPackage.Maps[0];
                    Guard.Against.Null(
                        map,
                        nameof(map),
                        "GetPreplannedMapService, InitializeOnlineAsync: First map in mobileMapPackage is null."
                    );

                    Guard.Against.Null(
                        localSettingsService,
                        nameof(localSettingsService),
                        "GetPreplannedMapService, InitializeOnlineAsync: localSettingService is null."
                    );

                    // If a scheduled update is available for the preplanned map, then delete the offline map directory,
                    // call DownloadMapAreaAsync, then just treat it as an offline map.
                    if (await IsScheduledUpdateAvailableAsync(map))
                    {
                        // Close the current mobile package if mobileMapPackage is not null.
                        mobileMapPackage?.Close();
                        DeleteDownloadedMaps(mapPackagePath);
                        await DownloadMapAreaAsync(offlineMapArea, offlineMapTask, mapPackagePath);
                        await GetOfflineMap(mapPackagePath);
                    }
                    else
                    {
                        // A scheduled update is not available, so display the map.
                        // Send notification that the map has changed.
                        // preplannedMapModel.Map = map;
                        SendMapUpdate(map);
                    }
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        DownloadPreplannedEvent,
                        "GetPreplannedMapService, InitializeOnlineAsync: mapPackagePath does not exist: {exception}.",
                        exception.ToString()
                    );
                }
            }
            // If there is not an area available locally,
            // then download it and treat it as an offline map.
            else
            {
                logger.LogDebug(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, InitializeOnlineAsync: No local map, calling DownloadMapAreaAsync."
                );
                await DownloadMapAreaAsync(offlineMapArea, offlineMapTask, mapPackagePath);
                await GetOfflineMap(mapPackagePath);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                DownloadPreplannedEvent,
                "Exception generated GetPreplannedMapService, GetPreplannedMap: {exception}.",
                exception.ToString()
            );
        }
    }

    private async Task Initialization()
    {
        bool checkedInitialRun,
            checkedHoursBetweenUpdates,
            checkedDeleteOfflineMap,
            checkedCauseMapDownload;
        try
        {
            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "GetPreplannedMapService, Initialization: localSettingsService can not be null."
            );
            // Track whether all initial run conditions have been checked (has been set or not);
            checkedInitialRun = false;
            // Boolean to track if this is the first time the app has been run.
            bool? initialRun;
            // Check to see if this is the first time the app has been run.
            initialRun = await localSettingsService.ReadSettingAsync<bool>("InitialRun");
            if (initialRun == null || initialRun == true)
            {
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, Initialization: initialRun is null or true, performing initialization and setting initialRun to false."
                );
                await localSettingsService.SaveSettingAsync("InitialRun", false);

                /*

                // Set the mapPackagePath to the default value.
                var mapPackagePath = Path.Combine(offlineDataFolder, PreplannedMapName);
                // Log mapPackagePath.
                logger.LogDebug(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, Initialization: mapPackagePath: {mapPackagePath}.",
                    mapPackagePath
                );
                // Save the mapPackagePath for future runs.
                await SetMapPackagePath(packagePathKey, mapPackagePath);

                */

                checkedInitialRun = true;
            }
            else
            {
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, Initialization: initialRun is false, this is not the first time the app has been run."
                );
                checkedInitialRun = true;
            }
            // Track whether all hoursBetweenUpdates conditions have been checked (has been set or not);
            checkedHoursBetweenUpdates = false;
            // Integer to track the number of hours between update checks.
            int? hoursBetweenUpdates;
            hoursBetweenUpdates = await localSettingsService.ReadSettingAsync<int>(
                hoursBetweenUpdateChecksKey
            );
            if (hoursBetweenUpdates == null || hoursBetweenUpdates == 0)
            {
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, Initialization: hoursBetweenUpdates is null or 0, setting to default value of {hoursBetweenUpdateChecks}.",
                    defaultHoursBetweenUpdateChecks
                );
                await localSettingsService.SaveSettingAsync(
                    hoursBetweenUpdateChecksKey,
                    defaultHoursBetweenUpdateChecks
                );
                checkedHoursBetweenUpdates = true;
            }
            else
            {
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, Initialization: hoursBetweenUpdates is {hoursBetweenUpdates}.",
                    hoursBetweenUpdates
                );
                checkedHoursBetweenUpdates = true;
            }
            // Track whether all deleteOfflineMap conditions have been checked (has been set or not);
            checkedDeleteOfflineMap = false;
            // Boolean to track if the offline map should be deleted.
            bool? deleteOfflineMapSetting;
            deleteOfflineMapSetting = await localSettingsService.ReadSettingAsync<bool>(
                deleteOfflineMapKey
            );
            if (deleteOfflineMapSetting == null)
            {
                // If the setting is null, then set it to false.
                await localSettingsService.SaveSettingAsync(deleteOfflineMapKey, false);
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, Initialization: deleteOfflineMapSetting is null, setting to false."
                );

                checkedDeleteOfflineMap = true;
            }
            else
            {
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, Initialization: deleteOfflineMapSetting is {deleteOfflineMapSetting}.",
                    deleteOfflineMapSetting
                );
                checkedDeleteOfflineMap = true;
            }

            // Track whether all causeMapDownload conditions have been checked (has been set or not);
            checkedCauseMapDownload = false;
            // Boolean to track if the offline map should be deleted.
            bool? causeMapDownloadSetting;
            causeMapDownloadSetting = await localSettingsService.ReadSettingAsync<bool>(
                downloadOfflineMapKey
            );
            if (causeMapDownloadSetting == null)
            {
                // If the setting is null, then set it to false.
                await localSettingsService.SaveSettingAsync(downloadOfflineMapKey, false);
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, Initialization: causeMapDownloadSetting is null, setting to false."
                );

                checkedCauseMapDownload = true;
            }
            else
            {
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, Initialization: causeMapDownloadSetting is {causeMapDownloadSetting}.",
                    causeMapDownloadSetting
                );
                checkedCauseMapDownload = true;
            }

            // If all checks have been done, trigger initialization complete.
            if (
                checkedInitialRun
                && checkedHoursBetweenUpdates
                && checkedDeleteOfflineMap
                && checkedCauseMapDownload
            )
            {
                stateMachine.Fire(PreplannedMapTrigger.InitializationComplete);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, Initialization: Exception: {exception}.",
                exception.ToString()
            );
        }
    }

    private async Task<string> GetMapPackagePath(string packagePathKey)
    {
        Guard.Against.NullOrEmpty(
            packagePathKey,
            nameof(packagePathKey),
            "GetPreplannedMapService, GetMapPackagePath: packagePathKey can not be null or empty."
        );
        Guard.Against.Null(
            localSettingsService,
            nameof(localSettingsService),
            "GetPreplannedMapService, GetMapPackagePath: localSettingsService can not be null."
        );

        var mapPackagePathStored = await localSettingsService.ReadSettingAsync<string>(
            packagePathKey
        );
        if (mapPackagePathStored is not null)
        {
            logger.LogInformation(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, GetMapPackagePath: mapPackagePathStored value: {mapPackagePathStored}",
                mapPackagePathStored.ToString()
            );
            return mapPackagePathStored;
        }
        else
        {
            logger.LogError(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, GetMapPackagePath: mapPackagePathStored is null, which is expected for the first time app is executed."
            );
            //TODO: Determine what to display in the UI when the app is first executed and no offline path has been set.
            return string.Empty;
        }
    }

    private async Task SetMapPackagePath(string packagePathKey, string mapPackagePath)
    {
        if (localSettingsService is not null)
        {
            // Path where offline maps are stored. Used for offline retrieval.
            await localSettingsService.SaveSettingAsync(packagePathKey, mapPackagePath.ToString());
        }
        else
        {
            throw new InvalidOperationException(message: "localSettingsService is null.");
        }
    }

    private async Task<DateTime> GetLastMapUpdateAsync(string mapLastCheckedforUpdateKey)
    {
        Guard.Against.NullOrEmpty(
            mapLastCheckedforUpdateKey,
            nameof(mapLastCheckedforUpdateKey),
            "GetPreplannedMapService, GetLastMapUpdateAsync: mapLastCheckedforUpdateKey can not be null or empty."
        );

        DateTime mapLastChangedDefault = default;

        Guard.Against.Null(
            localSettingsService,
            nameof(localSettingsService),
            "GetPreplannedMapService, GetLastMapUpdateAsync: localSettingsService can not be null."
        );

        var mapLastCheckedStored = await localSettingsService.ReadSettingAsync<DateTime>(
            mapLastCheckedforUpdateKey
        );
        if (mapLastCheckedStored != default)
        {
            logger.LogInformation(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, GetLastMapUpdateAsync: mapLastCheckedStored value: {mapLastCheckedStored}",
                mapLastCheckedStored.ToString()
            );
            return mapLastCheckedStored;
        }
        else
        {
            logger.LogInformation(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, GetLastMapUpdateAsync: mapLastCheckedStored is set to the default value for DateTime: {mapLastCheckedStored} which is expected for the first time app is executed.",
                mapLastCheckedStored.ToString()
            );
            return mapLastChangedDefault;
        }
    }

    private async Task<bool> IsScheduledUpdateAvailableAsync(Map map)
    {
        var scheduledUpdateNeeded = true;

        Guard.Against.Null(
            map,
            nameof(map),
            "GetPreplannedMapService, IsScheduledUpdateAvailableAsync: map can not be null."
        );

        try
        {
            // Get the information for offline updates.

            // create the offlineMapSyncTask to be used to check for scheduled updates
            var offlineMapSyncTask = await OfflineMapSyncTask.CreateAsync(map);

            var offlineMap = await offlineMapSyncTask.CheckForUpdatesAsync();

            Guard.Against.Null(
                offlineMap,
                nameof(offlineMap),
                "GetPreplannedMapService, IsScheduledUpdateAvailableAsync: offlineMap can not be null."
            );
            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "GetPreplannedMapService, IsScheduledUpdateAvailableAsync: localSettingsService can not be null."
            );

            var mapLastCheckedforUpdate = DateTime.UtcNow;
            await localSettingsService.SaveSettingAsync(
                mapLastCheckedforUpdateKey,
                mapLastCheckedforUpdate
            );
            logger.LogInformation(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, IsScheduledUpdateAvailableAsync: Map last checked for update {mapLastCheckedforUpdate} UTC.",
                mapLastCheckedforUpdate.ToString()
            );

            // Check if there are updates that can be downloaded.
            if (offlineMap.DownloadAvailability == OfflineUpdateAvailability.Available)
            {
                // Get the size of the update.
                double updateSize = offlineMap.ScheduledUpdatesDownloadSize / 1024;

                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, IsScheduledUpdateAvailableAsync: Update available. The update size is {updateSize} kilobytes.",
                    updateSize
                );
                scheduledUpdateNeeded = true;
            }
            else
            {
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, IsScheduledUpdateAvailableAsync: The preplanned map area is up to date."
                );
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, IsScheduledUpdateAvailableAsync: The preplanned map OfflineUpdateAvailability: {DownloadAvailability}",
                    offlineMap.DownloadAvailability
                );
                scheduledUpdateNeeded = false;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                DownloadPreplannedEvent,
                "Exception generated GetPreplannedMapService, IsScheduledUpdateAvailableAsync: {exception}.",
                exception.ToString()
            );
        }
        return scheduledUpdateNeeded;
    }

    private async Task DownloadMapAreaAsync(
        PreplannedMapArea mapArea,
        OfflineMapTask offlineMapTask,
        string mapPackagePath
    )
    {
        Guard.Against.Null(
            mapArea,
            nameof(mapArea),
            "GetPreplannedMapService, DownloadMapAreaAsync: mapArea can not be null."
        );
        Guard.Against.Null(
            offlineMapTask,
            nameof(offlineMapTask),
            "GetPreplannedMapService, DownloadMapAreaAsync: offlineMapTask can not be null."
        );
        Guard.Against.NullOrEmpty(
            mapPackagePath,
            nameof(mapPackagePath),
            "GetPreplannedMapService, DownloadMapAreaAsync: mapPackagePath can not be null or empty."
        );

        try
        {
            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "GetPreplannedMapService, DownloadMapAreaAsync: localSettingsService can not be null."
            );
            // Create download parameters.
            var parameters =
                await offlineMapTask.CreateDefaultDownloadPreplannedOfflineMapParametersAsync(
                    mapArea
                );

            // Set the update mode to not receive updates.
            parameters.UpdateMode = PreplannedUpdateMode.NoUpdates;

            logger.LogDebug(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, DownloadMapAreaAsync: Starting preplanned map download, storing map at {OfflineFolder}.",
                offlineDataFolder
            );

            // If the directory where the offline map is stored does not exist, create it.
            if (Directory.Exists(mapPackagePath) != true)
            {
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, DownloadMapAreaAsync: Directory {mapPackagePath} does not exist, creating it.",
                    mapPackagePath
                );
                Directory.CreateDirectory(mapPackagePath);
            }

            // Create the job.
            var job = offlineMapTask.DownloadPreplannedOfflineMap(parameters, mapPackagePath);
            // Set up event to update the progress while the job is in progress.
            job.ProgressChanged += OnJobProgressChanged;

            try
            {
                // Download the area.
                var results = await job.GetResultAsync();

                // Close the current mobile package.
                mobileMapPackage?.Close();

                // Set the current mobile map package.
                mobileMapPackage = results.MobileMapPackage;

                // Handle possible errors and show them to the user.
                if (results.HasErrors)
                {
                    // Accumulate all layer and table errors into a single message.
                    var errors = "";

                    foreach (var layerError in results.LayerErrors)
                    {
                        errors = $"{errors}\n{layerError.Key.Name} {layerError.Value.Message}";
                    }

                    foreach (var tableError in results.TableErrors)
                    {
                        errors = $"{errors}\n{tableError.Key.TableName} {tableError.Value.Message}";
                    }

                    // Show the message.
                    logger.LogError(
                        DownloadPreplannedEvent,
                        "GetPreplannedMapService, DownloadMapAreaAsync: Mobile map package load failed {errors}",
                        errors
                    );
                }
                else
                {
                    var mapLastCheckedforUpdate = DateTime.UtcNow;
                    await localSettingsService.SaveSettingAsync(
                        mapLastCheckedforUpdateKey,
                        mapLastCheckedforUpdate
                    );
                }
            }
            catch (Exception exception)
            {
                logger.LogError(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, DownloadMapAreaAsync: Exception generated: {exception}.",
                    exception.ToString()
                );
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                DownloadPreplannedEvent,
                "Exception generated GetPreplannedMapService, DownloadMapAreaAsync: Exception: {exception}.",
                exception.ToString()
            );
        }
    }

    private void OnJobProgressChanged(object? sender, EventArgs e)
    {
        Guard.Against.Null(
            sender,
            nameof(sender),
            "GetPreplannedMapService, OnJobProgressChanged: sender can not be null."
        );

        // Get the download progress.
        var downloadJob = sender as DownloadPreplannedOfflineMapJob;
        // Show the progress complete.
        var progress = downloadJob!.Progress;
        logger.LogDebug(
            DownloadPreplannedEvent,
            "GetPreplannedMapService, OnJobProgressChanged: Preplanned map download progress: {progress}",
            progress
        );
        // TODO: Send a message to the UI to update the progress bar.
    }

    private void SendMapUpdate(Map updatedMap)
    {
        if (
            updatedMap.InitialViewpoint is not null
            && updatedMap.InitialViewpoint.TargetGeometry.Extent is not null
        )
        {
            logger.LogDebug(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, SendMapUpdate: sending MapExtentChangedMessage MaxExtent (Wgs84): {extentValue}",
                updatedMap.InitialViewpoint.TargetGeometry.Extent
                    .Project(SpatialReferences.Wgs84)
                    .ToString()
            );

            // Set the current currentMapEnvelope.
            currentMapEnvelope = updatedMap.InitialViewpoint.TargetGeometry.Extent;

            // Send notification that the map extent has changed.
            WeakReferenceMessenger.Default.Send(
                new MapExtentChangedMessage(
                    new MainMapExtent(updatedMap.InitialViewpoint.TargetGeometry.Extent)
                )
            );
        }
        else
        {
            logger.LogError(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, SendMapUpdate: MapExtentChangedMessage MaxExtent: null. This could cause the application to be unreliable."
            );
        }

        // Send notification to the UI that the map has changed.
        preplannedMapModel.Map = updatedMap;
    }

    private async Task GetOfflineMap(string mapPackagePath)
    {
        try
        {
            if (mobileMapPackage is not null)
            {
                // Close the current mobile package.
                mobileMapPackage?.Close();
            }

            if (Directory.Exists(mapPackagePath))
            {
                try
                {
                    logger.LogDebug(
                        DownloadPreplannedEvent,
                        "GetPreplannedMapService, GetOfflineMap: Map in {mapPackagePath} exists, getting locally stored map.",
                        mapPackagePath
                    );

                    // Open local offline map package.
                    mobileMapPackage = await MobileMapPackage.OpenAsync(mapPackagePath);

                    // Load the package.
                    await mobileMapPackage.LoadAsync();

                    if (mobileMapPackage.Maps.Count == 0)
                    {
                        logger.LogError(
                            DownloadPreplannedEvent,
                            "GetPreplannedMapService, GetOfflineMap: mapPackage directore exists, but mobileMapPackage.Maps.Count is 0."
                        );

                        throw new Exception(
                            "GetPreplannedMapService, GetOfflineMap: mapPackage directore exists, but mobileMapPackage.Maps.Count is 0."
                        );
                    }

                    foreach (var packageMap in mobileMapPackage.Maps)
                    {
                        Guard.Against.Null(
                            packageMap.Item,
                            nameof(packageMap.Item),
                            "GetPreplannedMapService, GetOfflineMap: while listing maps, packageMap.Item is null."
                        );
                    }

                    // Get the first map in mobileMapPackage throwing an exception if null.
                    var map = mobileMapPackage.Maps[0];
                    Guard.Against.Null(
                        map,
                        nameof(map),
                        "GetPreplannedMapService, GetOfflineMap: First map in mobileMapPackage is null."
                    );

                    // Log the total number of layers in the map.
                    logger.LogTrace(
                        DownloadPreplannedEvent,
                        "GetPreplannedMapService, GetOfflineMap: There are {layers} total layers in the local map.",
                        map.AllLayers.Count
                    );

                    // Log the name of each layer in the map.
                    foreach (var layer in map.AllLayers)
                    {
                        logger.LogTrace(
                            DownloadPreplannedEvent,
                            "GetPreplannedMapService, GetOfflineMap: Layer name: {layerName}",
                            layer.Name
                        );
                    }

                    // Get the Layer Information for each layer in the map.
                    foreach (var layer in map.AllLayers)
                    {
                        await layer.LoadAsync();
                        logger.LogTrace(
                            DownloadPreplannedEvent,
                            "GetPreplannedMapService, GetOfflineMap: Layer Information: {layerToString}",
                            layer.ToString()
                        );
                    }

                    // Log the number of operational layers in the map.
                    logger.LogTrace(
                        DownloadPreplannedEvent,
                        "GetPreplannedMapService, GetOfflineMap: There are {layers} operational layers in the local map.",
                        map.OperationalLayers.Count
                    );

                    // Get the feature layers from the operational layers.
                    foreach (var layer in map.OperationalLayers)
                    {
                        if (layer is FeatureLayer featureLayer)
                        {
                            logger.LogTrace(
                                DownloadPreplannedEvent,
                                "GetPreplannedMapService, GetOfflineMap: Feature Layer name: {layerName}",
                                featureLayer.Name
                            );
                        }
                    }

                    // Iterate over the maps in the package
                    foreach (var mapDetail in mobileMapPackage.Maps)
                    {
                        // Iterate over the layers in the map
                        foreach (var layer in mapDetail.OperationalLayers)
                        {
                            // Check if the layer is a feature layer
                            if (layer is FeatureLayer featureLayer)
                            {
                                // Load the layer.
                                await featureLayer.LoadAsync();

                                // Select all of the features in the layer.
                                var featureQueryResult = await featureLayer.SelectFeaturesAsync(
                                    new QueryParameters { WhereClause = "1=1" },
                                    SelectionMode.New
                                );

                                // Log each of the selected features.
                                foreach (var feature in featureQueryResult)
                                {
                                    // Log the feature attributes.
                                    foreach (var attribute in feature.Attributes)
                                    {
                                        logger.LogTrace(
                                            DownloadPreplannedEvent,
                                            "GetPreplannedMapService, GetOfflineMap: Feature attribute name: {attributeName}, value: {attributeValue}",
                                            attribute.Key,
                                            attribute.Value
                                        );
                                    }
                                }
                                Guard.Against.Null(
                                    featureLayer.FeatureTable,
                                    nameof(featureLayer.FeatureTable),
                                    "GetPreplannedMapService, GetOfflineMap: featureLayer.FeatureTable is null."
                                );

                                // Get the feature table of the feature layer
                                var featureTable = featureLayer.FeatureTable;

                                // Log that there is a featureTable.
                                logger.LogTrace(
                                    DownloadPreplannedEvent,
                                    "GetPreplannedMapService, GetOfflineMap: Feature Table name: {tableName}",
                                    featureTable.TableName
                                );

                                // Log the fields in the featureTable.
                                foreach (var field in featureTable.Fields)
                                {
                                    logger.LogTrace(
                                        DownloadPreplannedEvent,
                                        "GetPreplannedMapService, GetOfflineMap: Field name: {fieldName}",
                                        field.Name
                                    );
                                }
                            }
                        }
                    }

                    // Send notification that the map has changed.
                    // preplannedMapModel.Map = map;
                    SendMapUpdate(map);
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        DownloadPreplannedEvent,
                        "GetPreplannedMapService, GetOfflineMap: Unable to get preplanned map from {mapPackagePath} with error {exception}.",
                        mapPackagePath,
                        exception.ToString()
                    );
                }
            }
            else
            {
                //TODO: This might be a good place to send an alert back to the UI (first use if offline won't work).

                logger.LogError(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, GetOfflineMap: The directory pointed to by mapPackagePath ({mapPackagePath}) does not exist."
                        + "One possibility is that this is the first time that the application has run and the device is offline.",
                    mapPackagePath
                );
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, GetOfflineMap: Exception: {exception}.",
                exception.ToString()
            );
        }
    }

    private void DeleteDownloadedMaps(string mapPackagePath)
    {
        try
        {
            logger.LogInformation(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, DeleteDownloadedMaps: Deleting offline maps at location {mapPackagePath}.",
                mapPackagePath
            );
            if (mapPackagePath is not null)
            {
                if (Directory.Exists(mapPackagePath))
                {
                    Directory.Delete(mapPackagePath, true);
                }
                else
                {
                    logger.LogInformation(
                        DownloadPreplannedEvent,
                        "GetPreplannedMapService, DeleteDownloadedMaps: Directory {mapPackagePath} does not exist, no need to delete the directory.",
                        mapPackagePath
                    );
                }
            }
            else
            {
                throw new ArgumentNullException(
                    paramName: nameof(mapPackagePath),
                    message: "mapPackagePath can not be null"
                );
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, DeleteDownloadedMaps: Exception: {exception}.",
                exception.ToString()
            );
        }
    }

    public async Task CheckForArcgisKey()
    {
        try
        {
            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "GetPreplannedMapService, CheckForArcgisKey: localSettingsService can not be null."
            );

            var arcgisKey = await localSettingsService.ReadSettingAsync<string>(arcgisApiKey);
            var offlineMapId = await localSettingsService.ReadSettingAsync<string>(
                offlineMapIdentifier
            );
            var arcgisKeyPresent = false;
            var offlineMapPresent = false;

            // If the arcgisKey is not null or blank, then set arcgisKeyPresent to true.
            if (arcgisKey is not null && arcgisKey != "")
            {
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, CheckForArcgisKey: arcgisKey is not null or blank."
                );
                // log the arcgisKey to debug.
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, CheckForArcgisKey: arcgisKey: {arcgisKey}.",
                    arcgisKey
                );

                arcgisKeyPresent = true;
            }
            else
            {
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, CheckForArcgisKey: arcgisKey is null or blank."
                );
                arcgisKeyPresent = false;
            }

            // If the offlineMapId is not null or blank, then set offlineMapPresent to true.
            if (offlineMapId is not null && offlineMapId != "")
            {
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, CheckForArcgisKey: offlineMapId is not null or blank."
                );
                // log the offlineMapId to debug.
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, CheckForArcgisKey: offlineMapId: {offlineMapId}.",
                    offlineMapId
                );

                offlineMapPresent = true;
            }
            else
            {
                logger.LogInformation(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, CheckForArcgisKey: offlineMapId is null or blank."
                );
                offlineMapPresent = false;
            }

            // If both the arcgisKey and offlineMapId are not null or blank, then send a message indicating that both have been set to some value.
            // This is used by the UI and the state machine to determine if the map can be displayed or if the user needs to be prompted to enter the arcgisKey or offlineMapId.
            if (arcgisKeyPresent && offlineMapPresent)
            {
                WeakReferenceMessenger.Default.Send(
                    new MapConfigurationMessage(
                        new PreplannedMapConfiguration(arcgisKeyPresent, offlineMapPresent)
                    )
                );
            }
            // If both the arcgisKey or offlineMapId are not valid, then send a message indicating the validity of each.
            // This is used by the UI to determine if user needs to be prompted to enter the arcgisKey or offlineMapId and by the state machine to delay start until both are valid.
            else
            {
                logger.LogDebug(
                    DownloadPreplannedEvent,
                    "GetPreplannedMapService, CheckForArcgisKey: arcgisKeyPresent: {arcgisKeyPresent}, offlineMapPresent: {offlineMapPresent}.",
                    arcgisKeyPresent,
                    offlineMapPresent
                );
                WeakReferenceMessenger.Default.Send(
                    new MapConfigurationMessage(
                        new PreplannedMapConfiguration(arcgisKeyPresent, offlineMapPresent)
                    )
                );
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                DownloadPreplannedEvent,
                "GetPreplannedMapService, CheckForArcgisKey: Exception: {exception}.",
                exception.ToString()
            );
        }
    }

    // Log state transitions.
    private void OnTransition(
        StateMachine<PreplannedMapState, PreplannedMapTrigger>.Transition transition
    )
    {
        logger.LogDebug(
            DownloadPreplannedEvent,
            "GetPreplannedMapService, OnTransition: Transitioned from {transition.Source} to {transition.Destination} via {transition.Trigger}.",
            transition.Source,
            transition.Destination,
            transition.Trigger
        );
    }
}
