using System.ComponentModel;
using System.Drawing;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.WinUI.Collections;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Location;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;
using Esri.ArcGISRuntime.UI.Editing;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NLog;
using NLog.Fluent;
using Stateless;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Models;
using WaterMeasurements.Services;
using WaterMeasurements.Services.IncrementalLoaders;
using WaterMeasurements.ViewModels;
using Windows.ApplicationModel.Store;
using Windows.Media.Capture.Frames;
using Windows.UI.Popups;
using WinRT;
using static WaterMeasurements.Models.PrePlannedMapConfiguration;
using Geometry = Esri.ArcGISRuntime.Geometry.Geometry;
using Symbol = Esri.ArcGISRuntime.Symbology.Symbol;

namespace WaterMeasurements.Views;

// Message to notify modules that the UIQueue has been set.
public class UIQueueSetMessage(UIQueue uiQueue) : ValueChangedMessage<UIQueue>(uiQueue) { }

// Message to notify modules that the MapPage has been unloaded.
public class MapPageUnloaded : ValueChangedMessage<MapPageUnloadedMessage>
{
    public MapPageUnloaded()
        : base(new MapPageUnloadedMessage()) { }
}

// Message to set the map to autopan.
public class SetMapAutoPanMessage(bool value) : ValueChangedMessage<bool>(value) { }

// Message to center the map.
public class SetMapCenterMessage(bool value) : ValueChangedMessage<bool>(value) { }

public partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }
    public SecchiViewModel SecchiView { get; }
    public MapConfigurationViewModel MapConfigurationView { get; }
    public DataCollectionViewModel DataCollectionView { get; }
    public SecchiConfigurationViewModel SecchiConfigurationView { get; }
    public NewLocationViewModel NewLocationView { get; }

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly SystemLocationDataSource systemLocation = new();

    // Current view for Secchi data and locations.
    // Obserable is defined  in MainPageModel.cs.
    private readonly SecchiPageSelection secchiPageSelection;

    // Current ArcGIS API key
    public string? apiKey;

    // Current WebMapId
    public string? webMapId;

    private struct SecchiChannelNumbers
    {
        public uint ObservationChannel { get; set; }
        public uint LocationChannel { get; set; }
        public uint GeoTriggerChannel { get; set; }
    }

    private SecchiChannelNumbers secchiChannelNumbers;

    // private FeatureTable? secchiLocationFeatures;

    // private FeatureLayer? secchiLocationLayer;

    // New location to add to the SecchiLocations table.
    private AddLocation addLocation;

    // New secchi measurement to add to the SecchiMeasurement table.
    private SecchiMeasurement secchiMeasurement;

    // Extent of current map
    private Geometry? extent;

    // Geometry editor to manage points on the map.
    private GeometryEditor? geometryEditor;

    // Graphics overlay to display points on the map.
    private GraphicsOverlay? graphicsOverlay;

    // Graphics overlay for point selection.
    private readonly GraphicsOverlay? selectionOverlay = new();

    // Current collection view.
    private string? currentCollectionView;

    // Timer to indicate when the Viewport has stopped changing.
    private readonly DispatcherTimer viewportTimer;

    // Flag to indicate if center or auto-pan buttons are selected.
    // This is also set to true at startup.
    // The purpose of this flag is to prevent the deselection of the
    // center and auto-pan buttons when the Viewport has stopped changing and
    // where the center and auto-pan buttons are typically deselected.
    private bool centerAutoPanSelected = false;

    private double geoTriggerDistance = 0;

    private bool secchiLocationAddPageSelected = false;

    [RelayCommand]
    private void ReCenter()
    {
        MapView.LocationDisplay.AutoPanMode = Esri.ArcGISRuntime
            .UI
            .LocationDisplayAutoPanMode
            .Recenter;
        // Clear the selected location.
        ClearLocationSelection();
    }

    [RelayCommand]
    private void AutoPan()
    {
        MapView.LocationDisplay.AutoPanMode = Esri.ArcGISRuntime
            .UI
            .LocationDisplayAutoPanMode
            .Navigation;
        // Clear the selected location.
        ClearLocationSelection();
    }

    [RelayCommand]
    public void StoreDeveloperKeyAsync()
    {
        apiKey = ApiKeyArcGIS.Password;
        Logger.Debug("API key changed to: " + apiKey);

        Task.Run(async () =>
            {
                await ViewModel.StoreSettingByKeyAsync(
                    PrePlannedMapConfiguration.Item[Key.ArcgisApiKey],
                    apiKey
                );
            })
            .Wait();
    }

    [RelayCommand]
    public void StoreWebMapIdAsync()
    {
        Logger.Debug("webMapId changed to: " + WebMapId.Text);

        Task.Run(async () =>
            {
                await ViewModel.StoreSettingByKeyAsync(
                    PrePlannedMapConfiguration.Item[Key.OfflineMapIdentifier],
                    WebMapId.Text
                );
            })
            .Wait();
    }

    private void RevealApiKey_Changed(object sender, RoutedEventArgs routedEventArgs)
    {
        _ = sender;
        _ = routedEventArgs;
        if (RevealApiKey.IsChecked == true)
        {
            ApiKeyArcGIS.PasswordRevealMode = PasswordRevealMode.Visible;
        }
        else
        {
            ApiKeyArcGIS.PasswordRevealMode = PasswordRevealMode.Hidden;
        }
    }

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        SecchiView = App.GetService<SecchiViewModel>();
        MapConfigurationView = App.GetService<MapConfigurationViewModel>();
        DataCollectionView = App.GetService<DataCollectionViewModel>();
        SecchiConfigurationView = App.GetService<SecchiConfigurationViewModel>();
        NewLocationView = App.GetService<NewLocationViewModel>();

        Logger.Debug("MainPage.xaml.cs, MainPage: Starting");

        // Set the initial Secchi Measurements page to the Collection Table.
        // This is used by the UI to determine which Secchi page to display.
        secchiPageSelection = new SecchiPageSelection { SecchiSelectView = "SecchiLoading" };

        secchiChannelNumbers = new();

        InitializeComponent();

        // Configure a timer to indicate when the Viewport has stopped changing.
        viewportTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        viewportTimer.Tick += (sender, eventArgs) =>
        {
            // Log to trace that the Viewport has stopped changing.
            Logger.Trace("MainPage.xaml.cs, MainPage: Viewport has stopped changing.");
            // Stop the timer.
            viewportTimer.Stop();
            // If the center or auto-pan buttons have been selected, then do not deselect them.
            if (!centerAutoPanSelected)
            {
                // Unselect the Center and AutoPan buttons.
                DeselectCenterAutoPan();
                centerAutoPanSelected = false;
            }
            // centerAutoPanSelected = false;
        };

        // Send the UI Dispatcher Queue to subscribers.
        DispatcherQueue.TryEnqueue(() =>
        {
            WeakReferenceMessenger.Default.Send(
                new UIQueueSetMessage(new UIQueue(DispatcherQueue.GetForCurrentThread()))
            );
        });

        // Handle the SetMapAutoPanMessage message.
        WeakReferenceMessenger.Default.Register<SetMapAutoPanMessage>(
            this,
            (recipient, message) =>
            {
                // Log to trace the value of message.Value with a label.
                Logger.Trace(
                    "MainPage.xaml.cs, MainPage: SetMapAutoPanMessage, message.Value: {messageValue}",
                    message.Value
                );

                MapView.LocationDisplay.AutoPanMode = Esri.ArcGISRuntime
                    .UI
                    .LocationDisplayAutoPanMode
                    .Navigation;
                centerAutoPanSelected = true;
                // Clear the selected location.
                ClearLocationSelection();
            }
        );

        // Handle the SetMapCenterMessage message.
        WeakReferenceMessenger.Default.Register<SetMapCenterMessage>(
            this,
            (recipient, message) =>
            {
                // Log to trace the value of message.Value with a label.
                Logger.Trace(
                    "MainPage.xaml.cs, MainPage: SetMapCenterMessage, message.Value: {messageValue}",
                    message.Value
                );

                MapView.LocationDisplay.AutoPanMode = Esri.ArcGISRuntime
                    .UI
                    .LocationDisplayAutoPanMode
                    .Recenter;
                centerAutoPanSelected = true;
                // Clear the selected location.
                ClearLocationSelection();
            }
        );

        // Handle the MapExtentChangedMessage.
        WeakReferenceMessenger.Default.Register<MapExtentChangedMessage>(
            this,
            (recipient, message) =>
            {
                extent = message.Value.Extent.Project(SpatialReferences.Wgs84);
                // Log to trace the value of message.Value with a label.
                Logger.Trace(
                    "MainPage.xaml.cs, MainPage: MapExtentChangedMessage, {envelope}",
                    extent.ToString()
                );
                var envelope = extent.As<Envelope>();
                // Log to trace the minX, minY, maxX, and maxY values of the envelope.
                Logger.Trace(
                    "MainPage.xaml.cs, MainPage: MapExtentChangedMessage, minX: {minX}, minY: {minY}, maxX: {maxX}, maxY: {maxY}",
                    envelope.XMin,
                    envelope.YMin,
                    envelope.XMax,
                    envelope.YMax
                );
            }
        );

        // Handle the SetSecchiSelectViewMessage message.
        WeakReferenceMessenger.Default.Register<SetSecchiSelectViewMessage>(
            this,
            (recipient, message) =>
            {
                // Log to trace the value of message.Value with a label.
                Logger.Trace(
                    "MainPage.xaml.cs, MainPage: SetSecchiSelectViewMessage, message.Value: {messageValue}",
                    message.Value
                );

                // Change the SecchiSelectView to the value of message.Value.
                // Do this on the UI thread.

                DispatcherQueue.TryEnqueue(() =>
                {
                    secchiPageSelection.SecchiSelectView = message.Value;
                });
            }
        );

        // Request the Secchi channel numbers from SecchiViewModel.
        var secchiChannelMessageResult =
            WeakReferenceMessenger.Default.Send<SecchiChannelRequestMessage>();
        if (secchiChannelMessageResult.Response.LocationChannel is not 0)
        {
            secchiChannelNumbers.LocationChannel = secchiChannelMessageResult
                .Response
                .LocationChannel;
            secchiChannelNumbers.ObservationChannel = secchiChannelMessageResult
                .Response
                .ObservationChannel;
            secchiChannelNumbers.GeoTriggerChannel = secchiChannelMessageResult
                .Response
                .GeoTriggerChannel;

            // Log to trace the individual channel numbers.
            Logger.Trace(
                "MainPage.xaml.cs, MainPage: SecchiChannelRequestMessage, ObservationChannel: {ObservationChannel}, LocationChannel: {LocationChannel}, GeoTriggerChannel: {GeoTriggerChannel}",
                secchiChannelNumbers.ObservationChannel,
                secchiChannelNumbers.LocationChannel,
                secchiChannelNumbers.GeoTriggerChannel
            );
        }

        // Register to get MapPageUnloadedMessage messages.
        WeakReferenceMessenger.Default.Register<MapPageUnloaded>(
            this,
            (recipient, message) =>
            {
                // Log to trace that the MapPageUnloaded message was received.
                Logger.Trace("MainPage.xaml.cs, MainPage: MapPageUnloaded message received.");
                if (MapView.GeometryEditor is not null)
                {
                    if (MapView.GeometryEditor.IsStarted)
                    {
                        MapView.GeometryEditor.Stop();
                    }
                }

                // Unregister all listeners.
                WeakReferenceMessenger.Default.UnregisterAll(this);
            }
        );

        currentCollectionView = "";

        Initialize();
    }

    private async void Initialize()
    {
        try
        {
            Logger.Debug("MainPage.xaml.cs, Initialize: Initializing MainPage");

            apiKey = await ViewModel.RetrieveSettingByKeyAsync<string>(
                PrePlannedMapConfiguration.Item[Key.ArcgisApiKey]
            );
            webMapId = await ViewModel.RetrieveSettingByKeyAsync<string>(
                PrePlannedMapConfiguration.Item[Key.OfflineMapIdentifier]
            );
            var preplannedMapName = await ViewModel.RetrieveSettingByKeyAsync<string>(
                PrePlannedMapConfiguration.Item[Key.PreplannedMapName]
            );

            if (string.IsNullOrEmpty(apiKey))
            {
                Logger.Error("MainPage.xaml.cs, Initialize: ApiKey key is null or empty.");
            }
            else
            {
                Logger.Debug(
                    "MainPage.xaml.cs, Initialize: ArcGIS API key initial value: {apiKey}.",
                    apiKey
                );
            }

            if (string.IsNullOrEmpty(webMapId))
            {
                Logger.Error("MainPage.xaml.cs, Initialize: WebMapId key is null or empty.");
            }
            else
            {
                Logger.Debug(
                    "MainPage.xaml.cs, Initialize: ArcGIS web map id initial value: {webMapId}.",
                    webMapId
                );
            }

            if (string.IsNullOrEmpty(preplannedMapName))
            {
                Logger.Error(
                    "MainPage.xaml.cs, Initialize: Preplanned map name key is null or empty."
                );
            }
            else
            {
                Logger.Debug(
                    "MainPage.xaml.cs, Initialize: Preplanned map name initial value: {preplannedMapName}.",
                    preplannedMapName
                );
            }

            SetLocationOverlay();

            // Register to get the current collection view.
            WeakReferenceMessenger.Default.Register<SetCurrentCollectionViewMessage>(
                this,
                (recipient, message) =>
                {
                    // Log the value of the message.
                    Logger.Debug(
                        "MainPage.xaml.cs, MainPage: SetCurrentCollectionViewMessage, Message value: {messageValue}.",
                        message.Value
                    );
                    // Set the current collection view.
                    SetLocationOverlay();
                }
            );

            /*
            if (MapView.GraphicsOverlays is not null)
            {
                // Create a graphics overlay and add it to the map view.
                graphicsOverlay = new GraphicsOverlay { Id = "SecchiLocations" };
                selectionOverlay = new GraphicsOverlay();
                // var secchiLocationPoints = new GraphicsOverlay();
                MapView.GraphicsOverlays.Add(graphicsOverlay);
                MapView.GraphicsOverlays.Add(selectionOverlay);

                // Load the Secchi locations onto the map in response to the FeatureTableMessage.
                if (secchiChannelNumbers.LocationChannel is not 0)
                {
                    // Register to get location featuretable messages on the secchiLocationsChannel.
                    WeakReferenceMessenger.Default.Register<FeatureTableMessage, uint>(
                        this,
                        secchiChannelNumbers.LocationChannel,
                        (recipient, message) =>
                        {
                            Logger.Trace(
                                "MainPage.xaml.cs, FeatureTableMessage, secchiLocationsChannel: {secchiLocationsChannel}, FeatureTable: {featureTable}.",
                                secchiChannelNumbers.LocationChannel,
                                message.Value.TableName
                            );
                            if (MapView.Map is not null)
                            {
                                secchiLocationFeatures = message.Value;

                                // create a where clause to get all the features
                                var queryParameters = new QueryParameters() { WhereClause = "1=1" };

                                // query the feature table
                                var queryResult = secchiLocationFeatures
                                    .QueryFeaturesAsync(queryParameters)
                                    .Result;

                                foreach (var feature in queryResult)
                                {
                                    // Create a new graphic using the feature's geometry and the collection location symbol
                                    var graphic = new Graphic(
                                        feature.Geometry,
                                        MapSymbols.CollectionLocationSymbol
                                    );

                                    // Add the LocationId from the feature's attributes to the graphic's attributes
                                    graphic.Attributes.Add(
                                        "LocationId",
                                        feature.Attributes["LocationId"]
                                    );

                                    // Add the graphic to the graphics overlay
                                    graphicsOverlay.Graphics.Add(graphic);
                                }
                            }
                            else
                            {
                                // Log to trace that the MapView.Map is null.
                                Logger.Error(
                                    "MainPage.xaml.cs, MainPage FeatureTableMessage handler: MapView.Map is null, locations will not be displayed."
                                );
                            }
                        }
                    );
                }
                else
                {
                    // Log to trace that the secchiLocationsChannel is not set.
                    Logger.Error("MainPage.xaml.cs, MainPage: secchiLocationsChannel is not set.");
                }
            }
            else
            {
                // Log to error that the MapView.GraphicsOverlays is null.
                Logger.Error("MainPage.xaml.cs, Initialize: MapView.GraphicsOverlays is null.");
            }

            */

            // Add a handler for the MapViewTapped event.
            MapView.GeoViewTapped += OnMapViewTapped;

            // Add a handler for the ViewpointChanged event.
            MapView.ViewpointChanged += OnViewPortChanged;

            // Create a geometry editor to allow the user to select a location on the map.
            geometryEditor = new GeometryEditor();
            MapView.GeometryEditor = geometryEditor;

            // Get the current value of the GeoTriggerDistanceMeters setting.
            geoTriggerDistance = await ViewModel.RetrieveSettingByKeyAsync<double>(
                SecchiConfiguration.Item[SecchiConfiguration.Key.GeoTriggerDistanceMeters]
            );

            // Log to trace the value of geoTriggerDistance with a label.
            Logger.Trace(
                "MainPage.xaml.cs, Initialize: GeoTriggerDistanceMeters: {geoTriggerDistance}",
                geoTriggerDistance
            );

            NewLocationView.LocationTypeSet = false;
            NewLocationView.LocationSourceSet = false;

            // Register for PreplannedMapConfigurationStatusMessage messages.
            // Log the result of the message.
            WeakReferenceMessenger.Default.Register<PreplannedMapConfigurationStatusMessage>(
                this,
                async (recipient, message) =>
                {
                    Logger.Debug(
                        "MainPage.xaml.cs, Initialize: PreplannedMapConfigurationStatusMessage, Configuration status: {status}.",
                        message.Value
                    );

                    // Both the ArcGIS API key and the offline map identifier are present, so initialization of the map can proceed.
                    if (message.Value)
                    {
                        // Set the location display's datasource to system and enable it.
                        MapView.LocationDisplay.DataSource = systemLocation;
                        MapView.LocationDisplay.IsEnabled = true;
                        AutoPan();
                        await systemLocation.StartAsync();
                        var locationDisplay = MapView.LocationDisplay.IsEnabled;
                        // Log to trace the value of locationDisplay with a label.
                        Logger.Trace(
                            "MainPage.xaml.cs, Initialize: Location Display IsEnabled: {locationDisplay}",
                            locationDisplay
                        );
                    }
                    // Either the ArcGIS API key or the offline map identifier is not present, so there is no need to start system location.
                    else
                    {
                        Logger.Info(
                            "MainPage.xaml.cs, Initialize: ArcGIS API Key or Offline Map Identifier not configured"
                        );
                    }
                }
            );

            // Request a current configuration from the GetPreplannedMapService via the MainViewModel.
            // Do this by calling PreplannedMapConfigurationStatusMessage() from RequestPreplannedMapConfigurationMessage in the MainViewModel.
            // This is done this way in order to manage the sequence of events.
            await ViewModel.RequestPreplannedMapConfigurationMessage();

            // Register for ArcGISRuntimeInitializedMessage messages.
            // Log the result of the message.
            WeakReferenceMessenger.Default.Register<ArcGISRuntimeInitializedMessage>(
                this,
                (recipient, message) =>
                {
                    Logger.Debug(
                        "MainPage.xaml.cs, Initialize: ArcGISRuntimeInitializedMessage, ArcGIS Runtime initialized: {isInitialized}.",
                        message.Value
                    );
                }
            );

            // Request an ArcGIS Runtime initialization from the ConfigurationService via the MainViewModel.
            // Do this by calling ArcGISRuntimeInitialize() from RequestArcGISRuntimeInitializeMessage in the MainViewModel.
            // This is done this way in order to manage the sequence of events.
            await ViewModel.RequestArcGISRuntimeInitializeMessage();

            // When the page is unloaded, unsubscribe from the location data source.
            MapPage.Unloaded += async (s, e) =>
            {
                Logger.Debug(
                    "MainPage.xaml.cs, Initialize: MapPage Unloaded, sending MapPageUnloaded message"
                );

                // Send a message notifying modules that the MapPage has been unloaded.
                WeakReferenceMessenger.Default.Send(new MapPageUnloaded());
                // Unregister all listeners.
                WeakReferenceMessenger.Default.UnregisterAll(this);
                // Stop the location data source.
                await systemLocation.StopAsync();
            };

            // When the map view unloads, try to clean up existing output data folders.
            MapView.Unloaded += (s, e) =>
            {
                Logger.Debug(
                    "MainPage.xaml.cs, Initialize (MapView.Unloaded handler): MapView Unloaded"
                );
                // ActionStatus.IsOpen = false;
                // Find output mobile map folders in the temp directory.
                var outputFolders = Directory.GetDirectories(
                    Environment.ExpandEnvironmentVariables("%TEMP%"),
                    "CullabySecchi*"
                );

                // Loop through the folder names and delete them.
                foreach (var dir in outputFolders)
                {
                    try
                    {
                        // Delete the folder.
                        Directory.Delete(dir, true);
                    }
                    catch (Exception)
                    {
                        // Ignore exceptions (files might be locked, for example).
                    }
                }
            };

            SettingsPanel.Unloaded += (s, e) =>
            {
                Logger.Debug(
                    "MainPage.xaml.cs, Initialize (SettingsPanel.Unloaded handler): SettingsPanel Unloaded"
                );
            };
        }
        catch (Exception exception)
        {
            // Show the exception message.
            // ActionStatus.Severity = InfoBarSeverity.Error;
            // ActionStatus.Title = exception.Message.GetType().Name;
            // ActionStatus.Content = exception.ToString();
            // ActionStatus.IsOpen = true;
            Logger.Error(
                exception,
                "An error occurred in MainPage.xaml.cs, Initialize: {exception}",
                exception.Message
            );
        }
    }

    // Convert from AddLocation to SecchiAddLocation.
    private static SecchiAddLocation ConvertToSecchiAddLocation(AddLocation addLocation)
    {
        return new SecchiAddLocation
        {
            LocationType = addLocation.LocationType,
            LocationSource = addLocation.LocationSource,
            LocationNumber = addLocation.LocationNumber,
            Latitude = addLocation.Latitude,
            Longitude = addLocation.Longitude,
            LocationName = addLocation.LocationName,
            Location = addLocation.Location
        };
    }

    private void ClearLocationSelection()
    {
        // Clear the selected location.
        if (ViewModel.SelectedLocation is not null)
        {
            ViewModel.SelectedLocation = null;
        }
        // Clear the selection overlay.
        selectionOverlay?.Graphics.Clear();
    }

    #region Event handlers

    private void OnViewPortChanged(object? sender, EventArgs eventargs)
    {
        // If the Viewport has changed, then stop the timer.
        // If the timer is already stopped, then this has no effect.
        viewportTimer.Stop();
        viewportTimer.Start();
    }

    private async void OnMapViewTapped(object? sender, GeoViewInputEventArgs eventArgs)
    {
        var tolerance = 20d; // Use larger tolerance for touch
        var maximumResults = 1; // Only return one graphic
        var onlyReturnPopups = false; // Don't return only popups

        try
        {
            // If not already on SecchiLocations view, then move to it.
            if (secchiPageSelection.SecchiSelectView != "SecchiLocations")
            {
                WeakReferenceMessenger.Default.Send<SetSecchiSelectViewMessage>(
                    new SetSecchiSelectViewMessage("SecchiLocations")
                );
            }

            if (graphicsOverlay is null)
            {
                // Log to trace that the graphicsOverlay is null.
                Logger.Error("MainPage.xaml.cs, OnMapViewTapped: graphicsOverlay is null.");
                return;
            }
            // Use the following method to identify graphics in a specific graphics overlay
            var identifyResults = await MapView.IdentifyGraphicsOverlayAsync(
                graphicsOverlay,
                eventArgs.Position,
                tolerance,
                onlyReturnPopups,
                maximumResults
            );

            // Check if we got results
            if (identifyResults.Graphics.Count > 0)
            {
                if (identifyResults.Graphics[0].Attributes.TryGetValue("LocationId", out var value))
                {
                    if (value is null)
                    {
                        // Log to trace that the value is null.
                        Logger.Error(
                            "MainPage.xaml.cs, OnMapViewTapped: value is null, LocationId not present."
                        );
                        return;
                    }
                    // Get the LocationId from the graphic's attributes.
                    var locationId = (int)value;

                    // Clear Center and AutoPan buttons.
                    DeselectCenterAutoPan();

                    // Find the location in the SecchiLocations list view.
                    SecchiLocationsListView.SelectedItem =
                        SecchiView.SecchiLocations.FirstOrDefault(location =>
                            location.LocationId == locationId
                        );
                    // Scroll the list view to the selected item.
                    SecchiLocationsListView.ScrollIntoView(SecchiLocationsListView.SelectedItem);

                    // Get the map point from the graphic.
                    var mapPoint = identifyResults.Graphics[0].Geometry as MapPoint;

                    if (mapPoint is not null)
                    {
                        // Center the map on the selected location
                        await MapView.SetViewpointCenterAsync(mapPoint);

                        var graphicWithSymbol = new Graphic(
                            mapPoint,
                            MapSymbols.HighlightLocationSymbol
                        );
                        if (selectionOverlay != null)
                        {
                            // Log to trace that the selectionOverlay is being cleared.
                            Logger.Trace(
                                "MainPage.xaml.cs, OnMapViewTapped: Clearing selectionOverlay."
                            );
                            selectionOverlay.Graphics.Clear();
                            // Log to trace that the graphicWithSymbol is being added to the selectionOverlay.
                            Logger.Trace(
                                "MainPage.xaml.cs, OnMapViewTapped: Adding graphicWithSymbol to selectionOverlay."
                            );
                            selectionOverlay.Graphics.Add(graphicWithSymbol);
                        }
                    }
                    else
                    {
                        // Log to error that the mapPoint is null.
                        Logger.Error(
                            "MainPage.xaml.cs, OnMapViewTapped: mapPoint is null, LocationId not present."
                        );
                    }
                }

                // Log to trace that a graphic was tapped.
                Logger.Trace(
                    "MainPage.xaml.cs, OnMapViewTapped: Tapped on graphic, {identifyResults}",
                    identifyResults.Graphics[0].Attributes["LocationId"]
                );
            }
        }
        catch (Exception exception)
        {
            // Log the exception message to error.
            Logger.Error(
                exception,
                "MainPage.xaml.cs, OnMapViewTapped: An error occurred in OnMapViewTapped: {exception}",
                exception.Message
            );
        }
    }

    private void SetLocationOverlay()
    {
        Guard.Against.Null(
            MapView.GraphicsOverlays,
            nameof(MapView.GraphicsOverlays),
            "MainPage.xaml.cs, SetLocationOverlay: MapView.GraphicsOverlays is null."
        );

        if (currentCollectionView != DataCollectionView.SelectView)
        {
            // Log to debug that the currentCollectionView is not equal to DataCollectionView.SelectView.
            Logger.Debug(
                "MainPage.xaml.cs, SetLocationOverlay: currentCollectionView: {currentCollectionView}, DataCollectionView.SelectView: {DataCollectionView.SelectView}",
                currentCollectionView,
                DataCollectionView.SelectView
            );
            if (graphicsOverlay is not null)
            {
                // Log to trace that the graphicsOverlay is being cleared.
                Logger.Trace("MainPage.xaml.cs, SetLocationOverlay: Clearing graphicsOverlay.");
                graphicsOverlay.ClearSelection();
            }
            if (selectionOverlay is not null)
            {
                // Log to trace that the selectionOverlay is being cleared.
                Logger.Trace("MainPage.xaml.cs, SetLocationOverlay: Clearing selectionOverlay.");
                selectionOverlay.Graphics.Clear();
            }

            // Clear the graphics overlays.
            MapView.GraphicsOverlays.Clear();

            // Select the appropriate overlay based on type of data being displayed.
            graphicsOverlay = DataCollectionView.SelectView switch
            {
                "Secchi" => SecchiView.SecchiLocationsOverlay,
                "Turbidity" => new(),
                "Quality" => new(),
                "Temperature" => new(),
                _ => null,
            };
            currentCollectionView = DataCollectionView.SelectView;

            // Check to see if the graphics overlay is null.
            Guard.Against.Null(
                graphicsOverlay,
                nameof(graphicsOverlay),
                "MainPage.xaml.cs, SetLocationOverlay: GraphicsOverlay is null."
            );

            // Check to see if the selection overlay is null.
            Guard.Against.Null(
                selectionOverlay,
                nameof(selectionOverlay),
                "MainPage.xaml.cs, SetLocationOverlay: SelectionOverlay is null."
            );

            // When the data collection view is changed, clear the selected location.
            // If this is not done, then the selected location will remain highlighted when the view is changed.
            // Another possibility is to re-select the location when the view is changed to a list with an item selected,
            // though that might create a slightly confusing user experience.
            ClearLocationSelection();

            // Add the graphics overlay to the map view.
            MapView.GraphicsOverlays.Add(graphicsOverlay);

            // Add the selection overlay to the map view.
            MapView.GraphicsOverlays.Add(selectionOverlay);
        }
    }

    private void MapNavView_Loaded(object sender, RoutedEventArgs e)
    {
        MapNavView.SelectedItem = MapNavView.MenuItems[1];
        centerAutoPanSelected = true;
    }

    private void DeselectCenterAutoPan()
    {
        // Deselect the Center and AutoPan buttons.
        // Center (MapNavView.MenuItems[0]) and
        // AutoPan (MapNavView.MenuItems[1]) are de-selected.
        MapNavView.SelectedItem = null;
    }

    private void CollectionNavView_Loaded(object sender, RoutedEventArgs e)
    {
        CollectionNavView.SelectedItem = CollectionNavView.MenuItems[0];
    }

    private void SecchiNavView_Loaded(object sender, RoutedEventArgs e)
    {
        SecchiNavView.SelectedItem = SecchiNavView.MenuItems[0];
    }

    public void SecchiLocationsListView_ItemClick(object sender, ItemClickEventArgs eventArgs)
    {
        _ = sender;
        var item = eventArgs.ClickedItem as SecchiLocationDisplay;

        if (item is not null)
        {
            Logger.Trace(
                "MainPage.xaml.cs, SecchiLocationsListView_ItemClick: {locationName}, Lat {}, Lon {}",
                item.LocationName,
                item.Latitude,
                item.Longitude
            );

            // Create a MapPoint from the latitude and longitude
            var mapPoint = new MapPoint(item.Longitude, item.Latitude, SpatialReferences.Wgs84);

            // Center the map on the selected location
            MapView.SetViewpointCenterAsync(mapPoint);

            // Deselect the Center and AutoPan buttons.
            DeselectCenterAutoPan();

            var graphicWithSymbol = new Graphic(mapPoint, MapSymbols.HighlightLocationSymbol);
            if (selectionOverlay != null)
            {
                // Log to trace that the selectionOverlay is being cleared.
                Logger.Trace(
                    "MainPage.xaml.cs, SecchiLocationsListView_ItemClick: Clearing selectionOverlay."
                );
                selectionOverlay.Graphics.Clear();
                // Log to trace that the graphicWithSymbol is being added to the selectionOverlay.
                Logger.Trace(
                    "MainPage.xaml.cs, SecchiLocationsListView_ItemClick: Adding graphicWithSymbol to selectionOverlay."
                );
                selectionOverlay.Graphics.Add(graphicWithSymbol);
            }
        }
    }

    /*
    public void SecchiCollectionListView_ItemClick(object sender, ItemClickEventArgs eventArgs)
    {
        _ = sender;
        var item = eventArgs.ClickedItem as SecchiCollectionDisplay;

        if (item is not null)
        {
            Logger.Debug(
                "MainPage.xaml.cs, SecchiCollectionListView_ItemClick: {locationName}, Lat {latitude}, Lon {longitude}, OBJECTID {ObjectId}",
                item.LocationName,
                item.Latitude,
                item.Longitude,
                item.ObjectId
            );

            if (item.Latitude is null || item.Longitude is null)
            {
                // Log to error that the Latitude or Longitude is null.
                Logger.Error(
                    "MainPage.xaml.cs, SecchiCollectionListView_ItemClick: Latitude({latitude}) or Longitude({longitude}) is null, unable to display location.",
                    item.Latitude,
                    item.Longitude
                );
                return;
            }

            // Log to debug that the Center and AutoPan buttons are being deselected.
            Logger.Debug(
                "MainPage.xaml.cs, SecchiCollectionListView_ItemClick: Deselecting Center and AutoPan buttons."
            );
            DeselectCenterAutoPan();

            // Create a MapPoint from the latitude and longitude
            var mapPoint = new MapPoint(
                (double)item.Longitude,
                (double)item.Latitude,
                SpatialReferences.Wgs84
            );

            // Center the map on the selected location
            MapView.SetViewpointCenterAsync(mapPoint);

            var graphicWithSymbol = new Graphic(mapPoint, MapSymbols.HighlightLocationSymbol);
            if (selectionOverlay != null)
            {
                // Log to trace that the selectionOverlay is being cleared.
                Logger.Trace(
                    "MainPage.xaml.cs, SecchiCollectionListView_ItemClick: Clearing selectionOverlay."
                );
                selectionOverlay.Graphics.Clear();
                // Log to trace that the graphicWithSymbol is being added to the selectionOverlay.
                Logger.Trace(
                    "MainPage.xaml.cs, SecchiCollectionListView_ItemClick: Adding graphicWithSymbol to selectionOverlay."
                );
                selectionOverlay.Graphics.Add(graphicWithSymbol);
            }
            else
            {
                // Log to error that the selectionOverlay is null.
                Logger.Error(
                    "MainPage.xaml.cs, SecchiCollectionListView_ItemClick: selectionOverlay is null."
                );
            }
        }
    }
    */

    public void SecchiNavView_ItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args
    )
    {
        // Log to debug that the MapNavView_ItemInvoked event was fired.
        Logger.Debug(
            "MainPage.xaml.cs, SecchiNavView_ItemInvoked(): SecchiNavView_ItemInvoked event."
        );

        // Log the name of the invoked item.
        Logger.Debug(
            "MainPage.xaml.cs, SecchiNavView_ItemInvoked(): Invoked item name: {invokedItemName}.",
            args.InvokedItemContainer.Name
        );

        // Log the sender name.
        Logger.Debug(
            "MainPage.xaml.cs, SecchiNavView_ItemInvoked(): Sender name: {senderName}.",
            sender.Name
        );

        if (args.InvokedItem != sender.SelectedItem)
        {
            // Track when specific pages have been navigated away from.
            // This can be used to do things like reload geotriggers when the user navigates away from the add location page.
            // Set a flag when a page is navigated to, such as SecchiNavLocationAdd, and check for it here.
            if (secchiLocationAddPageSelected)
            {
                if (args.InvokedItemContainer.Name is not "SecchiNavLocationAdd")
                {
                    // Log to trace that the secchiLocationAddPageSelected is false.
                    Logger.Trace(
                        "MainPage.xaml.cs, SecchiNavView_ItemInvoked(): Was on add location, navigated away."
                    );
                    secchiLocationAddPageSelected = false;
                }
            }
        }

        switch (args.InvokedItemContainer.Name)
        {
            case "SecchiNavMeasurementAdd":
                Logger.Debug(
                    "MainPage.xaml.cs, SecchiNavView_ItemInvoked(): Add Measurement selected."
                );
                secchiPageSelection.SecchiSelectView = "SecchiDataEntry";
                break;
            case "SecchiNavLocationAdd":
                Logger.Debug("MainPage.xaml.cs, SecchiNavView_ItemInvoked(): Locations selected.");
                secchiPageSelection.SecchiSelectView = "SecchiLocations";
                secchiLocationAddPageSelected = true;
                break;
            case "SecchiNavCollected":
                Logger.Debug("MainPage.xaml.cs, SecchiNavView_ItemInvoked(): Collected selected.");
                secchiPageSelection.SecchiSelectView = "SecchiCollectionTable";
                break;
            case "SecchiNavDiscard":
                Logger.Debug(
                    "MainPage.xaml.cs, SecchiNavView_ItemInvoked(): Discard item selected."
                );
                break;
            case "SecchiNavUpload":
                Logger.Debug("MainPage.xaml.cs, SecchiNavView_ItemInvoked(): Upload selected.");
                break;
            case "SecchiNavInfo":
                Logger.Debug("MainPage.xaml.cs, SecchiNavView_ItemInvoked(): Info item selected.");
                break;

            case "SettingsItem":
                Logger.Debug("MainPage.xaml.cs, SecchiNavView_ItemInvoked(): Settings selected.");
                secchiPageSelection.SecchiSelectView = "SecchiSettings";
                break;
            default:
                break;
        }
    }

    private void SaveSecchiMeasurements_Click()
    {
        // Collect the integers here
        if (MapView.LocationDisplay.Location is not null)
        {
            secchiMeasurement.Location = new MapPoint(
                MapView.LocationDisplay.Location.Position.X,
                MapView.LocationDisplay.Location.Position.Y,
                SpatialReferences.Wgs84
            );
            secchiMeasurement.Measurement1 = short.Parse(Measurement1.Text);
            secchiMeasurement.Measurement2 = short.Parse(Measurement2.Text);
            secchiMeasurement.Measurement3 = short.Parse(Measurement3.Text);

            SecchiView.ProcessSecchiMeasurements(secchiMeasurement);
        }
        else
        {
            // Write to debug that Location is not available, there is a problem with system location services.
            Logger.Error(
                "MainPage.xaml.cs, SaveSecchiMeasurements_Click: Location is not available, there is a problem with system location services."
            );

            /*
            ActionStatus.IsOpen = true;
            ActionStatus.Severity = InfoBarSeverity.Error;
            ActionStatus.Title = "Location not available";
            ActionStatus.Content =
                "Location is not available, there is a problem with system location services.";
            */
        }
    }

    private void CancelSecchiMeasurements_Click()
    {
        secchiPageSelection.SecchiSelectView = "SecchiCollectionTable";
    }

    [RelayCommand]
    private async Task SaveLocationAsync()
    {
        // Log to trace that the SaveLocationAsync method was called.
        Logger.Trace("MainPage.xaml.cs, SaveLocationAsync: SaveLocationAsync method called.");

        double latitude;
        double longitude;
        Graphic graphic;

        try
        {
            if (MapView.LocationDisplay.Location is null)
            {
                // Log to trace that the LocationDisplay.Location is null.
                Logger.Error(
                    "MainPage.xaml.cs, SaveLocationAsync: LocationDisplay.Location is null."
                );
                return;
            }

            if (MapView.GeometryEditor is null)
            {
                // Log to trace that the MapView.GeometryEditor is null.
                Logger.Error(
                    "MainPage.xaml.cs, SaveLocationAsync: MapView.GeometryEditor is null."
                );
                return;
            }

            if (graphicsOverlay is null)
            {
                // Log to trace that the graphicsOverlay is null.
                Logger.Error("MainPage.xaml.cs, SaveLocationAsync: graphicsOverlay is null.");
                return;
            }

            // Get the next location number.
            var nextLocationNumber = DataCollectionView.SelectView switch
            {
                "Secchi" => await SecchiView.NextLocationId(),
                "Turbidity" => new(),
                "Quality" => new(),
                "Temperature" => new(),
                _ => 0,
            };

            // Log to trace the nextLocationNumber.
            Logger.Trace(
                "MainPage.xaml.cs, SaveLocationAsync: nextLocationNumber: {nextLocationNumber}",
                nextLocationNumber
            );

            Guard.Against.NegativeOrZero(
                nextLocationNumber,
                nameof(nextLocationNumber),
                "MainPage.xaml.cs, SaveLocationAsync: nextLocationNumber is negative or zero."
            );

            addLocation.LocationNumber = nextLocationNumber;

            /*
            if (secchiLocationFeatures is null)
            {
                // Log to trace that the secchiLocationFeatures is null.
                Logger.Error(
                    "MainPage.xaml.cs, SaveLocationAsync: secchiLocationFeatures is null."
                );
                return;
            }

            // create a where clause to get all the features
            var queryParameters = new QueryParameters() { WhereClause = "1=1" };

            // query the feature table
            var queryResult = await secchiLocationFeatures.QueryFeaturesAsync(queryParameters);

            // find the maximum LocationId
            var maxLocationId = queryResult
                .Select(feature => Convert.ToInt32(feature.Attributes["LocationId"]))
                .Max();

            // the next location number is one more than the maximum
            var nextLocationNumber = maxLocationId + 1;

            // Log to trace the nextLocationNumber.
            Logger.Trace(
                "MainPage.xaml.cs, SaveLocationAsync: nextLocationNumber: {nextLocationNumber}",
                nextLocationNumber
            );
            */

            // Set the location name to the value of NewLocationView.LocationName.
            // This could be retrieved from either NewLocationView or from SecchiLocationName in MainPage.xaml
            addLocation.LocationName = NewLocationView.LocationName;

            // Log to trace NewLocationView.LocationName.
            Logger.Trace(
                "MainPage.xaml.cs, SaveLocationAsync: NewLocationView.LocationName: {LocationName}",
                NewLocationView.LocationName
            );

            switch (addLocation.LocationSource)
            {
                case LocationSource.EnteredLatLong:

                    // Log to trace the entered latitude and longitude.
                    Logger.Trace(
                        "MainPage.xaml.cs, SaveLocationAsync: EnteredLatLong: Latitude: {latitude}, Longitude: {longitude}.",
                        EnteredLatitude.Text,
                        EnteredLongitude.Text
                    );

                    if (graphicsOverlay is null)
                    {
                        // Log to trace that the graphicsOverlay is null.
                        Logger.Error(
                            "MainPage.xaml.cs, SaveLocationAsync: graphicsOverlay is null."
                        );
                        return;
                    }

                    // Set the location to the entered latitude and longitude.
                    // This is retrieved from either NewLocationView or from EnteredLatitude and EnteredLongitude in MainPage.xaml
                    addLocation.Location = new MapPoint(
                        double.Parse(NewLocationView.LongitudeEntry),
                        double.Parse(NewLocationView.LatitudeEntry),
                        SpatialReferences.Wgs84
                    );

                    latitude = addLocation.Location.As<MapPoint>().Y;
                    longitude = addLocation.Location.As<MapPoint>().X;

                    addLocation.Latitude = latitude;
                    addLocation.Longitude = longitude;

                    // Log to trace the latitude and lon values of the mapPoint.
                    Logger.Trace(
                        "MainPage.xaml.cs, SaveLocationAsync:: Added point to map at: Lat {latitude}, Lon {lon}.",
                        latitude,
                        longitude
                    );

                    // Create a new graphic using the feature's geometry and the collection location symbol
                    graphic = new Graphic(
                        addLocation.Location,
                        MapSymbols.CollectionLocationSymbol
                    );

                    // Add the LocationId from the feature's attributes to the graphic's attributes
                    graphic.Attributes.Add("LocationId", addLocation.LocationNumber);

                    // Add the graphic to the graphics overlay
                    graphicsOverlay.Graphics.Add(graphic);

                    await MapView.SetViewpointCenterAsync(addLocation.Location, 2500);

                    // Select the appropriate measurement type to add based on current view.
                    switch (DataCollectionView.SelectView)
                    {
                        case "Secchi":
                            SecchiView.AddNewLocation(ConvertToSecchiAddLocation(addLocation));
                            // Return to the location display view.
                            SecchiView.LocationDisplay = "CurrentLocations";
                            break;
                        case "Turbidity":
                            // Your code here
                            break;
                        case "Quality":
                            // Your code here
                            break;
                        case "Temperature":
                            // Your code here
                            break;
                        default:
                            // Log to error that the DataCollectionView.SelectView is not recognized and its value.
                            Logger.Error(
                                "MainPage.xaml.cs, SaveLocationAsync: Value for DataCollectionView.SelectView not recognized: {DataCollectionView.SelectView}.",
                                DataCollectionView.SelectView
                            );
                            break;
                    }

                    NewLocationView.LocationName = string.Empty;
                    NewLocationView.LatitudeEntry = string.Empty;
                    NewLocationView.LongitudeEntry = string.Empty;

                    NewLocationView.LocationCanBeSaved = false;

                    break;

                case LocationSource.CurrentGPS:
                    if (MapView.LocationDisplay.Location is null)
                    {
                        // Log to trace that the LocationDisplay.Location is null.
                        Logger.Error(
                            "MainPage.xaml.cs, SaveLocationAsync: LocationDisplay.Location is null."
                        );
                        break;
                    }

                    addLocation.Location = new MapPoint(
                        MapView.LocationDisplay.Location.Position.X,
                        MapView.LocationDisplay.Location.Position.Y,
                        SpatialReferences.Wgs84
                    );

                    latitude = addLocation.Location.Y;
                    addLocation.Latitude = latitude;
                    longitude = addLocation.Location.X;
                    addLocation.Longitude = longitude;

                    Logger.Trace(
                        "MainPage.xaml.cs, SaveLocationAsync, CurrentGPS: presentLocation: Lat {latitude}, Lon {longitude}.",
                        latitude,
                        longitude
                    );

                    if (graphicsOverlay is null)
                    {
                        // Log to trace that the graphicsOverlay is null.
                        Logger.Error(
                            "MainPage.xaml.cs, SaveLocationAsync: graphicsOverlay is null."
                        );
                    }
                    else
                    {
                        // Create a new graphic using the feature's geometry and the collection location symbol
                        graphic = new Graphic(
                            addLocation.Location,
                            MapSymbols.CollectionLocationSymbol
                        );

                        // Add the LocationId from the feature's attributes to the graphic's attributes
                        graphic.Attributes.Add("LocationId", addLocation.LocationNumber);

                        // Add the graphic to the graphics overlay
                        graphicsOverlay.Graphics.Add(graphic);
                    }

                    await MapView.SetViewpointCenterAsync(addLocation.Location, 2500);

                    // Select the appropriate measurement type to add based on current view.
                    switch (DataCollectionView.SelectView)
                    {
                        case "Secchi":
                            SecchiView.AddNewLocation(ConvertToSecchiAddLocation(addLocation));
                            // Return to the location display view.
                            SecchiView.LocationDisplay = "CurrentLocations";
                            break;
                        case "Turbidity":
                            // Your code here
                            break;
                        case "Quality":
                            // Your code here
                            break;
                        case "Temperature":
                            // Your code here
                            break;
                        default:
                            // Log to error that the DataCollectionView.SelectView is not recognized and its value.
                            Logger.Error(
                                "MainPage.xaml.cs, SaveLocationAsync: Value for DataCollectionView.SelectView not recognized: {DataCollectionView.SelectView}.",
                                DataCollectionView.SelectView
                            );
                            break;
                    }

                    NewLocationView.LocationName = string.Empty;
                    NewLocationView.LocationCanBeSaved = false;

                    break;

                case LocationSource.PointOnMap:
                    if (MapView.GeometryEditor is null)
                    {
                        // Log to trace that the MapView.GeometryEditor is null.
                        Logger.Error(
                            "MainPage.xaml.cs, SaveLocationAsync: MapView.GeometryEditor is null."
                        );
                        break;
                    }

                    if (MapView.GeometryEditor.IsStarted)
                    {
                        var newLocation = MapView.GeometryEditor.Stop();
                        if (newLocation is not null)
                        {
                            // Set the new location to the addLocation.Location.
                            addLocation.Location = newLocation.As<MapPoint>();

                            // Create a new graphic using the feature's geometry and the collection location symbol
                            graphic = new Graphic(
                                addLocation.Location,
                                MapSymbols.CollectionLocationSymbol
                            );

                            // Add the LocationId from the feature's attributes to the graphic's attributes
                            graphic.Attributes.Add("LocationId", addLocation.LocationNumber);

                            // Add the graphic to the graphics overlay
                            graphicsOverlay.Graphics.Add(graphic);

                            // log the latitute and longitude of the new location.
                            latitude = newLocation
                                .Project(SpatialReferences.Wgs84)
                                .As<MapPoint>()
                                .Y;
                            addLocation.Latitude = latitude;
                            longitude = newLocation
                                .Project(SpatialReferences.Wgs84)
                                .As<MapPoint>()
                                .X;
                            addLocation.Longitude = longitude;
                            Logger.Trace(
                                "MainPage.xaml.cs, SaveLocationAsync: point on map is at: Lat {lat}, Lon {lon}.",
                                latitude,
                                longitude
                            );
                            // Select the appropriate measurement type to add based on current view.
                            switch (DataCollectionView.SelectView)
                            {
                                case "Secchi":
                                    SecchiView.AddNewLocation(
                                        ConvertToSecchiAddLocation(addLocation)
                                    );
                                    // Return to the location display view.
                                    SecchiView.LocationDisplay = "CurrentLocations";
                                    break;
                                case "Turbidity":
                                    // Your code here
                                    break;
                                case "Quality":
                                    // Your code here
                                    break;
                                case "Temperature":
                                    // Your code here
                                    break;
                                default:
                                    // Log to error that the DataCollectionView.SelectView is not recognized and its value.
                                    Logger.Error(
                                        "MainPage.xaml.cs, SaveLocationAsync: Value for DataCollectionView.SelectView not recognized: {DataCollectionView.SelectView}.",
                                        DataCollectionView.SelectView
                                    );
                                    break;
                            }

                            NewLocationView.LocationName = string.Empty;

                            NewLocationView.LocationCanBeSaved = false;
                        }

                        // Since the drop-down is set to "Point on Map", start the geometry editor.
                        // This provides for a consistent user experience if the page is navigated away from and then back.
                        // MapView.GeometryEditor.Start(GeometryType.Point);
                    }
                    break;

                default:
                    Logger.Error("MainPage.xaml.cs, SaveLocationAsync: LocationSource not set.");
                    break;
            }
        }
        catch (Exception exception)
        {
            // Log to trace that an error occurred in SaveLocationAsync.
            Logger.Error(
                exception,
                "MainPage.xaml.cs, SaveLocationAsync: An error occurred in SaveLocationAsync: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    private void GeometryEditor_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs eventArgs
    )
    {
        var newLocationSymbol = new SimpleMarkerSymbol(
            SimpleMarkerSymbolStyle.Circle,
            Color.FromArgb(255, 0, 120, 212),
            9
        );

        try
        {
            // Log to trace that the GeometryEditor_PropertyChanged method was called.
            Logger.Trace(
                "MainPage.xaml.cs, GeometryEditor_PropertyChanged: GeometryEditor_PropertyChanged method called."
            );

            Guard.Against.Null(
                MapView.GeometryEditor,
                nameof(MapView.GeometryEditor),
                "GeometryEditor is null."
            );

            MapView.GeometryEditor.PropertyChanged -= GeometryEditor_PropertyChanged;

            Guard.Against.Null(sender, nameof(sender), "Sender is null.");

            var geometryEditor = sender as GeometryEditor;

            if (sender is not GeometryEditor)
            {
                // Log to trace that the geometryEditor is null.
                Logger.Trace(
                    "MainPage.xaml.cs, GeometryEditor_PropertyChanged: geometryEditor is null."
                );
                return;
            }

            Guard.Against.Null(eventArgs, nameof(eventArgs), "eventArgs is null.");

            if (eventArgs.PropertyName is null)
            {
                // Log to trace that the eventArgs.PropertyName is null.
                Logger.Trace(
                    "MainPage.xaml.cs, GeometryEditor_PropertyChanged: eventArgs.PropertyName is null."
                );
                return;
            }

            if (eventArgs.PropertyName == "Geometry")
            {
                if (sender is not GeometryEditor)
                {
                    // Log to trace that the geometryEditor is null.
                    Logger.Trace(
                        "MainPage.xaml.cs, GeometryEditor_PropertyChanged: geometryEditor is null."
                    );
                    return;
                }

                Guard.Against.Null(
                    geometryEditor,
                    nameof(geometryEditor),
                    "GeometryEditor is null."
                );

                var geometry = geometryEditor.Geometry;

                Guard.Against.Null(geometry, nameof(geometry), "Geometry is null.");

                if (geometry is MapPoint)
                {
                    var mapPoint = geometry as MapPoint;

                    if (geometry is not MapPoint)
                    {
                        // Log to trace that the mapPoint is null.
                        Logger.Trace(
                            "MainPage.xaml.cs, GeometryEditor_PropertyChanged: mapPoint is null."
                        );
                        return;
                    }

                    Guard.Against.Null(
                        MapView.LocationDisplay.Location,
                        nameof(MapView.LocationDisplay.Location),
                        "MapPoint is null."
                    );

                    var presentLocation = MapView.LocationDisplay.Location.Position;

                    var latitude = presentLocation.Y;
                    var longitude = presentLocation.X;

                    // Log to trace the latitude and lon values of the mapPoint.
                    Logger.Trace(
                        "MainPage.xaml.cs, GeometryEditor_PropertyChanged: MapPoint: Lat {latitude}, Lon {lon}.",
                        latitude,
                        longitude
                    );

                    MapView.GeometryEditor.Stop();
                    if (graphicsOverlay is not null)
                    {
                        // graphicsOverlay.Graphics.Add(new Graphic(geometry, newLocationSymbol));
                        // Add the new location to the map with a tag of 'LocationId' and the value of addLocation.LocationNumber.
                        // This is used to identify the location when it is selected.
                        graphicsOverlay.Graphics.Add(
                            new Graphic(
                                geometry,
                                new Dictionary<string, object?>
                                {
                                    { "LocationId", addLocation.LocationNumber }
                                },
                                newLocationSymbol
                            )
                        );
                        latitude = geometry.As<MapPoint>().Y;
                        longitude = geometry.As<MapPoint>().X;
                        // Log to trace the latitude and lon values of the mapPoint.
                        Logger.Trace(
                            "MainPage.xaml.cs, GeometryEditor_PropertyChanged: Added point to map at: Lat {latitude}, Lon {lon}.",
                            latitude,
                            longitude
                        );
                    }
                    else
                    {
                        // Log to trace that the graphicsOverlay is null.
                        Logger.Error(
                            "MainPage.xaml.cs, GeometryEditor_PropertyChanged: graphicsOverlay is null."
                        );
                    }
                }
            }
        }
        catch (Exception exception)
        {
            // Log to trace the exception message.
            Logger.Error(
                exception,
                "MainPage.xaml.cs, GeometryEditor_PropertyChanged: An error occurred in GeometryEditor_PropertyChanged: {exception}",
                exception.Message
            );
        }
    }

    private void LocationType_Click(object sender, RoutedEventArgs eventArgs)
    {
        _ = eventArgs as RoutedEventArgs;

        // Log to trace that the LocationType_Click method was called.
        Logger.Trace("MainPage.xaml.cs, LocationType_Click: LocationType_Click method called.");

        try
        {
            if (sender is null)
            {
                // Log to trace that the sender is null.
                Logger.Trace("MainPage.xaml.cs, LocationType_Click: sender is null.");
                return;
            }

            var menuFlyoutItem = sender as MenuFlyoutItem;

            if (menuFlyoutItem != null)
            {
                var tag = menuFlyoutItem.Tag as string;

                switch (tag)
                {
                    case "Occasional":
                        addLocation.LocationType = LocationType.Occasional;
                        SecchiLocationTypeDropDown.Content = "Occasional";
                        NewLocationView.LocationTypeSet = true;
                        break;
                    case "Ongoing":
                        addLocation.LocationType = LocationType.Ongoing;
                        SecchiLocationTypeDropDown.Content = "Ongoing";
                        NewLocationView.LocationTypeSet = true;
                        break;
                    default:
                        // Log to trace that an invalid tag value was encountered.
                        Logger.Trace(
                            "MainPage.xaml.cs, LocationType_Click: Invalid tag value: {tag}",
                            tag
                        );
                        NewLocationView.LocationTypeSet = false;
                        break;
                }
            }

            // Log to trace the value of locationType with a label.
            Logger.Trace(
                "MainPage.xaml.cs, LocationType_Click: Location Type: {locationType}",
                addLocation.LocationType
            );
        }
        catch (Exception exception)
        {
            // Log to trace the exception message.
            Logger.Error(
                exception,
                "MainPage.xaml.cs, LocationType_Click: An error occurred in LocationType_Click: {exception}",
                exception.Message
            );
        }
    }

    private void AddLocation_Click(object sender, RoutedEventArgs eventArgs)
    {
        // Log to trace that the AddLocation_Click method was called.
        Logger.Trace("MainPage.xaml.cs, AddLocation_Click: AddLocation_Click method called.");

        // Clear the selected location.
        ClearLocationSelection();

        // Deselect the Center and AutoPan buttons.
        DeselectCenterAutoPan();

        var currentCollectionType = SecchiLocationSourceDropDown.Content.ToString();

        if (currentCollectionType == "Map Point")
        {
            // if the drop-down for collection type is set to "Point on Map", start the geometry editor.
            // This provides for a consistent user experience if the page is navigated away from and then back.
            MapView.GeometryEditor?.Start(GeometryType.Point);
        }

        try
        {
            // Go to the add location view based on current measurement type.
            switch (DataCollectionView.SelectView)
            {
                case "Secchi":
                    // Go to the Secchi add location view.
                    SecchiView.LocationDisplay = "AddLocation";
                    break;
                case "Turbidity":
                    // Your code here
                    break;
                case "Quality":
                    // Your code here
                    break;
                case "Temperature":
                    // Your code here
                    break;
                default:
                    // Log to error that the DataCollectionView.SelectView is not recognized and its value.
                    Logger.Error(
                        "MainPage.xaml.cs, AddLocation_Click: Value for DataCollectionView.SelectView not recognized: {DataCollectionView.SelectView}.",
                        DataCollectionView.SelectView
                    );
                    break;
            }
        }
        catch (Exception exception)
        {
            // Log to trace the exception message.
            Logger.Error(
                exception,
                "MainPage.xaml.cs, AddLocation_Click: An error occurred in AddLocation_Click: {exception}",
                exception.Message
            );
        }
    }

    private void CancelAddLocation_Click(object sender, RoutedEventArgs eventArgs)
    {
        // Log to trace that the AddLocation_Click method was called.
        Logger.Trace(
            "MainPage.xaml.cs, CancelAddLocation_Click: CancelAddLocation_Click method called."
        );

        try
        {
            _ = eventArgs as RoutedEventArgs;
            _ = sender as Button;

            // Clear the location name, latitude, and longitude.
            NewLocationView.LocationName = string.Empty;
            NewLocationView.LatitudeEntry = string.Empty;
            NewLocationView.LongitudeEntry = string.Empty;

            var currentCollectionType = SecchiLocationSourceDropDown.Content.ToString();

            if (currentCollectionType == "Map Point")
            {
                // If the drop-down is set to "Point on Map" and the action was canceled, stop the geometry editor.
                // This keeps the map point square from appearing on the map when the user navigates away from the page.
                MapView.GeometryEditor?.Stop();
            }

            // Select the appropriate location list based on current view.
            switch (DataCollectionView.SelectView)
            {
                case "Secchi":
                    // Return to the location display view.
                    SecchiView.LocationDisplay = "CurrentLocations";
                    break;
                case "Turbidity":
                    // Your code here
                    break;
                case "Quality":
                    // Your code here
                    break;
                case "Temperature":
                    // Your code here
                    break;
                default:
                    // Log to error that the DataCollectionView.SelectView is not recognized and its value.
                    Logger.Error(
                        "MainPage.xaml.cs, SaveLocationAsync: Value for DataCollectionView.SelectView not recognized: {DataCollectionView.SelectView}.",
                        DataCollectionView.SelectView
                    );
                    break;
            }
        }
        catch (Exception exception)
        {
            // Log to trace the exception message.
            Logger.Error(
                exception,
                "MainPage.xaml.cs, AddLocation_Click: An error occurred in AddLocation_Click: {exception}",
                exception.Message
            );
        }
    }

    private void LocationSource_Click(object sender, RoutedEventArgs eventArgs)
    {
        _ = eventArgs as RoutedEventArgs;

        // Log to trace that the LocationSource_Click method was called.
        Logger.Trace("MainPage.xaml.cs, LocationSource_Click: LocationSource_Click method called.");

        try
        {
            if (sender is null)
            {
                // Log to trace that the sender is null.
                Logger.Trace("MainPage.xaml.cs, LocationSource_Click: sender is null.");
                return;
            }

            var menuFlyoutItem = sender as MenuFlyoutItem;

            if (menuFlyoutItem != null)
            {
                if (MapView.GeometryEditor is null)
                {
                    // Log to trace that the MapView.GeometryEditor is null.
                    Logger.Error(
                        "MainPage.xaml.cs, SaveSecchiLocation_Click: MapView.GeometryEditor is null."
                    );
                    return;
                }

                if (MapView.LocationDisplay.Location is null)
                {
                    // Log to trace that the LocationDisplay.Location is null.
                    Logger.Error(
                        "MainPage.xaml.cs, SaveSecchiLocation_Click: LocationDisplay.Location is null."
                    );
                    return;
                }

                var tag = menuFlyoutItem.Tag as string;

                switch (tag)
                {
                    case "CurrentGPS":
                        if (MapView.GeometryEditor.IsStarted)
                        {
                            MapView.GeometryEditor.Stop();
                        }
                        addLocation.LocationSource = LocationSource.CurrentGPS;
                        LatLongEntry.Visibility = Visibility.Collapsed;
                        NewLocationView.LocationSourceActive = false;
                        SecchiLocationSourceDropDown.Content = "Current GPS";
                        NewLocationView.LocationSourceSet = true;
                        break;
                    case "PointOnMap":
                        addLocation.LocationSource = LocationSource.PointOnMap;

                        var presentLocation = MapView.LocationDisplay.Location.Position;

                        Logger.Trace(
                            "MainPage.xaml.cs, SaveSecchiLocation_Click, PointOnMap: presentLocation: Lat {presentLocation.Y}, Lon {presentLocation.X}.",
                            presentLocation.Y,
                            presentLocation.X
                        );

                        MapView.SetViewpointCenterAsync(presentLocation, 2500);

                        // Use the geometry editor to allow the user to select a location on the map.
                        MapView.GeometryEditor.Start(GeometryType.Point);

                        MapView.SetViewpointCenterAsync(presentLocation, 2500);
                        LatLongEntry.Visibility = Visibility.Collapsed;
                        NewLocationView.LocationSourceActive = false;
                        SecchiLocationSourceDropDown.Content = "Map Point";
                        NewLocationView.LocationSourceSet = true;
                        break;
                    case "EnterLatLong":
                        if (addLocation.LocationSource == LocationSource.PointOnMap)
                        {
                            MapView.GeometryEditor.Stop();
                        }
                        addLocation.LocationSource = LocationSource.EnteredLatLong;
                        SecchiLocationSourceDropDown.Content = "Enter Lat/Long";
                        LatLongEntry.Visibility = Visibility.Visible;
                        NewLocationView.LocationSourceActive = true;
                        NewLocationView.LocationSourceSet = true;
                        break;
                    default:
                        // Log to trace that an invalid tag value was encountered.
                        Logger.Trace(
                            "MainPage.xaml.cs, LocationSource_Click: Invalid tag value: {tag}",
                            tag
                        );
                        break;
                }
            }

            // Log to trace the value of locationSource with a label.
            Logger.Trace(
                "MainPage.xaml.cs, LocationSource_Click: Location Source: {locationSource}",
                addLocation.LocationSource
            );
        }
        catch (Exception exception)
        {
            // Log to trace the exception message.
            Logger.Error(
                exception,
                "MainPage.xaml.cs, LocationSource_Click: An error occurred in LocationSource_Click: {exception}",
                exception.Message
            );
        }
    }

    private void Edit_Location_Click(object sender, RoutedEventArgs eventArgs)
    {
        // Log to trace that the Edit_Location_Click method was called.
        Logger.Trace("MainPage.xaml.cs, Edit_Location_Click: Edit_Location_Click method called.");

        var button = sender as Button;
        try
        {
            Guard.Against.Null(button, nameof(button), "Button in Edit_Location_Click is null.");

            var locationId = button.Tag;

            // Log to trace the value of sender and eventArgs.
            Logger.Trace(
                "MainPage.xaml.cs, Edit_Location_Click: LocationId: {locationId}",
                locationId
            );
        }
        catch (Exception exception)
        {
            // Log to trace the exception message.
            Logger.Error(
                exception,
                "MainPage.xaml.cs, Edit_Location_Click: An error occurred in Edit_Location_Click: {exception}",
                exception.Message
            );
        }
    }

    private void Delete_Location_Click(object sender, RoutedEventArgs eventArgs)
    {
        // Log to trace that the Delete_Location_Click method was called.
        Logger.Trace(
            "MainPage.xaml.cs, Delete_Location_Click: Delete_Location_Click method called."
        );

        var button = sender as Button;
        try
        {
            Guard.Against.Null(button, nameof(button), "Button in Delete_Location_Click is null.");

            // Log to trace the name of the button.
            Logger.Trace(
                "MainPage.xaml.cs, Delete_Location_Click: Button Name: {button.Name}",
                button.Name
            );

            // Get the locationId from the button's tag.
            var locationId = (int)button.Tag;

            // Log to trace the value of sender and eventArgs.
            Logger.Trace(
                "MainPage.xaml.cs, Delete_Location_Click: LocationId: {locationId}",
                locationId
            );

            if (selectionOverlay is not null)
            {
                // Log to trace that the selectionOverlay is being cleared.
                Logger.Trace("MainPage.xaml.cs, Delete_Location_Click: Clearing selectionOverlay.");
                selectionOverlay.Graphics.Clear();
            }

            // Select the appropriate measurement type to add based on current view.
            switch (DataCollectionView.SelectView)
            {
                case "Secchi":
                    // Delete the location.
                    SecchiView.DeleteLocation(locationId);
                    break;
                case "Turbidity":
                    // Your code here
                    break;
                case "Quality":
                    // Your code here
                    break;
                case "Temperature":
                    // Your code here
                    break;
                default:
                    // Log to error that the DataCollectionView.SelectView is not recognized and its value.
                    Logger.Error(
                        "MainPage.xaml.cs, SaveLocationAsync: Value for DataCollectionView.SelectView not recognized: {DataCollectionView.SelectView}.",
                        DataCollectionView.SelectView
                    );
                    break;
            }

            if (graphicsOverlay is null)
            {
                // Log to trace that the graphicsOverlay is null.
                Logger.Error(
                    "MainPage.xaml.cs, SaveSecchiLocation_Click: graphicsOverlay is null."
                );
                return;
            }
            else
            {
                // Log to trace that graphicsOverlay is not null.
                Logger.Trace(
                    "MainPage.xaml.cs, Delete_Location_Click: graphicsOverlay is not null."
                );

                var graphicCollection = graphicsOverlay.Graphics;

                // Log to trace the graphicCollection.
                Logger.Trace(
                    "MainPage.xaml.cs, Delete_Location_Click: Graphic Collection: {graphicCollection}",
                    graphicCollection
                );

                foreach (var graphic in graphicsOverlay.Graphics)
                {
                    foreach (var attribute in graphic.Attributes)
                    {
                        Logger.Trace(
                            "MainPage.xaml.cs, Delete_Location_Click: Geometry: {graphic.Geometry}, Key: {attribute.Key}, Value: {attribute.Value}",
                            graphic.Geometry,
                            attribute.Key,
                            attribute.Value
                        );
                    }

                    if (graphic.Attributes["LocationId"] is int locationIdAttribute)
                    {
                        if (locationIdAttribute == locationId)
                        {
                            graphicsOverlay.Graphics.Remove(graphic);
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            // Log to trace the exception message.
            Logger.Error(
                exception,
                "MainPage.xaml.cs, Delete_Location_Click: An error occurred in Delete_Location_Click: {exception}",
                exception.Message
            );
        }
    }

    private void Edit_Observation_Click(object sender, RoutedEventArgs eventArgs)
    {
        // Log to trace that the Edit_Observation_Click method was called.
        Logger.Trace(
            "MainPage.xaml.cs, Edit_Observation_Click: Edit_Observation_Click method called."
        );

        _ = eventArgs as RoutedEventArgs;
        var button = sender as Button;

        try
        {
            Guard.Against.Null(button, nameof(button), "Button in Edit_Observation_Click is null.");

            var objectId = button.Tag;

            // Log to trace the value of sender and eventArgs.
            Logger.Trace(
                "MainPage.xaml.cs, Edit_Observation_Click: OBJECTID: {objectId}",
                objectId
            );
        }
        catch (Exception exception)
        {
            // Log to trace the exception message.
            Logger.Error(
                exception,
                "MainPage.xaml.cs, Edit_Observation_Click: An error occurred in Edit_Observation_Click: {exception}",
                exception.Message
            );
        }
    }

    private void Delete_Observation_Click(object sender, RoutedEventArgs eventArgs)
    {
        // Log to trace that the Delete_Observation_Click method was called.
        Logger.Trace(
            "MainPage.xaml.cs, Delete_Observation_Click: Delete_Observation_Click method called."
        );

        _ = eventArgs as RoutedEventArgs;
        var button = sender as Button;

        try
        {
            Guard.Against.Null(
                button,
                nameof(button),
                "Button in Delete_Observation_Click is null."
            );

            var objectId = button.Tag;

            // Log to trace the value of sender and eventArgs.
            Logger.Trace(
                "MainPage.xaml.cs, Delete_Observation_Click: OBJECTID: {ObjectId}",
                objectId
            );

            // Delete the observation based on the objectId.
            SecchiView.DeleteObservation((long)objectId);
        }
        catch (Exception exception)
        {
            // Log to trace the exception message.
            Logger.Error(
                exception,
                "MainPage.xaml.cs, Delete_Observation_Click: An error occurred in Delete_Observation_Click: {exception}",
                exception.Message
            );
        }
    }

    #endregion Event handlers
}
