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

// Message to set the value of SecchiSelectView.
public class SetSecchiSelectViewMessage(string value) : ValueChangedMessage<string>(value) { }

public partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }
    public SecchiViewModel SecchiView { get; }
    public MapConfigurationViewModel MapConfigurationView { get; }
    public DataCollectionViewModel DataCollectionView { get; }
    public SecchiConfigurationViewModel SecchiConfigurationView { get; }
    public SecchiNewLocationViewModel SecchiNewLocationView { get; }

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

    private FeatureTable? secchiLocationFeatures;

    // private FeatureLayer? secchiLocationLayer;

    private SecchiAddLocation secchiAddLocation;

    // Extent of current map
    private Geometry? extent;

    // Geometry editor to manage points on the map.
    private GeometryEditor? geometryEditor;

    // Graphics overlay to display points on the map.
    private GraphicsOverlay? graphicsOverlay;

    // Graphics overlay for point selection.
    private GraphicsOverlay? selectionOverlay;

    // Marker symbol for a collection location.
    private readonly SimpleMarkerSymbol collectionLocationSymbol =
        new(SimpleMarkerSymbolStyle.Circle, Color.FromArgb(255, 0, 120, 212), 8);

    // Symbol for location highlighting.
    private readonly SimpleMarkerSymbol highlightLocationSymbol =
        new()
        {
            // Create a clear color, make it a bit bigger than the point on the map.
            // Then create a circle with a black outline.
            // The result is a black circle with a clear center that can be used as a highlight.
            Color = Color.FromArgb(0, 0, 0, 0),
            Size = 3,
            Style = SimpleMarkerSymbolStyle.Circle,
            Outline = new SimpleLineSymbol(
                SimpleLineSymbolStyle.Solid,
                Color.FromArgb(255, 174, 232, 255),
                3
            )
        };

    private double geoTriggerDistance = 0;

    private bool secchiLocationAddPageSelected = false;

    [RelayCommand]
    private void ReCenter()
    {
        MapView.LocationDisplay.AutoPanMode = Esri.ArcGISRuntime
            .UI
            .LocationDisplayAutoPanMode
            .Recenter;
    }

    [RelayCommand]
    private void AutoPan()
    {
        MapView.LocationDisplay.AutoPanMode = Esri.ArcGISRuntime
            .UI
            .LocationDisplayAutoPanMode
            .Navigation;
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
        SecchiNewLocationView = App.GetService<SecchiNewLocationViewModel>();

        Logger.Debug("MainPage.xaml.cs, MainPage: Starting");

        // Set the initial Secchi Measurements page to the Collection Table.
        // This is used by the UI to determine which Secchi page to display.
        secchiPageSelection = new SecchiPageSelection
        {
            SecchiSelectView = "SecchiCollectionTable"
        };

        secchiChannelNumbers = new();

        InitializeComponent();

        var crossMarkerSymbol = new SimpleMarkerSymbol(
            SimpleMarkerSymbolStyle.Cross,
            Color.FromArgb(255, 23, 217, 232),
            10
        );

        var geometryEditorStyle = new GeometryEditorStyle
        {
            VertexSymbol = new SimpleMarkerSymbol(
                SimpleMarkerSymbolStyle.Circle,
                Color.FromArgb(255, 255, 0, 0),
                10
            ),
            LineSymbol = new SimpleLineSymbol(
                SimpleLineSymbolStyle.Solid,
                Color.FromArgb(255, 0, 0, 255),
                2
            ),
            FillSymbol = new SimpleFillSymbol(
                SimpleFillSymbolStyle.Solid,
                Color.FromArgb(100, 0, 0, 255),
                new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, Color.FromArgb(255, 0, 0, 255), 2)
            ),
            SelectedVertexSymbol = crossMarkerSymbol
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

            if (MapView.GraphicsOverlays is not null)
            {
                // Create a graphics overlay and add it to the map view.
                graphicsOverlay = new GraphicsOverlay();
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
                                        collectionLocationSymbol
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

            // Add a handler for the MapViewTapped event.
            MapView.GeoViewTapped += OnMapViewTapped;

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

            SecchiNewLocationView.LocationTypeSet = false;
            SecchiNewLocationView.LocationSourceSet = false;

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

    #region Event handlers

    private async void OnMapViewTapped(object? sender, GeoViewInputEventArgs eventArgs)
    {
        var tolerance = 20d; // Use larger tolerance for touch
        var maximumResults = 1; // Only return one graphic
        var onlyReturnPopups = false; // Don't return only popups

        try
        {
            // If not already on SecchiAddLocation view, then move to it.
            if (secchiPageSelection.SecchiSelectView != "SecchiAddLocation")
            {
                WeakReferenceMessenger.Default.Send<SetSecchiSelectViewMessage>(
                    new SetSecchiSelectViewMessage("SecchiAddLocation")
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

                        var graphicWithSymbol = new Graphic(mapPoint, highlightLocationSymbol);
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

    private void MapNavView_Loaded(object sender, RoutedEventArgs e)
    {
        MapNavView.SelectedItem = MapNavView.MenuItems[1];
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

            var graphicWithSymbol = new Graphic(mapPoint, highlightLocationSymbol);
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
                Logger.Debug(
                    "MainPage.xaml.cs, SecchiNavView_ItemInvoked(): Add Location selected."
                );
                secchiPageSelection.SecchiSelectView = "SecchiAddLocation";
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
            SecchiMeasurements secchiMeasurements =
                new(
                    MapView.LocationDisplay.Location.Position,
                    short.Parse(Measurement1.Text),
                    short.Parse(Measurement2.Text),
                    short.Parse(Measurement3.Text)
                );
            SecchiView.ProcessSecchiMeasurements(secchiMeasurements);
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
    private async Task SaveSecchiLocationAsync()
    {
        // Log to trace that the SaveSecchiLocation_Click method was called.
        Logger.Trace(
            "MainPage.xaml.cs, SaveSecchiLocation_Click: SaveSecchiLocation_Click method called."
        );

        double latitude;
        double longitude;

        Graphic graphic = new();

        try
        {
            if (MapView.LocationDisplay.Location is null)
            {
                // Log to trace that the LocationDisplay.Location is null.
                Logger.Error(
                    "MainPage.xaml.cs, SaveSecchiLocation_Click: LocationDisplay.Location is null."
                );
                return;
            }

            if (MapView.GeometryEditor is null)
            {
                // Log to trace that the MapView.GeometryEditor is null.
                Logger.Error(
                    "MainPage.xaml.cs, SaveSecchiLocation_Click: MapView.GeometryEditor is null."
                );
                return;
            }

            if (graphicsOverlay is null)
            {
                // Log to trace that the graphicsOverlay is null.
                Logger.Error(
                    "MainPage.xaml.cs, SaveSecchiLocation_Click: graphicsOverlay is null."
                );
                return;
            }

            if (secchiLocationFeatures is null)
            {
                // Log to trace that the secchiLocationFeatures is null.
                Logger.Error(
                    "MainPage.xaml.cs, SaveSecchiLocation_Click: secchiLocationFeatures is null."
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

            secchiAddLocation.LocationNumber = nextLocationNumber;

            // Log to trace the nextLocationNumber.
            Logger.Trace(
                "MainPage.xaml.cs, SaveSecchiLocation_Click: nextLocationNumber: {nextLocationNumber}",
                nextLocationNumber
            );

            // Set the location name to the value of SecchiNewLocationView.LocationName.
            // This could be retrieved from either SecchiNewLocationView or from SecchiLocationName in MainPage.xaml
            secchiAddLocation.LocationName = SecchiNewLocationView.LocationName;

            // Log to trace SecchiNewLocationView.LocationName.
            Logger.Trace(
                "MainPage.xaml.cs, SaveSecchiLocation_Click: SecchiNewLocationView.LocationName: {LocationName}",
                SecchiNewLocationView.LocationName
            );

            switch (secchiAddLocation.LocationSource)
            {
                case LocationSource.EnteredLatLong:

                    // Log to trace the entered latitude and longitude.
                    Logger.Trace(
                        "MainPage.xaml.cs, SaveSecchiLocation_Click: EnteredLatLong: Latitude: {latitude}, Longitude: {longitude}.",
                        EnteredLatitude.Text,
                        EnteredLongitude.Text
                    );

                    if (graphicsOverlay is null)
                    {
                        // Log to trace that the graphicsOverlay is null.
                        Logger.Error(
                            "MainPage.xaml.cs, SaveSecchiLocation_Click: graphicsOverlay is null."
                        );
                        return;
                    }

                    // Set the location to the entered latitude and longitude.
                    // This is retrieved from either SecchiNewLocationView or from EnteredLatitude and EnteredLongitude in MainPage.xaml
                    secchiAddLocation.Location = new MapPoint(
                        double.Parse(SecchiNewLocationView.LongitudeEntry),
                        double.Parse(SecchiNewLocationView.LatitudeEntry),
                        SpatialReferences.Wgs84
                    );

                    latitude = secchiAddLocation.Location.As<MapPoint>().Y;
                    longitude = secchiAddLocation.Location.As<MapPoint>().X;

                    secchiAddLocation.Latitude = latitude;
                    secchiAddLocation.Longitude = longitude;

                    // Log to trace the latitude and lon values of the mapPoint.
                    Logger.Trace(
                        "MainPage.xaml.cs, GeometryEditor_PropertyChanged: Added point to map at: Lat {latitude}, Lon {lon}.",
                        latitude,
                        longitude
                    );

                    // Create a new graphic using the feature's geometry and the collection location symbol
                    graphic = new Graphic(secchiAddLocation.Location, collectionLocationSymbol);

                    // Add the LocationId from the feature's attributes to the graphic's attributes
                    graphic.Attributes.Add("LocationId", secchiAddLocation.LocationNumber);

                    // Add the graphic to the graphics overlay
                    graphicsOverlay.Graphics.Add(graphic);

                    await MapView.SetViewpointCenterAsync(secchiAddLocation.Location, 2500);

                    SecchiView.AddNewLocation(secchiAddLocation);

                    SecchiNewLocationView.LocationName = string.Empty;
                    SecchiNewLocationView.LatitudeEntry = string.Empty;
                    SecchiNewLocationView.LongitudeEntry = string.Empty;

                    SecchiNewLocationView.LocationCanBeSaved = false;

                    break;

                case LocationSource.CurrentGPS:
                    if (MapView.LocationDisplay.Location is null)
                    {
                        // Log to trace that the LocationDisplay.Location is null.
                        Logger.Error(
                            "MainPage.xaml.cs, SaveSecchiLocation_Click: LocationDisplay.Location is null."
                        );
                        break;
                    }

                    secchiAddLocation.Location = new MapPoint(
                        MapView.LocationDisplay.Location.Position.X,
                        MapView.LocationDisplay.Location.Position.Y,
                        SpatialReferences.Wgs84
                    );

                    latitude = secchiAddLocation.Location.Y;
                    secchiAddLocation.Latitude = latitude;
                    longitude = secchiAddLocation.Location.X;
                    secchiAddLocation.Longitude = longitude;

                    Logger.Trace(
                        "MainPage.xaml.cs, SaveSecchiLocation_Click, CurrentGPS: presentLocation: Lat {latitude}, Lon {longitude}.",
                        latitude,
                        longitude
                    );

                    if (graphicsOverlay is null)
                    {
                        // Log to trace that the graphicsOverlay is null.
                        Logger.Error(
                            "MainPage.xaml.cs, SaveSecchiLocation_Click: graphicsOverlay is null."
                        );
                    }
                    else
                    {
                        // Create a new graphic using the feature's geometry and the collection location symbol
                        graphic = new Graphic(secchiAddLocation.Location, collectionLocationSymbol);

                        // Add the LocationId from the feature's attributes to the graphic's attributes
                        graphic.Attributes.Add("LocationId", secchiAddLocation.LocationNumber);

                        // Add the graphic to the graphics overlay
                        graphicsOverlay.Graphics.Add(graphic);
                    }

                    await MapView.SetViewpointCenterAsync(secchiAddLocation.Location, 2500);

                    SecchiView.AddNewLocation(secchiAddLocation);

                    SecchiNewLocationView.LocationName = string.Empty;

                    SecchiNewLocationView.LocationCanBeSaved = false;

                    break;

                case LocationSource.PointOnMap:
                    if (MapView.GeometryEditor is null)
                    {
                        // Log to trace that the MapView.GeometryEditor is null.
                        Logger.Error(
                            "MainPage.xaml.cs, SaveSecchiLocation_Click: MapView.GeometryEditor is null."
                        );
                        break;
                    }

                    if (MapView.GeometryEditor.IsStarted)
                    {
                        var newLocation = MapView.GeometryEditor.Stop();
                        if (newLocation is not null)
                        {
                            // Set the new location to the secchiAddLocation.Location.
                            secchiAddLocation.Location = newLocation.As<MapPoint>();

                            // Create a new graphic using the feature's geometry and the collection location symbol
                            graphic = new Graphic(
                                secchiAddLocation.Location,
                                collectionLocationSymbol
                            );

                            // Add the LocationId from the feature's attributes to the graphic's attributes
                            graphic.Attributes.Add("LocationId", secchiAddLocation.LocationNumber);

                            // Add the graphic to the graphics overlay
                            graphicsOverlay.Graphics.Add(graphic);

                            // log the latitute and longitude of the new location.
                            latitude = newLocation
                                .Project(SpatialReferences.Wgs84)
                                .As<MapPoint>()
                                .Y;
                            secchiAddLocation.Latitude = latitude;
                            longitude = newLocation
                                .Project(SpatialReferences.Wgs84)
                                .As<MapPoint>()
                                .X;
                            secchiAddLocation.Longitude = longitude;
                            Logger.Trace(
                                "MainPage.xaml.cs, SaveSecchiLocation_Click: New Location is at: Lat {lat}, Lon {lon}.",
                                latitude,
                                longitude
                            );
                            // Create the feature.
                            if (secchiLocationFeatures is null)
                            {
                                // Log to trace that the secchiLocationFeatures is null.
                                Logger.Error(
                                    "MainPage.xaml.cs, SaveSecchiLocation_Click: secchiLocationFeatures is null."
                                );
                                return;
                            }

                            SecchiView.AddNewLocation(secchiAddLocation);

                            SecchiNewLocationView.LocationName = string.Empty;

                            SecchiNewLocationView.LocationCanBeSaved = false;
                        }

                        // Since the drop-down is set to "Point on Map", start the geometry editor.
                        // This provides for a consistent user experience if the page is navigated away from and then back.
                        MapView.GeometryEditor.Start(GeometryType.Point);
                    }
                    break;

                default:
                    Logger.Error(
                        "MainPage.xaml.cs, SaveSecchiLocation_Click: LocationSource not set."
                    );
                    break;
            }
        }
        catch (Exception exception)
        {
            // Log to trace that an error occurred in SaveSecchiLocation_Click.
            Logger.Error(
                exception,
                "MainPage.xaml.cs, SaveSecchiLocation_Click: An error occurred in SaveSecchiLocation_Click: {exception}.",
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
                        // Add the new location to the map with a tag of 'LocationId' and the value of secchiAddLocation.LocationNumber.
                        // This is used to identify the location when it is selected.
                        graphicsOverlay.Graphics.Add(
                            new Graphic(
                                geometry,
                                new Dictionary<string, object?>
                                {
                                    { "LocationId", secchiAddLocation.LocationNumber }
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
                        secchiAddLocation.LocationType = LocationType.Occasional;
                        SecchiLocationTypeDropDown.Content = "Occasional";
                        SecchiNewLocationView.LocationTypeSet = true;
                        break;
                    case "Ongoing":
                        secchiAddLocation.LocationType = LocationType.Ongoing;
                        SecchiLocationTypeDropDown.Content = "Ongoing";
                        SecchiNewLocationView.LocationTypeSet = true;
                        break;
                    default:
                        // Log to trace that an invalid tag value was encountered.
                        Logger.Trace(
                            "MainPage.xaml.cs, LocationType_Click: Invalid tag value: {tag}",
                            tag
                        );
                        SecchiNewLocationView.LocationTypeSet = false;
                        break;
                }
            }

            // Log to trace the value of locationType with a label.
            Logger.Trace(
                "MainPage.xaml.cs, LocationType_Click: Location Type: {locationType}",
                secchiAddLocation.LocationType
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
                        secchiAddLocation.LocationSource = LocationSource.CurrentGPS;
                        LatLongEntry.Visibility = Visibility.Collapsed;
                        SecchiNewLocationView.LocationSourceActive = false;
                        SecchiLocationSourceDropDown.Content = "Current GPS";
                        SecchiNewLocationView.LocationSourceSet = true;
                        break;
                    case "PointOnMap":
                        secchiAddLocation.LocationSource = LocationSource.PointOnMap;

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
                        SecchiNewLocationView.LocationSourceActive = false;
                        SecchiLocationSourceDropDown.Content = "Map Point";
                        SecchiNewLocationView.LocationSourceSet = true;
                        break;
                    case "EnterLatLong":
                        if (secchiAddLocation.LocationSource == LocationSource.PointOnMap)
                        {
                            MapView.GeometryEditor.Stop();
                        }
                        secchiAddLocation.LocationSource = LocationSource.EnteredLatLong;
                        SecchiLocationSourceDropDown.Content = "Enter Lat/Long";
                        LatLongEntry.Visibility = Visibility.Visible;
                        SecchiNewLocationView.LocationSourceActive = true;
                        SecchiNewLocationView.LocationSourceSet = true;
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
                secchiAddLocation.LocationSource
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
            Guard.Against.Null(
                secchiLocationFeatures,
                nameof(secchiLocationFeatures),
                "secchiLocationFeatures is null."
            );

            Guard.Against.Null(button, nameof(button), "Button in Delete_Location_Click is null.");

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

            // Delete the location.
            SecchiView.DeleteLocation(locationId);

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

    #endregion Event handlers
}
