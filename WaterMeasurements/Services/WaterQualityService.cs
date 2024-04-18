using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.Pkcs;
using System.Text;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stateless;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Contracts.Services.Instances;
using WaterMeasurements.Models;
using WaterMeasurements.Services;
using WaterMeasurements.Services.Instances;
using WaterMeasurements.Views;
using static System.Net.Mime.MediaTypeNames;

namespace WaterMeasurements.Services;

public partial class WaterQualityService : IWaterQualityService
{
    private readonly ILogger<WaterQualityService> logger;

    // Set the EventId for logging messages.
    internal EventId WaterQualityServiceLog = new(20, "WaterQualityService");

    public WaterQualityService(ILogger<WaterQualityService> logger)
    {
        this.logger = logger;

        // Log that the WaterQualityService has been created.
        logger.LogInformation(
            WaterQualityServiceLog,
            "WaterQualityService: WaterQualityService created."
        );

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
