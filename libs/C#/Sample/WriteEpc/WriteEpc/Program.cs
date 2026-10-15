using SReaderAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WriteEpc
{
    class Program
    {
        static void Main(string[] args)
        {
            SReader reader = null;
            try
            {
                //tcp connect
                reader = SReader.Create("tcp://192.168.1.136");
                //serial connect
                //reader = SReader.Create("sld:///COM4");

                reader.Connect();

                int[] antList = { 1 };
                reader.SetAntenna(antList);

                int power = 30;
                reader.SetReaderPower(power);

                byte[] newEpc = { 0xE2, 0x11, 0x22, 0x33 };

                Gen2.WriteData writeData = new Gen2.WriteData(2, newEpc, 0);

                reader.WriteMemory(null, writeData, null);

                reader.ShutDown();
            }
            catch(Exception e)
            {
                Console.WriteLine("error:" + e.Message);
            }
        }
    }
}
