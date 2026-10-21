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
            public int IdComando;
            public int Gpo;
            public int Seconds;
        }

        public sealed class GpoResult
        {
            public int IdComando;
            public int Gpo;
            public int Seconds;
            public bool Success;
            public string Message;
        }

        private sealed class PowerCommand
        {
            public int Id;
            public int Power;
        }

        public sealed class PowerResult
        {
            public int Id;
            public int Power;
            public bool Success;
            public string Message;
        }

        private sealed class AntennaConfig
        {
            public int Puerto;
            public int Potencia;
            public int RssiMin;
        }

        private readonly ConcurrentQueue<Reading> queue = new ConcurrentQueue<Reading>();
        private readonly ConcurrentQueue<GpoCommand> gpoQueue = new ConcurrentQueue<GpoCommand>();
        private readonly ConcurrentQueue<GpoResult> gpoResults = new ConcurrentQueue<GpoResult>();
        private readonly ConcurrentQueue<PowerCommand> powerQueue = new ConcurrentQueue<PowerCommand>();
        private readonly ConcurrentQueue<PowerResult> powerResults = new ConcurrentQueue<PowerResult>();
        private readonly List<AntennaConfig> activeAntennas = new List<AntennaConfig>();
        private readonly HashSet<string> background = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Logger logger;
        private readonly int channels;
        private readonly int[] portPowers;
        private volatile bool running;
        private volatile bool liveLogEnabled;
        private volatile bool coverageEnabled;
        private Thread thread;
        private SReader reader;

        public string Portal { get; private set; }
        public string Ip { get; private set; }
        public int IdLector { get; private set; }
        public string LastError { get; private set; }
        public bool IsConnected { get; private set; }
        public bool LiveLogEnabled
        {
            get { return liveLogEnabled; }
            set { liveLogEnabled = value; }
        }
        public bool CoverageEnabled
        {
            get { return coverageEnabled; }
            set { coverageEnabled = value; }
        }
        public DateTime LiveLogUntil { get; set; }

        public ReaderWorker(string portal, string ip, int idLector, int channels, JArray antennaRows, Logger logger)
        {
            Portal = portal;
            Ip = ip;
            IdLector = idLector;
            this.channels = channels > 0 ? channels : 8;
            this.logger = logger;
            portPowers = new int[this.channels];
            for (int i = 0; i < portPowers.Length; i++) { portPowers[i] = 20; }

            if (antennaRows != null)
            {
                foreach (JObject row in antennaRows)
                {
                    int port = (int)row["puerto"];
                    int power = (int)row["potencia"];
                    if (port >= 1 && port <= portPowers.Length)
                    {
                        portPowers[port - 1] = Math.Max(0, Math.Min(30, power));
                    }

                    int active = (int)row["activa"];
                    if (active != 1) { continue; }
                    AntennaConfig antenna = new AntennaConfig();
                    antenna.Puerto = port;
                    antenna.Potencia = power;
                    antenna.RssiMin = (int)row["rssi_min"];
                    activeAntennas.Add(antenna);
                }
            }

            activeAntennas.Sort(delegate(AntennaConfig a, AntennaConfig b) { return a.Puerto.CompareTo(b.Puerto); });
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

        public void EnqueueGpo(int idComando, int gpo, int seconds)
        {
            GpoCommand command = new GpoCommand();
            command.IdComando = idComando;
            command.Gpo = gpo;
            command.Seconds = seconds;
            gpoQueue.Enqueue(command);
        }

        public List<GpoResult> DrainGpoResults()
        {
            List<GpoResult> results = new List<GpoResult>();
            GpoResult result;
            while (gpoResults.TryDequeue(out result)) { results.Add(result); }
            return results;
        }

        public void EnqueuePower(int id, int power)
        {
            PowerCommand command = new PowerCommand();
            command.Id = id;
            command.Power = Math.Max(0, Math.Min(30, power));
            powerQueue.Enqueue(command);
        }

        public List<PowerResult> DrainPowerResults()
        {
            List<PowerResult> results = new List<PowerResult>();
            PowerResult result;
            while (powerResults.TryDequeue(out result)) { results.Add(result); }
            return results;
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

                    try
                    {
                        ApplyPower();
                    }
                    catch (Exception ex)
                    {
                        logger.Error("No se pudo aplicar potencia en " + Ip + ": " + ex.Message);
                    }

                    reader.TagRead += OnTagRead;
                    List<int> ports = new List<int>();
                    foreach (AntennaConfig antenna in activeAntennas) { ports.Add(antenna.Puerto); }
                    logger.Info("Lector " + Portal + " (" + Ip + ") leyendo puertos " + Join(ports) +
                        " a " + portPowers[0] + " dBm por puerto.");

                    while (running)
                    {
                        PowerCommand power;
                        while (powerQueue.TryDequeue(out power))
                        {
                            int[] previous = (int[])portPowers.Clone();
                            for (int i = 0; i < portPowers.Length; i++) { portPowers[i] = power.Power; }

                            PowerResult result = new PowerResult();
                            result.Id = power.Id;
                            result.Power = power.Power;

                            try
                            {
                                try { reader.Inventry_stop(); } catch { }
                                ApplyPower();
                                result.Success = true;
                                result.Message = "Potencia " + power.Power +
                                    " dBm aplicada a " + portPowers.Length + " puertos.";
                                logger.Info("Potencia actualizada en " + Ip + ": " +
                                    power.Power + " dBm en " + portPowers.Length + " puertos.");
                            }
                            catch (Exception ex)
                            {
                                Array.Copy(previous, portPowers, portPowers.Length);
                                try { ApplyPower(); } catch { }
                                result.Success = false;
                                result.Message = "No se pudo aplicar potencia: " + ex.Message;
                                logger.Error("Potencia en " + Ip + ": " + ex.Message);
                            }

                            powerResults.Enqueue(result);
                        }

                        GpoCommand gpo;
                        while (gpoQueue.TryDequeue(out gpo))
                        {
                            GpoResult result = new GpoResult();
                            result.IdComando = gpo.IdComando;
                            result.Gpo = gpo.Gpo;
                            result.Seconds = gpo.Seconds;

                            try
                            {
                                reader.SetGPO(result.Gpo, 1, result.Seconds);
                                result.Success = true;
                                result.Message = "Lector acepto pulso GPO " + result.Gpo +
                                    " por " + result.Seconds + " s.";
                                logger.Info("Salida GPO " + result.Gpo + " activa por " +
                                    gpo.Seconds + " segundos en " + Ip + ".");
                            }
                            catch (Exception ex)
                            {
                                result.Success = false;
                                result.Message = "Error GPO " + result.Gpo + ": " + ex.Message;
                                logger.Error("Salida GPO " + result.Gpo + " en " + Ip + ": " + ex.Message);
                            }

                            gpoResults.Enqueue(result);
                        }

                        foreach (AntennaConfig antenna in activeAntennas)
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
                foreach (AntennaConfig antenna in activeAntennas)
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

        private void ApplyPower()
        {
            if (reader == null) { throw new InvalidOperationException("No hay lector conectado."); }
            if (channels > 1) { reader.SetPortReadPower((int[])portPowers.Clone()); }
            else { reader.SetReaderPower(portPowers[0]); }
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
