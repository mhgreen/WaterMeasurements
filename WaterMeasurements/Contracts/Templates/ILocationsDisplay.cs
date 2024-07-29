using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WaterMeasurements.Models;

namespace WaterMeasurements.Contracts.Templates;

public interface ILocationsDisplay
{
    string LocationName { get; }
    double Latitude { get; }
    double Longitude { get; }
    LocationType LocationType { get; }
    RecordStatus RecordStatus { get; }
    int LocationId { get; }
    string LatLon => $"Lat: {Latitude:F4}, Lon: {Longitude:F4}";
}
