using System;
using System.IO;
using Newtonsoft.Json;

namespace PortalAgent
{
    public sealed class AgentConfig
    {
        public string Agente { get; set; }
        public string MarijoaUrl { get; set; }
        public string Token { get; set; }
        public string[] Lectores { get; set; }
        public int PollSegundos { get; set; }
        public int DedupeSegundos { get; set; }
        public bool LogEpcs { get; set; }
        public int ResumenLogSegundos { get; set; }
        public int FondoSegundos { get; set; }
        public int FondoSilencioSegundos { get; set; }
        public int FondoMinLecturas { get; set; }
        public string LogArchivo { get; set; }

        public static AgentConfig Load()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agent.json");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Falta agent.json junto al ejecutable.", path);
            }

            AgentConfig config = JsonConvert.DeserializeObject<AgentConfig>(File.ReadAllText(path));
            if (config == null) { throw new InvalidOperationException("agent.json esta vacio o es invalido."); }

            if (string.IsNullOrEmpty(config.Agente)) { config.Agente = "AGENTE-PORTONES-1"; }
            if (string.IsNullOrEmpty(config.MarijoaUrl))
            {
                throw new InvalidOperationException("agent.json: falta MarijoaUrl.");
            }
            if (string.IsNullOrEmpty(config.Token))
            {
                throw new InvalidOperationException("agent.json: falta Token.");
            }
            if (config.PollSegundos <= 0) { config.PollSegundos = 2; }
            if (config.DedupeSegundos <= 0) { config.DedupeSegundos = 60; }
            if (config.ResumenLogSegundos <= 0) { config.ResumenLogSegundos = 60; }
            if (config.FondoSegundos <= 0) { config.FondoSegundos = 180; }
            if (config.FondoSilencioSegundos < 0) { config.FondoSilencioSegundos = 0; }
            if (config.FondoMinLecturas <= 0) { config.FondoMinLecturas = 2; }
            if (string.IsNullOrEmpty(config.LogArchivo)) { config.LogArchivo = "logs/portal-agent.log"; }

            if (config.Lectores == null)
            {
                config.Lectores = new string[0];
            }

            for (int i = 0; i < config.Lectores.Length; i++)
            {
                config.Lectores[i] = (config.Lectores[i] ?? "").Trim();
            }

            return config;
        }
    }
}
