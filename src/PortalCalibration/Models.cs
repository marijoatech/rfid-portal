using System;
using System.Collections.Generic;
using System.Data;

namespace PortalCalibration
{
    public sealed class TagReading
    {
        public string Epc;
        public int Antenna;
        public int Rssi;
        public int Num;
        public string Tid;
        public DateTime SeenAt;
    }

    public sealed class ReaderSnapshot
    {
        public string Ip;
        public string Mask;
        public string Gateway;
        public string Serial;
        public int Type;
        public int VersionH;
        public int VersionL;
        public int GlobalPower;
        public string Region;
        public int Channels;
        public int[] Powers;
        public List<int> Detected = new List<int>();
        public int[] ReturnLoss;
        public string ModelName;
        public int CheckAnt;
        public int AntConfig;
    }

    public sealed class TagRow
    {
        public string Epc;
        public string Lote;
        public int Antenna;
        public int Rssi;
        public int Count;
        public DateTime First;
        public DateTime Last;
        public DataRow DataRow;
    }
}
