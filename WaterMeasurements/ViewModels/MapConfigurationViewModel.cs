using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Resources;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Newtonsoft.Json.Linq;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Helpers;
using WaterMeasurements.Models;
using WaterMeasurements.Services;
using WaterMeasurements.Views;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static WaterMeasurements.Models.PrePlannedMapConfiguration;

namespace WaterMeasurements.ViewModels;

public class SetLicenseTypeMessage(string licenseType)
    : ValueChangedMessage<string>(licenseType) { }

public partial class MapConfigurationViewModel : ObservableValidator
{
    private readonly ILogger<MapConfigurationViewModel> Logger;

    internal EventId MapConfigurationViewModelLog = new(12, "MapConfigurationViewModel");

    private readonly ILocalSettingsService? LocalSettingsService;

    //public event EventHandler? SettingsUpdateComplete;
    //public event EventHandler? SettingsUpdateFailed;

    private bool preplannedMapNameValid = false;
    private bool preplannedMapIdValid = false;
    private bool apiKeyValid = false;
    private bool licenseKeyValid = false;

    // It does not seem possible to localize the error messages in the DataAnnotations.
    // Using the ResourceExtensions.GetLocalized() method to get the localized strings.

    private static readonly string unableToValideWithoutName = ResourceExtensions.GetLocalized(
        "Error_UnableWithoutName"
    );

    private static readonly string unableToValideWithoutId = ResourceExtensions.GetLocalized(
        "Error_UnableWithoutId"
    );

    private static readonly string unableToValideWithoutKey = ResourceExtensions.GetLocalized(
        "Error_UnableWithoutKey"
    );

    private static readonly string stringNotOneToHundred = ResourceExtensions.GetLocalized(
        "Error_NotBetweenOneAndHundred"
    );

