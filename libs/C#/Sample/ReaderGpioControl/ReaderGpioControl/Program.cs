using SReaderAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace ReaderGpioControl
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

                reader.SetGPO(1, 1, 3);

                reader.GpioStatus += GpioStatus_startRead;

                reader.Gpi_Listeners_start();

                Thread.Sleep(10000);

                reader.Gpi_Listeners_stop();

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
