using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Esri.ArcGISRuntime.Data;

using WaterMeasurements.Models;
using WaterMeasurements.Views;
using WaterMeasurements.Services;
using WaterMeasurements.Contracts.Services;
using static WaterMeasurements.Models.SqliteModel;



namespace WaterMeasurements.Services;

public partial class SqliteService : ISqliteService
{
    public Task FeaturetableToDatabase(FeatureTable featureTable) =>
        throw new NotImplementedException();

    private string GetSqliteType(Field field)
    {
        var fieldType = field.FieldType.ToString();
        var sqliteType = GeodatabaseSqliteTypeConversion[fieldType];
        return sqliteType;
    }
}
