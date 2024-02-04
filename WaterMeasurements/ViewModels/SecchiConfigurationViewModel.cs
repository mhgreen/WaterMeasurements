using System;
using System.Resources;
using System.Collections.Generic;
using System.Globalization;
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
using static WaterMeasurements.Models.SecchiConfiguration;

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
using NLog;

namespace WaterMeasurements.ViewModels;

public class UrlAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        var stringValue = value as string;
        if (
            Uri.TryCreate(stringValue, UriKind.Absolute, out var uriResult)
            && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps)
        )
        {
            return uriResult.Host.Contains("arcgis.com", StringComparison.OrdinalIgnoreCase)
                && uriResult.AbsolutePath.EndsWith(
                    "FeatureServer",
                    StringComparison.OrdinalIgnoreCase
                );
        }
        return false;
    }

    public override string FormatErrorMessage(string name)
    {
        return string.Format(CultureInfo.CurrentCulture, ErrorMessageString, name);
    }
}

public partial class SecchiConfigurationViewModel : ObservableValidator
{
    private readonly ILogger<SecchiConfigurationViewModel> Logger;

    // Set the event id for logging messages.
    internal EventId SecchiConfigurationViewModelLog = new(14, "SecchiConfigurationViewModel");

    private readonly ILocalSettingsService? LocalSettingsService;

    private bool secchiObservationsValid = false;

    private bool secchiLocationsValid = false;

    private bool secchiTriggerDistanceValid = false;

    // It does not seem possible to localize the error messages in the DataAnnotations.
    // Using the ResourceExtensions.GetLocalized() method to get the localized strings.

    private readonly string unableWithoutURL = ResourceExtensions.GetLocalized(
        "Error_UnableWithoutURL"
    );
    private readonly string stringNotOneToHundredFifty = ResourceExtensions.GetLocalized(
        "Error_NotBetweenOneAndOneHundredFifty"
    );
    private readonly string urlInvalid = ResourceExtensions.GetLocalized("Error_InvalidURL");
    private readonly string unableWithoutDistance = ResourceExtensions.GetLocalized(
        "Error_UnableWithoutDistance"
    );
    private readonly string stringNotOneToTwenty = ResourceExtensions.GetLocalized(
        "Error_DistanceNotBetweenOneAndTwenty"
    );

