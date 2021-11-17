using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System;
using System.Linq;
using Advanced_Combat_Tracker;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Threading;
using System.IO;
using System.Text;
using System.Xml;
using System.Drawing;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Drawing.Text;

#region Properties\AssemblyInfo.cs

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("ACT_Adder")]
[assembly: AssemblyDescription("Adds numbers from /groupsay")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Mineeme")]
[assembly: AssemblyProduct("ACT_Adder")]
[assembly: AssemblyCopyright("Copyright ©  2021")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("0991c9ae-84a1-4a6d-b03f-6f9b75f24024")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers
// by using the '*' as ("1.0.*")
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

#endregion Properties\AssemblyInfo.cs

#region Adder.cs
// reference:System.Core.dll
// reference:System.Net.http.dll

namespace ACT_Adder
{
    public partial class Adder : UserControl, IActPluginV1
    {
        Label lblStatus;
        string settingsFile = Path.Combine(ActGlobals.oFormActMain.AppDataFolder.FullName, "Config\\Adder.config.xml");
        SettingsSerializer xmlSettings;
        bool initializing = true;
        bool importing = false;

        const int logTimeStampLength = 39; //# of chars in the timestamp
        const string logTimeStampRegexStr = @"^\(.{36}\] ";
        const string playerOrYou = @"((?<player>You)|\\aPC [^ ]+ (?<player>[^:]+):\w+\\/a) ";
        const string groupSay = playerOrYou + @"says? to the group, """;
        const string numSay = @"(?<count>\d+)""";
        const string targetSay = @"n[^ ]* (?<target>\d+)""";
        const string died = @"\\#FE642E(?<player>\w+) dies, taking their (?<count>\d+) increments of Wrath";
        const string numbers = @"\\aNPC \d+ The Abandoned Labomination:The Abandoned Labomination\\/a says, .Nuuuummmm";
        Regex reCount = new Regex(logTimeStampRegexStr + groupSay + numSay, RegexOptions.Compiled);
        Regex reTarget = new Regex(logTimeStampRegexStr + groupSay + targetSay, RegexOptions.Compiled);
        Regex reDied = new Regex(logTimeStampRegexStr + died, RegexOptions.Compiled);
        Regex reWinStart = new Regex(logTimeStampRegexStr + numbers, RegexOptions.Compiled);

        BindingList<Player> gridData_;
        public BindingList<Player> gridData { get { return gridData_; } }
        Player Need = new Player { count = "0", when = DateTime.MinValue, name = "Target" };

        Floater floater;
        string floatLoc = string.Empty;
        string floatSize = string.Empty;
        bool floaterShown = false;

        private SynchronizationContext _synchronizationContext;
        DateTime mostRecent = DateTime.MinValue;
        TimeSpan timeWindow;
        DateTime announced = DateTime.MinValue;
        int announcedTotal = -1;

        string macroFilePath;
        string curesFilePath;
        const string shoutMacroFile = "lab-macro.txt";
        const string curesMacroFile = "lab-cures.txt";
        string cureFormat = @"useabilityonplayer ""{0}"" Cure Curse ;g curing {1}";
        string undeterminedCure = "cure not available";

        public Adder()
        {
            InitializeComponent();
            _synchronizationContext = WindowsFormsSynchronizationContext.Current;
        }

        public void DeInitPlugin()
        {
            ActGlobals.oFormActMain.OnLogLineRead -= OFormActMain_OnLogLineRead;
            if (floaterShown)
            {
                floatLoc = floater.Location.ToString();
                floatSize = floater.Size.ToString();
            }
            SaveSettings();
            lblStatus.Text = "Plugin Exited";
        }

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            lblStatus = pluginStatusText;           // Save the status label's reference
            pluginScreenSpace.Controls.Add(this);   // Add this UserControl to the tab ACT provides
            this.Dock = DockStyle.Fill;             // Expand the UserControl to fill the tab's client space

            xmlSettings = new SettingsSerializer(this); // Create a new settings serializer and pass it this instance
            LoadSettings();
            initializing = false;

            gridData_ = new BindingList<Player>();
            dataGridView1.DataSource = gridData;

            floater = new Floater(gridData);
            floater.GeometryEvent += Floater_GeometryEvent;
            floater.ClearEvent += Floater_ClearEvent;

            SetTimeWindow();

            macroFilePath = Path.Combine(ActGlobals.oFormActMain.GameMacroFolder, shoutMacroFile);
            curesFilePath = Path.Combine(ActGlobals.oFormActMain.GameMacroFolder, curesMacroFile);
            SetMacroHelp();

            ActGlobals.oFormActMain.OnLogLineRead += OFormActMain_OnLogLineRead;

            if (ActGlobals.oFormActMain.GetAutomaticUpdatesAllowed())
            {
                // If ACT is set to automatically check for updates, check for updates to the plugin
                // If we don't put this on a separate thread, web latency will delay the plugin init phase
                new Thread(new ThreadStart(oFormActMain_UpdateCheckClicked)).Start();
            }

            pluginStatusText.Text = "Plugin Started";
        }

        void oFormActMain_UpdateCheckClicked()
        {
            try
            {
                Version localVersion = this.GetType().Assembly.GetName().Version;
                Task<Version> vtask = Task.Run(() => { return GetRemoteVersionAsync(); });
                vtask.Wait();
                if (vtask.Result > localVersion)
                {
                    DialogResult result = MessageBox.Show("There is an updated version of the Adder Plugin.\n\nSee the changes by clicking the About link in the plugin.\n\nUpdate it now?\n\n(If there is an update to ACT, you should click No and update ACT first.)", 
                        "New Version", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                    {
                        Task<FileInfo> ftask = Task.Run(() => { return GetRemoteFileAsync(); });
                        ftask.Wait();
                        if (ftask.Result != null)
                        {
                            ActPluginData pluginData = ActGlobals.oFormActMain.PluginGetSelfData(this);
                            pluginData.pluginFile.Delete();
                            File.Move(ftask.Result.FullName, pluginData.pluginFile.FullName);
                            Application.DoEvents();
                            ThreadInvokes.CheckboxSetChecked(ActGlobals.oFormActMain, pluginData.cbEnabled, false);
                            Application.DoEvents();
                            ThreadInvokes.CheckboxSetChecked(ActGlobals.oFormActMain, pluginData.cbEnabled, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ActGlobals.oFormActMain.WriteExceptionLog(ex, "Adder Plugin Update Download");
            }
        }

        private async Task<Version> GetRemoteVersionAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    ProductInfoHeaderValue hdr = new ProductInfoHeaderValue("ACT_Adder", "1");
                    client.DefaultRequestHeaders.UserAgent.Add(hdr);
                    HttpResponseMessage response = await client.GetAsync(@"https://api.github.com/repos/jeffjl74/ACT_Adder/releases/latest");
                    if (response.IsSuccessStatusCode)
                    {
                        //response.EnsureSuccessStatusCode();
                        string responseBody = await response.Content.ReadAsStringAsync();
                        Regex reVer = new Regex(@".tag_name.:.v([^""]+)""");
                        Match match = reVer.Match(responseBody);
                        if (match.Success)
                            return new Version(match.Groups[1].Value);
                    }
                    return new Version("0.0.0");
                }
            }
            catch { return new Version("0.0.0"); }
        }

        private async Task<FileInfo> GetRemoteFileAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    ProductInfoHeaderValue hdr = new ProductInfoHeaderValue("ACT_Adder", "1");
                    client.DefaultRequestHeaders.UserAgent.Add(hdr);
                    HttpResponseMessage response = await client.GetAsync(@"https://github.com/jeffjl74/ACT_Adder/releases/latest/download/adder.cs");
                    if (response.IsSuccessStatusCode)
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        string tmp = Path.GetTempFileName();
                        File.WriteAllText(tmp, responseBody);
                        FileInfo fi = new FileInfo(tmp);
                        return fi;
                    }
                }
                return null;
            }
            catch { return null; }
        }

        private void OFormActMain_OnLogLineRead(bool isImport, LogLineEventArgs logInfo)
        {
            importing = isImport;
            if (logInfo.detectedType == 0 && logInfo.logLine.Length > logTimeStampLength)
            {
                Match match = reCount.Match(logInfo.logLine);
                if (!match.Success)
                    match = reDied.Match(logInfo.logLine);
                if(match.Success)
                {
                    Player p = new Player 
                    { 
                        name = match.Groups["player"].Value, 
                        count = match.Groups["count"].Value, 
                        when = logInfo.detectedTime 
                    };
                    _synchronizationContext.Post(UpdatePlayer, p);
                }
                else
                {
                    match = reTarget.Match(logInfo.logLine);
                    if(match.Success)
                    {
                        Need.count = match.Groups["target"].Value;
                        Need.when = logInfo.detectedTime;
                        _synchronizationContext.Post(UpdateTarget, null);
                    }
                    else
                    {
                        match = reWinStart.Match(logInfo.logLine);
                        if (match.Success)
                            _synchronizationContext.Send(StartCountdown, null);
                    }
                }
            }
        }

        void LoadSettings()
        {
            xmlSettings.AddControlSetting(textBoxSeconds.Name, textBoxSeconds);
            xmlSettings.AddControlSetting(checkBoxPop.Name, checkBoxPop);
            xmlSettings.AddStringSetting("floatLoc");
            xmlSettings.AddStringSetting("floatSize");

            if (File.Exists(settingsFile))
            {
                FileStream fs = new FileStream(settingsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                XmlTextReader xReader = new XmlTextReader(fs);

                try
                {
                    while (xReader.Read())
                    {
                        if (xReader.NodeType == XmlNodeType.Element)
                        {
                            if (xReader.LocalName == "SettingsSerializer")
                            {
                                xmlSettings.ImportFromXml(xReader);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    lblStatus.Text = "Error loading settings: " + ex.Message;
                }
                xReader.Close();
            }
        }

        void SaveSettings()
        {
            FileStream fs = new FileStream(settingsFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            XmlTextWriter xWriter = new XmlTextWriter(fs, Encoding.UTF8);
            xWriter.Formatting = Formatting.Indented;
            xWriter.Indentation = 1;
            xWriter.IndentChar = '\t';
            xWriter.WriteStartDocument(true);
            xWriter.WriteStartElement("Config");    // <Config>
            xWriter.WriteStartElement("SettingsSerializer");    // <Config><SettingsSerializer>
            xmlSettings.ExportToXml(xWriter);   // Fill the SettingsSerializer XML
            xWriter.WriteEndElement();  // </SettingsSerializer>
            xWriter.WriteEndElement();  // </Config>
            xWriter.WriteEndDocument(); // Tie up loose ends (shouldn't be any)
            xWriter.Flush();    // Flush the file buffer to disk
            xWriter.Close();
        }

        private void SetMacroHelp()
        {
            dataGridView2.Rows.Add("cure players", "/do_file_commands " + curesMacroFile);
            dataGridView2.Rows.Add("shout cures", "/do_file_commands " + shoutMacroFile);
            dataGridView2.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            dataGridView2.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            // set the height of the dgv from the number of rows
            int height = dataGridView2.ColumnHeadersHeight - 1;
            foreach (DataGridViewRow dr in dataGridView2.Rows)
            {
                height += dr.Height;
            }
            dataGridView2.Height = height;
        }

        // UI thread
        void StartCountdown(object o)
        {
            ActGlobals.oFormActMain.SendToMacroFile(macroFilePath, undeterminedCure, "say ");
            ActGlobals.oFormActMain.SendToMacroFile(curesFilePath, undeterminedCure, "g");
            progressControl1.StartProgress();
            floater.StartProgress();
        }

        // UI thread
        void UpdatePlayer(object o)
        {
            Player p = o as Player;
            if(p != null)
            {
                DateTime start = p.when - timeWindow;
                //remove old need?
                if (Need.when < start && !string.IsNullOrEmpty(textBoxCures.Text))
                {
                    textBoxTarget.Text = string.Empty;
                    floater.SetNeed(string.Empty);
                    textBoxCures.Text = string.Empty;
                    floater.SetCure(string.Empty);
                }

                // update player
                Player found = gridData.SingleOrDefault(x => x.name == p.name);
                if (found == null)
                { 
                    // new player
                    gridData.Add(p);
                    dataGridView1.Columns["name"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                    dataGridView1.Columns["count"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                    dataGridView1.Columns["when"].DefaultCellStyle.Format = "T";
                    dataGridView1.Columns["when"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                    if(checkBoxPop.Checked && floater.Visible == false)
                        ShowFloater();
                }
                else
                {
                    found.count = p.count;
                    found.when = p.when;
                }
                mostRecent = p.when;

                // remove old tells
                foreach (Player player in gridData)
                {
                    if (player.when < start)
                    {
                        player.count = string.Empty;
                    }
                }

                SearchForTarget();
            }
        }

        private void ShowFloater()
        {
            floater.Show();
            floaterShown = true;
            floater.Location = StringToPoint(floatLoc);
            floater.Size = StringToSize(floatSize);
            floater.TopMost = true;
        }

        // UI thread
        void UpdateTarget(object o)
        {
            mostRecent = Need.when;
            textBoxTarget.Text = Need.count;
            floater.SetNeed(Need.count);
            textBoxCures.Text = string.Empty;
            floater.SetCure(string.Empty);
            if (checkBoxPop.Checked && floater.Visible == false)
                ShowFloater();
            SearchForTarget();
        }

        // UI thread.
        // If there is a recent "need" total,
        // sum the players' numbers, two at a time, looking for the needed total.
        void SearchForTarget()
        {
            if (mostRecent != DateTime.MinValue)
            {
                DateTime start = mostRecent - timeWindow;
                if(Need.when >= start)
                {
                    int playerCount = gridData_.Count;
                    int added = Need.IntCount();
                    bool found = false;
                    for(int i = 0; i< playerCount && !found; i++)
                    {
                        int outerCount = gridData_[i].IntCount();
                        if (gridData_[i].when < start || outerCount == 0)
                            continue; // skip anyone who has not reported
                        for(int j=i+1; j<playerCount; j++)
                        {
                            int innerCount = gridData_[j].IntCount();
                            if (gridData_[j].when < start || innerCount == 0)
                                continue; // skip anyone who has not reported
                            if (innerCount + outerCount == added)
                            {
                                //found a match
                                found = true;
                                string p1 = gridData_[i].name.Equals("You") ? ActGlobals.charName : gridData_[i].name;
                                string p2 = gridData_[j].name.Equals("You") ? ActGlobals.charName : gridData_[j].name;
                                string cure = "cure " + p1 + " and " + p2;
                                textBoxCures.Text = cure;
                                floater.SetCure(cure);
                                // only need one announcement
                                // announce if the previous one is old, or if the total changed (i.e. a corrected 'need')
                                if (announced < start || (announced >= start && announcedTotal != added))
                                {
                                    if(!importing)
                                        ActGlobals.oFormActMain.TTS(cure);
                                    announced = mostRecent;
                                    announcedTotal = added;
                                    ActGlobals.oFormActMain.SendToMacroFile(macroFilePath, cure, "shout ");
                                    ActGlobals.oFormActMain.SendToMacroFile(curesFilePath, 
                                        "cancel_spellcast" + Environment.NewLine
                                        + string.Format(cureFormat, p1, p1) + Environment.NewLine 
                                        + string.Format(cureFormat, p2, p2), "");
                                }
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void buttonClear_Click(object sender, EventArgs e)
        {
            gridData.Clear();
            Need.when = DateTime.MinValue;
            announced = DateTime.MinValue;
            textBoxCures.Text = string.Empty;
            floater.SetCure(string.Empty);
            textBoxTarget.Text = string.Empty;
            floater.SetNeed(string.Empty);
            progressControl1.ClearProgress();
            floater.StopProgress();
        }

        private void textBoxSeconds_TextChanged(object sender, EventArgs e)
        {
            if (!initializing)
            {
                SetTimeWindow();
                SaveSettings();
            }
        }

        private void SetTimeWindow()
        {
            int secs;
            if (!int.TryParse(textBoxSeconds.Text, out secs))
                secs = 30;
            timeWindow = new TimeSpan(0, 0, secs);
            progressControl1.ProgressMaximum = secs;
            floater.SetProgressMax(secs);
        }

        private void textBoxTarget_Leave(object sender, EventArgs e)
        {
            Need.count = textBoxTarget.Text;
            floater.SetNeed(Need.count);
            SearchForTarget();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                VisitLink();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void VisitLink()
        {
            // Change the color of the link text by setting LinkVisited
            // to true.
            linkLabel1.LinkVisited = true;
            //Call the Process.Start method to open the default browser
            //with a URL:
            System.Diagnostics.Process.Start("https://github.com/jeffjl74/ACT_Adder#adder-plugin-for-advanced-combat-tracker");
        }

        private List<int> StringToInts(string input)
        {
            List<int> result = new List<int>();
            if (!string.IsNullOrEmpty(input))
            {
                // remove everything that is not a digit or a comma
                string clean = Regex.Replace(input, @"[^0-9,]+", "");
                string[] coords = clean.Split(',');
                for (int i = 0; i < coords.Length; i++)
                    result.Add(int.Parse(coords[i]));
            }
            return result;
        }

        private Point StringToPoint(string input)
        {
            List<int> coords = StringToInts(input);
            if (coords.Count == 2)
                return new Point(coords[0], coords[1]);
            else
            {
                // default location of the popup
                // roughly center in the plugin
                Point pt = new Point(this.Left + this.Width / 2, this.Top + this.Height / 2);
                Point pt2 = this.ParentForm.PointToScreen(pt);
                return pt2;
            }
        }

        private Size StringToSize(string input)
        {
            List<int> coords = StringToInts(input);
            if (coords.Count == 2)
                return new Size(coords[0], coords[1]);
            else
                return new Size(200, 225); // default size of the popup
        }

        private void checkBoxPop_CheckedChanged(object sender, EventArgs e)
        {
            if (!initializing)
            {
                if (!checkBoxPop.Checked)
                    floater.Visible = false;
                else
                {
                    if (floater.Visible == false)
                    {
                        ShowFloater();
                    }
                }
            }
        }

        // save floating window size and position
        private void Floater_GeometryEvent(object sender, EventArgs e)
        {
            Floater.GeometryEventArgs args = e as Floater.GeometryEventArgs;
            if (args != null)
            {
                floatLoc = args.location.ToString();
                floatSize = args.size.ToString();
                SaveSettings();
            }
        }

        private void Floater_ClearEvent(object sender, EventArgs e)
        {
            buttonClear_Click(sender, e);
            progressControl1.ClearProgress();
        }

        private void dataGridView1_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e != null)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == dataGridView1.Columns["count"].Index)
                {
                    if (e.Value != null)
                    {
                        if (string.IsNullOrEmpty(e.Value.ToString()))
                            dataGridView1.Rows[e.RowIndex].Cells["name"].Style.ForeColor = Color.Red;
                        else
                            dataGridView1.Rows[e.RowIndex].Cells["name"].Style.ForeColor = Color.Black;
                    }
                }
            }
        }
    }
}

#endregion Adder.cs

#region Adder.Designer.cs

namespace ACT_Adder
{
    public partial class Adder
    {
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            this.label1 = new System.Windows.Forms.Label();
            this.textBoxTarget = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.textBoxCures = new System.Windows.Forms.TextBox();
            this.buttonClear = new System.Windows.Forms.Button();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.label3 = new System.Windows.Forms.Label();
            this.textBoxSeconds = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.linkLabel1 = new System.Windows.Forms.LinkLabel();
            this.checkBoxPop = new System.Windows.Forms.CheckBox();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.progressControl1 = new ACT_Adder.ProgressControl();
            this.dataGridView2 = new System.Windows.Forms.DataGridView();
            this.Purpose = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Command = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.label5 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.progressControl1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView2)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(6, 219);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(36, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Need:";
            // 
            // textBoxTarget
            // 
            this.textBoxTarget.Location = new System.Drawing.Point(50, 216);
            this.textBoxTarget.Name = "textBoxTarget";
            this.textBoxTarget.Size = new System.Drawing.Size(55, 20);
            this.textBoxTarget.TabIndex = 1;
            this.textBoxTarget.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.toolTip1.SetToolTip(this.textBoxTarget, "Required total from adding two numbers");
            this.textBoxTarget.Leave += new System.EventHandler(this.textBoxTarget_Leave);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(7, 245);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(37, 13);
            this.label2.TabIndex = 5;
            this.label2.Text = "Cures:";
            // 
            // textBoxCures
            // 
            this.textBoxCures.Location = new System.Drawing.Point(50, 242);
            this.textBoxCures.Name = "textBoxCures";
            this.textBoxCures.Size = new System.Drawing.Size(281, 20);
            this.textBoxCures.TabIndex = 6;
            // 
            // buttonClear
            // 
            this.buttonClear.Location = new System.Drawing.Point(164, 268);
            this.buttonClear.Name = "buttonClear";
            this.buttonClear.Size = new System.Drawing.Size(75, 23);
            this.buttonClear.TabIndex = 8;
            this.buttonClear.Text = "Clear";
            this.toolTip1.SetToolTip(this.buttonClear, "Clear the current data");
            this.buttonClear.UseVisualStyleBackColor = true;
            this.buttonClear.Click += new System.EventHandler(this.buttonClear_Click);
            // 
            // dataGridView1
            // 
            this.dataGridView1.AllowUserToAddRows = false;
            this.dataGridView1.AllowUserToDeleteRows = false;
            this.dataGridView1.AllowUserToResizeRows = false;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            dataGridViewCellStyle1.ForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle1.SelectionForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
            this.dataGridView1.DefaultCellStyle = dataGridViewCellStyle1;
            this.dataGridView1.Location = new System.Drawing.Point(0, 0);
            this.dataGridView1.MultiSelect = false;
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.ReadOnly = true;
            this.dataGridView1.Size = new System.Drawing.Size(399, 210);
            this.dataGridView1.TabIndex = 11;
            this.dataGridView1.TabStop = false;
            this.dataGridView1.CellFormatting += new System.Windows.Forms.DataGridViewCellFormattingEventHandler(this.dataGridView1_CellFormatting);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(126, 219);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(34, 13);
            this.label3.TabIndex = 2;
            this.label3.Text = "within";
            this.label3.TextAlign = System.Drawing.ContentAlignment.TopRight;
            // 
            // textBoxSeconds
            // 
            this.textBoxSeconds.Location = new System.Drawing.Point(164, 216);
            this.textBoxSeconds.Name = "textBoxSeconds";
            this.textBoxSeconds.Size = new System.Drawing.Size(36, 20);
            this.textBoxSeconds.TabIndex = 3;
            this.textBoxSeconds.Text = "30";
            this.textBoxSeconds.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.textBoxSeconds.TextChanged += new System.EventHandler(this.textBoxSeconds_TextChanged);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(204, 219);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(47, 13);
            this.label4.TabIndex = 4;
            this.label4.Text = "seconds";
            // 
            // linkLabel1
            // 
            this.linkLabel1.AutoSize = true;
            this.linkLabel1.Location = new System.Drawing.Point(4, 289);
            this.linkLabel1.Name = "linkLabel1";
            this.linkLabel1.Size = new System.Drawing.Size(35, 13);
            this.linkLabel1.TabIndex = 9;
            this.linkLabel1.TabStop = true;
            this.linkLabel1.Text = "About";
            this.toolTip1.SetToolTip(this.linkLabel1, "Link to the plugin description");
            this.linkLabel1.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabel1_LinkClicked);
            // 
            // checkBoxPop
            // 
            this.checkBoxPop.AutoSize = true;
            this.checkBoxPop.Checked = true;
            this.checkBoxPop.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxPop.Location = new System.Drawing.Point(337, 244);
            this.checkBoxPop.Name = "checkBoxPop";
            this.checkBoxPop.Size = new System.Drawing.Size(57, 17);
            this.checkBoxPop.TabIndex = 7;
            this.checkBoxPop.Text = "Popup";
            this.toolTip1.SetToolTip(this.checkBoxPop, "Check to show pop-up window");
            this.checkBoxPop.UseVisualStyleBackColor = true;
            this.checkBoxPop.CheckedChanged += new System.EventHandler(this.checkBoxPop_CheckedChanged);
            // 
            // progressControl1
            // 
            this.progressControl1.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.progressControl1.Location = new System.Drawing.Point(258, 217);
            this.progressControl1.Name = "progressControl1";
            this.progressControl1.ProgressMaximum = 30;
            this.progressControl1.ProgressMinimum = 0;
            this.progressControl1.ProgressValue = 0;
            this.progressControl1.Size = new System.Drawing.Size(141, 20);
            this.progressControl1.TabIndex = 11;
            this.progressControl1.TabStop = false;
            this.toolTip1.SetToolTip(this.progressControl1, "Time remaining in the current cycle");
            // 
            // dataGridView2
            // 
            this.dataGridView2.AllowUserToAddRows = false;
            this.dataGridView2.AllowUserToDeleteRows = false;
            this.dataGridView2.AllowUserToResizeRows = false;
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            dataGridViewCellStyle2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            dataGridViewCellStyle2.ForeColor = System.Drawing.SystemColors.WindowText;
            dataGridViewCellStyle2.Padding = new System.Windows.Forms.Padding(1);
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle2.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.dataGridView2.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle2;
            this.dataGridView2.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView2.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.Purpose,
            this.Command});
            this.dataGridView2.EnableHeadersVisualStyles = false;
            this.dataGridView2.Location = new System.Drawing.Point(9, 337);
            this.dataGridView2.Name = "dataGridView2";
            this.dataGridView2.ReadOnly = true;
            this.dataGridView2.RowHeadersVisible = false;
            this.dataGridView2.Size = new System.Drawing.Size(385, 89);
            this.dataGridView2.TabIndex = 10;
            // 
            // Purpose
            // 
            dataGridViewCellStyle3.SelectionBackColor = System.Drawing.Color.White;
            dataGridViewCellStyle3.SelectionForeColor = System.Drawing.Color.Black;
            this.Purpose.DefaultCellStyle = dataGridViewCellStyle3;
            this.Purpose.HeaderText = "Purpose";
            this.Purpose.Name = "Purpose";
            this.Purpose.ReadOnly = true;
            // 
            // Command
            // 
            this.Command.HeaderText = "EQII Command (can be placed in a macro)";
            this.Command.Name = "Command";
            this.Command.ReadOnly = true;
            // 
            // label5
            // 
            this.label5.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.label5.Location = new System.Drawing.Point(2, 312);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(390, 2);
            this.label5.TabIndex = 12;
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(10, 318);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(241, 13);
            this.label6.TabIndex = 13;
            this.label6.Text = "EQII commands created when a solution is found:";
            // 
            // Adder
            // 
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.dataGridView2);
            this.Controls.Add(this.progressControl1);
            this.Controls.Add(this.checkBoxPop);
            this.Controls.Add(this.linkLabel1);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.textBoxSeconds);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.dataGridView1);
            this.Controls.Add(this.buttonClear);
            this.Controls.Add(this.textBoxCures);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.textBoxTarget);
            this.Controls.Add(this.label1);
            this.Name = "Adder";
            this.Size = new System.Drawing.Size(402, 470);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.progressControl1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView2)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }
        private Label label1;
        private TextBox textBoxTarget;
        private Label label2;
        private TextBox textBoxCures;
        private Button buttonClear;
        private DataGridView dataGridView1;
        private Label label3;
        private TextBox textBoxSeconds;
        private Label label4;
        private LinkLabel linkLabel1;
        private CheckBox checkBoxPop;
        private ToolTip toolTip1;
        private System.ComponentModel.IContainer components;
        private ProgressControl progressControl1;
        private DataGridView dataGridView2;
        private Label label5;
        private Label label6;
        private DataGridViewTextBoxColumn Purpose;
        private DataGridViewTextBoxColumn Command;
    }
}

#endregion Adder.Designer.cs

#region Player.cs

namespace ACT_Adder
{
    public class Player : INotifyPropertyChanged
    {
        string name_;
        string count_;
        DateTime when_;

		public string name 
		{ 
			get { return name_; }
			set { name_ = value; NotifyPropertyChanged("name"); }
		}
		public string count
		{
			get { return count_; }
			set { count_ = value; NotifyPropertyChanged("count"); }
		}
		public DateTime when
		{
			get { return when_; }
			set { when_ = value; NotifyPropertyChanged("when"); }
		}

        public event PropertyChangedEventHandler PropertyChanged;

		private void NotifyPropertyChanged(string p)
        {
			if (PropertyChanged != null)
				PropertyChanged(this, new PropertyChangedEventArgs(p));
		}

        public int IntCount()
        {
			int result;
			int.TryParse(count, out result);
			return result;
        }
		
        public override string ToString()
        {
            return string.Format("{0}:{1}", name, count);
        }
    }
}

#endregion Player.cs

#region Floater.cs

namespace ACT_Adder
{
    public partial class Floater : Form
    {
        BindingList<Player> data_;

        public event EventHandler ClearEvent;
        public event EventHandler GeometryEvent;
        public class GeometryEventArgs : EventArgs
        {
            public Size size { get; set; }
            public Point location { get; set; }
        }

        public Floater(BindingList<Player> data)
        {
            InitializeComponent();
            data_ = data;
        }

        private void Floater_Shown(object sender, EventArgs e)
        {
            dataGridView1.DataSource = data_;
            dataGridView1.Columns["when"].Visible = false;
            dataGridView1.Columns["name"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dataGridView1.Columns["count"].Width = 40;
            TopMost = true;
        }

        // just hide (not close) if user presses the [X]
        private void Floater_FormClosing(object sender, FormClosingEventArgs e)
        {
            if(e.CloseReason == CloseReason.UserClosing)
            {
                this.Hide();
                e.Cancel = true;
            }
        }

        public void SetCure(string txt)
        {
            textBoxCure.Text = txt;
        }

        // "need" goes in the window header
        public void SetNeed(string txt)
        {
            if (string.IsNullOrEmpty(txt))
                this.Text = "Adder";
            else
                this.Text = "Adder - need " + txt;
        }

        public void SetProgressMax(int seconds)
        {
            progressControl1.ProgressMaximum = seconds;
        }

        public void StartProgress()
        {
            progressControl1.StartProgress();
        }

        public void StopProgress()
        {
            progressControl1.ClearProgress();
        }

        // save changes to size/loc by telling the main form about them
        private void Floater_ResizeEnd(object sender, EventArgs e)
        {
            if (GeometryEvent != null)
            {
                GeometryEventArgs args = new GeometryEventArgs { size = this.Size, location = this.Location };
                GeometryEvent.Invoke(this, args);
            }
        }

        // tell the main form to clear the data
        private void textBoxCure_ClickX(object sender, EventArgs e)
        {
            progressControl1.ClearProgress();
            if (ClearEvent != null)
                ClearEvent.Invoke(this, new EventArgs());
        }

        // red text for players that have not reported
        private void dataGridView1_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if(e != null)
            {
                if(e.RowIndex >= 0 && e.ColumnIndex == dataGridView1.Columns["count"].Index)
                {
                    if(e.Value != null)
                    {
                        if (string.IsNullOrEmpty(e.Value.ToString()))
                            dataGridView1.Rows[e.RowIndex].Cells["name"].Style.ForeColor = Color.Red;
                        else
                            dataGridView1.Rows[e.RowIndex].Cells["name"].Style.ForeColor = Color.Black;
                    }
                }
            }
        }

    }
}

#endregion Floater.cs

#region Floater.Designer.cs

namespace ACT_Adder
{
    partial class Floater
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.panel1 = new System.Windows.Forms.Panel();
            this.progressControl1 = new ACT_Adder.ProgressControl();
            this.textBoxCure = new ACT_Adder.TextBoxX();
            this.panel2 = new System.Windows.Forms.Panel();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.progressControl1)).BeginInit();
            this.panel2.SuspendLayout();
            this.SuspendLayout();
            // 
            // dataGridView1
            // 
            this.dataGridView1.AllowUserToAddRows = false;
            this.dataGridView1.AllowUserToDeleteRows = false;
            this.dataGridView1.AllowUserToResizeRows = false;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.ColumnHeadersVisible = false;
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            dataGridViewCellStyle1.ForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle1.SelectionForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
            this.dataGridView1.DefaultCellStyle = dataGridViewCellStyle1;
            this.dataGridView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGridView1.Location = new System.Drawing.Point(0, 0);
            this.dataGridView1.MultiSelect = false;
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.ReadOnly = true;
            this.dataGridView1.RowHeadersVisible = false;
            this.dataGridView1.Size = new System.Drawing.Size(159, 141);
            this.dataGridView1.TabIndex = 0;
            this.dataGridView1.TabStop = false;
            this.dataGridView1.CellFormatting += new System.Windows.Forms.DataGridViewCellFormattingEventHandler(this.dataGridView1_CellFormatting);
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.progressControl1);
            this.panel1.Controls.Add(this.textBoxCure);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
            this.panel1.Location = new System.Drawing.Point(0, 0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(159, 44);
            this.panel1.TabIndex = 2;
            // 
            // progressControl1
            // 
            this.progressControl1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressControl1.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.progressControl1.Location = new System.Drawing.Point(3, 21);
            this.progressControl1.Name = "progressControl1";
            this.progressControl1.ProgressMaximum = 30;
            this.progressControl1.ProgressMinimum = 0;
            this.progressControl1.ProgressValue = 0;
            this.progressControl1.Size = new System.Drawing.Size(153, 20);
            this.progressControl1.TabIndex = 1;
            this.progressControl1.TabStop = false;
            // 
            // textBoxCure
            // 
            this.textBoxCure.ButtonTextClear = true;
            this.textBoxCure.Dock = System.Windows.Forms.DockStyle.Fill;
            this.textBoxCure.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.textBoxCure.Location = new System.Drawing.Point(0, 0);
            this.textBoxCure.Name = "textBoxCure";
            this.textBoxCure.ReadOnly = true;
            this.textBoxCure.Size = new System.Drawing.Size(159, 20);
            this.textBoxCure.TabIndex = 0;
            this.textBoxCure.TabStop = false;
            this.textBoxCure.ClickX += new System.EventHandler(this.textBoxCure_ClickX);
            // 
            // panel2
            // 
            this.panel2.Controls.Add(this.dataGridView1);
            this.panel2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel2.Location = new System.Drawing.Point(0, 44);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(159, 141);
            this.panel2.TabIndex = 3;
            // 
            // Floater
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(159, 185);
            this.Controls.Add(this.panel2);
            this.Controls.Add(this.panel1);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "Floater";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.Text = "Adder";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Floater_FormClosing);
            this.Shown += new System.EventHandler(this.Floater_Shown);
            this.ResizeEnd += new System.EventHandler(this.Floater_ResizeEnd);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.progressControl1)).EndInit();
            this.panel2.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Panel panel2;
        private TextBoxX textBoxCure;
        private ProgressControl progressControl1;
    }
}
#endregion Floater.Designer.cs

