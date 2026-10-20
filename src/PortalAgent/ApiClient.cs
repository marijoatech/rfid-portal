using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PortalAgent
{
    public sealed class ApiClient
    {
        private readonly AgentConfig config;
        private readonly Logger logger;

        public ApiClient(AgentConfig config, Logger logger)
        {
            this.config = config;
            this.logger = logger;
            ServicePointManager.Expect100Continue = false;
        }

        public JObject Heartbeat(List<string> connectedReaders)
        {
            NameValueCollection data = new NameValueCollection();
            data["accion"] = "heartbeat";
            data["agente"] = config.Agente;
            data["token"] = config.Token;
            data["lectores"] = JsonConvert.SerializeObject(
                connectedReaders == null ? new List<string>() : connectedReaders);
            return Post(data);
        }

        public JObject SendReadings(string portal, int idSesion, List<Reading> readings)
        {
            JArray array = new JArray();
            foreach (Reading reading in readings)
            {
                JObject item = new JObject();
                item["epc"] = reading.Epc;
                item["lote"] = reading.Lote;
                item["ant"] = reading.Ant;
                item["rssi"] = reading.Rssi;
                item["fecha"] = reading.SeenAt.ToString("yyyy-MM-dd HH:mm:ss");
                array.Add(item);
            }

            NameValueCollection data = new NameValueCollection();
            data["accion"] = "lecturas";
            data["agente"] = config.Agente;
            data["token"] = config.Token;
            data["portal"] = portal;
            data["id_sesion"] = idSesion.ToString();
            data["lecturas"] = array.ToString(Formatting.None);
            return Post(data);
        }

        public JObject SetBackground(string portal, int idLector, List<string> epcs, string modo)
        {
            NameValueCollection data = new NameValueCollection();
            data["accion"] = "fondo";
            data["agente"] = config.Agente;
            data["token"] = config.Token;
            data["portal"] = portal;
            data["id_lector"] = idLector.ToString();
            data["modo"] = modo;
            data["epcs"] = JsonConvert.SerializeObject(epcs);
            return Post(data);
        }

        public JObject Ack(int idComando, string estado, string resultado)
        {
            NameValueCollection data = new NameValueCollection();
            data["accion"] = "ack";
            data["agente"] = config.Agente;
            data["token"] = config.Token;
            data["id_comando"] = idComando.ToString();
            data["estado"] = estado;
            data["resultado"] = resultado;
            return Post(data);
        }

        private JObject Post(NameValueCollection data)
        {
            using (WebClient client = new WebClient())
            {
                client.Encoding = Encoding.UTF8;
                client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                byte[] response = client.UploadValues(config.MarijoaUrl, "POST", data);
                string text = Encoding.UTF8.GetString(response);
                JObject result = JObject.Parse(text);
                string estado = (string) result["estado"];
                if (estado != "Ok")
                {
                    string mensaje = (string) result["mensaje"];
                    throw new InvalidOperationException("API: " + (string.IsNullOrEmpty(mensaje) ? text : mensaje));
                }
                return result;
            }
        }
    }
}
