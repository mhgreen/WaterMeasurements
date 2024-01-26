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
using NLog;

namespace WaterMeasurements.ViewModels;

public partial class SecchiConfigurationViewModel : ObservableValidator
{
    private readonly ILogger<SecchiConfigurationViewModel> Logger;

    // Set the event id for logging messages.
    internal EventId SecchiConfigurationViewModelLog = new(14, "SecchiConfigurationViewModel");

    private readonly ILocalSettingsService? LocalSettingsService;

    private bool secchiObservationsValid = false;

    // It does not seem possible to localize the error messages in the DataAnnotations.
    // Using the ResourceExtensions.GetLocalized() method to get the localized strings.

    private static readonly string unableToValide = ResourceExtensions.GetLocalized(
        "Error_UnableWithoutName"
    );
    private static readonly string stringNotOneToHundred = ResourceExtensions.GetLocalized(
        "Error_NotBetweenOneAndHundred"
    );
    private static readonly string stringNotOneToTwoHundred = ResourceExtensions.GetLocalized(
        "Error_NotBetweenOneAndTwoHundred"
    );
    private static readonly string distanceNotBetweenOneAndTwenty = ResourceExtensions.GetLocalized(
        "DistanceNotBetweenOneAndTwenty"
    );

    [ObservableProperty]
    [Required(ErrorMessage = "NeedURL")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "NotOneToHundred")]
    private string? secchiObservations;

    [ObservableProperty]
    [Required(ErrorMessage = "NeedURL")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "NotOneToHundred")]
    private string? secchiLocations;

    [ObservableProperty]
    [Required(ErrorMessage = "NeedDistance")]
    [Range(1, 20, ErrorMessage = "NotBetweenOneAndTwenty")]
    private double? geoTriggerDistance;

    [ObservableProperty]
    private string? secchiObservationsError;

    [ObservableProperty]
    private string? secchiObservationsErrorVisibility = "Collapsed";

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
                if (firstValidationResult.ToString() == "NeedName")
                {
                    Logger.LogTrace(
                        SecchiConfigurationViewModelLog,
                        "SecchiObservationsIsChanging(): NeedName error."
                    );
                    SecchiObservationsError = unableToValide;
                    SecchiObservationsErrorVisibility = "Visible";
                }
                if (firstValidationResult.ToString() == "NotOneToHundred")
                {
                    Logger.LogTrace(
                        SecchiConfigurationViewModelLog,
                        "SecchiObservationsIsChanging(): NotOneToHundred error."
                    );
                    SecchiObservationsError = stringNotOneToHundred;
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
                "Configuration Service, StoreSecchiObservationsAsync(): LocalSettingsService is null."
            );
            if (SecchiObservations is not null && secchiObservationsValid)
            {
                Task.Run(async () =>
                    {
                        await LocalSettingsService.SaveSettingAsync(
                            SecchiConfiguration.Item[
                                SecchiConfiguration.Key.SecchiObservationsGeodatabase
                            ],
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
                "Configuration Service, StoreSecchiObservationsAsync(): exception: {exception}.",
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
                "Configuration Service, Initialize(): LocalSettingsService is null."
            );

            // Get the URL for Secchi observations from local settings.
            SecchiObservations = await LocalSettingsService.ReadSettingAsync<string>(
                SecchiConfiguration.Item[SecchiConfiguration.Key.SecchiObservationsGeodatabase]
            );

            // Get the URL for Secchi locations from local settings.
            SecchiLocations = await LocalSettingsService.ReadSettingAsync<string>(
                SecchiConfiguration.Item[SecchiConfiguration.Key.SecchiLocationsGeodatabase]
            );

            // Get the GeoTriggerDistance from local settings.
            GeoTriggerDistance = await LocalSettingsService.ReadSettingAsync<double>(
                SecchiConfiguration.Item[SecchiConfiguration.Key.GeoTriggerDistanceMeters]
            );
        }
        catch (Exception exception)
        {
            Logger.LogError(
                SecchiConfigurationViewModelLog,
                exception,
                "Configuration Service, Initialize(): exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    public void SecchiKeyHelpClick(object sender, RoutedEventArgs e)
    {
        // Log that the ArcGISApiKeyHelpClick event was fired.
        Logger.LogDebug(
            SecchiConfigurationViewModelLog,
            "SecchiKeyHelpClick(): SecchiKeyHelpClick event fired."
        );
    }

    public string Errors =>
        string.Join(
            Environment.NewLine,
            from ValidationResult error in GetErrors(null)
            select error.ErrorMessage
        );
}
