using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Text;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Geotriggers;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Portal;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Tasks;
using Esri.ArcGISRuntime.Tasks.Offline;
using Esri.ArcGISRuntime.UI.Controls;
using FTD2XX_NET;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stateless;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Contracts.Services.Instances;
using WaterMeasurements.Models;
using WaterMeasurements.Services;
using WaterMeasurements.Services.Instances;
using WaterMeasurements.Views;
using Windows.ApplicationModel.Background;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Storage;
using Windows.UI.Core;
using static System.Net.Mime.MediaTypeNames;

namespace WaterMeasurements.Services;

public partial class WaterQualityService : IWaterQualityService
{
    private readonly ILogger<WaterQualityService> logger;

    // Set the EventId for logging messages.
    internal EventId WaterQualityServiceLog = new(20, "WaterQualityService");

    // Disabling the warning IDE0052 because the CommunicationService is used in the constructor.
#pragma warning disable IDE0052
    private readonly ICommunicationService CommunicationService;
#pragma warning restore IDE0052

    public WaterQualityService(
        ILogger<WaterQualityService> logger,
        ICommunicationService CommunicationService
    )
    {
        this.logger = logger;

        // Log that the WaterQualityService has been created.
        logger.LogInformation(
            WaterQualityServiceLog,
            "WaterQualityService: WaterQualityService created."
        );

        // Set the CommunicationService.
        this.CommunicationService = CommunicationService;

        // Initialize the WaterQualityService.
        Initialize();
    }

    private void Initialize()
    {
        // Log that the WaterQualityService has been initialized.
        logger.LogInformation(
            WaterQualityServiceLog,
            "WaterQualityService: WaterQualityService initialized."
        );
    }
}
