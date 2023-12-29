using System.Diagnostics;

using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

using Esri.ArcGISRuntime.Location;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;

using NLog;

using Ardalis.GuardClauses;

using WaterMeasurements.ViewModels;
using WaterMeasurements.Models;
using WaterMeasurements.Services.Instances;
using Microsoft.Extensions.Logging;
using NLog.Fluent;
using WaterMeasurements.Services;
using WaterMeasurements.Contracts.Services;
using static WaterMeasurements.Models.PrePlannedMapConfiguration;

namespace WaterMeasurements.Views;

// Message to notify modules that the UIQueue has been set.
public class UIQueueSetMessage : ValueChangedMessage<UIQueue>
{
    public UIQueueSetMessage(UIQueue uiQueue)
        : base(uiQueue) { }
}

// Message to notify modules that the MapPage has been unloaded.
public class MapPageUnloaded : ValueChangedMessage<MapPageUnloadedMessage>
{
    public MapPageUnloaded()
        : base(new MapPageUnloadedMessage()) { }
}

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }
    public SecchiViewModel SecchiView { get; }

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly SystemLocationDataSource systemLocation = new();

    // Current ArcGIS API key
    public string? apiKey;

    // Current WebMapId
    public string? webMapIdKey;

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
    public void StoreDeveloperKey()
    {
        apiKey = ApiKeyArcGIS.Password;
        Logger.Debug("API key changed to: " + apiKey);
        ViewModel.StoreSettingByKey(PrePlannedMapConfiguration.Item[Key.ArcgisApiKey], apiKey);
    }

    [RelayCommand]
    public void StoreWebMapKey()
    {
        webMapIdKey = SecchiWebMapId.Text;
        Logger.Debug("webMapIdKey key changed to: " + webMapIdKey);
        ViewModel.StoreSettingByKey(PrePlannedMapConfiguration.Item[Key.OfflineMapIdentifier], webMapIdKey);
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
        Logger.Debug("MainPage.xaml.cs, MainPage: Starting");

        InitializeComponent();

        // Send the UI Dispatcher Queue to subscribers.
        DispatcherQueue.TryEnqueue(() =>
        {
            WeakReferenceMessenger.Default.Send(
                new UIQueueSetMessage(new UIQueue(DispatcherQueue.GetForCurrentThread()))
            );
        });

        Initialize();
    }

    private async void Initialize()
    {
        try
        {
            Logger.Debug("MainPage.xaml.cs, Initialize: Initializing MainPage");

            apiKey = await ViewModel.RetrieveSettingByKey<string>(PrePlannedMapConfiguration.Item[Key.ArcgisApiKey]);
            webMapIdKey = await ViewModel.RetrieveSettingByKey<string>(PrePlannedMapConfiguration.Item[Key.OfflineMapIdentifier]);

            Logger.Debug("ArcGIS API key initial value: " + apiKey);
            Logger.Debug("ArcGIS Secchi web map id initial value: " + webMapIdKey);

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

            /*
            CollectionStackPanel.Visibility = Visibility.Visible;
            var integer4TextBox = new TextBox();
            var transparentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush() { Opacity = 0 };
            integer4TextBox.Background = transparentBrush;
            integer4TextBox.Name = "Integer4TextBox";
            TextBoxExtensions.SetValidationMode(integer4TextBox, TextBoxExtensions.ValidationMode.Dynamic);
            TextBoxExtensions.SetValidationType(integer4TextBox, TextBoxExtensions.ValidationType.Number);
            CollectionStackPanel.Children.Add(integer4TextBox);
            var undecidedButton = new Button();
            undecidedButton.Name = "UndecidedButton";
            undecidedButton.Content = "Undecided";
            undecidedButton.Background = transparentBrush;
            undecidedButton.Click += OkButton_Click;
            ButtonStackPanel.Children.Add(undecidedButton);
            */

            // await DataCollectionDialog.ShowAsync();

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

    /*
    private void MainPage_Loaded(object sender, RoutedEventArgs routedEventArgs)
    {
        // CollectionStackPanel.UpdateLayout();
    }
    */

    private void SaveSecchiMeasurements_Click(object sender, RoutedEventArgs e)
    {
        // Collect the integers here
        ViewModel.ShowSecchiCollectionPoint = true;
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
            // ActionStatus.IsOpen = true;
            // ActionStatus.Severity = InfoBarSeverity.Error;
            // ActionStatus.Title = "Location not available";
            // ActionStatus.Content = "Location is not available, there is a problem with system location services.";
        }
    }

    private void CancelSecchiMeasurements_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowSecchiCollectionPoint = true;
    }
}
