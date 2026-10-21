using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace PortalCalibration
{
    public sealed class MainForm : Form
    {
        private readonly ReaderService service = new ReaderService();
        private readonly ConcurrentQueue<TagReading> incoming = new ConcurrentQueue<TagReading>();
        private readonly Timer uiTimer = new Timer();
        private readonly DataTable tagsTable = new DataTable();
        private readonly DataTable antennasTable = new DataTable();
        private readonly Dictionary<string, TagRow> tagRows = new Dictionary<string, TagRow>();
        private readonly Dictionary<string, int> learningCounts = new Dictionary<string, int>();

        private HashSet<string> background = new HashSet<string>();
        private DateTime learningStart;
        private bool learning;
        private long totalReads;
        private long filteredReads;

        private TextBox txtIp;
        private Label lblConnection;
        private Label lblDevice;
        private Label lblCounters;
        private Label lblBackground;
        private DataGridView gridTags;
        private DataGridView gridAntennas;
        private TextBox txtLog;
        private NumericUpDown numQ;
        private NumericUpDown numTimeMs;
        private NumericUpDown numRssi;
        private NumericUpDown numGpoSeconds;
        private ComboBox cmbSession;
        private CheckBox chkIgnoreBackground;
        private CheckBox chkBuzzer;
        private Button btnConnect;
        private Button btnDisconnect;
        private Button btnApply;
        private Button btnRefresh;
        private Button btnStart;
        private Button btnStop;
        private Button btnLearn;
        private Button btnViewBackground;
        private Button btnClear;
        private Button btnSaveProfile;
        private Button btnLoadProfile;
        private Button btnGpo1;
        private Button btnGpo2;
        private Button btnGpo3;
        private Button btnGpo4;
        private ToolTip helpTip;

        public MainForm()
        {
            Text = "Portal RFID - Consola de calibracion";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1240, 760);
            MinimumSize = new Size(1100, 700);
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;

            BuildUi();

            service.Log += HandleServiceLog;
            service.TagArrived += delegate(TagReading reading) { incoming.Enqueue(reading); };

            uiTimer.Interval = 200;
            uiTimer.Tick += TimerTick;
            uiTimer.Start();

            FormClosing += OnFormClosing;
            UpdateButtons();
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(root);

            Panel top = new Panel();
            top.Dock = DockStyle.Fill;
            root.Controls.Add(top, 0, 0);

            Label lblIp = new Label();
            lblIp.Text = "IP del lector:";
            lblIp.SetBounds(10, 15, 85, 20);
            top.Controls.Add(lblIp);

            txtIp = new TextBox();
            txtIp.Text = "192.168.3.60";
            txtIp.SetBounds(100, 12, 130, 24);
            top.Controls.Add(txtIp);

            btnConnect = MakeButton("Conectar", 245, 10, 100, 30);
            btnConnect.Click += delegate { ConnectReader(); };
            top.Controls.Add(btnConnect);

            btnDisconnect = MakeButton("Desconectar", 355, 10, 110, 30);
            btnDisconnect.Click += delegate { DisconnectReader(); };
            top.Controls.Add(btnDisconnect);

            lblConnection = new Label();
            lblConnection.Text = "Desconectado";
            lblConnection.ForeColor = Color.Firebrick;
            lblConnection.SetBounds(485, 15, 740, 20);
            top.Controls.Add(lblConnection);

            SplitContainer main = new SplitContainer();
            main.Dock = DockStyle.Fill;
            main.Orientation = Orientation.Vertical;
            main.FixedPanel = FixedPanel.Panel1;
            root.Controls.Add(main, 0, 1);

            FlowLayoutPanel left = new FlowLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.FlowDirection = FlowDirection.TopDown;
            left.WrapContents = false;
            left.AutoScroll = true;
            left.Padding = new Padding(4);
            main.Panel1.Controls.Add(left);

            BuildDeviceGroup(left);
            BuildAntennaGroup(left);
            BuildReadingGroup(left);
            BuildProfileGroup(left);

            SplitContainer right = new SplitContainer();
            right.Dock = DockStyle.Fill;
            right.Orientation = Orientation.Horizontal;
            right.FixedPanel = FixedPanel.Panel2;
            main.Panel2.Controls.Add(right);

            BuildTagGrid(right.Panel1);
            BuildBottomPanel(right.Panel2);

            Load += delegate
            {
                try
                {
                    if (main.Width > 900) { main.SplitterDistance = 440; }
                    if (right.Height > 500) { right.SplitterDistance = Math.Max(260, right.Height - 210); }
                }
                catch { }
            };
        }

        private void BuildDeviceGroup(FlowLayoutPanel left)
        {
            GroupBox group = AddGroup(left, "Equipo", 138);

            lblDevice = new Label();
            lblDevice.Text = "Sin conexion.";
            lblDevice.SetBounds(10, 22, 390, 106);
            group.Controls.Add(lblDevice);
        }

        private void BuildAntennaGroup(FlowLayoutPanel left)
        {
            GroupBox group = AddGroup(left, "Antenas y potencia", 246);

            antennasTable.Columns.Add("Activa", typeof(bool));
            antennasTable.Columns.Add("Antena", typeof(int));
            antennasTable.Columns.Add("Detectada", typeof(string));
            antennasTable.Columns.Add("ROE", typeof(string));
            antennasTable.Columns.Add("Potencia", typeof(int));

            gridAntennas = new DataGridView();
            gridAntennas.SetBounds(10, 22, 390, 176);
            gridAntennas.ReadOnly = false;
            gridAntennas.AutoGenerateColumns = false;
            gridAntennas.AllowUserToAddRows = false;
            gridAntennas.AllowUserToDeleteRows = false;
            gridAntennas.AllowUserToResizeRows = false;
            gridAntennas.RowHeadersVisible = false;
            gridAntennas.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            gridAntennas.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridAntennas.MultiSelect = false;
            gridAntennas.EditMode = DataGridViewEditMode.EditOnEnter;
            gridAntennas.BackgroundColor = Color.White;
            gridAntennas.BorderStyle = BorderStyle.FixedSingle;

            DataGridViewCheckBoxColumn colActive = new DataGridViewCheckBoxColumn();
            colActive.Name = "Activa";
            colActive.DataPropertyName = "Activa";
            colActive.HeaderText = "Usar";
            colActive.FillWeight = 38;
            gridAntennas.Columns.Add(colActive);
            gridAntennas.Columns.Add(MakeTextColumn("Antena", "Puerto", 45, true));
            gridAntennas.Columns.Add(MakeTextColumn("Detectada", "Detectada", 70, true));
            gridAntennas.Columns.Add(MakeTextColumn("ROE", "ROE", 55, true));
            gridAntennas.Columns.Add(MakeTextColumn("Potencia", "Pot. dBm", 75, false));
            gridAntennas.DataSource = antennasTable;
            gridAntennas.CurrentCellDirtyStateChanged += delegate
            {
                if (gridAntennas.IsCurrentCellDirty)
                {
                    gridAntennas.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            group.Controls.Add(gridAntennas);

            btnApply = MakeButton("Aplicar", 10, 206, 110, 28);
            btnApply.Click += delegate { ApplyConfiguration(); };
            group.Controls.Add(btnApply);

            btnRefresh = MakeButton("Refrescar estado", 128, 206, 130, 28);
            btnRefresh.Click += delegate { RefreshSnapshot(); };
            group.Controls.Add(btnRefresh);

            Label note = new Label();
            note.Text = "Potencia 0-30 dBm.\r\nROE = calidad de antena/cable.";
            note.ForeColor = Color.DimGray;
            note.SetBounds(266, 204, 140, 38);
            group.Controls.Add(note);
        }

        private void BuildReadingGroup(FlowLayoutPanel left)
        {
            GroupBox group = AddGroup(left, "Lectura en vivo", 258);
            helpTip = new ToolTip();

            Label lblQ = new Label();
            lblQ.Text = "Q:";
            lblQ.SetBounds(10, 28, 25, 20);
            group.Controls.Add(lblQ);

            numQ = new NumericUpDown();
            numQ.Minimum = 0;
            numQ.Maximum = 15;
            numQ.Value = 4;
            numQ.SetBounds(38, 25, 55, 24);
            group.Controls.Add(numQ);

            Label lblSession = new Label();
            lblSession.Text = "Session:";
            lblSession.SetBounds(110, 28, 60, 20);
            group.Controls.Add(lblSession);

            cmbSession = new ComboBox();
            cmbSession.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSession.Items.Add("S0 (pocas)");
            cmbSession.Items.Add("S1 (muchas)");
            cmbSession.Items.Add("S2");
            cmbSession.Items.Add("S3");
            cmbSession.SelectedIndex = 1;
            cmbSession.SetBounds(175, 25, 110, 24);
            group.Controls.Add(cmbSession);

            Label lblTime = new Label();
            lblTime.Text = "Tiempo/antena:";
            lblTime.SetBounds(10, 62, 100, 20);
            group.Controls.Add(lblTime);

            numTimeMs = new NumericUpDown();
            numTimeMs.Minimum = 100;
            numTimeMs.Maximum = 25500;
            numTimeMs.Increment = 100;
            numTimeMs.Value = 500;
            numTimeMs.SetBounds(112, 59, 80, 24);
            group.Controls.Add(numTimeMs);

            Label lblMs = new Label();
            lblMs.Text = "ms";
            lblMs.SetBounds(195, 62, 30, 20);
            group.Controls.Add(lblMs);

            Label lblRssi = new Label();
            lblRssi.Text = "RSSI min:";
            lblRssi.SetBounds(228, 62, 65, 20);
            group.Controls.Add(lblRssi);

            numRssi = new NumericUpDown();
            numRssi.Minimum = -100;
            numRssi.Maximum = 0;
            numRssi.Value = -80;
            numRssi.SetBounds(294, 59, 70, 24);
            group.Controls.Add(numRssi);

            Label lblDbm = new Label();
            lblDbm.Text = "dBm";
            lblDbm.SetBounds(369, 62, 35, 20);
            group.Controls.Add(lblDbm);

            chkIgnoreBackground = new CheckBox();
            chkIgnoreBackground.Text = "Ignorar EPC de fondo (no mostrar)";
            chkIgnoreBackground.SetBounds(10, 91, 390, 22);
            helpTip.SetToolTip(chkIgnoreBackground,
                "Fondo = EPC fijos del deposito que se ven aunque la puerta este vacia. " +
                "Al marcarlo no se muestran en la tabla.");
            group.Controls.Add(chkIgnoreBackground);

            btnStart = MakeButton("Iniciar", 10, 119, 100, 30);
            btnStart.Click += delegate { StartScan(); };
            group.Controls.Add(btnStart);

            btnStop = MakeButton("Detener", 118, 119, 100, 30);
            btnStop.Click += delegate { StopScan(); };
            group.Controls.Add(btnStop);

            btnClear = MakeButton("Limpiar", 226, 119, 88, 30);
            btnClear.Click += delegate { ClearTags(); };
            group.Controls.Add(btnClear);

            btnLearn = MakeButton("Aprender fondo (10 s)", 10, 155, 155, 28);
            btnLearn.Click += delegate { StartLearning(); };
            helpTip.SetToolTip(btnLearn,
                "Con la puerta vacia, durante 10 segundos aprende los EPC que se ven " +
                "2 o mas veces. No son piezas en transito.");
            group.Controls.Add(btnLearn);

            btnViewBackground = MakeButton("Ver fondo", 173, 155, 105, 28);
            btnViewBackground.Click += delegate { ViewBackground(); };
            group.Controls.Add(btnViewBackground);

            lblBackground = new Label();
            lblBackground.Text = "Fondo: 0 EPCs";
            lblBackground.SetBounds(286, 160, 120, 20);
            group.Controls.Add(lblBackground);

            lblCounters = new Label();
            lblCounters.Text = "Distintas: 0   Lecturas: 0   Filtradas: 0";
            lblCounters.SetBounds(10, 190, 400, 22);
            group.Controls.Add(lblCounters);

            Label note = new Label();
            note.Text = "Fondo: EPC vistos 2+ veces en 10 s.\r\n" +
                "El umbral/fondo se aplica a lecturas nuevas; Limpiar reinicia la vista.";
            note.ForeColor = Color.DimGray;
            note.SetBounds(10, 216, 400, 36);
            group.Controls.Add(note);
        }

        private void BuildProfileGroup(FlowLayoutPanel left)
        {
            GroupBox group = AddGroup(left, "Perfil del porton", 82);

            btnSaveProfile = MakeButton("Guardar perfil", 10, 22, 155, 28);
            btnSaveProfile.Click += delegate { SaveProfile(); };
            group.Controls.Add(btnSaveProfile);

            btnLoadProfile = MakeButton("Cargar perfil", 175, 22, 155, 28);
            btnLoadProfile.Click += delegate { LoadProfile(); };
            group.Controls.Add(btnLoadProfile);

            Label lblPath = new Label();
            lblPath.Text = "Archivo: perfil_porton.xml";
            lblPath.ForeColor = Color.DimGray;
            lblPath.SetBounds(10, 56, 390, 20);
            group.Controls.Add(lblPath);
        }

        private void BuildTagGrid(Control parent)
        {
            tagsTable.Columns.Add("Lote", typeof(string));
            tagsTable.Columns.Add("EPC", typeof(string));
            tagsTable.Columns.Add("Antena", typeof(int));
            tagsTable.Columns.Add("RSSI", typeof(int));
            tagsTable.Columns.Add("Lecturas", typeof(int));
            tagsTable.Columns.Add("Primera", typeof(string));
            tagsTable.Columns.Add("Ultima", typeof(string));

            gridTags = new DataGridView();
            gridTags.Dock = DockStyle.Fill;
            gridTags.ReadOnly = true;
            gridTags.AutoGenerateColumns = false;
            gridTags.AllowUserToAddRows = false;
            gridTags.AllowUserToDeleteRows = false;
            gridTags.AllowUserToResizeRows = false;
            gridTags.RowHeadersVisible = false;
            gridTags.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            gridTags.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridTags.MultiSelect = false;
            gridTags.BackgroundColor = Color.White;
            gridTags.BorderStyle = BorderStyle.None;
            gridTags.EnableHeadersVisualStyles = false;
            gridTags.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
            gridTags.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(55, 55, 55);
            gridTags.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            gridTags.DefaultCellStyle.Font = new Font("Consolas", 9f);

            gridTags.Columns.Add(MakeTextColumn("Lote", "Lote", 15, true));
            gridTags.Columns.Add(MakeTextColumn("EPC", "EPC", 31, true));
            gridTags.Columns.Add(MakeTextColumn("Antena", "Ant.", 9, true));
            gridTags.Columns.Add(MakeTextColumn("RSSI", "RSSI", 9, true));
            gridTags.Columns.Add(MakeTextColumn("Lecturas", "Lecturas", 9, true));
            gridTags.Columns.Add(MakeTextColumn("Primera", "Primera", 13, true));
            gridTags.Columns.Add(MakeTextColumn("Ultima", "Ultima", 14, true));
            gridTags.DataSource = tagsTable;
            gridTags.CellFormatting += FormatTagRow;
            parent.Controls.Add(gridTags);
        }

        private void BuildBottomPanel(Control parent)
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            parent.Controls.Add(layout);

            GroupBox gpo = new GroupBox();
            gpo.Text = "Salidas del lector (solo por red)";
            gpo.Dock = DockStyle.Fill;
            layout.Controls.Add(gpo, 0, 0);

            Label lblPulse = new Label();
            lblPulse.Text = "Pulso (seg):";
            lblPulse.SetBounds(10, 27, 85, 20);
            gpo.Controls.Add(lblPulse);

            numGpoSeconds = new NumericUpDown();
            numGpoSeconds.Minimum = 1;
            numGpoSeconds.Maximum = 60;
            numGpoSeconds.Value = 3;
            numGpoSeconds.SetBounds(95, 24, 55, 24);
            gpo.Controls.Add(numGpoSeconds);

            btnGpo1 = MakeButton("1 Roja", 165, 23, 75, 28);
            btnGpo1.Click += delegate { PulseGpo(1); };
            gpo.Controls.Add(btnGpo1);

            btnGpo2 = MakeButton("2 Amarilla", 244, 23, 88, 28);
            btnGpo2.Click += delegate { PulseGpo(2); };
            gpo.Controls.Add(btnGpo2);

            btnGpo3 = MakeButton("3 Verde", 336, 23, 78, 28);
            btnGpo3.Click += delegate { PulseGpo(3); };
            gpo.Controls.Add(btnGpo3);

            btnGpo4 = MakeButton("4 Parlante", 418, 23, 92, 28);
            btnGpo4.Click += delegate { PulseGpo(4); };
            gpo.Controls.Add(btnGpo4);

            chkBuzzer = new CheckBox();
            chkBuzzer.Text = "Buzzer del lector";
            chkBuzzer.SetBounds(520, 27, 150, 22);
            chkBuzzer.CheckedChanged += delegate { BuzzerChanged(); };
            gpo.Controls.Add(chkBuzzer);

            GroupBox logGroup = new GroupBox();
            logGroup.Text = "Registro";
            logGroup.Dock = DockStyle.Fill;
            layout.Controls.Add(logGroup, 0, 1);

            txtLog = new TextBox();
            txtLog.Dock = DockStyle.Fill;
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 8.5f);
            txtLog.BackColor = Color.White;
            logGroup.Controls.Add(txtLog);
        }

        private static Button MakeButton(string text, int x, int y, int width, int height)
        {
            Button button = new Button();
            button.Text = text;
            button.SetBounds(x, y, width, height);
            return button;
        }

        private static DataGridViewTextBoxColumn MakeTextColumn(string name, string header, int fillWeight, bool readOnly)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.DataPropertyName = name;
            column.HeaderText = header;
            column.FillWeight = fillWeight;
            column.ReadOnly = readOnly;
            return column;
        }

        private static GroupBox AddGroup(FlowLayoutPanel parent, string text, int height)
        {
            GroupBox group = new GroupBox();
            group.Text = text;
            group.Width = 410;
            group.Height = height;
            group.Margin = new Padding(4, 3, 4, 6);
            parent.Controls.Add(group);
            return group;
        }

        private void ConnectReader()
        {
            if (service.IsConnected) { return; }

            string ip = txtIp.Text.Trim();
            UseWaitCursor = true;
            lblConnection.Text = "Conectando a " + ip + "...";
            lblConnection.ForeColor = Color.DarkOrange;
            Application.DoEvents();

            try
            {
                ReaderSnapshot snapshot = service.Connect(ip);
                ApplySnapshot(snapshot);
                Log("Conectado a " + ip + ". Modelo: " + snapshot.ModelName +
                    ", " + snapshot.Channels + " canales.");
            }
            catch (Exception ex)
            {
                lblConnection.Text = "Desconectado";
                lblConnection.ForeColor = Color.Firebrick;
                Log("ERROR al conectar: " + ex.Message);
                MessageBox.Show(this, "No se pudo conectar al lector.\r\n\r\n" + ex.Message,
                    "Error de conexion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
                UpdateButtons();
            }
        }

        private void DisconnectReader()
        {
            try { service.Disconnect(); }
            catch (Exception ex) { Log("ERROR al desconectar: " + ex.Message); }

            lblConnection.Text = "Desconectado";
            lblConnection.ForeColor = Color.Firebrick;
            UpdateButtons();
        }

        private void ApplySnapshot(ReaderSnapshot snapshot)
        {
            string globalPower = snapshot.GlobalPower > 30
                ? "por puerto" : snapshot.GlobalPower + " dBm";
            lblDevice.Text =
                "Modelo: " + snapshot.ModelName + " (" + snapshot.Channels + " canales)\r\n" +
                "Type: 0x" + snapshot.Type.ToString("X2") +
                "   Firmware: " + snapshot.VersionH + "." + snapshot.VersionL + "\r\n" +
                "Serie: " + snapshot.Serial + "\r\n" +
                "IP: " + snapshot.Ip + "\r\n" +
                "Mascara: " + snapshot.Mask + "   Gateway: " + snapshot.Gateway + "\r\n" +
                "Region: " + snapshot.Region + "   Potencia global: " + globalPower;

            lblConnection.Text = "Conectado: " + snapshot.ModelName + " / " + snapshot.Channels + " canales";
            lblConnection.ForeColor = Color.DarkGreen;

            antennasTable.Rows.Clear();
            bool anyDetected = snapshot.Detected != null && snapshot.Detected.Count > 0;
            for (int ant = 1; ant <= snapshot.Channels; ant++)
            {
                bool detected = snapshot.Detected != null && snapshot.Detected.Contains(ant);
                bool active = anyDetected ? detected : ant == 1;
                int loss = snapshot.ReturnLoss != null && snapshot.ReturnLoss.Length >= ant
                    ? snapshot.ReturnLoss[ant - 1] : -1;

                antennasTable.Rows.Add(
                    active,
                    ant,
                    detected ? "Si" : "No",
                    loss >= 0 ? loss.ToString() : "-",
                    snapshot.Powers[ant - 1]);
            }

            Log("Antenas detectadas: " + JoinList(snapshot.Detected) +
                " | ROE: " + JoinLoss(snapshot.ReturnLoss));
            UpdateButtons();
        }

        private void RefreshSnapshot()
        {
            if (!service.IsConnected) { return; }

            try
            {
                if (service.IsScanning) { service.StopScan(); }
                ReaderSnapshot snapshot = service.ReadSnapshot();
                ApplySnapshot(snapshot);
                Log("Estado del lector actualizado.");
            }
            catch (Exception ex)
            {
                Log("ERROR al refrescar: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplyConfiguration()
        {
            if (!service.IsConnected) { return; }

            try
            {
                int[] powers;
                bool[] enabled;
                List<int> active;
                CollectAntennaConfig(out powers, out enabled, out active);

                if (active.Count == 0)
                {
                    MessageBox.Show(this, "Selecciona al menos una antena.", "Configuracion",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                int scanTime = (int)(numTimeMs.Value / 100);
                if (scanTime < 1) { scanTime = 1; }
                if (scanTime > 255) { scanTime = 255; }

                if (service.IsScanning) { service.StopScan(); }
                service.Apply(powers, (int)numQ.Value, cmbSession.SelectedIndex, scanTime, active);
                Log("Configuracion aplicada. Antenas activas: " + JoinList(active) +
                    " | Potencias: " + JoinArray(powers));
            }
            catch (Exception ex)
            {
                Log("ERROR al aplicar configuracion: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Configuracion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UpdateButtons();
            }
        }

        private void CollectAntennaConfig(out int[] powers, out bool[] enabled, out List<int> active)
        {
            gridAntennas.EndEdit();
            int count = antennasTable.Rows.Count;
            if (count == 0) { throw new InvalidOperationException("Primero conectate al lector."); }

            powers = new int[count];
            enabled = new bool[count];
            active = new List<int>();

            for (int i = 0; i < count; i++)
            {
                DataRow row = antennasTable.Rows[i];
                int antenna = Convert.ToInt32(row["Antena"]);
                int power = Convert.ToInt32(row["Potencia"]);
                if (power < 0) { power = 0; }
                if (power > 30) { power = 30; }
                row["Potencia"] = power;

                bool on = Convert.ToBoolean(row["Activa"]);
                powers[antenna - 1] = power;
                enabled[antenna - 1] = on;
                if (on) { active.Add(antenna); }
            }
        }

        private void StartScan()
        {
            if (!service.IsConnected) { return; }
            if (service.IsScanning) { return; }

            try
            {
                int[] powers;
                bool[] enabled;
                List<int> active;
                CollectAntennaConfig(out powers, out enabled, out active);
                if (active.Count == 0)
                {
                    MessageBox.Show(this, "Selecciona al menos una antena.", "Lectura",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                int scanTime = (int)(numTimeMs.Value / 100);
                if (scanTime < 1) { scanTime = 1; }
                if (scanTime > 255) { scanTime = 255; }

                service.Apply(powers, (int)numQ.Value, cmbSession.SelectedIndex, scanTime, active);
                service.StartScan();
                Log("Lectura iniciada en antenas: " + JoinList(active) + ".");
            }
            catch (Exception ex)
            {
                Log("ERROR al iniciar: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Lectura", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UpdateButtons();
            }
        }

        private void StopScan()
        {
            try
            {
                service.StopScan();
            }
            catch (Exception ex)
            {
                Log("ERROR al detener: " + ex.Message);
            }
            UpdateButtons();
        }

        private void ClearTags()
        {
            tagsTable.Rows.Clear();
            tagRows.Clear();
            totalReads = 0;
            filteredReads = 0;
            learningCounts.Clear();
            UpdateCounters();
            Log("Vista de etiquetas limpiada.");
        }

        private void StartLearning()
        {
            if (!service.IsConnected) { return; }
            if (!service.IsScanning)
            {
                DialogResult answer = MessageBox.Show(this,
                    "La lectura esta detenida. Se va a iniciar con la configuracion actual " +
                    "y aprender el fondo durante 10 segundos.\r\n\r\nContinuar?",
                    "Aprender fondo", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) { return; }
                StartScan();
                if (!service.IsScanning) { return; }
            }

            learningCounts.Clear();
            learningStart = DateTime.Now;
            learning = true;
            lblBackground.Text = "Aprendiendo fondo...";
            Log("Aprendiendo fondo durante 10 segundos (puerta vacia).");
        }

        private void FinishLearning()
        {
            learning = false;
            HashSet<string> next = new HashSet<string>();
            foreach (KeyValuePair<string, int> pair in learningCounts)
            {
                if (pair.Value >= 2) { next.Add(pair.Key); }
            }

            background = next;
            lblBackground.Text = "Fondo: " + background.Count + " EPCs";
            Log("Fondo aprendido: " + background.Count + " EPCs (2 o mas lecturas en 10 s).");
            UpdateButtons();
        }

        private void ViewBackground()
        {
            if (background.Count == 0) { return; }

            Form dialog = new Form();
            dialog.Text = "EPC de fondo aprendidos";
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.ClientSize = new Size(620, 420);
            dialog.Font = new Font("Segoe UI", 9f);

            ListBox list = new ListBox();
            list.Dock = DockStyle.Fill;
            list.Font = new Font("Consolas", 9f);
            list.IntegralHeight = false;

            List<string> ordered = new List<string>(background);
            ordered.Sort();
            foreach (string epc in ordered)
            {
                list.Items.Add(epc + "   ->   " + NormalizeEpc(epc));
            }
            dialog.Controls.Add(list);

            Button close = new Button();
            close.Text = "Cerrar";
            close.Dock = DockStyle.Bottom;
            close.Height = 34;
            close.Click += delegate { dialog.Close(); };
            dialog.Controls.Add(close);

            dialog.ShowDialog(this);
        }

        private void SaveProfile()
        {
            try
            {
                int[] powers;
                bool[] enabled;
                List<int> active;
                CollectAntennaConfig(out powers, out enabled, out active);

                PortalProfile profile = new PortalProfile();
                profile.Ip = txtIp.Text.Trim();
                profile.Channels = antennasTable.Rows.Count;
                profile.Powers = powers;
                profile.Enabled = enabled;
                profile.Q = (int)numQ.Value;
                profile.Session = cmbSession.SelectedIndex;
                profile.ScanTimeMs = (int)numTimeMs.Value;
                profile.RssiMin = (int)numRssi.Value;
                profile.IgnoreBackground = chkIgnoreBackground.Checked;
                profile.Background = new string[background.Count];
                background.CopyTo(profile.Background);

                ProfileStore.Save(profile);
                Log("Perfil guardado en " + ProfileStore.FilePath + ".");
                MessageBox.Show(this, "Perfil guardado en:\r\n" + ProfileStore.FilePath,
                    "Perfil", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log("ERROR al guardar perfil: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Perfil", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadProfile()
        {
            try
            {
                PortalProfile profile = ProfileStore.Load();
                if (profile == null)
                {
                    MessageBox.Show(this, "No existe " + ProfileStore.FilePath + ".",
                        "Perfil", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                txtIp.Text = profile.Ip;
                numQ.Value = profile.Q;
                if (profile.Session >= 0 && profile.Session < cmbSession.Items.Count)
                {
                    cmbSession.SelectedIndex = profile.Session;
                }
                numTimeMs.Value = profile.ScanTimeMs;
                numRssi.Value = profile.RssiMin;
                chkIgnoreBackground.Checked = profile.IgnoreBackground;

                background = new HashSet<string>();
                if (profile.Background != null)
                {
                    foreach (string epc in profile.Background) { if (!string.IsNullOrEmpty(epc)) { background.Add(epc); } }
                }
                lblBackground.Text = "Fondo: " + background.Count + " EPCs";

                if (antennasTable.Rows.Count == profile.Channels)
                {
                    for (int i = 0; i < profile.Channels; i++)
                    {
                        DataRow row = antennasTable.Rows[i];
                        row["Activa"] = profile.Enabled[i];
                        row["Potencia"] = profile.Powers[i];
                    }
                }
                else
                {
                    antennasTable.Rows.Clear();
                    for (int i = 0; i < profile.Channels; i++)
                    {
                        antennasTable.Rows.Add(profile.Enabled[i], i + 1, "?", "-", profile.Powers[i]);
                    }
                }

                Log("Perfil cargado desde " + ProfileStore.FilePath +
                    ". Revisa y toca Aplicar para llevarlo al lector.");
            }
            catch (Exception ex)
            {
                Log("ERROR al cargar perfil: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Perfil", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PulseGpo(int id)
        {
            if (!service.IsConnected) { return; }
            if (service.IsScanning)
            {
                MessageBox.Show(this, "Detene la lectura antes de probar las salidas GPO.",
                    "GPO", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                int seconds = (int)numGpoSeconds.Value;
                service.PulseGpo(id, seconds);
            }
            catch (Exception ex)
            {
                Log("ERROR GPO " + id + ": " + ex.Message);
                MessageBox.Show(this, ex.Message, "GPO", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BuzzerChanged()
        {
            if (!service.IsConnected || service.IsScanning) { return; }
            try
            {
                service.SetBuzzer(chkBuzzer.Checked);
            }
            catch (Exception ex)
            {
                Log("ERROR buzzer: " + ex.Message);
            }
        }

        private void TimerTick(object sender, EventArgs e)
        {
            DrainQueue();
            UpdateCounters();
            UpdateButtons();

            if (learning && (DateTime.Now - learningStart).TotalSeconds >= 10.0)
            {
                FinishLearning();
            }
        }

        private void DrainQueue()
        {
            TagReading reading;
            int guard = 0;
            while (incoming.TryDequeue(out reading) && guard < 5000)
            {
                guard++;
                ProcessReading(reading);
            }
        }

        private void ProcessReading(TagReading reading)
        {
            if (reading == null || string.IsNullOrEmpty(reading.Epc)) { return; }
            totalReads++;

            if (learning)
            {
                int count;
                learningCounts.TryGetValue(reading.Epc, out count);
                learningCounts[reading.Epc] = count + 1;
            }

            if (reading.Rssi < (int)numRssi.Value)
            {
                filteredReads++;
                return;
            }

            if (chkIgnoreBackground.Checked && background.Contains(reading.Epc))
            {
                filteredReads++;
                return;
            }

            string lote = NormalizeEpc(reading.Epc);
            TagRow row;
            if (!tagRows.TryGetValue(reading.Epc, out row))
            {
                row = new TagRow();
                row.Epc = reading.Epc;
                row.Lote = lote;
                row.First = reading.SeenAt;
                row.Count = 0;

                DataRow data = tagsTable.NewRow();
                data["Lote"] = lote;
                data["EPC"] = reading.Epc;
                data["Antena"] = reading.Antenna;
                data["RSSI"] = reading.Rssi;
                data["Lecturas"] = 0;
                data["Primera"] = reading.SeenAt.ToString("HH:mm:ss");
                data["Ultima"] = reading.SeenAt.ToString("HH:mm:ss.fff");
                tagsTable.Rows.InsertAt(data, 0);

                row.DataRow = data;
                tagRows[reading.Epc] = row;
            }

            row.Count++;
            row.Antenna = reading.Antenna;
            row.Rssi = reading.Rssi;
            row.Last = reading.SeenAt;

            row.DataRow["Antena"] = reading.Antenna;
            row.DataRow["RSSI"] = reading.Rssi;
            row.DataRow["Lecturas"] = row.Count;
            row.DataRow["Ultima"] = reading.SeenAt.ToString("HH:mm:ss.fff");
        }

        private void UpdateCounters()
        {
            lblCounters.Text = "Distintas: " + tagsTable.Rows.Count +
                "   Lecturas: " + totalReads +
                "   Filtradas: " + filteredReads;
            if (!learning) { lblBackground.Text = "Fondo: " + background.Count + " EPCs"; }
        }

        private void UpdateButtons()
        {
            bool connected = service.IsConnected;
            bool scanning = service.IsScanning;

            btnConnect.Enabled = !connected;
            btnDisconnect.Enabled = connected;
            btnApply.Enabled = connected && !scanning;
            btnRefresh.Enabled = connected && !scanning;
            btnStart.Enabled = connected && !scanning;
            btnStop.Enabled = connected && scanning;
            btnLearn.Enabled = connected;
            btnViewBackground.Enabled = background.Count > 0;
            btnSaveProfile.Enabled = true;
            btnLoadProfile.Enabled = true;

            btnGpo1.Enabled = connected && !scanning;
            btnGpo2.Enabled = connected && !scanning;
            btnGpo3.Enabled = connected && !scanning;
            btnGpo4.Enabled = connected && !scanning;
            chkBuzzer.Enabled = connected && !scanning;
        }

        private void FormatTagRow(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) { return; }
            DataGridViewRow row = gridTags.Rows[e.RowIndex];

            object rssiValue = row.Cells["RSSI"].Value;
            int rssi;
            if (rssiValue != null && int.TryParse(rssiValue.ToString(), out rssi))
            {
                if (rssi >= -60) { e.CellStyle.BackColor = Color.FromArgb(232, 248, 234); }
                else if (rssi >= -67) { e.CellStyle.BackColor = Color.FromArgb(255, 249, 219); }
                else { e.CellStyle.BackColor = Color.FromArgb(255, 235, 230); }
            }

            object loteValue = row.Cells["Lote"].Value;
            if (loteValue != null && loteValue.ToString() == "(no Marijoa)")
            {
                e.CellStyle.ForeColor = Color.DarkRed;
            }
        }

        private void HandleServiceLog(string message)
        {
            SafeLog(message);
        }

        private void SafeLog(string message)
        {
            if (IsDisposed) { return; }
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action<string>(SafeLog), message);
                    return;
                }
                AppendLog(message);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void AppendLog(string message)
        {
            if (txtLog == null || txtLog.IsDisposed) { return; }
            if (txtLog.TextLength > 50000) { txtLog.Clear(); }
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }

        private void Log(string message)
        {
            SafeLog(message);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            uiTimer.Stop();
            try { service.Dispose(); }
            catch { }
        }

        private static string NormalizeEpc(string epc)
        {
            string text = (epc == null ? "" : epc.Trim()).ToLowerInvariant();
            if (text.Length == 0) { return "(vacio)"; }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9') { return "(no Marijoa)"; }
            }

            string clean = text.TrimStart('0');
            return clean.Length == 0 ? "(cero)" : clean;
        }

        private static string JoinList(List<int> values)
        {
            if (values == null || values.Count == 0) { return "-"; }
            string text = "";
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) { text += ", "; }
                text += values[i];
            }
            return text;
        }

        private static string JoinArray(int[] values)
        {
            if (values == null || values.Length == 0) { return "-"; }
            string text = "";
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) { text += ", "; }
                text += values[i];
            }
            return text;
        }

        private static string JoinLoss(int[] values)
        {
            if (values == null || values.Length == 0) { return "-"; }
            string text = "";
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) { text += ", "; }
                text += values[i] < 0 ? "-" : values[i].ToString();
            }
            return text;
        }
    }
}
