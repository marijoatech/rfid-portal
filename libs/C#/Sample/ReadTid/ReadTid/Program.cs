using SReaderAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ReadTid
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

                reader.TagRead += TagRead;

                int q = 4;
                int session = 0;
                Gen2.EmbedTagOp embed = new Gen2.EmbedTagOp(q, session, Gen2.Bank.TID, 0, 6, 0);
                for (int i = 0; i < 20; i++)
                    reader.Inventry_mix(embed, null);

                reader.TagRead -= TagRead;

                reader.ShutDown();
            }
            catch(Exception e)
            {
                Console.WriteLine("error:" + e.Message);
            }
        }

        static public void TagRead(Object sender, TagReadDataEventArgs e)
        {
            string epc = e.TagData.EpcString;
            int ant = e.TagData.Ant;
            int rssi = e.TagData.Rssi;
            Console.WriteLine("epc:" + epc + " ant:" + ant + " rssi:" + rssi);
            if (e.TagData.Data != null)
                Console.WriteLine("tid:" + e.TagData.DataString);
        }

        static public void ReadException(object sender, ReaderExceptionEventArgs e)
        {
            Console.WriteLine("error:" + e.Message);
        }

        static public void GpioStatus_startRead(object sender, GpioStatusEventArgs e)
        {
            Console.WriteLine("ID:{0}, High: {1}", e.Gpio_Pin.Id, e.Gpio_Pin.High);
        }
    }
}