#region TextBoxX.cs

namespace ACT_Adder
{
    public partial class TextBoxX : TextBox
    {
        private readonly Label lblClear;

        // new event handler for the X "button"
        [Browsable(true)]
        [Category("Action")]
        [Description("Invoked when user clicks X")]
        public event EventHandler ClickX;

        // required TextBox stuff
        public bool ButtonTextClear { get; set; }
        public AutoScaleMode AutoScaleMode;

        public TextBoxX()
        {
            InitializeComponent();

            ButtonTextClear = true;

            Resize += PositionX;

            lblClear = new Label()
            {
                Location = new Point(100, 0),
                AutoSize = true,
                Text = " x ",
                ForeColor = Color.Gray,
                Font = new Font("Tahoma", 8.25F),
                Cursor = Cursors.Arrow
            };

            Controls.Add(lblClear);
            lblClear.Click += LblClear_Click;
            lblClear.BringToFront();
        }

        private void LblClear_Click(object sender, EventArgs e)
        {
            Text = string.Empty; 
            ButtonX_Click(sender, e);
        }

        protected void ButtonX_Click(object sender, EventArgs e)
        {
            // report the event to the parent
            if (ClickX != null)
                ClickX(this, e);
        }

        private void PositionX(object sender, EventArgs e)
        { 
            lblClear.Location = new Point(Width - lblClear.Width, ((Height - lblClear.Height) / 2) - 1); 
        }
    }
}

