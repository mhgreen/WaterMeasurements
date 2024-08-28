using System.Threading.Tasks;

namespace WaterMeasurements.Contracts.Services;

public interface IActivationService
{
    Task ActivateAsync(object activationArgs);
}
