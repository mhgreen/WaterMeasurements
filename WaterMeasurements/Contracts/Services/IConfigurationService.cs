using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WaterMeasurements.Contracts.Services;
public interface IConfigurationService
{
    public Task ArcGISRuntimeInitialize();
}
