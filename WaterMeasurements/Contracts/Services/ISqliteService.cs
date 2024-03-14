using WaterMeasurements.Models;

namespace WaterMeasurements.Contracts.Services;

public interface ISqliteService
{
    public Task<IEnumerable<SecchiLocation>> GetSecchiLocationsFromSqlite(
        int pageSize,
        int pageNumber
    );

    public Task AddLocationRecordToTable(Location Location, DbType DbType);

    public Task DeleteLocationRecordFromTable(int LocationId, DbType DbType);

    public Task UpdadateLocationRecordInTable(Location Location, DbType DbType);
}
