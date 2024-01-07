using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Input;

using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Mapping;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Microsoft.UI.Dispatching;

using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Models;
using WaterMeasurements.Views;
using WaterMeasurements.Services;
using static WaterMeasurements.Models.PrePlannedMapConfiguration;

using Ardalis.GuardClauses;
using Newtonsoft.Json.Linq;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using RabbitMQ.Client;
using Esri.ArcGISRuntime.UI.Controls;
using Microsoft.UI.Xaml;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.ComponentModel;

namespace WaterMeasurements.ViewModels;

public partial class MapConfigurationViewModel : ObservableValidator
{
    private readonly ILogger<MapConfigurationViewModel> Logger;

    internal EventId MapConfigurationViewModelLog = new(12, "MapConfigurationViewModel");

    private readonly ILocalSettingsService? LocalSettingsService;

    public event EventHandler? SettingsUpdateComplete;
    public event EventHandler? SettingsUpdateFailed;

    [ObservableProperty]
    [Required(ErrorMessage = "Preplanned Map Name is required.")]
    private string? preplannedMapName;

    [ObservableProperty]
    public string selectView = "Map";

    [RelayCommand]
    public async Task StorePreplannedMapNameAsync()
    {
        try
        {
            Logger.LogDebug(
                MapConfigurationViewModelLog,
                "Preplanned Map Name changed to: {preplannedMap}",
                PreplannedMapName
            );
            ValidateProperty(PreplannedMapName, nameof(PreplannedMapName));
            if (HasErrors)
            {
                Logger.LogDebug(
                    MapConfigurationViewModelLog,
                    "StorePreplannedMapNameAsync(): Validating PrePlannedMapName produced errors, not saving the value."
                );
                return;
            }
            Guard.Against.Null(
                LocalSettingsService,
                nameof(LocalSettingsService),
                "Configuration Service, StorePreplannedMapNameAsync(): LocalSettingsService is null."
            );
            if (PreplannedMapName != null)
            {
                await LocalSettingsService.SaveSettingAsync(
                    PrePlannedMapConfiguration.Item[Key.PreplannedMapName],
                    PreplannedMapName
                );
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(
                MapConfigurationViewModelLog,
                exception,
                "Configuration Service, StorePreplannedMapNameAsync(): exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [RelayCommand]
    public async Task UpdateSettingsAsync()
    {
        try
        {
            Logger.LogDebug(
                MapConfigurationViewModelLog,
                "UpdateSettingsAsync(): Preplanned Map Name changed to: {preplannedMap}",
                PreplannedMapName
            );
            ValidateAllProperties();

            if (HasErrors)
            {
                Logger.LogDebug(
                    MapConfigurationViewModelLog,
                    "UpdateSettingsAsync(): HasErrors is true, returning."
                );
                SettingsUpdateFailed?.Invoke(this, EventArgs.Empty);
                return;
            }

            Guard.Against.Null(
                LocalSettingsService,
                nameof(LocalSettingsService),
                "Configuration Service, UpdateSettingsAsync(): LocalSettingsService is null."
            );
            if (PreplannedMapName != null)
            {
                await LocalSettingsService.SaveSettingAsync(
                    PrePlannedMapConfiguration.Item[Key.PreplannedMapName],
                    PreplannedMapName
                );
            }
            SettingsUpdateComplete?.Invoke(this, EventArgs.Empty);
            // SelectView = "Map";
        }
        catch (Exception exception)
        {
            Logger.LogError(
                MapConfigurationViewModelLog,
                exception,
                "Configuration Service, UpdateSettingsAsync(): exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [RelayCommand]
    public void SelectMapPage()
    {
        // Log to trace that the SelectMapPage command was called.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "SelectMapPage(): SelectMapPage command called."
        );
        SelectView = "Map";
    }

    [RelayCommand]
    private void ShowErrors()
    {
        var message = string.Join(Environment.NewLine, GetErrors().Select(e => e.ErrorMessage));

        // Log to trace that the ShowErrors command was called.
        Logger.LogTrace(MapConfigurationViewModelLog, "ShowErrors(): ShowErrors command called.");
        // Log the message.
        Logger.LogTrace(MapConfigurationViewModelLog, "ShowErrors(): Message: {message}.", message);
    }

    public MapConfigurationViewModel(
        ILocalSettingsService? localSettingsService,
        ILogger<MapConfigurationViewModel> logger
    )
    {
        Logger = logger;
        LocalSettingsService = localSettingsService;

        ErrorsChanged += MapConfigurationErrorsChanged;
        PropertyChanged += MapConfigurationPropertyChanged;

        _ = Initialize();
        // Log that the MapConfigurationViewModel is starting.
        Logger.LogInformation(MapConfigurationViewModelLog, "Starting MapConfigurationViewModel");
    }

    private async Task Initialize()
    {
        try
        {
            Guard.Against.Null(
                LocalSettingsService,
                nameof(LocalSettingsService),
                "Configuration Service, Initialize(): LocalSettingsService is null."
            );
            // Get the preplanned map name from the local settings.
            PreplannedMapName = await LocalSettingsService.ReadSettingAsync<string>(
                PrePlannedMapConfiguration.Item[Key.PreplannedMapName]
            );
        }
        catch (Exception exception)
        {
            Logger.LogError(
                MapConfigurationViewModelLog,
                exception,
                "Configuration Service, Initialize(): exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    public void MapNavView_ItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args
    )
    {
        var navOptions = new FrameNavigationOptions
        {
            TransitionInfoOverride = args.RecommendedNavigationTransitionInfo,
            IsNavigationStackEnabled = false,
        };

        // Log to debug that the MapNavView_ItemInvoked event was fired.
        Logger.LogDebug(
            MapConfigurationViewModelLog,
            "MapNavView_ItemInvoked(): MapNavView_ItemInvoked event fired."
        );

        // Log the name of the invoked item.
        Logger.LogDebug(
            MapConfigurationViewModelLog,
            "MapNavView_ItemInvoked(): Invoked item name: {invokedItemName}.",
            args.InvokedItemContainer.Name
        );

        // Log the sender name.
        Logger.LogDebug(
            MapConfigurationViewModelLog,
            "MapNavView_ItemInvoked(): Sender name: {senderName}.",
            sender.Name
        );

        switch (args.InvokedItemContainer.Name)
        {
            case "MapNavCenter":
                // Log that the MapNavCenter item was invoked.
                Logger.LogDebug(
                    MapConfigurationViewModelLog,
                    "MapNavView_ItemInvoked(): MapNavCenter item invoked."
                );
                // Send a center message.
                WeakReferenceMessenger.Default.Send(new SetMapCenterMessage(true));
                break;
            case "MapNavAutoPan":
                // Log that the MapNavAutoPan item was invoked.
                Logger.LogDebug(
                    MapConfigurationViewModelLog,
                    "MapNavView_ItemInvoked(): MapNavAutoPan item invoked."
                );
                // Send an auto pan message.
                WeakReferenceMessenger.Default.Send(new SetMapAutoPanMessage(true));
                break;
            case "SettingsItem":
                // Log that the SettingsItem item was invoked.
                Logger.LogDebug(
                    MapConfigurationViewModelLog,
                    "MapNavView_ItemInvoked(): SettingsItem item invoked."
                );
                SelectView = "Settings";
                break;
            default:
                break;
        }
    }

    public void ArcGISApiKeyHelpClick(object sender, RoutedEventArgs e)
    {
        // Log that the ArcGISApiKeyHelpClick event was fired.
        Logger.LogDebug(
            MapConfigurationViewModelLog,
            "ArcGISApiKeyHelpClick(): ArcGISApiKeyHelpClick event fired."
        );
    }

    public string Errors =>
        string.Join(
            Environment.NewLine,
            from ValidationResult error in GetErrors(null)
            select error.ErrorMessage
        );

    private void MapConfigurationPropertyChanged(object? sender, PropertyChangedEventArgs error)
    {
        // Log to trace that the MapConfigurationPropertyChanged event was fired.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "MapConfigurationPropertyChanged(): MapConfigurationPropertyChanged event fired."
        );

        if (error.PropertyName != nameof(HasErrors))
        {
            // OnPropertyChanged(nameof(HasErrors)); // Update HasErrors on every change, so I can bind to it.
            // Log to trace that the HasErrors property was updated.
            Logger.LogTrace(
                MapConfigurationViewModelLog,
                "MapConfigurationPropertyChanged(), Property in error: {PropertyInError}.",
                error.PropertyName
            );
        }
    }

    private void MapConfigurationErrorsChanged(object? sender, DataErrorsChangedEventArgs error)
    {
        // OnPropertyChanged(nameof(Errors)); // Update Errors on every Error change, so I can bind to it.
        // Log to trace that the MapConfigurationErrorsChanged event was fired.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "MapConfigurationErrorsChanged(): MapConfigurationErrorsChanged event fired."
        );
        // Log the property name.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "MapConfigurationErrorsChanged(): Property name: {propertyName}.",
            error.PropertyName
        );
        if (string.IsNullOrEmpty(Errors))
        {
            // Log that the Errors property is empty.
            Logger.LogTrace(
                MapConfigurationViewModelLog,
                "MapConfigurationErrorsChanged(): Entry passed validation."
            );
        }
        else
        {
            // Log that the Errors property is not empty.
            Logger.LogTrace(
                MapConfigurationViewModelLog,
                "MapConfigurationErrorsChanged(): Errors: {errors}.",
            Errors
            );
        }
    }
}
