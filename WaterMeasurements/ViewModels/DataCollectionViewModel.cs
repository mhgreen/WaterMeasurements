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

public partial class DataCollectionViewModel : ObservableValidator
{
    private readonly ILogger<MapConfigurationViewModel> logger;
    internal EventId DataCollectionViewModelLog => new(13, "DataCollectionViewModel");
    private readonly ILocalSettingsService? LocalSettingsService;

    [ObservableProperty]
    public string selectView = "Secchi";

    public DataCollectionViewModel(
        ILogger<MapConfigurationViewModel> logger,
        ILocalSettingsService? localSettingsService
    )
    {
        this.logger = logger;
        LocalSettingsService = localSettingsService;

        _ = InitializeAsync();

        // Log that the DataCollectionViewModel has been created.
        logger.LogInformation(DataCollectionViewModelLog, "Starting DataCollectionViewModel.");
    }

    private async Task InitializeAsync()
    {
        try
        {
            Guard.Against.Null(
                LocalSettingsService,
                nameof(LocalSettingsService),
                "DataCollectionViewModel, InitializeAsync(): LocalSettingsService is null."
            );
            // Get the preplanned map name from the local settings.
            var PreplannedMapName = await LocalSettingsService.ReadSettingAsync<string>(
                PrePlannedMapConfiguration.Item[Key.PreplannedMapName]
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                DataCollectionViewModelLog,
                exception,
                "DataCollectionViewModel, InitializeAsync(): exception: {exception}.",
                exception.Message.ToString()
            );
        }
    }

    public void CollectionNavView_ItemInvoked(
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
        logger.LogDebug(
            DataCollectionViewModelLog,
            "DataCollectionViewModel, CollectionNavView_ItemInvoked(): CollectionNavView_ItemInvoked event fired."
        );

        // Log the name of the invoked item.
        logger.LogDebug(
            DataCollectionViewModelLog,
            "DataCollectionViewModel, CollectionNavView_ItemInvoked(): Invoked item name: {invokedItemName}.",
            args.InvokedItemContainer.Name
        );

        // Log the sender name.
        logger.LogDebug(
            DataCollectionViewModelLog,
            "DataCollectionViewModel, CollectionNavView_ItemInvoked(): Sender name: {senderName}.",
            sender.Name
        );

        switch (args.InvokedItemContainer.Name)
        {
            case "CollectionNavSecchi":
                SelectView = "Secchi";
                // Log that the Secchi item was selected.
                logger.LogDebug(
                    DataCollectionViewModelLog,
                    "DataCollectionViewModel, CollectionNavView_ItemInvoked(): Secchi item selected."
                );
                break;
            case "CollectionNavTurbidity":
                SelectView = "Turbidity";
                // Log that the Turbidity item was selected.
                logger.LogDebug(
                    DataCollectionViewModelLog,
                    "DataCollectionViewModel, CollectionNavView_ItemInvoked(): Turbidity item selected."
                );
                break;
            default:
                break;
        }
    }
}