    [ObservableProperty]
    [Required(ErrorMessage = "NeedURL")]
    [Url(ErrorMessage = "InvalidURL")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "ObsNotOneToHundredFifty")]
    private string? secchiObservations;

    [ObservableProperty]
    [Required(ErrorMessage = "NeedURL")]
    [Url(ErrorMessage = "InvalidURL")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "LocNotOneToHundredFifty")]
    private string? secchiLocations;

    [ObservableProperty]
    [Required(ErrorMessage = "NeedDistance")]
    [Range(1, 20, ErrorMessage = "NotBetweenOneAndTwenty")]
    private double? secchiGeoTriggerDistance;

    [ObservableProperty]
    private string? secchiObservationsError;

    [ObservableProperty]
    private string? secchiObservationsErrorVisibility = "Collapsed";

    [ObservableProperty]
    private string? secchiLocationsError;

    [ObservableProperty]
    private string? secchiLocationsErrorVisibility = "Collapsed";

    [ObservableProperty]
    private string? secchiGeotriggerDistanceError;

    [ObservableProperty]
    private string? secchiGeotriggerDistanceErrorVisibility = "Collapsed";

    [RelayCommand]
    public void SecchiObservationsIsChanging()
    {
        // Log to trace that the SecchiObservationsIsChanging command was called.
        Logger.LogTrace(
            SecchiConfigurationViewModelLog,
            "SecchiObservationsIsChanging(): SecchiObservationsIsChanging invoked."
        );
        // Log the SecchiObservations.
        Logger.LogTrace(
            SecchiConfigurationViewModelLog,
            "SecchiObservationsIsChanging(): SecchiObservations: {secchiObservations}.",
            SecchiObservations
        );

        var results = new List<ValidationResult>();
        secchiObservationsValid = Validator.TryValidateProperty(
            SecchiObservations,
            new ValidationContext(this, null, null) { MemberName = nameof(SecchiObservations) },
            results
        );
        // Log isValid to debug.
        Logger.LogTrace(
            SecchiConfigurationViewModelLog,
            "SecchiObservationsIsChanging(): isValid: {isValid}.",
            secchiObservationsValid
        );
        if (secchiObservationsValid is false)
        {
            var firstValidationResult = results.FirstOrDefault();
            if (firstValidationResult != null)
            {
                Logger.LogDebug(
                    SecchiConfigurationViewModelLog,
                    "SecchiObservationsIsChanging(): firstValidationResult: {firstValidationResult}.",
                    firstValidationResult
                );
            }
            if (firstValidationResult is not null)
            {
                if (firstValidationResult.ToString() == "NeedURL")
                {
                    Logger.LogTrace(
                        SecchiConfigurationViewModelLog,
                        "SecchiObservationsIsChanging(): NeedURL error."
                    );
                    SecchiObservationsError = unableWithoutURL;
                    SecchiObservationsErrorVisibility = "Visible";
                }
                if (firstValidationResult.ToString() == "ObsNotOneToHundredFifty")
                {
                    Logger.LogTrace(
                        SecchiConfigurationViewModelLog,
                        "SecchiObservationsIsChanging(): ObsNotOneToHundredFifty error."
                    );
                    SecchiObservationsError = stringNotOneToHundredFifty;
                    SecchiObservationsErrorVisibility = "Visible";
                }
                if (firstValidationResult.ToString() == "InvalidURL")
                {
                    Logger.LogTrace(
                        SecchiConfigurationViewModelLog,
                        "SecchiObservationsIsChanging(): InvalidURL error."
                    );
                    SecchiObservationsError = urlInvalid;
                    SecchiObservationsErrorVisibility = "Visible";
                }
            }
        }
        else
        {
            SecchiObservationsErrorVisibility = "Collapsed";
        }
    }

    [RelayCommand]
    public void StoreSecchiObservationsAsync()
    {
        try
        {
            Logger.LogTrace(
                SecchiConfigurationViewModelLog,
                "StoreSecchiObservationsAsync(): invoked with SecchiObservations set to: {SecchiObservations}",
                SecchiObservations
            );

            Guard.Against.Null(
                LocalSettingsService,
                nameof(LocalSettingsService),
                "StoreSecchiObservationsAsync(): LocalSettingsService is null."
            );
            if (SecchiObservations is not null && secchiObservationsValid)
            {
                Task.Run(async () =>
                    {
                        await LocalSettingsService.SaveSettingAsync(
                            SecchiConfiguration.Item[Key.SecchiObservationsGeodatabase],
                            SecchiObservations
                        );
                    })
                    .Wait();
            }
            else
            {
                Logger.LogTrace(
                    SecchiConfigurationViewModelLog,
                    "StoreSecchiObservationsAsync(): SecchiObservations did not pass validation or is not yet validated via SecchiObservationsIsChanging(), not saving the value."
                );
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(
                SecchiConfigurationViewModelLog,
                exception,
                "StoreSecchiObservationsAsync(): exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [RelayCommand]
    public void SecchiLocationsIsChanging()
    {
        // Log to trace that the SecchiLocationsIsChanging command was called.
        Logger.LogTrace(
            SecchiConfigurationViewModelLog,
            "SecchiLocationsIsChanging(): SecchiLocationsIsChanging invoked."
        );
        // Log the SecchiLocations.
        Logger.LogTrace(
            SecchiConfigurationViewModelLog,
            "SecchiLocationsIsChanging(): SecchiLocations: {secchiLocations}.",
            SecchiLocations
        );

        var results = new List<ValidationResult>();
        secchiLocationsValid = Validator.TryValidateProperty(
            SecchiLocations,
            new ValidationContext(this, null, null) { MemberName = nameof(SecchiLocations) },
            results
        );
        // Log isValid to debug.
        Logger.LogTrace(
            SecchiConfigurationViewModelLog,
            "SecchiLocationsIsChanging(): isValid: {isValid}.",
            secchiLocationsValid
        );
        if (secchiLocationsValid is false)
        {
            var firstValidationResult = results.FirstOrDefault();
            if (firstValidationResult != null)
            {
                Logger.LogDebug(
                    SecchiConfigurationViewModelLog,
                    "SecchiLocationsIsChanging(): firstValidationResult: {firstValidationResult}.",
                    firstValidationResult
                );
            }
            if (firstValidationResult is not null)
            {
                if (firstValidationResult.ToString() == "NeedURL")
                {
                    Logger.LogTrace(
                        SecchiConfigurationViewModelLog,
                        "SecchiLocationsIsChanging(): NeedURL error."
                    );
                    SecchiLocationsError = unableWithoutURL;
                    SecchiLocationsErrorVisibility = "Visible";
                }
                if (firstValidationResult.ToString() == "LocNotOneToHundredFifty")
                {
                    Logger.LogTrace(
                        SecchiConfigurationViewModelLog,
                        "SecchiLocationsIsChanging(): LocNotOneToHundredFifty error."
                    );
                    SecchiLocationsError = stringNotOneToHundredFifty;
                    SecchiLocationsErrorVisibility = "Visible";
                }
                if (firstValidationResult.ToString() == "InvalidURL")
                {
                    Logger.LogTrace(
                        SecchiConfigurationViewModelLog,
                        "SecchiLocationsIsChanging(): InvalidURL error."
                    );
                    SecchiLocationsError = urlInvalid;
                    SecchiLocationsErrorVisibility = "Visible";
                }
            }
        }
        else
        {
            SecchiLocationsErrorVisibility = "Collapsed";
        }
    }

    [RelayCommand]
    public void StoreSecchiLocationsAsync()
    {
        try
        {
            Logger.LogTrace(
                SecchiConfigurationViewModelLog,
                "StoreSecchiLocationsAsync(): invoked with SecchiLocations set to: {SecchiLocations}",
                SecchiLocations
            );

            Guard.Against.Null(
                LocalSettingsService,
                nameof(LocalSettingsService),
                "StoreSecchiLocationsAsync(): LocalSettingsService is null."
            );
            if (SecchiLocations is not null && secchiLocationsValid)
            {
                Task.Run(async () =>
                    {
                        await LocalSettingsService.SaveSettingAsync(
                            SecchiConfiguration.Item[Key.SecchiLocationsGeodatabase],
                            SecchiLocations
                        );
                    })
                    .Wait();
            }
            else
            {
                Logger.LogTrace(
                    SecchiConfigurationViewModelLog,
                    "StoreSecchiLocationsAsync(): SecchiLocations did not pass validation or is not yet validated via SecchiLocationsIsChanging(), not saving the value."
                );
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(
                SecchiConfigurationViewModelLog,
                exception,
                "StoreSecchiLocationsAsync(): exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [RelayCommand]
    public void SecchiGeoTriggerDistanceIsChanging()
    {
        // Log to trace that the SecchiGeoTriggerDistanceIsChanging command was called.
        Logger.LogTrace(
            SecchiConfigurationViewModelLog,
            "SecchiGeoTriggerDistanceIsChanging(): SecchiGeoTriggerDistanceIsChanging invoked."
        );
        // Log the SecchiGeoTriggerDistance.
        Logger.LogTrace(
            SecchiConfigurationViewModelLog,
            "SecchiGeoTriggerDistanceIsChanging(): SecchiGeoTriggerDistance: {secchiGeoTriggerDistance}.",
            SecchiGeoTriggerDistance
        );

        var results = new List<ValidationResult>();
        secchiTriggerDistanceValid = Validator.TryValidateProperty(
            SecchiGeoTriggerDistance,
            new ValidationContext(this, null, null)
            {
                MemberName = nameof(SecchiGeoTriggerDistance)
            },
            results
        );
        // Log isValid to debug.
        Logger.LogTrace(
            SecchiConfigurationViewModelLog,
            "SecchiGeoTriggerDistanceIsChanging(): isValid: {isValid}.",
            secchiTriggerDistanceValid
        );
        if (secchiTriggerDistanceValid is false)
        {
            var firstValidationResult = results.FirstOrDefault();
            if (firstValidationResult != null)
            {
                Logger.LogDebug(
                    SecchiConfigurationViewModelLog,
                    "SecchiGeoTriggerDistanceIsChanging(): firstValidationResult: {firstValidationResult}.",
                    firstValidationResult
                );
            }
            if (firstValidationResult is not null)
            {
                if (firstValidationResult.ToString() == "NeedDistance")
                {
                    Logger.LogTrace(
                        SecchiConfigurationViewModelLog,
                        "SecchiGeoTriggerDistanceIsChanging(): NeedDistance error."
                    );
                    SecchiGeotriggerDistanceError = unableWithoutDistance;
                    SecchiGeotriggerDistanceErrorVisibility = "Visible";
                }
                if (firstValidationResult.ToString() == "NotBetweenOneAndTwenty")
                {
                    Logger.LogTrace(
                        SecchiConfigurationViewModelLog,
                        "SecchiGeoTriggerDistanceIsChanging(): NotBetweenOneAndTwenty error."
                    );
                    SecchiGeotriggerDistanceError = stringNotOneToTwenty;
                    SecchiGeotriggerDistanceErrorVisibility = "Visible";
                }
            }
        }
        else
        {
            SecchiGeotriggerDistanceErrorVisibility = "Collapsed";
        }
    }

    [RelayCommand]
    public void StoreSecchiGeoTriggerDistanceAsync()
    {
        try
        {
            Logger.LogTrace(
                SecchiConfigurationViewModelLog,
                "StoreSecchiGeoTriggerDistanceAsync(): invoked with SecchiGeoTriggerDistance set to: {SecchiGeoTriggerDistance}",
                SecchiGeoTriggerDistance
            );

            Guard.Against.Null(
                LocalSettingsService,
                nameof(LocalSettingsService),
                "StoreSecchiGeoTriggerDistanceAsync(): LocalSettingsService is null."
            );
            if (SecchiGeoTriggerDistance is not null && secchiTriggerDistanceValid)
            {
                Task.Run(async () =>
                    {
                        await LocalSettingsService.SaveSettingAsync(
                            SecchiConfiguration.Item[Key.GeoTriggerDistanceMeters],
                            SecchiGeoTriggerDistance
                        );
                    })
                    .Wait();
            }
            else
            {
                Logger.LogTrace(
                    SecchiConfigurationViewModelLog,
                    "StoreSecchiGeoTriggerDistanceAsync(): SecchiGeoTriggerDistance is null or zero, not saving the value."
                );
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(
                SecchiConfigurationViewModelLog,
                exception,
                "StoreSecchiGeoTriggerDistanceAsync(): exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [RelayCommand]
    private void ShowErrors()
    {
        var message = string.Join(Environment.NewLine, GetErrors().Select(e => e.ErrorMessage));

        // Log to trace that the ShowErrors command was called.
        Logger.LogTrace(
            SecchiConfigurationViewModelLog,
            "ShowErrors(): ShowErrors command called."
        );
        // Log the message.
        Logger.LogTrace(
            SecchiConfigurationViewModelLog,
            "ShowErrors(): Message: {message}.",
            message
        );
    }

    public SecchiConfigurationViewModel(
        ILocalSettingsService? localSettingsService,
        ILogger<SecchiConfigurationViewModel> logger
    )
    {
        Logger = logger;
        LocalSettingsService = localSettingsService;

        // ErrorsChanged += MapConfigurationErrorsChanged;
        // PropertyChanged += MapConfigurationPropertyChanged;

        _ = Initialize();
        // Log that the MapConfigurationViewModel is starting.
        Logger.LogInformation(
            SecchiConfigurationViewModelLog,
            "Starting SecchiConfigurationViewModel"
        );
    }

    private async Task Initialize()
    {
        try
        {
            Guard.Against.Null(
                LocalSettingsService,
                nameof(LocalSettingsService),
                "Initialize(): LocalSettingsService is null."
            );

            // Get the URL for Secchi observations from local settings.
            SecchiObservations = await LocalSettingsService.ReadSettingAsync<string>(
                SecchiConfiguration.Item[Key.SecchiObservationsGeodatabase]
            );
            
            // Get the URL for Secchi locations from local settings.
            SecchiLocations = await LocalSettingsService.ReadSettingAsync<string>(
                SecchiConfiguration.Item[Key.SecchiLocationsGeodatabase]
            );

            // Get the GeoTriggerDistance from local settings.
            SecchiGeoTriggerDistance = await LocalSettingsService.ReadSettingAsync<double>(
                SecchiConfiguration.Item[Key.GeoTriggerDistanceMeters]
            );

            // Log to trace the value of unableWithoutURL.
            Logger.LogTrace(
                SecchiConfigurationViewModelLog,
                "Initialize(): unableWithoutURL: {unableWithoutURL}.",
                unableWithoutURL
            );
        }
        catch (Exception exception)
        {
            Logger.LogError(
                SecchiConfigurationViewModelLog,
                exception,
                "Initialize(): exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    public void SecchiKeyHelpClick(object sender, RoutedEventArgs eventArgs)
    {
        // Log that the ArcGISApiKeyHelpClick event was fired.
        Logger.LogDebug(
            SecchiConfigurationViewModelLog,
            "SecchiKeyHelpClick(): SecchiKeyHelpClick event sender {sender}, RoutedEventArgs {eventArgs}.",
            sender.ToString(),
            eventArgs.ToString()
        );
    }

    public string Errors =>
        string.Join(
            Environment.NewLine,
            from ValidationResult error in GetErrors(null)
            select error.ErrorMessage
        );
}
