using WaterMeasurements.Models;

namespace WaterMeasurements.Contracts.Services;

public interface ISqliteService
{
    public Task<IEnumerable<SecchiLocation>> GetSecchiLocationsFromSqlite(
        int pageSize,
        int pageNumber
    );
}
