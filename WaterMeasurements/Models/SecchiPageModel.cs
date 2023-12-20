using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WaterMeasurements.Models;

// State of the SecchiViewModel service.
public enum SecchiServiceState
{
    WaitingForObservations,
    WaitingForLocations,
    HaveObservationsAndLocations,
    Running,
    RunningInternetAvailable,
    RunningInternetUnavailable,
    AppClosing
}

// Triggers for the SecchiViewModel service.
public enum SecchiServiceTrigger
{
    Startup,
    AppClosing,
    InternetUnavailableRecieved,
    InternetAvailableRecieved,
    ObservationFeatureTableReceived,
    LocationFeatureTableReceived,
    ObservationAndLocationFeatureTablesReceived,
    UiThreadRecievedorPresent,
    Cancel
}
