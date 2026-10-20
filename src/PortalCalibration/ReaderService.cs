using System;
using System.Collections.Generic;
using System.Threading;
using SReaderAPI;

namespace PortalCalibration
{
    public sealed class ReaderService : IDisposable
    {
        private SReader reader;
        private Thread scanner;
        private volatile bool running;
        private int channels;
        private int q = 4;
        private int session = 1;
        private int scanTime = 5;
        private List<int> antennas = new List<int>();

        public event Action<string> Log;
        public event Action<TagReading> TagArrived;

        public bool IsConnected
        {
            get { return reader != null; }
        }

        public bool IsScanning
        {
            get { return running; }
        }

        public int Channels
        {
            get { return channels; }
        }

        public ReaderSnapshot Connect(string ip)
        {
            if (string.IsNullOrEmpty(ip)) { throw new ArgumentException("Falta la IP del lector."); }
            if (reader != null) { Disconnect(); }

            SReader created = SReader.Create("tcp://" + ip.Trim());
            created.Connect();
            created.TagRead += OnTagRead;
            reader = created;

            LogMessage("Conectado a " + ip.Trim() + ".");
            ReaderSnapshot snapshot = ReadSnapshot();
            channels = snapshot.Channels;
            return snapshot;
        }

        public ReaderSnapshot ReadSnapshot()
        {
            if (reader == null) { throw new InvalidOperationException("No hay lector conectado."); }

            ReaderSnapshot snapshot = new ReaderSnapshot();
            ReaderInfo info = reader.GetReaderInfo();
            snapshot.Type = info.Type;
            snapshot.VersionH = info.VersionH;
            snapshot.VersionL = info.VersionL;
            snapshot.GlobalPower = info.Power;
            snapshot.CheckAnt = info.CheckAnt;
            snapshot.AntConfig = info.Ant;
            snapshot.ModelName = ModelName(info.Type);

            try { snapshot.Region = info.getCurrentRegionString(); }
            catch { snapshot.Region = "?"; }

            try { snapshot.Serial = reader.GetSerialNO(); }
            catch { snapshot.Serial = "?"; }

            try
            {
                Ip_Info current = reader.GetIp();
                snapshot.Ip = current.IP();
                snapshot.Mask = current.Mask();
                snapshot.Gateway = current.Gate();
            }
            catch
            {
                snapshot.Ip = "?";
                snapshot.Mask = "?";
                snapshot.Gateway = "?";
            }

            int[] powers = null;
            try { powers = reader.GetPortReadPower(); }
            catch { }

            snapshot.Channels = ChannelsFor(info.Type, powers);
            if (powers == null || powers.Length != snapshot.Channels)
            {
                powers = new int[snapshot.Channels];
                for (int i = 0; i < powers.Length; i++) { powers[i] = info.Power; }
            }
            snapshot.Powers = powers;

            List<int> detected = null;
            try { detected = reader.AntennaDetected(); }
            catch { }
            if (detected != null) { snapshot.Detected = detected; }

            snapshot.ReturnLoss = new int[snapshot.Channels];
            for (int ant = 1; ant <= snapshot.Channels; ant++)
            {
                snapshot.ReturnLoss[ant - 1] = -1;
                try { snapshot.ReturnLoss[ant - 1] = reader.CheckReturnLoss(ant); }
                catch { }
            }

            channels = snapshot.Channels;
            return snapshot;
        }

        public void Apply(int[] powers, int newQ, int newSession, int newScanTime, List<int> enabled)
        {
            if (reader == null) { throw new InvalidOperationException("No hay lector conectado."); }
            if (running) { throw new InvalidOperationException("Detenga la lectura antes de aplicar la configuracion."); }
            if (enabled == null || enabled.Count == 0) { throw new InvalidOperationException("No hay antenas activas."); }
            if (powers == null || powers.Length != channels) { throw new InvalidOperationException("La cantidad de potencias no coincide con los canales."); }

            if (channels == 1) { reader.SetReaderPower(powers[0]); }
            else { reader.SetPortReadPower(powers); }

            reader.SetScanTime(newScanTime);
            q = newQ;
            session = newSession;
            scanTime = newScanTime;
            antennas = new List<int>(enabled);

            LogMessage("Configuracion aplicada: potencias=" + JoinInts(powers) + " | Q=" + q +
                " | Session=S" + session + " | Time=" + (scanTime * 100) + " ms | Antenas=" + JoinInts(enabled));
        }

        public void StartScan()
        {
            if (reader == null) { throw new InvalidOperationException("No hay lector conectado."); }
            if (running) { return; }
            if (antennas.Count == 0) { throw new InvalidOperationException("No hay antenas activas."); }

            running = true;
            scanner = new Thread(ScanLoop);
            scanner.IsBackground = true;
            scanner.Name = "Portal-RFID-Scan";
            scanner.Start();
            LogMessage("Lectura iniciada.");
        }

