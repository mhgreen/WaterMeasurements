using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Resources;
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
using NLog;
using RabbitMQ.Client;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Helpers;
using WaterMeasurements.Models;
using WaterMeasurements.Services;
using WaterMeasurements.Views;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static WaterMeasurements.Models.PrePlannedMapConfiguration;

namespace WaterMeasurements.ViewModels;

// Message to set the value of the current collection view (selectView).
public class SetCurrentCollectionViewMessage(string value) : ValueChangedMessage<string>(value) { }

public partial class DataCollectionViewModel : ObservableValidator
{
    private readonly ILogger<MapConfigurationViewModel> logger;
    internal static EventId DataCollectionViewModelLog => new(13, "DataCollectionViewModel");
    private readonly ILocalSettingsService? LocalSettingsService;

    [ObservableProperty]
    public string? selectView;

    public DataCollectionViewModel(
        ILogger<MapConfigurationViewModel> logger,
        ILocalSettingsService? localSettingsService
    )
    {
        this.logger = logger;
        LocalSettingsService = localSettingsService;

        // Register to get the current collection view.
        WeakReferenceMessenger.Default.Register<SetCurrentCollectionViewMessage>(
            this,
            (recipient, message) =>
            {
                // Log the value of the message.
                logger.LogDebug(
                    DataCollectionViewModelLog,
                    "DataCollectionViewModel, SetCurrentCollectionViewMessage: Message value: {messageValue}.",
                    message.Value
                );
                // Set the current collection view.
                SelectView = message.Value;
            }
        );

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
            // Send a message to set the current collection view to Secchi.
            WeakReferenceMessenger.Default.Send(new SetCurrentCollectionViewMessage("Secchi"));
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
        _ = new FrameNavigationOptions
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
                // Send a message to set the current collection view to Secchi.
                WeakReferenceMessenger.Default.Send(new SetCurrentCollectionViewMessage("Secchi"));
                break;
            case "CollectionNavTurbidity":
                SelectView = "Turbidity";
                // Log that the Turbidity item was selected.
                logger.LogDebug(
                    DataCollectionViewModelLog,
                    "DataCollectionViewModel, CollectionNavView_ItemInvoked(): Turbidity item selected."
                );
                // Send a message to set the current collection view to Turbidity.
                WeakReferenceMessenger.Default.Send(
                    new SetCurrentCollectionViewMessage("Turbidity")
                );
                break;
            case "CollectionNavQuality":
                SelectView = "Quality";
                // Log that the Quality item was selected.
                logger.LogDebug(
                    DataCollectionViewModelLog,
                    "DataCollectionViewModel, CollectionNavView_ItemInvoked(): Quality item selected."
                );
                // Send a message to set the current collection view to Quality.
                WeakReferenceMessenger.Default.Send(new SetCurrentCollectionViewMessage("Quality"));
                break;
            case "CollectionNavTemperature":
                SelectView = "Temperature";
                // Log that the Temperature item was selected.
                logger.LogDebug(
                    DataCollectionViewModelLog,
                    "DataCollectionViewModel, CollectionNavView_ItemInvoked(): Temperature item selected."
                );
                // Send a message to set the current collection view to Temperature.
                WeakReferenceMessenger.Default.Send(
                    new SetCurrentCollectionViewMessage("Temperature")
                );
                break;
            default:
                break;
        }
    }
}
