namespace WaterMeasurements.Contracts.Services;

public interface IGetPreplannedMapService
{
    // Check for ArcGIS API and return true if present, false if not.
    public Task<bool> IsArcgisApiKeyPresent();

    // Check for offline map id and return true if present, false if not.
    public Task<bool> IsOfflineMapIdPresent();

    // Verify that the ArcGIS API key is valid and that the offline map ID is valid.
    // Send a PreplannedMapConfigurationStatusMessage with true if both are valid, false if not.
    public Task PreplannedMapConfigurationStatusMessage();
}
