using System;
using System.Resources;
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
using WaterMeasurements.Helpers;
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
using Microsoft.UI.Xaml.Input;

namespace WaterMeasurements.ViewModels;

public partial class MapConfigurationViewModel : ObservableValidator
{
    private readonly ILogger<MapConfigurationViewModel> Logger;

    internal EventId MapConfigurationViewModelLog = new(12, "MapConfigurationViewModel");

    private readonly ILocalSettingsService? LocalSettingsService;

    //public event EventHandler? SettingsUpdateComplete;
    //public event EventHandler? SettingsUpdateFailed;

    private bool preplannedMapNameValid = false;

    // It does not seem possible to localize the error messages in the DataAnnotations.
    // Using the ResourceExtensions.GetLocalized() method to get the localized strings.

    private static readonly string unableToValide = ResourceExtensions.GetLocalized(
        "Error_UnableWithoutName"
    );
    private static readonly string stringNotOneToHundred = ResourceExtensions.GetLocalized(
        "Error_NotBetweenOneAndHundred"
    );

    [ObservableProperty]
    [Required(ErrorMessage = "NeedName")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "NotOneToHundred")]
    private string? preplannedMapName;

    [ObservableProperty]
    private string? preplannedMapNameError;

    [ObservableProperty]
    private string? preplannedMapNameErrorVisibility = "Collapsed";

    [ObservableProperty]
    public string selectView = "Map";

    [RelayCommand]
    public void PreplannedMapNameIsChanging()
    {
        // Log to trace that the PreplannedMapNameIsChanging command was called.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "PreplannedMapNameIsChanging(): PreplannedMapNameIsChanging invoked."
        );
        // Log the PreplannedMapName.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "PreplannedMapNameIsChanging(): PreplannedMapName: {preplannedMapName}.",
            PreplannedMapName
        );

        var results = new List<ValidationResult>();
        preplannedMapNameValid = Validator.TryValidateProperty(
            PreplannedMapName,
            new ValidationContext(this, null, null) { MemberName = nameof(PreplannedMapName) },
            results
        );
        // Log isValid to debug.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "PreplannedMapNameIsChanging(): isValid: {isValid}.",
            preplannedMapNameValid
        );
        if (preplannedMapNameValid is false)
        {
            var firstValidationResult = results.FirstOrDefault();
            if (firstValidationResult != null)
            {
                Logger.LogDebug(
                    MapConfigurationViewModelLog,
                    "PreplannedMapNameIsChanging(): firstValidationResult: {firstValidationResult}.",
                    firstValidationResult
                );
            }
            if (firstValidationResult is not null)
            {
                if (firstValidationResult.ToString() == "NeedName")
                {
                    Logger.LogTrace(
                        MapConfigurationViewModelLog,
                        "PreplannedMapNameIsChanging(): NeedName error."
                    );
                    PreplannedMapNameError = unableToValide;
                    PreplannedMapNameErrorVisibility = "Visible";
                }
                if (firstValidationResult.ToString() == "NotOneToHundred")
                {
                    Logger.LogTrace(
                        MapConfigurationViewModelLog,
                        "PreplannedMapNameIsChanging(): NotOneToHundred error."
                    );
                    PreplannedMapNameError = stringNotOneToHundred;
                    PreplannedMapNameErrorVisibility = "Visible";
                }
            }
        }
        else
        {
            PreplannedMapNameErrorVisibility = "Collapsed";
        }
    }

    [RelayCommand]
    public void StorePreplannedMapNameAsync()
    {
        try
        {
            Logger.LogTrace(
                MapConfigurationViewModelLog,
                "StorePreplannedMapNameAsync(): invoked with preplannedMap set to: {preplannedMap}",
                PreplannedMapName
            );

            Guard.Against.Null(
                LocalSettingsService,
                nameof(LocalSettingsService),
                "Configuration Service, StorePreplannedMapNameAsync(): LocalSettingsService is null."
            );
            if (PreplannedMapName is not null && preplannedMapNameValid)
            {
                Task.Run(async () =>
                    {
                        await LocalSettingsService.SaveSettingAsync(
                            PrePlannedMapConfiguration.Item[PrePlannedMapConfiguration.Key.PreplannedMapName],
                            PreplannedMapName
                        );
                    })
                    .Wait();
            }
            else
            {
                Logger.LogTrace(
                    MapConfigurationViewModelLog,
                    "StorePreplannedMapNameAsync(): PreplannedMapName did not pass validation or is not yet validated via PreplannedMapNameIsChanging(), not saving the value."
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

        // ErrorsChanged += MapConfigurationErrorsChanged;
        // PropertyChanged += MapConfigurationPropertyChanged;

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
            // Get the preplanned map name from local settings.
            PreplannedMapName = await LocalSettingsService.ReadSettingAsync<string>(
                PrePlannedMapConfiguration.Item[PrePlannedMapConfiguration.Key.PreplannedMapName]
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
        /*
        var navOptions = new FrameNavigationOptions
        {
            TransitionInfoOverride = args.RecommendedNavigationTransitionInfo,
            IsNavigationStackEnabled = false,
        };
        */


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

    public void ArcGISApiKeyHelpClick()
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
            ClearErrors(nameof(PreplannedMapName));
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
