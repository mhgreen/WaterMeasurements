using WaterMeasurements.Models;

namespace WaterMeasurements.Contracts.Services;

public interface ISqliteService
{
    public Task<IEnumerable<SecchiLocation>> GetSecchiLocationsFromSqlite(
        int pageSize,
        int pageNumber
    );

    public Task AddLocationRecordToTable(LocationRecord Location, DbType DbType);

    public Task<LocationRecord> GetLocationRecordFromTable(int LocationId, DbType DbType);

    public Task DeleteLocationRecordFromTable(int LocationId, DbType DbType);

    public Task UpdadateLocationRecordInTable(LocationRecord Location, DbType DbType);

    public Task<IEnumerable<LocationRecord>> GetRecordGroupFromSqlite(
        DbType dbType,
        int pageSize,
        int pageNumber
    );

    public Task<CreateLocationDetailResult> CreateLocationDetailTable(DbType dbType);

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

    public Task SetLocationRecordtoCollectedState(
        int locationId,
        DbType DbType,
        LocationCollected LocationCollectedState,
        LocationsCollectedStateScope SetScope
    );

    public Task<LocationCollected> GetLocationRecordCollectionState(int LocationId, DbType DbType);
}
