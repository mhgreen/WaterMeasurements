using System.Threading.Tasks;

namespace WaterMeasurements.Activation;

public interface IActivationHandler
{
    bool CanHandle(object args);

    Task HandleAsync(object args);
}
