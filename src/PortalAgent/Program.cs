using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace PortalAgent
{
    internal static class Program
    {
        private static volatile bool running = true;
        private static Mutex singleInstance;
        private static bool training;
        private static DateTime trainingUntil;
        private static string trainingPortal = "";
        private static int trainingCommandId;
        private static readonly Dictionary<string, int> trainingCounts = new Dictionary<string, int>();

        private static void Main(string[] args)
        {
            Console.Title = "Portal RFID Agent - modo consola";
            Console.SetOut(new ConsoleFilter(Console.Out));

            AgentConfig config;
            Logger logger;
            try
            {
                config = AgentConfig.Load();
                logger = new Logger(config.LogArchivo);
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR al cargar agent.json: " + ex.Message);
                Console.WriteLine("Crea agent.json junto al ejecutable con MarijoaUrl, Token y Agente.");
                Environment.ExitCode = 1;
                return;
            }

            logger.Info("Portal Agent iniciado. Agente=" + config.Agente +
                " | Marijoa=" + config.MarijoaUrl +
                " | Lectores=" + (config.Lectores.Length == 0 ? "todos" : string.Join(",", config.Lectores)));

            bool createdNew;
            singleInstance = new Mutex(true, "Local\\PortalAgent-" + config.Agente, out createdNew);
            if (!createdNew)
            {
                Console.WriteLine("Ya hay un PortalAgent corriendo para " + config.Agente + ".");
                return;
            }

            ApiClient api = new ApiClient(config, logger);
            Dictionary<string, ReaderWorker> workers = new Dictionary<string, ReaderWorker>();
            Dictionary<string, int> sessions = new Dictionary<string, int>();
            Dictionary<string, string> sessionCodes = new Dictionary<string, string>();
            Dictionary<string, DateTime> lastSent = new Dictionary<string, DateTime>();

            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                running = false;
            };

            DateTime nextHeartbeat = DateTime.MinValue;

            while (running)
            {
                if (DateTime.Now >= nextHeartbeat)
                {
                    try
                    {
                        List<string> conectados = new List<string>();
                        foreach (ReaderWorker worker in workers.Values)
                        {
                            if (worker.IsConnected) { conectados.Add(worker.Ip); }
                        }
                        JObject heartbeat = api.Heartbeat(conectados);
                        ProcessHeartbeat(heartbeat, config, workers, sessions, sessionCodes, api, logger);
                        nextHeartbeat = DateTime.Now.AddSeconds(config.PollSegundos);
                    }
                    catch (Exception ex)
                    {
                        logger.Error("Heartbeat: " + ex.Message);
                        nextHeartbeat = DateTime.Now.AddSeconds(5);
                    }
                }

                HandleTrainingFlags(workers, sessions, api, logger, config);
                if (training)
                {
                    TrainingTick(workers, api, logger, config);
                }
                else
                {
                    SendQueues(workers, sessions, sessionCodes, api, logger, lastSent,
                        config.DedupeSegundos, config.LogEpcs);
                }
                Thread.Sleep(1000);
            }

            foreach (ReaderWorker worker in workers.Values)
            {
                try { worker.Stop(); } catch { }
            }
            logger.Info("Portal Agent detenido.");
            Console.Out.Flush();
        }

        private static void ProcessHeartbeat(
            JObject heartbeat,
            AgentConfig config,
            Dictionary<string, ReaderWorker> workers,
            Dictionary<string, int> sessions,
            Dictionary<string, string> sessionCodes,
            ApiClient api,
            Logger logger)
        {
            JArray portales = heartbeat["portales"] as JArray;
            HashSet<string> desired = new HashSet<string>();
            Dictionary<int, string> portalCodes = new Dictionary<int, string>();

            if (portales == null)
            {
                logger.Error("La API no devolvio portales.");
                return;
            }

            foreach (JObject portal in portales)
            {
                string codigo = (string) portal["codigo"];
                portalCodes[(int) portal["id_portal"]] = codigo;
                JObject sesion = portal["sesion"] as JObject;
                int idSesion = 0;
                string codigoSesion = "";

                if (sesion != null)
                {
                    idSesion = (int) sesion["id_sesion"];
                    codigoSesion = (string) sesion["codigo"];
                }

                int anterior = sessions.ContainsKey(codigo) ? sessions[codigo] : 0;
                if (idSesion > 0)
                {
                    sessions[codigo] = idSesion;
                    sessionCodes[codigo] = codigoSesion;
                    if (anterior != idSesion)
                    {
                        logger.Info("Sesion armada: " + codigoSesion + " en portal " + codigo + ".");
                    }
                }
                else
                {
                    sessions.Remove(codigo);
                    sessionCodes.Remove(codigo);
                    if (anterior > 0)
                    {
                        logger.Info("Portal " + codigo + " sin sesion armada.");
                    }
                }

                JArray lectores = portal["lectores"] as JArray;
                if (lectores == null) { continue; }

                foreach (JObject lector in lectores)
                {
                    string ip = (string) lector["ip"];
                    if (!IsConfigured(ip, config.Lectores)) { continue; }

                    string key = codigo + "|" + ip;
                    desired.Add(key);
                    List<string> fondo = new List<string>();
                    JArray fondoRows = lector["fondo"] as JArray;
                    if (fondoRows != null)
                    {
                        foreach (JToken item in fondoRows)
                        {
                            string epc = (string)item;
                            if (!string.IsNullOrEmpty(epc)) { fondo.Add(epc); }
                        }
                    }

                    if (workers.ContainsKey(key))
                    {
                        workers[key].SetBackground(fondo);
                        continue;
                    }

                    int idLector = (int) lector["id_lector"];
                    int canales = (int) lector["canales"];
                    JArray antenas = lector["antenas"] as JArray;
                    ReaderWorker worker = new ReaderWorker(codigo, ip, idLector, canales, antenas, logger);
                    worker.SetBackground(fondo);
                    workers[key] = worker;
                    worker.Start();
                    logger.Info("Agregado lector " + ip + " para portal " + codigo + ".");
                }
            }

            List<string> remove = new List<string>();
            foreach (string key in workers.Keys)
            {
                if (!desired.Contains(key)) { remove.Add(key); }
            }
            foreach (string key in remove)
            {
                ReaderWorker worker = workers[key];
                try { worker.Stop(); } catch { }
                workers.Remove(key);
                logger.Info("Lector retirado: " + worker.Ip + " (" + worker.Portal + ").");
            }

            JArray comandos = heartbeat["comandos"] as JArray;
            if (comandos != null)
            {
                foreach (JObject comando in comandos)
                {
                    string tipo = (string) comando["tipo"];
                    int idComando = (int) comando["id_comando"];
                    int idPortal = (int) comando["id_portal"];
                    string codigo = portalCodes.ContainsKey(idPortal) ? portalCodes[idPortal] : "";
                    logger.Info("Comando recibido: " + tipo + " #" + idComando +
                        (codigo == "" ? "" : " para " + codigo) + ".");

                    if (tipo == "APRENDER_FONDO")
                    {
                        if (codigo == "" || !HasWorkerForPortal(workers, codigo))
                        {
                            AckCommand(api, idComando, "Error", "No hay lector local para el portal.");
                        }
                        else if (sessions.ContainsKey(codigo))
                        {
                            AckCommand(api, idComando, "Error", "Cierre la sesion antes de aprender fondo.");
                        }
                        else if (StartTraining(workers, logger, config, codigo))
                        {
                            trainingCommandId = idComando;
                        }
                        else
                        {
                            AckCommand(api, idComando, "Error", "Ya hay un entrenamiento en curso.");
                        }
                    }
                    else if (tipo == "LIMPIAR_FONDO")
                    {
                        if (codigo == "" || !HasWorkerForPortal(workers, codigo))
                        {
                            AckCommand(api, idComando, "Error", "No hay lector local para el portal.");
                        }
                        else
                        {
                            ClearBackground(workers, api, logger, codigo);
                            AckCommand(api, idComando, "Ejecutado", "Fondo limpiado.");
                        }
                    }
                    else if (tipo == "PROBAR_SIRENA")
                    {
                        if (codigo == "" || !HasWorkerForPortal(workers, codigo))
                        {
                            AckCommand(api, idComando, "Error", "No hay lector local para el portal.");
                            continue;
                        }

                        int gpo = 1;
                        int segundos = 3;
                        string payload = (string) comando["payload"];
                        if (!string.IsNullOrEmpty(payload))
                        {
                            try
                            {
                                JObject datos = JObject.Parse(payload);
                                if (datos["gpo"] != null) { gpo = (int) datos["gpo"]; }
                                if (datos["segundos"] != null) { segundos = (int) datos["segundos"]; }
                            }
                            catch { }
                        }
                        gpo = Math.Max(1, Math.Min(3, gpo));
                        segundos = Math.Max(1, Math.Min(60, segundos));

                        bool enviado = false;
                        foreach (ReaderWorker worker in workers.Values)
                        {
                            if (worker.Portal == codigo)
                            {
                                worker.EnqueueGpo(gpo, segundos);
                                enviado = true;
                            }
                        }

                        if (enviado)
                        {
                            AckCommand(api, idComando, "Ejecutado",
                                "Sirena GPO " + gpo + " por " + segundos + " s.");
                        }
                        else
                        {
                            AckCommand(api, idComando, "Error", "No se pudo encolar la sirena.");
                        }
                    }
                }
            }
        }

        private static void SendQueues(
            Dictionary<string, ReaderWorker> workers,
            Dictionary<string, int> sessions,
            Dictionary<string, string> sessionCodes,
            ApiClient api,
            Logger logger,
            Dictionary<string, DateTime> lastSent,
            int dedupeSeconds,
            bool logEpcs)
        {
            Dictionary<string, List<Reading>> batches = new Dictionary<string, List<Reading>>();

            foreach (ReaderWorker worker in workers.Values)
            {
                List<Reading> readings = worker.Drain();
                foreach (Reading reading in readings)
                {
                    // Con una sesion armada se envia todo: el servidor decide si
                    // el EPC es fondo o si pertenece al documento. Asi una pieza
                    // aprendida como fondo no queda bloqueada si luego sale.
                    if (!sessions.ContainsKey(worker.Portal) && worker.IsBackground(reading.Epc))
                    {
                        continue;
                    }

                    string key = worker.Portal + "|" + reading.Epc;
                    DateTime last;
                    if (lastSent.TryGetValue(key, out last) &&
                        (DateTime.Now - last).TotalSeconds < dedupeSeconds)
                    {
                        continue;
                    }
                    lastSent[key] = DateTime.Now;

                    if (!batches.ContainsKey(worker.Portal))
                    {
                        batches[worker.Portal] = new List<Reading>();
                    }
                    batches[worker.Portal].Add(reading);
                }
            }

            foreach (KeyValuePair<string, List<Reading>> batch in batches)
            {
                string portal = batch.Key;
                List<Reading> all = batch.Value;
                int idSesion = sessions.ContainsKey(portal) ? sessions[portal] : 0;
                string codigoSesion = sessionCodes.ContainsKey(portal) ? sessionCodes[portal] : "-";

                for (int offset = 0; offset < all.Count; offset += 100)
                {
                    int count = Math.Min(100, all.Count - offset);
                    List<Reading> chunk = all.GetRange(offset, count);
                    if (logEpcs)
                    {
                        foreach (Reading reading in chunk)
                        {
                            logger.Info("Lectura " + portal +
                                " | EPC=" + reading.Epc +
                                " | lote=" + reading.Lote +
                                " | ant=" + reading.Ant +
                                " | rssi=" + reading.Rssi);
                        }
                    }
                    try
                    {
                        JObject result = api.SendReadings(portal, idSesion, chunk);
                        logger.Info("Portal " + portal + " | sesion " + codigoSesion +
                            " | enviadas " + chunk.Count +
                            " | guardadas " + (int) result["guardados"] +
                            " | repetidas " + (int) result["repetidos"] +
                            " | logs " + (int) result["logs"] +
                            " | sin_doc " + (int) result["sin_doc"]);
                    }
                    catch (Exception ex)
                    {
                        logger.Error("Envio de lecturas " + portal + ": " + ex.Message);
                    }
                }
            }
        }

        private static bool IsConfigured(string ip, string[] configured)
        {
            if (configured == null || configured.Length == 0) { return true; }
            foreach (string item in configured)
            {
                if (string.Equals((item ?? "").Trim(), (ip ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string FlagPath(string name)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
        }

        private static void HandleTrainingFlags(
            Dictionary<string, ReaderWorker> workers,
            Dictionary<string, int> sessions,
            ApiClient api,
            Logger logger,
            AgentConfig config)
        {
            string clearFlag = FlagPath("limpiar-fondo.flag");
            if (File.Exists(clearFlag))
            {
                try { File.Delete(clearFlag); } catch { }
                ClearBackground(workers, api, logger, "");
            }

            string trainFlag = FlagPath("entrenar-fondo.flag");
            if (training || !File.Exists(trainFlag)) { return; }

            try { File.Delete(trainFlag); } catch { }

            if (sessions.Count > 0)
            {
                logger.Error("No se puede aprender fondo con una sesion armada. Cierre la sesion y repita.");
                return;
            }

            if (!StartTraining(workers, logger, config, ""))
            {
                logger.Error("No hay lector disponible para aprender el fondo.");
            }
        }

        private static bool StartTraining(
            Dictionary<string, ReaderWorker> workers,
            Logger logger,
            AgentConfig config,
            string portal)
        {
            if (training) { return false; }

            bool found = false;
            foreach (ReaderWorker worker in workers.Values)
            {
                if (portal != "" && worker.Portal != portal) { continue; }
                worker.Drain();
                found = true;
            }
            if (!found) { return false; }

            trainingCounts.Clear();
            trainingPortal = portal ?? "";
            trainingCommandId = 0;
            training = true;
            trainingUntil = DateTime.Now.AddSeconds(config.FondoSegundos);
            logger.Info("Aprendiendo fondo de " + (trainingPortal == "" ? "todos los portales" : trainingPortal) +
                " durante " + config.FondoSegundos +
                " segundos. La puerta debe estar vacia.");
            return true;
        }

        private static void TrainingTick(
            Dictionary<string, ReaderWorker> workers,
            ApiClient api,
            Logger logger,
            AgentConfig config)
        {
            foreach (ReaderWorker worker in workers.Values)
            {
                if (trainingPortal != "" && worker.Portal != trainingPortal) { continue; }
                List<Reading> readings = worker.Drain();
                foreach (Reading reading in readings)
                {
                    string key = worker.Portal + "|" + worker.Ip + "|" + reading.Epc;
                    int count;
                    trainingCounts.TryGetValue(key, out count);
                    trainingCounts[key] = count + 1;
                }
            }

            if (DateTime.Now < trainingUntil) { return; }

            training = false;
            int totalLearned = 0;
            foreach (ReaderWorker worker in workers.Values)
            {
                if (trainingPortal != "" && worker.Portal != trainingPortal) { continue; }
                string prefix = worker.Portal + "|" + worker.Ip + "|";
                List<string> learned = new List<string>();
                foreach (KeyValuePair<string, int> item in trainingCounts)
                {
                    if (!item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { continue; }
                    if (item.Value < config.FondoMinLecturas) { continue; }
                    learned.Add(item.Key.Substring(prefix.Length));
                }

                try
                {
                    api.SetBackground(worker.Portal, worker.IdLector, learned, "reemplazar");
                    worker.SetBackground(learned);
                    totalLearned += learned.Count;
                    logger.Info("Fondo de " + worker.Ip + ": " + learned.Count +
                        " EPCs aprendidos (minimo " + config.FondoMinLecturas + " lecturas).");
                }
                catch (Exception ex)
                {
                    logger.Error("No se pudo guardar el fondo de " + worker.Ip + ": " + ex.Message);
                }
            }

            if (trainingCommandId > 0)
            {
                AckCommand(api, trainingCommandId, "Ejecutado",
                    "Fondo aprendido: " + totalLearned + " EPCs.");
            }

            trainingCounts.Clear();
            trainingPortal = "";
            trainingCommandId = 0;
        }

        private static void ClearBackground(
            Dictionary<string, ReaderWorker> workers,
            ApiClient api,
            Logger logger,
            string portal)
        {
            training = false;
            trainingPortal = "";
            trainingCommandId = 0;
            trainingCounts.Clear();

            foreach (ReaderWorker worker in workers.Values)
            {
                if (portal != "" && worker.Portal != portal) { continue; }
                try
                {
                    api.SetBackground(worker.Portal, worker.IdLector, new List<string>(), "limpiar");
                    worker.SetBackground(new string[0]);
                    logger.Info("Fondo limpiado para " + worker.Ip + ".");
                }
                catch (Exception ex)
                {
                    logger.Error("No se pudo limpiar el fondo de " + worker.Ip + ": " + ex.Message);
                }
            }
        }

        private static bool HasWorkerForPortal(
            Dictionary<string, ReaderWorker> workers,
            string portal)
        {
            foreach (ReaderWorker worker in workers.Values)
            {
                if (worker.Portal == portal) { return true; }
            }
            return false;
        }

        private static void AckCommand(
            ApiClient api,
            int idComando,
            string estado,
            string resultado)
        {
            try
            {
                if (idComando > 0) { api.Ack(idComando, estado, resultado); }
            }
            catch { }
        }
    }
}
