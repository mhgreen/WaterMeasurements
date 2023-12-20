using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using WaterMeasurements.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace WaterMeasurements.Services;

public partial class InitializeServices : ObservableRecipient
{
    private readonly ILogger<InitializeServices> logger;
    internal EventId InitializeServicesEvent = new(11, "InitializeServices");

    public InitializeServices(ILogger<InitializeServices> logger)
    {
        this.logger = logger;
        logger.LogInformation(InitializeServicesEvent, "Initializing InitializeServices");
    }
}
