using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WaterMeasurements.Models;

public static class SqliteModel
{

    public static Dictionary<string, string> GeodatabaseSqliteTypeConversion
    {
        get; private set;
    }
        = new()
    {
    {"Int16", "INTEGER"},
    {"Int32", "INTEGER"},
    {"Int64", "INTEGER"},
    {"Float32", "REAL"},
    {"Float64", "REAL"},
    {"Date", "NUMERIC"},
    {"Text", "TEXT"},
    {"OID", "NUMERIC"},
    {"GlobalID", "TEXT"},
    {"Guid", "TEXT"},
    {"XML", "TEXT"},
    {"Geometry", "BLOB"},
    {"Raster", "BLOB"},
    {"Blob", "BLOB"}
    };
}
