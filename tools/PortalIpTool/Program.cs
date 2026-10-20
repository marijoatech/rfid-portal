using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using SReaderAPI;

namespace PortalRfidIpTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new IpToolForm());
        }
    }

    internal sealed class FoundReader
    {
        public string Ip;
        public string Mask;
        public string Gateway;
        public string Mac;

        public override string ToString()
        {
            string gw = string.IsNullOrEmpty(Gateway) ? "-" : Gateway;
            return Ip + "   (mascara " + Mask + ", gateway " + gw + ", MAC " + Mac + ")";
        }
    }

    internal sealed class IpToolForm : Form
    {
        private TextBox txtCurrentIp;
        private TextBox txtNewIp;
        private TextBox txtMask;
        private TextBox txtGateway;
        private Button btnTest;
        private Button btnRead;
        private Button btnChange;
        private Button btnSearch;
        private Button btnNet;
        private Button btnClose;
        private ListBox lstFound;
        private TextBox txtLog;
        private Label lblStatus;
        private bool busy;

        public IpToolForm()
        {
            Text = "Portal RFID - Configurar IP del lector";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(700, 620);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9f);
            BuildUi();
        }

        private void BuildUi()
        {
            Label title = new Label();
            title.Text = "Configuracion de red del lector RFID";
            title.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            title.SetBounds(16, 12, 660, 28);
            Controls.Add(title);

            Label help = new Label();
            help.Text = "Para el primer contacto, la notebook debe estar en la misma red que el lector.\r\n" +
                "Ejemplo: IP 192.168.1.100 / mascara 255.255.255.0 (gateway vacio). Conecta el cable directo al lector o al switch.\r\n" +
                "El lector de fabrica viene en 192.168.1.136. Despues de cambiar la IP hay que apagar y volver a encender el lector.";
            help.ForeColor = Color.DimGray;
            help.SetBounds(16, 42, 668, 58);
            Controls.Add(help);

            GroupBox grpConn = new GroupBox();
            grpConn.Text = "Conexion con el lector";
            grpConn.SetBounds(16, 104, 668, 78);
            Controls.Add(grpConn);

            Label lblCurrent = new Label();
            lblCurrent.Text = "IP actual:";
            lblCurrent.SetBounds(14, 31, 70, 20);
            grpConn.Controls.Add(lblCurrent);

            txtCurrentIp = new TextBox();
            txtCurrentIp.Text = "192.168.1.136";
            txtCurrentIp.SetBounds(88, 28, 150, 23);
            grpConn.Controls.Add(txtCurrentIp);

            btnTest = new Button();
            btnTest.Text = "Probar conexion";
            btnTest.SetBounds(258, 26, 130, 28);
            btnTest.Click += delegate
            {
                string ip = txtCurrentIp.Text.Trim();
                RunReaderOp("Probando conexion con " + ip + "...", delegate { TestConnection(ip); });
            };
            grpConn.Controls.Add(btnTest);

            btnRead = new Button();
            btnRead.Text = "Leer IP actual";
            btnRead.SetBounds(398, 26, 130, 28);
            btnRead.Click += delegate
            {
                string ip = txtCurrentIp.Text.Trim();
                RunReaderOp("Leyendo configuracion del lector...", delegate { ReadCurrentIp(ip); });
            };
            grpConn.Controls.Add(btnRead);

            GroupBox grpNew = new GroupBox();
            grpNew.Text = "Nueva configuracion de red";
            grpNew.SetBounds(16, 190, 668, 142);
            Controls.Add(grpNew);

            Label lblNew = new Label();
            lblNew.Text = "Nueva IP:";
            lblNew.SetBounds(14, 31, 90, 20);
            grpNew.Controls.Add(lblNew);

            txtNewIp = new TextBox();
            txtNewIp.Text = "192.168.3.136";
            txtNewIp.SetBounds(112, 28, 150, 23);
            grpNew.Controls.Add(txtNewIp);

            Label lblMask = new Label();
            lblMask.Text = "Mascara:";
            lblMask.SetBounds(14, 65, 90, 20);
            grpNew.Controls.Add(lblMask);

            txtMask = new TextBox();
            txtMask.Text = "255.255.255.0";
            txtMask.SetBounds(112, 62, 150, 23);
            grpNew.Controls.Add(txtMask);

            Label lblGate = new Label();
            lblGate.Text = "Gateway:";
            lblGate.SetBounds(14, 99, 90, 20);
            grpNew.Controls.Add(lblGate);

            txtGateway = new TextBox();
            txtGateway.Text = "192.168.3.1";
            txtGateway.SetBounds(112, 96, 150, 23);
            grpNew.Controls.Add(txtGateway);

            btnChange = new Button();
            btnChange.Text = "Cambiar IP del lector";
            btnChange.SetBounds(330, 50, 170, 38);
            btnChange.Click += delegate { ChangeIpClicked(); };
            grpNew.Controls.Add(btnChange);

            Label lblNewNote = new Label();
            lblNewNote.Text = "Se aplica al apagar y volver\r\na encender el lector.";
            lblNewNote.ForeColor = Color.DimGray;
            lblNewNote.SetBounds(330, 94, 300, 36);
            grpNew.Controls.Add(lblNewNote);

            GroupBox grpSearch = new GroupBox();
            grpSearch.Text = "Buscar lectores en la red (si no conoces la IP)";
            grpSearch.SetBounds(16, 340, 668, 132);
            Controls.Add(grpSearch);

            btnSearch = new Button();
            btnSearch.Text = "Buscar lectores";
            btnSearch.SetBounds(14, 28, 120, 28);
            btnSearch.Click += delegate { SearchReaders(); };
            grpSearch.Controls.Add(btnSearch);

            lstFound = new ListBox();
            lstFound.SetBounds(146, 22, 508, 96);
            lstFound.SelectedIndexChanged += delegate
            {
                FoundReader found = lstFound.SelectedItem as FoundReader;
                if (found != null) { txtCurrentIp.Text = found.Ip; }
            };
            grpSearch.Controls.Add(lstFound);

            Label lblLog = new Label();
            lblLog.Text = "Registro:";
            lblLog.SetBounds(16, 480, 100, 20);
            Controls.Add(lblLog);

            txtLog = new TextBox();
            txtLog.SetBounds(16, 500, 668, 78);
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 8.5f);
            txtLog.BackColor = Color.White;
            Controls.Add(txtLog);

            lblStatus = new Label();
            lblStatus.Text = "Listo.";
            lblStatus.SetBounds(16, 586, 360, 20);
            Controls.Add(lblStatus);

            btnNet = new Button();
            btnNet.Text = "Red de Windows";
            btnNet.SetBounds(410, 580, 130, 28);
            btnNet.Click += delegate
            {
                try { System.Diagnostics.Process.Start("ncpa.cpl"); }
                catch (Exception ex) { Log("No se pudo abrir la configuracion de red: " + ex.Message); }
            };
            Controls.Add(btnNet);

            btnClose = new Button();
            btnClose.Text = "Salir";
            btnClose.SetBounds(550, 580, 134, 28);
            btnClose.Click += delegate { Close(); };
            Controls.Add(btnClose);
        }

        private void ChangeIpClicked()
        {
            string current = txtCurrentIp.Text.Trim();
            string newIp = txtNewIp.Text.Trim();
            string mask = txtMask.Text.Trim();
            string gateway = txtGateway.Text.Trim();

            int[] ipOct;
            int[] maskOct;
            int[] gateOct;

            if (!ParseOctets(current, out ipOct))
            {
                MessageBox.Show(this, "La IP actual no es valida.", "IP invalida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!ParseOctets(newIp, out ipOct))
            {
                MessageBox.Show(this, "La IP nueva no es valida.", "IP invalida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!ParseOctets(mask, out maskOct))
            {
                MessageBox.Show(this, "La mascara no es valida.", "Mascara invalida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (gateway != "" && !ParseOctets(gateway, out gateOct))
            {
                MessageBox.Show(this, "El gateway no es valido (dejalo vacio si no corresponde).", "Gateway invalido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult answer = MessageBox.Show(this,
                "Se va a cambiar la IP del lector de " + current + " a " + newIp + ".\r\n\r\n" +
                "Despues del cambio hay que apagar y volver a encender el lector.\r\n\r\nContinuar?",
                "Confirmar cambio de IP", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

            if (answer != DialogResult.OK) { return; }

            RunReaderOp("Cambiando la IP del lector...", delegate { ApplyIp(current, newIp, mask, gateway); });
        }

        private void TestConnection(string ip)
        {
            SReader reader = null;
            try
            {
                reader = SReader.Create("tcp://" + ip);
                reader.Connect();
                ReaderInfo info = reader.GetReaderInfo();
                string serial = reader.GetSerialNO();
                Ip_Info current = reader.GetIp();

                Ui(delegate
                {
                    Log("Conectado a " + ip + ".");
                    Log("Modelo: " + DescribeType(info.Type) + " (Type 0x" + info.Type.ToString("X2") + ")");
                    Log("Firmware: " + info.VersionH + "." + info.VersionL + " | Potencia actual: " + info.Power + " dBm | Antenas configuradas: " + info.Ant);
                    Log("Region: " + info.getCurrentRegionString() + " | Serie: " + serial);
                    Log("Red actual: IP " + current.IP() + " | mascara " + current.Mask() + " | gateway " + current.Gate());
                    txtCurrentIp.Text = current.IP();
                });
            }
            finally
            {
                if (reader != null) { try { reader.ShutDown(); } catch { } }
            }
        }

        private void ReadCurrentIp(string ip)
        {
            SReader reader = null;
            try
            {
                reader = SReader.Create("tcp://" + ip);
                reader.Connect();
                Ip_Info current = reader.GetIp();
                Ui(delegate
                {
                    Log("IP: " + current.IP() + " | mascara: " + current.Mask() + " | gateway: " + current.Gate());
                    txtCurrentIp.Text = current.IP();
                });
            }
            finally
            {
                if (reader != null) { try { reader.ShutDown(); } catch { } }
            }
        }

        private void ApplyIp(string currentIp, string newIp, string mask, string gateway)
        {
            SReader reader = null;
            try
            {
                int[] ipOct;
                int[] maskOct;
                int[] gateOct = new int[4];
                ParseOctets(newIp, out ipOct);
                ParseOctets(mask, out maskOct);
                if (gateway != "") { ParseOctets(gateway, out gateOct); }

                reader = SReader.Create("tcp://" + currentIp);
                reader.Connect();

                Ip_Info info = new Ip_Info();
                info.Ip1 = ipOct[0]; info.Ip2 = ipOct[1]; info.Ip3 = ipOct[2]; info.Ip4 = ipOct[3];
                info.Mask1 = maskOct[0]; info.Mask2 = maskOct[1]; info.Mask3 = maskOct[2]; info.Mask4 = maskOct[3];
                info.Gate1 = gateOct[0]; info.Gate2 = gateOct[1]; info.Gate3 = gateOct[2]; info.Gate4 = gateOct[3];

                reader.SetIP(info);

                Ui(delegate
                {
                    Log("IP cambiada a " + newIp + " (mascara " + mask + ", gateway " + (gateway == "" ? "0.0.0.0" : gateway) + ").");
                    Log("AHORA: apaga y volve a encender el lector para que tome la nueva IP.");
                    MessageBox.Show(this,
                        "IP cambiada a " + newIp + ".\r\n\r\n" +
                        "Apaga y volve a encender el lector.\r\n" +
                        "Despues, configura la notebook en 192.168.3.x y proba la conexion con la IP nueva.",
                        "Cambio realizado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
            }
            finally
            {
                if (reader != null) { try { reader.ShutDown(); } catch { } }
            }
        }

        private void SearchReaders()
        {
            if (busy) { return; }
            busy = true;
            SetButtonsEnabled(false);
            lblStatus.Text = "Buscando lectores...";
            UseWaitCursor = true;
            Log("Buscando lectores por UDP (3 segundos)...");

            Thread thread = new Thread(delegate()
            {
                List<FoundReader> found = new List<FoundReader>();
                UdpClient listener = null;
                try
                {
                    listener = new UdpClient(1600);
                    listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    listener.Client.ReceiveTimeout = 500;

                    byte[] message = Encoding.ASCII.GetBytes("IP_FOUND");
                    foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (nic.OperationalStatus != OperationalStatus.Up) { continue; }
                        foreach (UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
                        {
                            if (address.Address.AddressFamily != AddressFamily.InterNetwork) { continue; }
                            try
                            {
                                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                                {
                                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                                    socket.Bind(new IPEndPoint(address.Address, 0));
                                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                                    socket.SendTo(message, new IPEndPoint(IPAddress.Broadcast, 1500));
                                }
                            }
                            catch { }
                        }
                    }

                    DateTime limit = DateTime.UtcNow.AddSeconds(3);
                    while (DateTime.UtcNow < limit)
                    {
                        try
                        {
                            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                            byte[] data = listener.Receive(ref remote);
                            string text = Encoding.UTF8.GetString(data).Replace("\\\"", "\"");
                            string ip = Extract(text, "Ip");
                            if (ip == "") { continue; }

                            FoundReader item = new FoundReader();
                            item.Ip = ip;
                            item.Mask = Extract(text, "Mask");
                            item.Gateway = Extract(text, "Gateway");
                            item.Mac = Extract(text, "Mac");

                            bool exists = false;
                            foreach (FoundReader old in found) { if (old.Ip == item.Ip) { exists = true; } }
                            if (!exists) { found.Add(item); }
                        }
                        catch (SocketException) { }
                    }
                }
                catch (Exception ex)
                {
                    Ui(delegate { Log("ERROR buscando lectores: " + ex.Message); });
                }
                finally
                {
                    if (listener != null) { listener.Close(); }
                }

                Ui(delegate
                {
                    lstFound.Items.Clear();
                    foreach (FoundReader item in found) { lstFound.Items.Add(item); }

                    if (found.Count == 0)
                    {
                        Log("No se encontro ningun lector. Verifica cable, energia y que la notebook este en la misma red (ej. 192.168.1.x).");
                    }
                    else
                    {
                        Log("Lectores encontrados: " + found.Count + ". Eleji uno de la lista para usar su IP.");
                        lstFound.SelectedIndex = 0;
                    }

                    busy = false;
                    SetButtonsEnabled(true);
                    lblStatus.Text = "Listo.";
                    UseWaitCursor = false;
                });
            });

            thread.IsBackground = true;
            thread.Start();
        }

        private void RunReaderOp(string status, Action action)
        {
            if (busy) { return; }
            busy = true;
            SetButtonsEnabled(false);
            lblStatus.Text = status;
            UseWaitCursor = true;
            Log(status);

            Thread thread = new Thread(delegate()
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Ui(delegate { Log("ERROR: " + ex.Message); });
                }
                finally
                {
                    Ui(delegate
                    {
                        busy = false;
                        SetButtonsEnabled(true);
                        lblStatus.Text = "Listo.";
                        UseWaitCursor = false;
                    });
                }
            });

            thread.IsBackground = true;
            thread.Start();
        }

        private void SetButtonsEnabled(bool enabled)
        {
            btnTest.Enabled = enabled;
            btnRead.Enabled = enabled;
            btnChange.Enabled = enabled;
            btnSearch.Enabled = enabled;
            btnNet.Enabled = enabled;
        }

        private void Log(string text)
        {
            if (txtLog.TextLength > 30000) { txtLog.Clear(); }
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
        }

        private void Ui(Action action)
        {
            if (IsDisposed) { return; }
            try
            {
                if (InvokeRequired) { Invoke(action); }
                else { action(); }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private static string Extract(string json, string key)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "";
        }

        private static bool ParseOctets(string text, out int[] octets)
        {
            octets = new int[4];
            string[] parts = (text == null ? "" : text.Trim()).Split('.');
            if (parts.Length != 4) { return false; }

            for (int i = 0; i < 4; i++)
            {
                int value;
                if (!int.TryParse(parts[i], out value) || value < 0 || value > 255) { return false; }
                octets[i] = value;
            }

            return true;
        }

        private static string DescribeType(int type)
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
    }
}