    [ObservableProperty]
    [Required(ErrorMessage = "NeedName")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "NotOneToHundred")]
    private string? preplannedMapName;

    [ObservableProperty]
    [Required(ErrorMessage = "NeedId")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "NotOneToHundred")]
    private string? preplannedMapId;

    [ObservableProperty]
    [Required(ErrorMessage = "NeedKey")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "NotOneToHundred")]
    private string? apiKey;

    [ObservableProperty]
    [Required(ErrorMessage = "NeedLicense")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "NotOneToHundred")]
    private string? licenseKey;

    [ObservableProperty]
    private string? preplannedMapNameError;

    [ObservableProperty]
    private string? preplannedMapNameErrorVisibility = "Collapsed";

    [ObservableProperty]
    private string? preplannedMapIdError;

    [ObservableProperty]
    private string? preplannedMapIdErrorVisibility = "Collapsed";

    [ObservableProperty]
    private string? apiKeyError;

    [ObservableProperty]
    private string? apiKeyErrorVisibility = "Collapsed";

    [ObservableProperty]
    private string? licenseKeyError;

    [ObservableProperty]
    private string? licenseKeyErrorVisibility = "Collapsed";

    [ObservableProperty]
    public string selectView = "Map";

    [RelayCommand]
    public void PreplannedMapNameIsChanging()
    {
        // Log to trace that the PreplannedMapNameIsChanging command was called.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "MapConfigurationViewModel, PreplannedMapNameIsChanging: PreplannedMapNameIsChanging invoked."
        );
        // Log the PreplannedMapName.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "MapConfigurationViewModel, PreplannedMapNameIsChanging: PreplannedMapName: {preplannedMapName}.",
            PreplannedMapName
        );

        try
        {
            var results = new List<ValidationResult>();
            preplannedMapNameValid = Validator.TryValidateProperty(
                PreplannedMapName,
                new ValidationContext(this, null, null) { MemberName = nameof(PreplannedMapName) },
                results
            );
            // Log isValid to trace.
            Logger.LogTrace(
                MapConfigurationViewModelLog,
                "MapConfigurationViewModel, PreplannedMapNameIsChanging: isValid: {isValid}.",
                preplannedMapNameValid
            );
            if (preplannedMapNameValid is false)
            {
                var firstValidationResult = results.FirstOrDefault();
                if (firstValidationResult != null)
                {
                    Logger.LogDebug(
                        MapConfigurationViewModelLog,
                        "MapConfigurationViewModel, PreplannedMapNameIsChanging: firstValidationResult: {firstValidationResult}.",
                        firstValidationResult
                    );
                }
                if (firstValidationResult is not null)
                {
                    if (firstValidationResult.ToString() == "NeedName")
                    {
                        Logger.LogTrace(
                            MapConfigurationViewModelLog,
                            "MapConfigurationViewModel, PreplannedMapNameIsChanging: NeedName error."
                        );
                        PreplannedMapNameError = unableToValideWithoutName;
                        PreplannedMapNameErrorVisibility = "Visible";
                    }
                    if (firstValidationResult.ToString() == "NotOneToHundred")
                    {
                        Logger.LogTrace(
                            MapConfigurationViewModelLog,
                            "MapConfigurationViewModel, PreplannedMapNameIsChanging: NotOneToHundred error."
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
        catch (Exception exception)
        {
            Logger.LogError(
                MapConfigurationViewModelLog,
                exception,
                "MapConfigurationViewModel, PreplannedMapNameIsChanging: exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [RelayCommand]
    public void PreplannedMapIdIsChanging()
    {
        // Log to trace that the PreplannedMapIdIsChanging command was called.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "MapConfigurationViewModel, PreplannedMapIdIsChanging: PreplannedMapIdIsChanging invoked."
        );
        // Log the PreplannedMapId.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "MapConfigurationViewModel, PreplannedMapIdIsChanging: PreplannedMapId: {preplannedMapId}.",
            PreplannedMapId
        );

        try
        {
            var results = new List<ValidationResult>();
            preplannedMapIdValid = Validator.TryValidateProperty(
                PreplannedMapId,
                new ValidationContext(this, null, null) { MemberName = nameof(PreplannedMapId) },
                results
            );
            // Log isValid to trace.
            Logger.LogTrace(
                MapConfigurationViewModelLog,
                "MapConfigurationViewModel, PreplannedMapIdIsChanging: isValid: {isValid}.",
                preplannedMapIdValid
            );
            if (preplannedMapIdValid is false)
            {
                var firstValidationResult = results.FirstOrDefault();
                if (firstValidationResult != null)
                {
                    Logger.LogTrace(
                        MapConfigurationViewModelLog,
                        "MapConfigurationViewModel, PreplannedMapIdIsChanging: firstValidationResult: {firstValidationResult}.",
                        firstValidationResult
                    );
                }
                if (firstValidationResult is not null)
                {
                    if (firstValidationResult.ToString() == "NeedId")
                    {
                        Logger.LogTrace(
                            MapConfigurationViewModelLog,
                            "MapConfigurationViewModel, PreplannedMapIdIsChanging: NeedId error."
                        );
                        PreplannedMapIdError = unableToValideWithoutId;
                        PreplannedMapIdErrorVisibility = "Visible";
                    }
                    if (firstValidationResult.ToString() == "NotOneToHundred")
                    {
                        Logger.LogTrace(
                            MapConfigurationViewModelLog,
                            "MapConfigurationViewModel, PreplannedMapIdIsChanging: NotOneToHundred error."
                        );
                        PreplannedMapIdError = stringNotOneToHundred;
                        PreplannedMapIdErrorVisibility = "Visible";
                    }
                }
            }
            else
            {
                PreplannedMapIdErrorVisibility = "Collapsed";
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(
                MapConfigurationViewModelLog,
                exception,
                "MapConfigurationViewModel, PreplannedMapIdIsChanging: exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [RelayCommand]
    public void ApiKeyIsChanging()
    {
        // Log to trace that the ApiKeyIsChanging command was called.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "MapConfigurationViewModel, ApiKeyIsChanging: ApiKeyIsChanging invoked."
        );
        // Log the ApiKey.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "MapConfigurationViewModel, ApiKeyIsChanging: ApiKey: {apiKey}.",
            ApiKey
        );
        try
        {
            var results = new List<ValidationResult>();
            apiKeyValid = Validator.TryValidateProperty(
                ApiKey,
                new ValidationContext(this, null, null) { MemberName = nameof(ApiKey) },
                results
            );
            // Log isValid to trace.
            Logger.LogTrace(
                MapConfigurationViewModelLog,
                "MapConfigurationViewModel, ApiKeyIsChanging: isValid: {isValid}.",
                apiKeyValid
            );
            if (apiKeyValid is false)
            {
                var firstValidationResult = results.FirstOrDefault();
                if (firstValidationResult != null)
                {
                    Logger.LogTrace(
                        MapConfigurationViewModelLog,
                        "MapConfigurationViewModel, ApiKeyIsChanging: firstValidationResult: {firstValidationResult}.",
                        firstValidationResult
                    );
                }
                if (firstValidationResult is not null)
                {
                    if (firstValidationResult.ToString() == "NeedKey")
                    {
                        Logger.LogTrace(
                            MapConfigurationViewModelLog,
                            "MapConfigurationViewModel, ApiKeyIsChanging: NeedKey error."
                        );
                        ApiKeyError = unableToValideWithoutKey;
                        ApiKeyErrorVisibility = "Visible";
                    }
                    if (firstValidationResult.ToString() == "NotOneToHundred")
                    {
                        Logger.LogTrace(
                            MapConfigurationViewModelLog,
                            "MapConfigurationViewModel, ApiKeyIsChanging: NotOneToHundred error."
                        );
                        ApiKeyError = stringNotOneToHundred;
                        ApiKeyErrorVisibility = "Visible";
                    }
                }
            }
            else
            {
                ApiKeyErrorVisibility = "Collapsed";
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(
                MapConfigurationViewModelLog,
                exception,
                "MapConfigurationViewModel, ApiKeyIsChanging: exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [RelayCommand]
    public void LicenseKeyIsChanging()
    {
        // Log to trace that the LicenseKeyIsChanging command was called.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "MapConfigurationViewModel, LicenseKeyIsChanging: LicenseKeyIsChanging invoked."
        );
        // Log the LicenseKey.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "MapConfigurationViewModel, LicenseKeyIsChanging: LicenseKey: {licenseKey}.",
            LicenseKey
        );
        try
        {
            var results = new List<ValidationResult>();
            licenseKeyValid = Validator.TryValidateProperty(
                LicenseKey,
                new ValidationContext(this, null, null) { MemberName = nameof(LicenseKey) },
                results
            );
            // Log isValid to trace.
            Logger.LogTrace(
                MapConfigurationViewModelLog,
                "MapConfigurationViewModel, LicenseKeyIsChanging: isValid: {isValid}.",
                licenseKeyValid
            );
            if (licenseKeyValid is false)
            {
                var firstValidationResult = results.FirstOrDefault();
                if (firstValidationResult != null)
                {
                    Logger.LogTrace(
                        MapConfigurationViewModelLog,
                        "MapConfigurationViewModel, LicenseKeyIsChanging: firstValidationResult: {firstValidationResult}.",
                        firstValidationResult
                    );
                }
                if (firstValidationResult is not null)
                {
                    if (firstValidationResult.ToString() == "NeedLicense")
                    {
                        Logger.LogTrace(
                            MapConfigurationViewModelLog,
                            "MapConfigurationViewModel, LicenseKeyIsChanging: NeedLicense error."
                        );
                        LicenseKeyError = unableToValideWithoutKey;
                        LicenseKeyErrorVisibility = "Visible";
                    }
                    if (firstValidationResult.ToString() == "NotOneToHundred")
                    {
                        Logger.LogTrace(
                            MapConfigurationViewModelLog,
                            "MapConfigurationViewModel, LicenseKeyIsChanging: NotOneToHundred error."
                        );
                        LicenseKeyError = stringNotOneToHundred;
                        LicenseKeyErrorVisibility = "Visible";
                    }
                }
            }
            else
            {
                LicenseKeyErrorVisibility = "Collapsed";
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(
                MapConfigurationViewModelLog,
                exception,
                "MapConfigurationViewModel, LicenseKeyIsChanging: exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [RelayCommand]
    public void StoreApiKeyAsync()
    {
        try
        {
            Logger.LogTrace(
                MapConfigurationViewModelLog,
                "MapConfigurationViewModel, StoreApiKeyAsync: invoked with apiKey set to: {apiKey}",
                ApiKey
            );

            Guard.Against.Null(
                LocalSettingsService,
                nameof(LocalSettingsService),
                "MapConfigurationViewModel, StoreApiKeyAsync: LocalSettingsService is null."
            );
            if (ApiKey is not null && apiKeyValid)
            {
                Task.Run(async () =>
                    {
                        await LocalSettingsService.SaveSettingAsync(Item[Key.ArcgisApiKey], ApiKey);
                    })
                    .Wait();
            }
            else
            {
                Logger.LogTrace(
                    MapConfigurationViewModelLog,
                    "MapConfigurationViewModel, StoreApiKeyAsync: ApiKey did not pass validation or is not yet validated via ApiKeyIsChanging(), not saving the value."
                );
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(
                MapConfigurationViewModelLog,
                exception,
                "MapConfigurationViewModel, StoreApiKeyAsync: exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [RelayCommand]
    public void StorePreplannedMapNameAsync()
    {
        try
        {
            Logger.LogTrace(
                MapConfigurationViewModelLog,
                "MapConfigurationViewModel, StorePreplannedMapNameAsync: invoked with preplannedMap set to: {preplannedMap}",
                PreplannedMapName
            );

            Guard.Against.Null(
                LocalSettingsService,
                nameof(LocalSettingsService),
                "MapConfigurationViewModel, StorePreplannedMapNameAsync: LocalSettingsService is null."
            );
            if (PreplannedMapName is not null && preplannedMapNameValid)
            {
                Task.Run(async () =>
                    {
                        await LocalSettingsService.SaveSettingAsync(
                            Item[Key.PreplannedMapName],
                            PreplannedMapName
                        );
                    })
                    .Wait();
            }
            else
            {
                Logger.LogTrace(
                    MapConfigurationViewModelLog,
                    "MapConfigurationViewModel, StorePreplannedMapNameAsync: PreplannedMapName did not pass validation or is not yet validated via PreplannedMapNameIsChanging(), not saving the value."
                );
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(
                MapConfigurationViewModelLog,
                exception,
                "MapConfigurationViewModel, StorePreplannedMapNameAsync: exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [RelayCommand]
    public void StorePreplannedMapIdAsync()
    {
        try
        {
            Logger.LogTrace(
                MapConfigurationViewModelLog,
                "MapConfigurationViewModel, StorePreplannedMapIdAsync: invoked with preplannedMap set to: {preplannedMap}",
                PreplannedMapId
            );

            Guard.Against.Null(
                LocalSettingsService,
                nameof(LocalSettingsService),
                "MapConfigurationViewModel, StorePreplannedMapIdAsync: LocalSettingsService is null."
            );
            if (PreplannedMapId is not null && preplannedMapIdValid)
            {
                Task.Run(async () =>
                    {
                        await LocalSettingsService.SaveSettingAsync(
                            Item[Key.OfflineMapIdentifier],
                            PreplannedMapId
                        );
                    })
                    .Wait();
            }
            else
            {
                Logger.LogTrace(
                    MapConfigurationViewModelLog,
                    "MapConfigurationViewModel, StorePreplannedMapIdAsync: PreplannedMapId did not pass validation or is not yet validated via PreplannedMapIdIsChanging(), not saving the value."
                );
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(
                MapConfigurationViewModelLog,
                exception,
                "MapConfigurationViewModel, StorePreplannedMapIdAsync: exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [RelayCommand]
    public void StoreLicenseKeyAsync()
    {
        try
        {
            Logger.LogTrace(
                MapConfigurationViewModelLog,
                "MapConfigurationViewModel, StoreLicenseKeyAsync: invoked with licenseKey set to: {licenseKey}",
                LicenseKey
            );

            Guard.Against.Null(
                LocalSettingsService,
                nameof(LocalSettingsService),
                "MapConfigurationViewModel, StoreLicenseKeyAsync: LocalSettingsService is null."
            );
            if (LicenseKey is not null && licenseKeyValid)
            {
                Task.Run(async () =>
                    {
                        await LocalSettingsService.SaveSettingAsync(
                            Item[Key.ArcgisLicenseKey],
                            LicenseKey
                        );
                    })
                    .Wait();
            }
            else
            {
                Logger.LogTrace(
                    MapConfigurationViewModelLog,
                    "MapConfigurationViewModel, StoreLicenseKeyAsync: LicenseKey did not pass validation or is not yet validated via LicenseKeyIsChanging(), not saving the value."
                );
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(
                MapConfigurationViewModelLog,
                exception,
                "MapConfigurationViewModel, StoreLicenseKeyAsync: exception: {exception}.",
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
            "MapConfigurationViewModel, SelectMapPage: SelectMapPage command called."
        );
        SelectView = "Map";
    }

    [RelayCommand]
    private void ShowErrors()
    {
        var message = string.Join(Environment.NewLine, GetErrors().Select(e => e.ErrorMessage));

        // Log to trace that the ShowErrors command was called.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "MapConfigurationViewModel, ShowErrors: ShowErrors command called."
        );
        // Log the message.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "MapConfigurationViewModel, ShowErrors: Message: {message}.",
            message
        );
    }

    public MapConfigurationViewModel(
        ILocalSettingsService? localSettingsService,
        ILogger<MapConfigurationViewModel> logger
    )
    {
        Logger = logger;
        LocalSettingsService = localSettingsService;

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
                "MapConfigurationViewModel, Initialize(): LocalSettingsService is null."
            );
            // Get the preplanned map name from local settings.
            PreplannedMapName = await LocalSettingsService.ReadSettingAsync<string>(
                PrePlannedMapConfiguration.Item[PrePlannedMapConfiguration.Key.PreplannedMapName]
            );
            // Log the preplanned map name.
            Logger.LogDebug(
                MapConfigurationViewModelLog,
                "MapConfigurationViewModel, Initialize: PreplannedMapName: {PreplannedMapName}.",
                PreplannedMapName
            );
            // Get the preplanned map id from local settings.
            PreplannedMapId = await LocalSettingsService.ReadSettingAsync<string>(
                PrePlannedMapConfiguration.Item[PrePlannedMapConfiguration.Key.OfflineMapIdentifier]
            );
            // Log the preplanned map id.
            Logger.LogDebug(
                MapConfigurationViewModelLog,
                "MapConfigurationViewModel, Initialize: PreplannedMapId: {PreplannedMapId}.",
                PreplannedMapId
            );
            // Get the api key from local settings.
            ApiKey = await LocalSettingsService.ReadSettingAsync<string>(
                PrePlannedMapConfiguration.Item[PrePlannedMapConfiguration.Key.ArcgisApiKey]
            );
            // Log the api key.
            Logger.LogDebug(
                MapConfigurationViewModelLog,
                "MapConfigurationViewModel, Initialize: ApiKey: {ApiKey}.",
                ApiKey
            );
            // Get the license key from local settings.
            LicenseKey = await LocalSettingsService.ReadSettingAsync<string>(
                PrePlannedMapConfiguration.Item[PrePlannedMapConfiguration.Key.ArcgisLicenseKey]
            );
            // Log the license key.
            Logger.LogDebug(
                MapConfigurationViewModelLog,
                "MapConfigurationViewModel, Initialize: LicenseKey: {LicenseKey}.",
                LicenseKey
            );
            // Define a message handler for the SetLicenseTypeMessage.
            WeakReferenceMessenger.Default.Register<
                MapConfigurationViewModel,
                SetLicenseTypeMessage
            >(
                this,
                async (recipient, message) =>
                {
                    // Log the license type.
                    Logger.LogDebug(
                        MapConfigurationViewModelLog,
                        "MapConfigurationViewModel, Initialize: License type: {licenseType}.",
                        message.Value
                    );
                    // set the license type in the local settings.
                    await LocalSettingsService.SaveSettingAsync(
                        Item[Key.CurrentArcGisKey],
                        message.Value
                    );
                }
            );
        }
        catch (Exception exception)
        {
            Logger.LogError(
                MapConfigurationViewModelLog,
                exception,
                "MapConfigurationViewModel, Initialize: exception: {exception}.",
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
            "MapConfigurationViewModel, MapNavView_ItemInvoked: MapNavView_ItemInvoked event fired."
        );

        try
        {
            // Log the name of the invoked item.
            Logger.LogDebug(
                MapConfigurationViewModelLog,
                "MapConfigurationViewModel, MapNavView_ItemInvoked: Invoked item name: {invokedItemName}.",
                args.InvokedItemContainer.Name
            );

            // Log the sender name.
            Logger.LogDebug(
                MapConfigurationViewModelLog,
                "MapConfigurationViewModel, MapNavView_ItemInvoked: Sender name: {senderName}.",
                sender.Name
            );

            Guard.Against.Null(
                args.InvokedItemContainer,
                nameof(args.InvokedItemContainer),
                "MapConfigurationViewModel, MapNavView_ItemInvoked: InvokedItemContainer is null."
            );

            switch (args.InvokedItemContainer.Name)
            {
                case "MapNavCenter":
                    // Log that the MapNavCenter item was invoked.
                    Logger.LogDebug(
                        MapConfigurationViewModelLog,
                        "MapConfigurationViewModel, MapNavView_ItemInvoked: MapNavCenter item invoked."
                    );
                    // Send a center message.
                    WeakReferenceMessenger.Default.Send(new SetMapCenterMessage(true));
                    break;
                case "MapNavAutoPan":
                    // Log that the MapNavAutoPan item was invoked.
                    Logger.LogDebug(
                        MapConfigurationViewModelLog,
                        "MapConfigurationViewModel, MapNavView_ItemInvoked: MapNavAutoPan item invoked."
                    );
                    // Send an auto pan message.
                    WeakReferenceMessenger.Default.Send(new SetMapAutoPanMessage(true));
                    break;
                case "SettingsItem":
                    // Log that the SettingsItem item was invoked.
                    Logger.LogDebug(
                        MapConfigurationViewModelLog,
                        "MapConfigurationViewModel, MapNavView_ItemInvoked: SettingsItem item invoked."
                    );
                    SelectView = "Settings";
                    break;
                default:
                    break;
            }
        }
        catch (Exception exception)
        {
            // Log the exception message.
            Logger.LogError(
                exception,
                "MapConfigurationViewModel, MapNavView_ItemInvoked: An error occurred in MapNavView_ItemInvoked: {exception}",
                exception.Message
            );
        }
    }

    public void ArcGISApiKeyHelpClick()
    {
        // Log that the ArcGISApiKeyHelpClick event was fired.
        Logger.LogDebug(
            MapConfigurationViewModelLog,
            "MapConfigurationViewModel, ArcGISApiKeyHelpClick: ArcGISApiKeyHelpClick event fired."
        );
    }

    public string Errors =>
        string.Join(
            Environment.NewLine,
            from ValidationResult error in GetErrors(null)
            select error.ErrorMessage
        );

    /*
    private void MapConfigurationPropertyChanged(object? sender, PropertyChangedEventArgs error)
    {
        // Log to trace that the MapConfigurationPropertyChanged event was fired.
        Logger.LogTrace(
            MapConfigurationViewModelLog,
            "MapConfigurationPropertyChanged(): MapConfigurationPropertyChanged event fired."
        );

        if (error.PropertyName != nameof(HasErrors))
        {
            // Update Errors on every Error change for binding.
            // OnPropertyChanged(nameof(HasErrors));
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
        // Update Errors on every Error change for binding.
        // OnPropertyChanged(nameof(Errors));
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
    */
}
