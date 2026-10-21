using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace PortalAgent
{
    internal static class Program
    {
        private sealed class PendingConfig
        {
            public int Power;
            public int Remaining;
            public int Succeeded;
            public int Failed;
            public string Errors = "";
        }

        private sealed class PendingGpo
        {
            public int Gpo;
            public int Seconds;
            public int Remaining;
            public int Succeeded;
            public int Failed;
            public string Errors = "";
        }

        private sealed class LogSummary
        {
            public DateTime Since = DateTime.Now;
            public int Envios;
            public int Enviadas;
            public int Guardadas;
            public int Repetidas;
            public int Logs;
            public int SinDoc;
            public string Sesion = "-";
            public Queue<string> Ultimos = new Queue<string>();
        }

        private static volatile bool running = true;
        private static Mutex singleInstance;
        private static readonly Dictionary<int, PendingConfig> pendingConfigs =
            new Dictionary<int, PendingConfig>();
        private static readonly Dictionary<int, PendingGpo> pendingGpos =
            new Dictionary<int, PendingGpo>();
        private static readonly Dictionary<string, LogSummary> logSummaries =
            new Dictionary<string, LogSummary>();
        private static bool training;
        private static DateTime trainingStartedAt;
        private static DateTime trainingUntil;
        private static DateTime trainingLastNewAt;
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

                ProcessGpoResults(workers, api, logger);
                ProcessPowerResults(workers, api, logger);
                HandleTrainingFlags(workers, sessions, api, logger, config);
                if (training)
                {
                    TrainingTick(workers, api, logger, config);
                }
                else
                {
                    SendQueues(workers, sessions, sessionCodes, api, logger, lastSent,
                        config.DedupeSegundos, config.LogEpcs, config.ResumenLogSegundos);
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
                    else if (tipo == "DETENER_APRENDIZAJE")
                    {
                        if (!training ||
                            (trainingPortal != "" && trainingPortal != codigo))
                        {
                            AckCommand(api, idComando, "Error",
                                "No hay un aprendizaje en curso para este portal.");
                        }
                        else
                        {
                            int aprendidos = FinishTraining(workers, api, logger, config,
                                "detencion manual desde Marijoa");
                            AckCommand(api, idComando, "Ejecutado",
                                "Aprendizaje finalizado: " + aprendidos + " EPCs.");
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
                        gpo = Math.Max(1, Math.Min(4, gpo));
                        segundos = Math.Max(1, Math.Min(60, segundos));

                        List<ReaderWorker> objetivos = new List<ReaderWorker>();
                        foreach (ReaderWorker worker in workers.Values)
                        {
                            if (worker.Portal == codigo) { objetivos.Add(worker); }
                        }

                        if (objetivos.Count == 0)
                        {
                            AckCommand(api, idComando, "Error", "No se pudo encolar la salida GPO.");
                        }
                        else
                        {
                            PendingGpo pendiente = new PendingGpo();
                            pendiente.Gpo = gpo;
                            pendiente.Seconds = segundos;
                            pendiente.Remaining = objetivos.Count;
                            pendingGpos[idComando] = pendiente;

                            foreach (ReaderWorker worker in objetivos)
                            {
                                worker.EnqueueGpo(idComando, gpo, segundos);
                            }
                            logger.Info("Salida GPO " + gpo + " por " + segundos +
                                " s encolada para " + objetivos.Count + " lector(es) de " + codigo + ".");
                        }
                    }
                    else if (tipo == "CONFIGURAR")
                    {
                        int potencia = 20;
                        string payload = (string) comando["payload"];
                        if (!string.IsNullOrEmpty(payload))
                        {
                            try
                            {
                                JObject datos = JObject.Parse(payload);
                                if (datos["potencia"] != null) { potencia = (int) datos["potencia"]; }
                            }
                            catch { }
                        }
                        potencia = Math.Max(0, Math.Min(30, potencia));

                        List<ReaderWorker> objetivos = new List<ReaderWorker>();
                        foreach (ReaderWorker worker in workers.Values)
                        {
                            if (worker.Portal == codigo) { objetivos.Add(worker); }
                        }

                        if (objetivos.Count == 0)
                        {
                            AckCommand(api, idComando, "Error",
                                "No hay lector local para aplicar la potencia.");
                            continue;
                        }

                        if (pendingConfigs.ContainsKey(idComando)) { continue; }

                        PendingConfig pendiente = new PendingConfig();
                        pendiente.Power = potencia;
                        pendiente.Remaining = objetivos.Count;
                        pendingConfigs[idComando] = pendiente;

                        foreach (ReaderWorker worker in objetivos)
                        {
                            worker.EnqueuePower(idComando, potencia);
                        }
                        logger.Info("Potencia " + potencia + " dBm encolada para " +
                            objetivos.Count + " lector(es) de " + codigo + ".");
                    }
                    else if (tipo == "INICIAR_LOG" || tipo == "INICIAR_COBERTURA" ||
                        tipo == "DETENER_LOG")
                    {
                        bool activar = tipo != "DETENER_LOG";
                        bool cobertura = tipo == "INICIAR_COBERTURA";
                        bool encontrado = false;

                        foreach (ReaderWorker worker in workers.Values)
                        {
                            if (worker.Portal != codigo) { continue; }
                            worker.LiveLogEnabled = activar && !cobertura;
                            worker.CoverageEnabled = activar && cobertura;
                            worker.LiveLogUntil = activar
                                ? DateTime.Now.AddMinutes(3)
                                : DateTime.MinValue;
                            encontrado = true;
                        }

                        if (encontrado)
                        {
                            AckCommand(api, idComando, "Ejecutado",
                                activar
                                    ? (cobertura ? "Prueba de cobertura iniciada." :
                                        "Log en tiempo real iniciado.")
                                    : "Log en tiempo real detenido.");
                            logger.Info((activar
                                    ? (cobertura ? "Prueba de cobertura iniciada" : "Log en vivo iniciado")
                                    : "Log en vivo detenido") +
                                " para " + codigo + ".");
                        }
                        else
                        {
                            AckCommand(api, idComando, "Error", "No hay lector local para el log.");
                        }
                    }
                }
            }
        }

        private static void ProcessGpoResults(
            Dictionary<string, ReaderWorker> workers,
            ApiClient api,
            Logger logger)
        {
            List<int> terminados = new List<int>();

            foreach (ReaderWorker worker in workers.Values)
            {
                foreach (ReaderWorker.GpoResult result in worker.DrainGpoResults())
                {
                    PendingGpo pendiente;
                    if (!pendingGpos.TryGetValue(result.IdComando, out pendiente)) { continue; }

                    pendiente.Remaining--;
                    if (result.Success)
                    {
                        pendiente.Succeeded++;
                    }
                    else
                    {
                        pendiente.Failed++;
                        pendiente.Errors += (pendiente.Errors == "" ? "" : " | ") + result.Message;
                    }

                    if (pendiente.Remaining > 0) { continue; }

                    if (pendiente.Failed == 0)
                    {
                        AckCommand(api, result.IdComando, "Ejecutado",
                            "Lector acepto salida GPO " + pendiente.Gpo + " por " +
                            pendiente.Seconds + " s (" + pendiente.Succeeded + " lector/es).");
                    }
                    else
                    {
                        AckCommand(api, result.IdComando, "Error",
                            "Fallaron " + pendiente.Failed + " lector(es): " + pendiente.Errors);
                    }
                    terminados.Add(result.IdComando);
                }
            }

            foreach (int id in terminados) { pendingGpos.Remove(id); }
        }

        private static void ProcessPowerResults(
            Dictionary<string, ReaderWorker> workers,
            ApiClient api,
            Logger logger)
        {
            List<int> terminados = new List<int>();

            foreach (ReaderWorker worker in workers.Values)
            {
                foreach (ReaderWorker.PowerResult result in worker.DrainPowerResults())
                {
                    PendingConfig pendiente;
                    if (!pendingConfigs.TryGetValue(result.Id, out pendiente)) { continue; }

                    pendiente.Remaining--;
                    if (result.Success)
                    {
                        pendiente.Succeeded++;
                    }
                    else
                    {
                        pendiente.Failed++;
                        pendiente.Errors += (pendiente.Errors == "" ? "" : " | ") + result.Message;
                    }

                    if (pendiente.Remaining > 0) { continue; }

                    if (pendiente.Failed == 0)
                    {
                        AckCommand(api, result.Id, "Ejecutado",
                            "Potencia " + pendiente.Power + " dBm aplicada a " +
                            pendiente.Succeeded + " lector(es).");
                    }
                    else
                    {
                        AckCommand(api, result.Id, "Error",
                            "Fallaron " + pendiente.Failed + " lector(es): " + pendiente.Errors);
                    }
                    terminados.Add(result.Id);
                }
            }

            foreach (int id in terminados) { pendingConfigs.Remove(id); }
        }

        private static void SendQueues(
            Dictionary<string, ReaderWorker> workers,
            Dictionary<string, int> sessions,
            Dictionary<string, string> sessionCodes,
            ApiClient api,
            Logger logger,
            Dictionary<string, DateTime> lastSent,
            int dedupeSeconds,
            bool logEpcs,
            int summarySeconds)
        {
            Dictionary<string, List<Reading>> batches = new Dictionary<string, List<Reading>>();
            Dictionary<string, List<Reading>> debugBatches = new Dictionary<string, List<Reading>>();
            Dictionary<string, Reading> debugUnicos = new Dictionary<string, Reading>();

            foreach (ReaderWorker worker in workers.Values)
            {
                List<Reading> readings = worker.Drain();
                foreach (Reading reading in readings)
                {
                    if ((worker.LiveLogEnabled || worker.CoverageEnabled) &&
                        DateTime.Now >= worker.LiveLogUntil)
                    {
                        worker.LiveLogEnabled = false;
                        worker.CoverageEnabled = false;
                        logger.Info("Log en vivo cerrado automaticamente por limite de 3 minutos: " +
                            worker.Portal + " (" + worker.Ip + ").");
                    }
                    if ((worker.CoverageEnabled ||
                        (worker.LiveLogEnabled && !worker.IsBackground(reading.Epc))))
                    {
                        string debugKey = worker.Portal + "|" + reading.Epc;
                        Reading debug;
                        if (debugUnicos.TryGetValue(debugKey, out debug))
                        {
                            debug.Repeticiones++;
                        }
                        else
                        {
                            reading.Repeticiones = 1;
                            debugUnicos[debugKey] = reading;
                            if (!debugBatches.ContainsKey(worker.Portal))
                            {
                                debugBatches[worker.Portal] = new List<Reading>();
                            }
                            debugBatches[worker.Portal].Add(reading);
                        }
                    }

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

            foreach (KeyValuePair<string, List<Reading>> debugBatch in debugBatches)
            {
                List<Reading> debugAll = debugBatch.Value;
                for (int offset = 0; offset < debugAll.Count; offset += 100)
                {
                    int count = Math.Min(100, debugAll.Count - offset);
                    List<Reading> debugChunk = debugAll.GetRange(offset, count);
                    try
                    {
                        api.SendDebugReadings(debugBatch.Key, debugChunk);
                    }
                    catch (Exception ex)
                    {
                        logger.Error("Log en vivo " + debugBatch.Key + ": " + ex.Message);
                    }
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
                        UpdateLogSummary(portal, codigoSesion, chunk, result);
                    }
                    catch (Exception ex)
                    {
                        logger.Error("Envio de lecturas " + portal + ": " + ex.Message);
                    }
                }
            }

            FlushLogSummaries(logger, summarySeconds);
        }

        private static void UpdateLogSummary(
            string portal,
            string sessionCode,
            List<Reading> readings,
            JObject result)
        {
            LogSummary summary;
            if (!logSummaries.TryGetValue(portal, out summary))
            {
                summary = new LogSummary();
                logSummaries[portal] = summary;
            }

            summary.Envios++;
            summary.Enviadas += readings.Count;
            summary.Guardadas += (int) result["guardados"];
            summary.Repetidas += (int) result["repetidos"];
            summary.Logs += (int) result["logs"];
            summary.SinDoc += (int) result["sin_doc"];
            summary.Sesion = string.IsNullOrEmpty(sessionCode) ? "-" : sessionCode;

            foreach (Reading reading in readings)
            {
                string lote = reading.Lote;
                if (summary.Ultimos.Contains(lote)) { continue; }
                summary.Ultimos.Enqueue(lote);
                while (summary.Ultimos.Count > 3) { summary.Ultimos.Dequeue(); }
            }
        }

        private static void FlushLogSummaries(Logger logger, int summarySeconds)
        {
            if (summarySeconds <= 0) { summarySeconds = 60; }
            DateTime now = DateTime.Now;

            foreach (KeyValuePair<string, LogSummary> item in logSummaries)
            {
                LogSummary summary = item.Value;
                if ((now - summary.Since).TotalSeconds < summarySeconds) { continue; }

                if (summary.Enviadas > 0)
                {
                    logger.Info("Resumen " + item.Key + " | " +
                        Math.Max(1, (int)(now - summary.Since).TotalSeconds) + "s" +
                        " | sesion " + summary.Sesion +
                        " | envios " + summary.Envios +
                        " | enviadas " + summary.Enviadas +
                        " | guardadas " + summary.Guardadas +
                        " | repetidas " + summary.Repetidas +
                        " | logs " + summary.Logs +
                        " | sin_doc " + summary.SinDoc +
                        " | ultimos " + string.Join(",", summary.Ultimos.ToArray()));
                }

                summary.Since = now;
                summary.Envios = 0;
                summary.Enviadas = 0;
                summary.Guardadas = 0;
                summary.Repetidas = 0;
                summary.Logs = 0;
                summary.SinDoc = 0;
                summary.Sesion = "-";
                summary.Ultimos.Clear();
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
            trainingStartedAt = DateTime.Now;
            trainingLastNewAt = trainingStartedAt;
            trainingUntil = DateTime.Now.AddSeconds(config.FondoSegundos);
            logger.Info("Aprendiendo fondo de " + (trainingPortal == "" ? "todos los portales" : trainingPortal) +
                ". Esperando cierre manual; maximo " + config.FondoSegundos +
                " s. La puerta debe estar vacia.");
            return true;
        }

        private static void TrainingTick(
            Dictionary<string, ReaderWorker> workers,
            ApiClient api,
            Logger logger,
            AgentConfig config)
        {
            Dictionary<string, List<Reading>> debugBatches = new Dictionary<string, List<Reading>>();
            Dictionary<string, Reading> debugUnicos = new Dictionary<string, Reading>();

            foreach (ReaderWorker worker in workers.Values)
            {
                if (trainingPortal != "" && worker.Portal != trainingPortal) { continue; }
                List<Reading> readings = worker.Drain();
                foreach (Reading reading in readings)
                {
                    string key = worker.Portal + "|" + worker.Ip + "|" + reading.Epc;
                    int count;
                    bool nuevo = !trainingCounts.TryGetValue(key, out count);
                    trainingCounts[key] = count + 1;
                    if (nuevo) { trainingLastNewAt = DateTime.Now; }

                    string debugKey = worker.Portal + "|" + reading.Epc;
                    Reading debug;
                    if (debugUnicos.TryGetValue(debugKey, out debug))
                    {
                        debug.Repeticiones++;
                    }
                    else
                    {
                        reading.Repeticiones = 1;
                        debugUnicos[debugKey] = reading;
                        if (!debugBatches.ContainsKey(worker.Portal))
                        {
                            debugBatches[worker.Portal] = new List<Reading>();
                        }
                        debugBatches[worker.Portal].Add(reading);
                    }
                }
            }

            foreach (KeyValuePair<string, List<Reading>> batch in debugBatches)
            {
                try
                {
                    api.SendDebugReadings(batch.Key, batch.Value);
                }
                catch (Exception ex)
                {
                    logger.Error("Progreso de aprendizaje " + batch.Key + ": " + ex.Message);
                }
            }

            DateTime ahora = DateTime.Now;
            bool sinNuevos = config.FondoSilencioSegundos > 0 &&
                (ahora - trainingLastNewAt).TotalSeconds >= config.FondoSilencioSegundos;
            bool maximo = ahora >= trainingUntil;
            if (!sinNuevos && !maximo) { return; }

            FinishTraining(workers, api, logger, config,
                maximo ? "limite maximo de " + config.FondoSegundos + " s" :
                    config.FondoSilencioSegundos + " s sin EPCs nuevos");
        }

        private static int FinishTraining(
            Dictionary<string, ReaderWorker> workers,
            ApiClient api,
            Logger logger,
            AgentConfig config,
            string motivo)
        {
            training = false;
            int totalLearned = 0;
            DateTime ahora = DateTime.Now;
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
                    "Fondo aprendido: " + totalLearned + " EPCs en " +
                    Math.Max(1, (int)(ahora - trainingStartedAt).TotalSeconds) + " s.");
            }

            logger.Info("Aprendizaje finalizado por " + motivo + ".");

            trainingCounts.Clear();
            trainingPortal = "";
            trainingCommandId = 0;
            return totalLearned;
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
