using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WaterMeasurements.Models;

public class FTDIPort
{
    private readonly string nodeComportName;
    private readonly string nodeDescription;
    private readonly string nodeSerialNumber;

    // Constructor
    public FTDIPort()
    {
        nodeComportName = string.Empty;
        nodeDescription = string.Empty;
        nodeSerialNumber = string.Empty;
    }

    // Constructor

    public FTDIPort(string ComportName, string Description, string SerialNumber)
    {
        nodeComportName = ComportName;
        nodeDescription = Description;
        nodeSerialNumber = SerialNumber;
    }

    public string NodeComportName => nodeComportName;

    public string NodeDescription => nodeDescription;

    public string NodeSerialNumber => nodeSerialNumber;
}
