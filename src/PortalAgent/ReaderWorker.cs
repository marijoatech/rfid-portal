using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
using SReaderAPI;

namespace PortalAgent
{
    public sealed class ReaderWorker
    {
        private sealed class GpoCommand
        {
            public int Id;
            public int Seconds;
        }

        private sealed class AntennaConfig
        {
            public int Puerto;
            public int Potencia;
            public int RssiMin;
        }

        private readonly ConcurrentQueue<Reading> queue = new ConcurrentQueue<Reading>();
        private readonly ConcurrentQueue<GpoCommand> gpoQueue = new ConcurrentQueue<GpoCommand>();
        private readonly List<AntennaConfig> antennas = new List<AntennaConfig>();
        private readonly HashSet<string> background = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Logger logger;
        private readonly int channels;
        private volatile bool running;
        private Thread thread;
        private SReader reader;

        public string Portal { get; private set; }
        public string Ip { get; private set; }
        public int IdLector { get; private set; }
        public string LastError { get; private set; }
        public bool IsConnected { get; private set; }

        public ReaderWorker(string portal, string ip, int idLector, int channels, JArray antennaRows, Logger logger)
        {
            Portal = portal;
            Ip = ip;
            IdLector = idLector;
            this.channels = channels > 0 ? channels : 8;
            this.logger = logger;

            if (antennaRows != null)
            {
                foreach (JObject row in antennaRows)
                {
                    int active = (int)row["activa"];
                    if (active != 1) { continue; }
                    AntennaConfig antenna = new AntennaConfig();
                    antenna.Puerto = (int)row["puerto"];
                    antenna.Potencia = (int)row["potencia"];
                    antenna.RssiMin = (int)row["rssi_min"];
                    antennas.Add(antenna);
                }
            }

            antennas.Sort(delegate(AntennaConfig a, AntennaConfig b) { return a.Puerto.CompareTo(b.Puerto); });
        }

        public void Start()
        {
            if (running) { return; }
            running = true;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "Reader " + Ip;
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            try
            {
                if (reader != null) { reader.Inventry_stop(); }
            }
            catch { }

            Thread old = thread;
            thread = null;
            if (old != null && old.IsAlive) { old.Join(4000); }
        }

        public List<Reading> Drain()
        {
            List<Reading> readings = new List<Reading>();
            Reading reading;
            while (queue.TryDequeue(out reading)) { readings.Add(reading); }
            return readings;
        }

        public void SetBackground(IEnumerable<string> values)
        {
            background.Clear();
            if (values == null) { return; }
            foreach (string value in values)
            {
                string epc = (value ?? "").Trim();
                if (epc != "") { background.Add(epc); }
            }
        }

        public bool IsBackground(string epc)
        {
            return !string.IsNullOrEmpty(epc) && background.Contains(epc);
        }

        public void EnqueueGpo(int id, int seconds)
        {
            GpoCommand command = new GpoCommand();
            command.Id = id;
            command.Seconds = seconds;
            gpoQueue.Enqueue(command);
        }

        private void Loop()
        {
            while (running)
            {
                try
                {
                    reader = SReader.Create("tcp://" + Ip);
                    reader.Connect();
                    IsConnected = true;
                    LastError = "";
                    logger.Info("Lector " + Portal + " (" + Ip + ") conectado.");

                    int[] powers = new int[channels];
                    for (int i = 0; i < powers.Length; i++) { powers[i] = 20; }
                    foreach (AntennaConfig antenna in antennas)
                    {
                        if (antenna.Puerto >= 1 && antenna.Puerto <= powers.Length)
                        {
                            powers[antenna.Puerto - 1] = antenna.Potencia;
                        }
                    }

                    try
                    {
                        if (channels > 1) { reader.SetPortReadPower(powers); }
                        else { reader.SetReaderPower(powers[0]); }
                    }
                    catch (Exception ex)
                    {
                        logger.Error("No se pudo aplicar potencia en " + Ip + ": " + ex.Message);
                    }

                    reader.TagRead += OnTagRead;
                    List<int> ports = new List<int>();
                    foreach (AntennaConfig antenna in antennas) { ports.Add(antenna.Puerto); }
                    logger.Info("Lector " + Portal + " (" + Ip + ") leyendo puertos " + Join(ports) + ".");

                    while (running)
                    {
                        GpoCommand gpo;
                        while (gpoQueue.TryDequeue(out gpo))
                        {
                            try
                            {
                                reader.SetGPO(gpo.Id, 1, gpo.Seconds);
                                logger.Info("Sirena: GPO " + gpo.Id + " activo por " +
                                    gpo.Seconds + " segundos en " + Ip + ".");
                            }
                            catch (Exception ex)
                            {
                                logger.Error("Sirena GPO " + gpo.Id + " en " + Ip + ": " + ex.Message);
                            }
                        }

                        foreach (AntennaConfig antenna in antennas)
                        {
                            if (!running) { break; }
                            if (channels <= 1)
                            {
                                reader.Inventry(new Gen2.InventryValue(4, 1), null);
                            }
                            else
                            {
                                reader.Inventry(new Gen2.InventryValue(true, 0, antenna.Puerto, 5, 4, 1), null);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    if (running) { logger.Error("Lector " + Ip + ": " + ex.Message); }
                }
                finally
                {
                    IsConnected = false;
                    try
                    {
                        if (reader != null)
                        {
                            reader.TagRead -= OnTagRead;
                            reader.Inventry_stop();
                            reader.ShutDown();
                        }
                    }
                    catch { }
                    reader = null;
                }

                if (running) { Thread.Sleep(5000); }
            }
        }

        private void OnTagRead(object sender, TagReadDataEventArgs e)
        {
            if (!running) { return; }

            try
            {
                TagData tag = e.TagData;
                string epc = (tag.EpcString ?? "").Trim();
                string lote = Normalize(epc);
                if (lote == "") { return; }

                int rssiMin = -80;
                foreach (AntennaConfig antenna in antennas)
                {
                    if (antenna.Puerto == tag.Ant) { rssiMin = antenna.RssiMin; break; }
                }
                if (tag.Rssi < rssiMin) { return; }

                Reading reading = new Reading();
                reading.Epc = epc;
                reading.Lote = lote;
                reading.Ant = tag.Ant;
                reading.Rssi = tag.Rssi;
                reading.SeenAt = DateTime.Now;
                queue.Enqueue(reading);
            }
            catch (Exception ex)
            {
                logger.Error("Lector " + Ip + " procesando etiqueta: " + ex.Message);
            }
        }

        private static string Normalize(string epc)
        {
            string text = (epc ?? "").Trim().ToLowerInvariant();
            if (text.Length == 0) { return ""; }
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9') { return ""; }
            }
            string clean = text.TrimStart('0');
            return clean.Length == 0 ? "" : clean;
        }

        private static string Join(List<int> values)
        {
            string text = "";
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) { text += ","; }
                text += values[i];
            }
            return text == "" ? "(ninguno)" : text;
        }
    }
}
