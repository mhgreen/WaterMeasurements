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

namespace WaterMeasurements.ViewModels;

public partial class MapConfigurationViewModel : ObservableValidator
{
    private readonly ILogger<MapConfigurationViewModel> logger;

    internal EventId MapConfigurationViewModelLog = new(12, "MapConfigurationViewModel");

    private readonly ILocalSettingsService? localSettingsService;

    public event EventHandler? SettingsUpdateComplete;
    public event EventHandler? SettingsUpdateFailed;

    [ObservableProperty]
    [Required(ErrorMessage = "Preplanned Map Name is required.")]
    [MinLength(1, ErrorMessage = "Preplanned Map Name must be at least 1 character.")]
    private string? preplannedMapName;

    [RelayCommand]
    public async Task StorePreplannedMapNameAsync()
    {
        try
        {
            logger.LogDebug(
                MapConfigurationViewModelLog,
                "Preplanned Map Name changed to: {preplannedMap}",
                PreplannedMapName
            );
            ValidateProperty(PreplannedMapName, nameof(PreplannedMapName));
            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "Configuration Service, StorePreplannedMapNameAsync(): localSettingsService is null."
            );
            if (PreplannedMapName != null)
            {
                await localSettingsService.SaveSettingAsync(
                    PrePlannedMapConfiguration.Item[Key.PreplannedMapName],
                    PreplannedMapName
                );
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
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
            logger.LogDebug(
                MapConfigurationViewModelLog,
                "UpdateSettingsAsync(): Preplanned Map Name changed to: {preplannedMap}",
                PreplannedMapName
            );
            ValidateAllProperties();

            if (HasErrors)
            {
                logger.LogDebug(
                    MapConfigurationViewModelLog,
                    "UpdateSettingsAsync(): HasErrors is true, returning."
                );
                SettingsUpdateFailed?.Invoke(this, EventArgs.Empty);
                return;
            }

            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "Configuration Service, UpdateSettingsAsync(): localSettingsService is null."
            );
            if (PreplannedMapName != null)
            {
                await localSettingsService.SaveSettingAsync(
                    PrePlannedMapConfiguration.Item[Key.PreplannedMapName],
                    PreplannedMapName
                );
            }
            SettingsUpdateComplete?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            logger.LogError(
                MapConfigurationViewModelLog,
                exception,
                "Configuration Service, UpdateSettingsAsync(): exception: {exception}.",
                exception.Message.ToString()
            ); 
        }
    }

    public MapConfigurationViewModel(
        ILocalSettingsService? localSettingsService,
        ILogger<MapConfigurationViewModel> logger
    )
    {
        this.logger = logger;
        this.localSettingsService = localSettingsService;

        _ = Initialize();
        // Log that the MapConfigurationViewModel is starting.
        logger.LogInformation(MapConfigurationViewModelLog, "Starting MapConfigurationViewModel");
    }

    private async Task Initialize()
    {
        try
        {
            Guard.Against.Null(
                localSettingsService,
                nameof(localSettingsService),
                "Configuration Service, Initialize(): localSettingsService is null."
            );
            // Get the preplanned map name from the local settings.
            PreplannedMapName = await localSettingsService.ReadSettingAsync<string>(
                PrePlannedMapConfiguration.Item[Key.PreplannedMapName]
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                MapConfigurationViewModelLog,
                exception,
                "Configuration Service, Initialize(): exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }
}
