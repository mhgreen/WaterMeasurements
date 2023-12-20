using System.Diagnostics;

using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

using Esri.ArcGISRuntime.Location;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;

using WaterMeasurements.ViewModels;
using WaterMeasurements.Models;

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
    private readonly SystemLocationDataSource systemLocation = new();

    // Retrieve the local app data store.
    private readonly Windows.Storage.ApplicationDataContainer localSettings = Windows
        .Storage
        .ApplicationData
        .Current
        .LocalSettings;

    // Current ArcGIS API key
    private string? apiKey;

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

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        SecchiView = App.GetService<SecchiViewModel>();
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
            apiKey = (string?)localSettings.Values["apiKey"];
            if (apiKey == "" || apiKey is null)
            {
                // ActionStatus.IsOpen = true;
                // ActionStatus.Severity = InfoBarSeverity.Error;
                // ActionStatus.Title = "ArcGIS API Key Invalid";
                // ActionStatus.Content = "API Key is blank, configure an API key in Settings";
            }

            // Set the location display's datasource to system and enable it.
            MapView.LocationDisplay.DataSource = systemLocation;
            MapView.LocationDisplay.IsEnabled = true;
            AutoPan();
            await systemLocation.StartAsync();
            var locationDisplay = MapView.LocationDisplay.IsEnabled;
            // Log to debug the value of locationDisplay with a label.
            Debug.WriteLine($"Location Display IsEnabled: {locationDisplay}");

            // When the page is unloaded, unsubscribe from the location data source.
            MapPage.Unloaded += async (s, e) =>
            {
                Debug.WriteLine("MapPage Unloaded");
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
                Debug.WriteLine("MapView Unloaded");
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
            Debug.WriteLine(exception.ToString());
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
