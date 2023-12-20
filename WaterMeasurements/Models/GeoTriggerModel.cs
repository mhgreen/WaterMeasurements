using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geotriggers;
using Esri.ArcGISRuntime.Location;

using Windows.Networking.Connectivity;

namespace WaterMeasurements.Models;

// Record for GeoTrigger notification.
public readonly record struct GeoTriggerNotification(
    GeotriggerNotificationInfo GeotriggerNotificationInfo
);

// Record to add a GeoTrigger with featuretable, name, channel, and trigger distance.
public readonly record struct GeoTriggerAdd(
    FeatureTable FeatureTable,
    string Name,
    uint Channel,
    double TriggerDistance
);
