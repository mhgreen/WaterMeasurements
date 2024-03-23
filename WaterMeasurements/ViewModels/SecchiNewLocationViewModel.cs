using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Resources;
using System.Text.RegularExpressions;
using Ardalis.GuardClauses;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
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
using NLog;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Helpers;
using WaterMeasurements.Models;
using WaterMeasurements.Services;
using WaterMeasurements.Views;
using WinRT;
using Geometry = Esri.ArcGISRuntime.Geometry.Geometry;

namespace WaterMeasurements.ViewModels;

public sealed partial class LocationNameValidAttribute : ValidationAttribute
{
    [GeneratedRegex("^[A-Z0-9.,_+-{}\\[\\] ()|:@^?']*$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex MyRegex();

    protected override ValidationResult IsValid(object? value, ValidationContext validationContext)
    {
        if (value is string locationName)
        {
            if (locationName.Length > 0)
            {
                if (MyRegex().IsMatch(locationName))
                {
                    return ValidationResult.Success!;
                }
            }
        }
        return new("InvalidLocationName");
    }
}

public sealed partial class CoordinateValidAttribute : ValidationAttribute
{
    [GeneratedRegex(@"^[\+\-]?\d*\.?\d*$")]
    private static partial Regex MyRegex();

    protected override ValidationResult IsValid(object? value, ValidationContext validationContext)
    {
        if (value is string coordinate)
        {
            if (coordinate.Length > 0)
            {
                if (MyRegex().IsMatch(coordinate))
                {
                    // convert to double.
                    // double latitudeDouble = Convert.ToDouble(latitude);
                    return ValidationResult.Success!;
                }
            }
        }
        return new("InvalidCoordinate");
    }
}

public partial class SecchiNewLocationViewModel : ObservableValidator
{
    private readonly ILogger<SecchiNewLocationViewModel> logger;

    internal EventId SecchiNewLocationViewModelLog = new(18, "SecchiNewLocationViewModel");

    private readonly ILocalSettingsService? LocalSettingsService;

    // It does not seem possible to localize the error messages in the DataAnnotations.
    // Using the ResourceExtensions.GetLocalized() method to get the localized strings.

    private readonly string invalidLocationName;
    private readonly string coordinateInvalid;
    private readonly string locationNeeded;
    private readonly string notOneToHundred;
    private readonly string latitudeNeeded;
    private readonly string latitudeNotInEnvelope;
    private readonly string longitudeNeeded;
    private readonly string longitudeNotInEnvelope;
    private readonly string typeAndSourceNeeded;

    // Extent of current map
    private Geometry? extent;

    public Envelope? envelope;

    private bool locationNameValid;
    private bool latitudeEntryValid;
    private bool longitudeEntryValid;

    public SecchiNewLocationViewModel(
        ILogger<SecchiNewLocationViewModel> logger,
        ILocalSettingsService localSettingsService
    )
    {
        this.logger = logger;
        LocalSettingsService = localSettingsService;

        locationName = string.Empty;

        // Handle the MapExtentChangedMessage.
        WeakReferenceMessenger.Default.Register<MapExtentChangedMessage>(
            this,
            (recipient, message) =>
            {
                extent = message.Value.Extent.Project(SpatialReferences.Wgs84);
                // Log to trace the value of message.Value with a label.
                logger.LogTrace(
                    SecchiNewLocationViewModelLog,
                    "SecchiNewLocationViewModel, Initialize: MapExtentChangedMessage, {envelope}",
                    extent.ToString()
                );
                envelope = extent.As<Envelope>();
                // Log to trace the minX, minY, maxX, and maxY values of the envelope.
                logger.LogTrace(
                    SecchiNewLocationViewModelLog,
                    "SecchiNewLocationViewModel, Initialize: MapExtentChangedMessage, minX: {minX}, minY: {minY}, maxX: {maxX}, maxY: {maxY}",
                    envelope.XMin,
                    envelope.YMin,
                    envelope.XMax,
                    envelope.YMax
                );
            }
        );

        locationNeeded = ResourceExtensions.GetLocalized("Error_Location_Name_Needed");
        notOneToHundred = ResourceExtensions.GetLocalized("Error_NotBetweenOneAndHundred");
        invalidLocationName = ResourceExtensions.GetLocalized("Error_Invalid_Location_Name");
        latitudeNeeded = ResourceExtensions.GetLocalized("Error_LatitudeNeeded");
        latitudeNotInEnvelope = ResourceExtensions.GetLocalized("Error_LatitudeNotInEnvelope");
        longitudeNeeded = ResourceExtensions.GetLocalized("Error_LongitudeNeeded");
        longitudeNotInEnvelope = ResourceExtensions.GetLocalized("Error_LongitudeNotInEnvelope");
        coordinateInvalid = ResourceExtensions.GetLocalized("Error_CoordinateInvalid");
        typeAndSourceNeeded = ResourceExtensions.GetLocalized("Error_TypeAndSourceNeeded");

        locationNameValid = false;
        latitudeEntryValid = false;
        longitudeEntryValid = false;

        locationName = string.Empty;
        latitudeEntry = string.Empty;
        longitudeEntry = string.Empty;

        // Initialize the view model.
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            SourceAndTypeError = typeAndSourceNeeded;

            Guard.Against.Null(
                LocalSettingsService,
                nameof(LocalSettingsService),
                "Configuration Service, Initialize(): LocalSettingsService is null."
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                SecchiNewLocationViewModelLog,
                exception,
                "SecchiNewLocationViewModel, Initialize(): exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [ObservableProperty]
    private string? locationNameError;

    [ObservableProperty]
    private string? locationNameErrorVisibility = "Collapsed";

    [ObservableProperty]
    private string? latitudeEntryError;

    [ObservableProperty]
    private string? latitudeEntryErrorVisibility = "Collapsed";

    [ObservableProperty]
    private string? longitudeEntryError;

    [ObservableProperty]
    private string? longitudeEntryErrorVisibility = "Collapsed";

    [ObservableProperty]
    private string? sourceAndTypeError;

    [ObservableProperty]
    private string? sourceAndTypeErrorVisibility = "Collapsed";

    [ObservableProperty]
    private bool locationTypeSet;

    partial void OnLocationTypeSetChanged(bool oldValue, bool newValue)
    {
        // Log to trace that LocationTypeSetChanged was called.
        logger.LogTrace(
            SecchiNewLocationViewModelLog,
            "SecchiNewLocationViewModel: OnLocationTypeSetChanged invoked."
        );
        // Log to trace the newValue and oldValue.
        logger.LogTrace(
            SecchiNewLocationViewModelLog,
            "SecchiNewLocationViewModel: OnLocationTypeSetChanged, oldValue: {oldValue}, newValue: {newValue}.",
            oldValue,
            newValue
        );
        if (newValue)
        {
            LocationTypeSet = newValue;
            CanSaveLocation();
        }
    }

    [ObservableProperty]
    private bool locationSourceSet;

    partial void OnLocationSourceSetChanged(bool oldValue, bool newValue)
    {
        // Log to trace that LocationSourceSetChanged was called.
        logger.LogTrace(
            SecchiNewLocationViewModelLog,
            "SecchiNewLocationViewModel: OnLocationSourceSetChanged invoked."
        );
        // Log to trace the newValue and oldValue.
        logger.LogTrace(
            SecchiNewLocationViewModelLog,
            "SecchiNewLocationViewModel: OnLocationSourceSetChanged, oldValue: {oldValue}, newValue: {newValue}.",
            oldValue,
            newValue
        );
        if (newValue)
        {
            LocationSourceSet = newValue;
            CanSaveLocation();
        }
    }

    [ObservableProperty]
    private bool locationNameSet;

    [ObservableProperty]
    [Required(ErrorMessage = "NameNeeded")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "NotOneToHundred")]
    [LocationNameValid(ErrorMessage = "InvalidLocationName")]
    private string locationName;

    [RelayCommand]
    public void LocationNameIsChanging()
    {
        // Log to trace that the LocationNameIsChanging command was called.
        logger.LogTrace(
            SecchiNewLocationViewModelLog,
            "SecchiNewLocationViewModel: LocationNameIsChanging invoked."
        );
        // Log the LocationName.
        logger.LogTrace(
            SecchiNewLocationViewModelLog,
            "SecchiNewLocationViewModel: LocationNameIsChanging, LocationName: {LocationName}.",
            LocationName
        );
        try
        {
            var results = new List<ValidationResult>();
            locationNameValid = Validator.TryValidateProperty(
                LocationName,
                new ValidationContext(this, null, null) { MemberName = nameof(LocationName) },
                results
            );
            // Log isValid to debug.
            logger.LogTrace(
                SecchiNewLocationViewModelLog,
                "SecchiNewLocationViewModel: LocationNameIsChanging locationNameValid: {isValid}.",
                locationNameValid
            );
            if (locationNameValid is false)
            {
                var firstValidationResult = results.FirstOrDefault();
                if (firstValidationResult != null)
                {
                    logger.LogDebug(
                        SecchiNewLocationViewModelLog,
                        "SecchiNewLocationViewModel: LocationNameIsChanging firstValidationResult: {firstValidationResult}.",
                        firstValidationResult
                    );
                }
                if (firstValidationResult is not null)
                {
                    if (firstValidationResult.ToString() == "NameNeeded")
                    {
                        logger.LogTrace(
                            SecchiNewLocationViewModelLog,
                            "SecchiNewLocationViewModel: LocationNameIsChanging NeedName error."
                        );
                        LocationNameError = locationNeeded;
                        LocationNameErrorVisibility = "Visible";
                        LocationCanBeSaved = false;
                    }
                    if (firstValidationResult.ToString() == "NotOneToHundred")
                    {
                        logger.LogTrace(
                            SecchiNewLocationViewModelLog,
                            "SecchiNewLocationViewModel: LocationNameIsChanging NotOneToHundred error."
                        );
                        LocationNameError = notOneToHundred;
                        LocationNameErrorVisibility = "Visible";
                        LocationCanBeSaved = false;
                    }
                    if (firstValidationResult.ToString() == "InvalidLocationName")
                    {
                        logger.LogTrace(
                            SecchiNewLocationViewModelLog,
                            "SecchiNewLocationViewModel: LocationNameIsChanging InvalidLocationName error."
                        );
                        LocationNameError = invalidLocationName;
                        LocationNameErrorVisibility = "Visible";
                        LocationCanBeSaved = false;
                    }
                }
            }
            else
            {
                LocationNameSet = true;
                CanSaveLocation();
                LocationNameErrorVisibility = "Collapsed";
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                SecchiNewLocationViewModelLog,
                exception,
                "SecchiNewLocationViewModel: LocationNameIsChanging exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [ObservableProperty]
    [Required(ErrorMessage = "LatitudeNeeded")]
    [CoordinateValid(ErrorMessage = "InvalidCoordinate")]
    private string latitudeEntry;

    [RelayCommand]
    public void LatitudeIsChanging()
    {
        // Log to trace that the LatitudeIsChanging command was called.
        logger.LogTrace(
            SecchiNewLocationViewModelLog,
            "SecchiNewLocationViewModel: LatitudeIsChanging invoked."
        );
        // Log the Latitude.
        logger.LogTrace(
            SecchiNewLocationViewModelLog,
            "SecchiNewLocationViewModel: LatitudeIsChanging, Latitude: {Latitude}.",
            LatitudeEntry
        );
        try
        {
            var results = new List<ValidationResult>();
            latitudeEntryValid = Validator.TryValidateProperty(
                LatitudeEntry,
                new ValidationContext(this, null, null) { MemberName = nameof(LatitudeEntry) },
                results
            );
            // Log isValid to debug.
            logger.LogTrace(
                SecchiNewLocationViewModelLog,
                "SecchiNewLocationViewModel: LatitudeEntryIsChanging latitudeEntryValid: {isValid}.",
                latitudeEntryValid
            );
            if (latitudeEntryValid is false)
            {
                var firstValidationResult = results.FirstOrDefault();
                if (firstValidationResult != null)
                {
                    logger.LogDebug(
                        SecchiNewLocationViewModelLog,
                        "SecchiNewLocationViewModel: LatitudeEntryIsChanging firstValidationResult: {firstValidationResult}.",
                        firstValidationResult
                    );
                }
                if (firstValidationResult is not null)
                {
                    if (firstValidationResult.ToString() == "LatitudeNeeded")
                    {
                        logger.LogTrace(
                            SecchiNewLocationViewModelLog,
                            "SecchiNewLocationViewModel: LatitudeEntryIsChanging NeedName error."
                        );
                        LatitudeEntryError = latitudeNeeded;
                        LatitudeEntryErrorVisibility = "Visible";
                        LocationCanBeSaved = false;
                    }

                    if (firstValidationResult.ToString() == "InvalidCoordinate")
                    {
                        logger.LogTrace(
                            SecchiNewLocationViewModelLog,
                            "SecchiNewLocationViewModel: LatitudeEntryIsChanging InvalidCoordinate error."
                        );
                        LatitudeEntryError = coordinateInvalid;
                        LatitudeEntryErrorVisibility = "Visible";
                        LocationCanBeSaved = false;
                    }
                }
            }
            else
            {
                if (envelope is null)
                {
                    logger.LogError(
                        SecchiNewLocationViewModelLog,
                        "SecchiNewLocationViewModel: LatitudeEntryIsChanging envelope is null."
                    );
                    return;
                }
                var coordinateDouble = Convert.ToDouble(LatitudeEntry);
                if (coordinateDouble < envelope.YMin || coordinateDouble > envelope.YMax)
                {
                    logger.LogTrace(
                        SecchiNewLocationViewModelLog,
                        "SecchiNewLocationViewModel: LatitudeEntryIsChanging LatitudeNotInEnvelope error."
                    );
                    LatitudeEntryError = latitudeNotInEnvelope;
                    LatitudeEntryErrorVisibility = "Visible";
                    LocationCanBeSaved = false;
                }
                else
                {
                    latitudeEntryValid = true;
                    CanSaveLocation();
                    LatitudeEntryErrorVisibility = "Collapsed";
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                SecchiNewLocationViewModelLog,
                exception,
                "SecchiNewLocationViewModel: LatitudeEntryIsChanging exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [ObservableProperty]
    [Required(ErrorMessage = "LongitudeNeeded")]
    [CoordinateValid(ErrorMessage = "InvalidCoordinate")]
    private string longitudeEntry;

    [RelayCommand]
    public void LongitudeIsChanging()
    {
        // Log to trace that the LongitudeIsChanging command was called.
        logger.LogTrace(
            SecchiNewLocationViewModelLog,
            "SecchiNewLocationViewModel: LongitudeIsChanging invoked."
        );
        // Log the Longitude.
        logger.LogTrace(
            SecchiNewLocationViewModelLog,
            "SecchiNewLocationViewModel: LongitudeIsChanging, Longitude: {Longitude}.",
            LongitudeEntry
        );
        try
        {
            var results = new List<ValidationResult>();
            longitudeEntryValid = Validator.TryValidateProperty(
                LongitudeEntry,
                new ValidationContext(this, null, null) { MemberName = nameof(LongitudeEntry) },
                results
            );
            // Log isValid to debug.
            logger.LogTrace(
                SecchiNewLocationViewModelLog,
                "SecchiNewLocationViewModel: LongitudeEntryIsChanging longitudeEntryValid: {isValid}.",
                longitudeEntryValid
            );
            if (longitudeEntryValid is false)
            {
                var firstValidationResult = results.FirstOrDefault();
                if (firstValidationResult != null)
                {
                    logger.LogDebug(
                        SecchiNewLocationViewModelLog,
                        "SecchiNewLocationViewModel: LongitudeEntryIsChanging firstValidationResult: {firstValidationResult}.",
                        firstValidationResult
                    );
                }
                if (firstValidationResult is not null)
                {
                    if (firstValidationResult.ToString() == "LongitudeNeeded")
                    {
                        logger.LogTrace(
                            SecchiNewLocationViewModelLog,
                            "SecchiNewLocationViewModel: LongitudeEntryIsChanging NeedName error."
                        );
                        LongitudeEntryError = longitudeNeeded;
                        LongitudeEntryErrorVisibility = "Visible";
                        LocationCanBeSaved = false;
                    }

                    if (firstValidationResult.ToString() == "InvalidCoordinate")
                    {
                        logger.LogTrace(
                            SecchiNewLocationViewModelLog,
                            "SecchiNewLocationViewModel: LongitudeEntryIsChanging InvalidCoordinate error."
                        );
                        LongitudeEntryError = coordinateInvalid;
                        LongitudeEntryErrorVisibility = "Visible";
                        LocationCanBeSaved = false;
                    }
                }
            }
            else
            {
                if (envelope is null)
                {
                    logger.LogError(
                        SecchiNewLocationViewModelLog,
                        "SecchiNewLocationViewModel: LongitudeEntryIsChanging envelope is null."
                    );
                    return;
                }
                var coordinateDouble = Convert.ToDouble(LongitudeEntry);
                if (coordinateDouble < envelope.XMin || coordinateDouble > envelope.XMax)
                {
                    logger.LogTrace(
                        SecchiNewLocationViewModelLog,
                        "SecchiNewLocationViewModel: LongitudeEntryIsChanging LongitudeNotInEnvelope error."
                    );
                    LongitudeEntryError = longitudeNotInEnvelope;
                    LongitudeEntryErrorVisibility = "Visible";
                    LocationCanBeSaved = false;
                }
                else
                {
                    longitudeEntryValid = true;
                    CanSaveLocation();
                    LongitudeEntryErrorVisibility = "Collapsed";
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                SecchiNewLocationViewModelLog,
                exception,
                "SecchiNewLocationViewModel: LongitudeEntryIsChanging exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    [ObservableProperty]
    private bool locationCanBeSaved = false;

    [ObservableProperty]
    // If location source is active, which is set in MainPage.xaml.cs,
    // then valid latitude and longitude values are needed to save the location.
    private bool locationSourceActive = false;

    partial void OnLocationSourceActiveChanged(bool oldValue, bool newValue)
    {
        // Log to trace that LocationSourceActiveChanged was called.
        logger.LogTrace(
            SecchiNewLocationViewModelLog,
            "SecchiNewLocationViewModel: OnLocationSourceActiveChanged invoked."
        );
        // Log to trace the newValue and oldValue.
        logger.LogTrace(
            SecchiNewLocationViewModelLog,
            "SecchiNewLocationViewModel: OnLocationSourceActiveChanged, oldValue: {oldValue}, newValue: {newValue}.",
            oldValue,
            newValue
        );
        if (newValue)
        {
            LocationSourceActive = newValue;
            CanSaveLocation();
        }
    }

    private void CanSaveLocation()
    {
        if (LocationSourceSet && LocationTypeSet)
        {
            SourceAndTypeErrorVisibility = "Collapsed";
        }

        if (LocationSourceActive)
        {
            if (
                LocationTypeSet
                && LocationSourceSet
                && LocationNameSet
                && latitudeEntryValid
                && longitudeEntryValid
            )
            {
                LocationCanBeSaved = true;
            }
            else
            {
                LocationCanBeSaved = false;
            }
        }
        else
        {
            if (LocationTypeSet && LocationSourceSet && LocationNameSet)
            {
                LocationCanBeSaved = true;
            }
            else
            {
                LocationCanBeSaved = false;
            }
        }
    }
}
