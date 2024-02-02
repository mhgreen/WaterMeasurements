using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using WaterMeasurements.Models;

using Esri.ArcGISRuntime.Data;

namespace WaterMeasurements.Contracts.Services;
public interface ISqliteService
{
    Task FeatureToTable(FeatureTable featureTable, DbType dbType);
}
