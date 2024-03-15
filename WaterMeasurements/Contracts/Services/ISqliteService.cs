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

    public Task<IEnumerable<Location>> GetRecordGroupFromSqlite(
        DbType dbType,
        int pageSize,
        int pageNumber
    );

    public Task CreateLocationDetailTable(DbType dbType);

    public Task AddLocationDetailRecordToDetailTable(
        int locationId,
        LocationDetail locationDetail,
        DbType dbType
    );

    public Task UpdateLocationDetailRecordInDetailTable(
        int locationId,
        LocationDetail locationDetail,
        DbType dbType
    );

    public Task<LocationDetail> GetLocationDetailRecordFromDetailTable(
        int locationId,
        DbType dbType
    );

    public Task DeleteLocationDetailRecordFromDetailTable(int locationId, DbType dbType);
}