#endregion TextBoxX.cs

#region TextBoxX.Designer.cs

namespace ACT_Adder
{
    partial class TextBoxX
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        }

        #endregion
    }
}

#endregion TextBoxX.Designer.cs

#region ProgressControl.cs

namespace ACT_Adder
{
    // Progress bar that counts down from ProgressMaximum to ProgressMinimum
    // at one count per second.
    public partial class ProgressControl : PictureBox
    {
        System.Timers.Timer timer = new System.Timers.Timer();
        
        [Browsable(true)]
        [Category("Design")]
        [Description("Minimum value")]
        public int ProgressMinimum { get { return progressMinimum_; } set { progressMinimum_ = value; } }
        private int progressMinimum_ = 0;

        [Browsable(true)]
        [Category("Design")]
        [Description("Maximum value")]
        public int ProgressMaximum { get { return progressMaximum_; } set { progressMaximum_ = value; } }
        private int progressMaximum_ = 30;

        [Browsable(true)]
        [Category("Design")]
        [Description("Current value")]
        public int ProgressValue { 
            get { return progressValue_; } 
            set 
            {
                progressValue_ = value; 
                Refresh();
                if(!timer.Enabled)
                {
                    timer.Enabled = true;
                    timer.Start();
                }
            } 
        }
        private int progressValue_ = 0;

