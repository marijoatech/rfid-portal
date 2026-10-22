using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Threading;
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

        public JObject SendDebugReadings(string portal, List<Reading> readings)
        {
            JArray array = new JArray();
            foreach (Reading reading in readings)
            {
                JObject item = new JObject();
                item["epc"] = reading.Epc;
                item["lote"] = reading.Lote;
                item["ant"] = reading.Ant;
                item["rssi"] = reading.Rssi;
                item["repeticiones"] = reading.Repeticiones;
                item["fecha"] = reading.SeenAt.ToString("yyyy-MM-dd HH:mm:ss");
                array.Add(item);
            }

            NameValueCollection data = new NameValueCollection();
            data["accion"] = "lecturas_debug";
            data["agente"] = config.Agente;
            data["token"] = config.Token;
            data["portal"] = portal;
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
            WebException lastNetworkError = null;

            for (int intento = 0; intento < 2; intento++)
            {
                try
                {
                    using (NoKeepAliveWebClient client = new NoKeepAliveWebClient())
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
                            throw new InvalidOperationException("API: " +
                                (string.IsNullOrEmpty(mensaje) ? text : mensaje));
                        }
                        return result;
                    }
                }
                catch (WebException ex)
                {
                    lastNetworkError = ex;
                    if (intento == 0) { Thread.Sleep(300); }
                }
            }

            if (lastNetworkError == null)
            {
                throw new InvalidOperationException("No se pudo completar la solicitud a Marijoa.");
            }

            StringBuilder detalle = new StringBuilder();
            detalle.Append(lastNetworkError.Status);
            HttpWebResponse httpResponse = lastNetworkError.Response as HttpWebResponse;
            if (httpResponse != null)
            {
                detalle.Append(" | HTTP ");
                detalle.Append((int)httpResponse.StatusCode);
                detalle.Append(" ");
                detalle.Append(httpResponse.StatusDescription);
            }
            if (lastNetworkError.InnerException != null)
            {
                detalle.Append(" | ");
                detalle.Append(lastNetworkError.InnerException.Message);
            }
            else if (!string.IsNullOrEmpty(lastNetworkError.Message))
            {
                detalle.Append(" | ");
                detalle.Append(lastNetworkError.Message);
            }

            throw new InvalidOperationException("No se pudo conectar con Marijoa: " + detalle);
        }

        private sealed class NoKeepAliveWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                HttpWebRequest http = request as HttpWebRequest;
                if (http != null)
                {
                    http.KeepAlive = false;
                    http.Timeout = 15000;
                    http.ReadWriteTimeout = 15000;
                }
                return request;
            }
        }
    }
}