        public void StopScan()
        {
            if (reader == null) { return; }
            if (!running && scanner == null) { return; }

            running = false;
            try { reader.Inventry_stop(); }
            catch { }

            Thread old = scanner;
            scanner = null;
            if (old != null && old.IsAlive) { old.Join(3000); }
            if (old != null && old.IsAlive) { LogMessage("Aviso: el hilo de lectura no termino a tiempo."); }
            else { LogMessage("Lectura detenida."); }
        }

        public void PulseGpo(int id, int seconds)
        {
            if (reader == null) { throw new InvalidOperationException("No hay lector conectado."); }
            if (running) { throw new InvalidOperationException("Detenga la lectura antes de probar GPO."); }
            reader.SetGPO(id, 1, seconds);
            LogMessage("GPO " + id + " activo por " + seconds + " segundos.");
        }

        public void SetBuzzer(bool enabled)
        {
            if (reader == null) { throw new InvalidOperationException("No hay lector conectado."); }
            if (running) { throw new InvalidOperationException("Detenga la lectura antes de cambiar el buzzer."); }
            reader.SetBuzzer(enabled ? 1 : 0);
            LogMessage("Buzzer del lector: " + (enabled ? "encendido" : "apagado") + ".");
        }

        public void Disconnect()
        {
            try { StopScan(); }
            catch { }

            SReader old = reader;
            reader = null;
            if (old == null) { return; }

            try { old.TagRead -= OnTagRead; }
            catch { }
            try { old.ShutDown(); }
            catch { }
            try { old.Dispose(); }
            catch { }

            LogMessage("Desconectado.");
        }

        public void Dispose()
        {
            Disconnect();
        }

        private void ScanLoop()
        {
            try
            {
                while (running)
                {
                    List<int> current = new List<int>(antennas);
                    foreach (int ant in current)
                    {
                        if (!running) { break; }
                        if (channels <= 1)
                        {
                            Gen2.InventryValue value = new Gen2.InventryValue(q, session);
                            reader.Inventry(value, null);
                        }
                        else
                        {
                            Gen2.InventryValue value = new Gen2.InventryValue(true, 0, ant, scanTime, q, session);
                            reader.Inventry(value, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (running) { LogMessage("ERROR de lectura: " + ex.Message); }
            }
            finally
            {
                running = false;
            }
        }

        private void OnTagRead(object sender, TagReadDataEventArgs e)
        {
            try
            {
                TagData tag = e.TagData;
                TagReading reading = new TagReading();
                reading.Epc = tag.EpcString;
                reading.Antenna = tag.Ant;
                reading.Rssi = tag.Rssi;
                reading.Num = tag.Num;
                reading.Tid = tag.Data == null ? "" : tag.DataString;
                reading.SeenAt = DateTime.Now;

                Action<TagReading> handler = TagArrived;
                if (handler != null) { handler(reading); }
            }
            catch (Exception ex)
            {
                LogMessage("ERROR procesando una etiqueta: " + ex.Message);
            }
        }

        private void LogMessage(string message)
        {
            Action<string> handler = Log;
            if (handler != null) { handler(message); }
        }

        public static string ModelName(int type)
        {
            switch (type)
            {
                case 0x10: return "M100";
                case 0x20: return "R2000 4 canales";
                case 0x26: return "R2000 8 canales";
                case 0x27: return "R2000 16 canales";
                case 0x31: return "E310 1 canal";
                case 0x35: return "E310 4 canales";
                case 0x38: return "E310 8 canales";
                case 0x39: return "E310 16 canales";
                case 0x70: return "E510 1 canal";
                case 0x55: return "E510 4 canales";
                case 0x56: return "E510 8 canales";
                case 0x57: return "E510 16 canales";
                case 0x71: return "E710 1 canal";
                case 0x75: return "E710 4 canales";
                case 0x76: return "E710 8 canales";
                case 0x77: return "E710 16 canales";
                default: return "Modelo desconocido";
            }
        }

        public static int ChannelsFor(int type, int[] powers)
        {
            switch (type)
            {
                case 0x10:
                case 0x31:
                case 0x37:
                case 0x70:
                case 0x71:
                case 0x91:
                    return 1;
                case 0x20:
                case 0x35:
                case 0x55:
                case 0x75:
                    return 4;
                case 0x26:
                case 0x38:
                case 0x56:
                case 0x76:
                    return 8;
                case 0x27:
                case 0x39:
                case 0x57:
                case 0x77:
                    return 16;
            }

            return powers != null && powers.Length > 0 ? powers.Length : 4;
        }

        private static string JoinInts(int[] values)
        {
            if (values == null) { return ""; }
            string text = "";
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) { text += ", "; }
                text += values[i];
            }
            return text;
        }

        private static string JoinInts(List<int> values)
        {
            if (values == null) { return ""; }
            string text = "";
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) { text += ", "; }
                text += values[i];
            }
            return text;
        }
    }
}