        // required by PictureBox
        public AutoScaleMode AutoScaleMode;

        public ProgressControl()
        {
            InitializeComponent();

            Paint += ProgressControl_Paint;

            timer.Elapsed += Timer_Elapsed;
            timer.SynchronizingObject = this;
            timer.Interval = 1000;
            timer.Enabled = false;
        }

        public void StartProgress()
        {
            progressValue_ = progressMaximum_;
            Refresh();
            if (!timer.Enabled)
            {
                timer.Enabled = true;
                timer.Start();
            }
        }

        public void ClearProgress()
        {
            if(progressValue_ > progressMinimum_)
                progressValue_ = 1;
        }

        private void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (progressValue_ > progressMinimum_)
            {
                progressValue_--;
                Refresh();
            }
        }

        private void ProgressControl_Paint(object sender, PaintEventArgs e)
        {
            // Clear everything
            e.Graphics.Clear(BackColor);

            // Draw the progress bar.
            float fraction =
                (float)(ProgressValue - ProgressMinimum) /
                (ProgressMaximum - ProgressMinimum);
            int wid = (int)(fraction * ClientSize.Width);
            e.Graphics.FillRectangle(
                Brushes.LightGreen, 0, 0, wid,
                ClientSize.Height);

            // Draw the text.
            e.Graphics.TextRenderingHint =
                TextRenderingHint.AntiAliasGridFit;
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                e.Graphics.DrawString(
                    ProgressValue.ToString(),
                    this.Font, Brushes.Black,
                    ClientRectangle, sf);
            }
        }
    }
}

#endregion ProgressControl.cs

#region ProgressControl.Designer.cs

namespace ACT_Adder
{
    partial class ProgressControl
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        }

        #endregion
    }
}

#endregion ProgressControl.Designer.cs
