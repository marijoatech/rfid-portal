using System;

namespace PortalAgent
{
    public sealed class Reading
    {
        public string Epc;
        public string Lote;
        public int Ant;
        public int Rssi;
        public DateTime SeenAt;
        public int Repeticiones = 1;
    }
}
