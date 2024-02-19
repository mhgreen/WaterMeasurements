using System.Drawing;
using Ardalis.GuardClauses;
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
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NLog;
using WaterMeasurements.Models;
using WaterMeasurements.Services;
using WaterMeasurements.Services.IncrementalLoaders;
using WaterMeasurements.ViewModels;
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

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }
    public SecchiViewModel SecchiView { get; }
    public MapConfigurationViewModel MapConfigurationView { get; }
    public DataCollectionViewModel DataCollectionView { get; }
    public SecchiConfigurationViewModel SecchiConfigurationView { get; }

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly SystemLocationDataSource systemLocation = new();

    // Current ArcGIS API key
    public string? apiKey;

    // Current WebMapId
    public string? webMapId;

    // Source of the location
    private LocationSource locationSource;

    // Type of location
    private LocationType locationType;

    // Extent of current map
    private Geometry? extent;

    // Geometry editor to manage points on the map.
    private GeometryEditor? geometryEditor;

    // Graphics overlay to display points on the map.
    private GraphicsOverlay? graphicsOverlay;

    // Selected graphic to manage points on the map.
    private Graphic? selectedGraphic;

    public IncrementalLoadingCollection<
        SecchiLocationIncrementalLoader,
        SecchiLocationDisplay
    > SecchiLocationsIncrementalLoading;

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
    public async Task StoreWebMapIdAsync()
    {
        Logger.Debug("webMapId changed to: " + WebMapId.Text);

        await ViewModel.StoreSettingByKeyAsync(
            PrePlannedMapConfiguration.Item[Key.OfflineMapIdentifier],
            WebMapId.Text
        );
    }

    private void RevealApiKey_Changed(object sender, RoutedEventArgs e)
    {
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

        Logger.Debug("MainPage.xaml.cs, MainPage: Starting");

        SecchiLocationsIncrementalLoading = [];

        InitializeComponent();

        SimpleMarkerSymbol crossMarkerSymbol = new SimpleMarkerSymbol(
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
                Logger.Debug("MainPage.xaml.cs, Initialize: ApiKey key is null or empty.");
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
                Logger.Debug("MainPage.xaml.cs, Initialize: WebMapId key is null or empty.");
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
                Logger.Debug(
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

            // Create a graphics overlay and add it to the map view.
            graphicsOverlay = new GraphicsOverlay();
            MapView.GraphicsOverlays.Add(graphicsOverlay);

            // Create a geometry editor to allow the user to select a location on the map.
            geometryEditor = new GeometryEditor();
            MapView.GeometryEditor = geometryEditor;

            SecchiLocationsIncrementalLoading = new IncrementalLoadingCollection<
                SecchiLocationIncrementalLoader,
                SecchiLocationDisplay
            >(itemsPerPage: 5);

            /*
            // Configure SecchiLocationsListView to use the SecchiLocationsIncrementalLoading collection.
            var secchiLocationsIncrementalLoading = new IncrementalLoadingCollection<
                SecchiLocationIncrementalLoader,
                SecchiLocationDisplay
            >(itemsPerPage: 5);
            SecchiLocationsListView.ItemsSource = secchiLocationsIncrementalLoading;
            SecchiLocationsListView.IsItemClickEnabled = true;
            SecchiLocationsListView.ItemClick += (source, eventArgs) =>
            {
                var item = eventArgs.ClickedItem as SecchiLocationDisplay;
                if (item is not null)
                {
                    Logger.Debug(
                        "MainPage.xaml.cs, Initialize: SecchiLocationsListView.ItemClick, ClickedItem: {item}",
                        item.LocationName
                    );
                }
            };
            */


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
            Logger.Debug(
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
        ViewModel.ShowSecchiCollectionPoint = true;
    }

    private void SaveSecchiLocation_Click()
    {
        // Log to trace that the SaveSecchiLocation_Click method was called.
        Logger.Trace(
            "MainPage.xaml.cs, SaveSecchiLocation_Click: SaveSecchiLocation_Click method called."
        );

        if (MapView.LocationDisplay.Location is null)
        {
            // Log to trace that the LocationDisplay.Location is null.
            Logger.Error(
                "MainPage.xaml.cs, SaveSecchiLocation_Click: LocationDisplay.Location is null."
            );
            return;
        }

        var presentLocation = MapView.LocationDisplay.Location.Position;

        Logger.Trace(
            "MainPage.xaml.cs, SaveSecchiLocation_Click: presentLocation: Lat {presentLocation.Y}, Lon {presentLocation.X}.",
            presentLocation.Y,
            presentLocation.X
        );

        if (MapView.GeometryEditor is null)
        {
            // Log to trace that the MapView.GeometryEditor is null.
            Logger.Error(
                "MainPage.xaml.cs, SaveSecchiLocation_Click: MapView.GeometryEditor is null."
            );
            return;
        }

        // Use the geometry editor to allow the user to select a location on the map.
        MapView.GeometryEditor.Start(GeometryType.Point);

        MapView.SetViewpointCenterAsync(presentLocation, 17);
    }

    private void GeometryEditor_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs eventArgs
    )
    {
        SimpleMarkerSymbol newLocationSymbol = new SimpleMarkerSymbol(
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

                    var lat = presentLocation.Y;
                    var lon = presentLocation.X;

                    // Log to trace the lat and lon values of the mapPoint.
                    Logger.Trace(
                        "MainPage.xaml.cs, GeometryEditor_PropertyChanged: MapPoint: Lat {lat}, Lon {lon}.",
                        lat,
                        lon
                    );

                    MapView.GeometryEditor.Stop();
                    if (graphicsOverlay is not null)
                    {
                        graphicsOverlay.Graphics.Add(new Graphic(geometry, newLocationSymbol));
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

    private void CancelSecchiLocation_Click()
    {
        // Log to trace that the CancelSecchiLocation_Click method was called.
        Logger.Trace(
            "MainPage.xaml.cs, CancelSecchiLocation_Click: CancelSecchiLocation_Click method called."
        );
    }

    private void LocationType_Click(object sender, RoutedEventArgs eventArgs)
    {
        _ = eventArgs as RoutedEventArgs;

        // Log to trace that the LocationType_Click method was called.
        Logger.Trace("MainPage.xaml.cs, LocationType_Click: LocationType_Click method called.");

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
                case "OneTime":
                    locationType = LocationType.OneTime;
                    SecchiLocationTypeDropDown.Content = "One-Time";
                    break;
                case "Permanent":
                    locationType = LocationType.Permanent;
                    SecchiLocationTypeDropDown.Content = "Permanent";
                    break;
                default:
                    // Log to trace that an invalid tag value was encountered.
                    Logger.Trace(
                        "MainPage.xaml.cs, LocationType_Click: Invalid tag value: {tag}",
                        tag
                    );
                    break;
            }
        }

        // Log to trace the value of locationType with a label.
        Logger.Trace(
            "MainPage.xaml.cs, LocationType_Click: Location Type: {locationType}",
            locationType
        );
    }

    private void LocationSource_Click(object sender, RoutedEventArgs eventArgs)
    {
        _ = eventArgs as RoutedEventArgs;

        // Log to trace that the LocationSource_Click method was called.
        Logger.Trace("MainPage.xaml.cs, LocationSource_Click: LocationSource_Click method called.");

        if (sender is null)
        {
            // Log to trace that the sender is null.
            Logger.Trace("MainPage.xaml.cs, LocationSource_Click: sender is null.");
            return;
        }

        var menuFlyoutItem = sender as MenuFlyoutItem;

        if (menuFlyoutItem != null)
        {
            var tag = menuFlyoutItem.Tag as string;

            switch (tag)
            {
                case "CurrentGPS":
                    locationSource = LocationSource.CurrentGPS;
                    LatLongEntry.Visibility = Visibility.Collapsed;
                    SecchiLocationSourceDropDown.Content = "Current GPS";
                    break;
                case "PointOnMap":
                    locationSource = LocationSource.PointOnMap;
                    LatLongEntry.Visibility = Visibility.Collapsed;
                    SecchiLocationSourceDropDown.Content = "Map Point";
                    break;
                case "EnterLatLong":
                    locationSource = LocationSource.EnteredLatLong;
                    SecchiLocationSourceDropDown.Content = "Enter Lat/Long";
                    LatLongEntry.Visibility = Visibility.Visible;
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
            locationSource
        );
    }

    private void Edit_Location_Click(object sender, RoutedEventArgs eventArgs)
    {
        // Log to trace that the Edit_Location_Click method was called.
        Logger.Trace("MainPage.xaml.cs, Edit_Location_Click: Edit_Location_Click method called.");

        var button = sender as Button;

        Guard.Against.Null(button, nameof(button), "Button in Edit_Location_Click is null.");

        var locationId = button.Tag;

        // Log to trace the value of sender and eventArgs.
        Logger.Trace("MainPage.xaml.cs, Edit_Location_Click: LocationId: {locationId}", locationId);
    }

    #endregion Event handlers
}
