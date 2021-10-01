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
using System.Data;

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
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

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

        string macroFile;

        const int logTimeStampLength = 39; //# of chars in the timestamp
        const string logTimeStampRegexStr = @"^\(.{36}\] ";
        const string playerOrYou = @"((?<player>You)|\\aPC [^ ]+ (?<player>[^:]+):\w+\\/a) ";
        const string groupSay = playerOrYou + @"says? to the group, """;
        const string numSay = @"(?<count>\d+)""";
        const string targetSay = @"n[^ ]* (?<target>\d+)""";
        const string died = @"\\#FE642E(?<player>\w+) dies, taking their (?<count>\d+) increments of Wrath";
        Regex reCount = new Regex(logTimeStampRegexStr + groupSay + numSay, RegexOptions.Compiled);
        Regex reTarget = new Regex(logTimeStampRegexStr + groupSay + targetSay, RegexOptions.Compiled);
        Regex reDied = new Regex(logTimeStampRegexStr + died, RegexOptions.Compiled);

        BindingList<Player> gridData_;
        public BindingList<Player> gridData { get { return gridData_; } }
        Player Need = new Player { count = "0", when = DateTime.MinValue, name = "Target" };

        Floater floater;
        string floatLoc = string.Empty;
        string floatSize = string.Empty;

        private SynchronizationContext _synchronizationContext;
        DateTime mostRecent = DateTime.MinValue;
        TimeSpan timeWindow;
        DateTime announced = DateTime.MinValue;
        int announcedTotal = -1;

        public Adder()
        {
            InitializeComponent();
            _synchronizationContext = WindowsFormsSynchronizationContext.Current;
        }

        public void DeInitPlugin()
        {
            ActGlobals.oFormActMain.OnLogLineRead -= OFormActMain_OnLogLineRead;
            floatLoc = floater.Location.ToString();
            floatSize = floater.Size.ToString();
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

            timeWindow = new TimeSpan(0, 0, 30);
            
            macroFile = Path.Combine(ActGlobals.oFormActMain.GameMacroFolder, "lab-macro.txt");

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
                    DialogResult result = MessageBox.Show("There is an updated version of the Adder Plugin.  Update it now?\n\n(If there is an update to ACT, you should click No and update ACT first.)", 
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

        // UI thread
        void UpdatePlayer(object o)
        {
            Player p = o as Player;
            if(p != null)
            {
                //remove old need
                DateTime start = p.when - timeWindow;
                if (Need.when < start && !string.IsNullOrEmpty(textBoxCures.Text))
                {
                    textBoxTarget.Text = string.Empty;
                    floater.SetNeed(string.Empty);
                    textBoxCures.Text = string.Empty;
                    floater.SetCure(string.Empty);
                    ActGlobals.oFormActMain.SendToMacroFile(macroFile, "cure not available", "say ");
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
                    {
                        floater.Show();
                        floater.Location = StringToPoint(floatLoc);
                        floater.Size = StringToSize(floatSize);
                        floater.TopMost = true;
                    }
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

        // UI thread
        void UpdateTarget(object o)
        {
            textBoxTarget.Text = Need.count;
            floater.SetNeed(Need.count);
            if (!string.IsNullOrEmpty(textBoxCures.Text))
                ActGlobals.oFormActMain.SendToMacroFile(macroFile, "cure not available", "say ");
            textBoxCures.Text = string.Empty;
            floater.SetCure(string.Empty);
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
                        if (gridData_[i].when < start)
                            continue;
                        int outerCount = gridData_[i].IntCount();
                        for(int j=i+1; j<playerCount; j++)
                        {
                            if (gridData_[j].when < start)
                                continue;
                            if (gridData_[j].IntCount() + outerCount == added)
                            {
                                //found a match
                                found = true;
                                string p1 = gridData_[i].name.Equals("You") ? ActGlobals.charName : gridData_[i].name;
                                string p2 = gridData_[j].name.Equals("You") ? ActGlobals.charName : gridData_[j].name;
                                string cure = "cure " + p1 + " and " + p2;
                                textBoxCures.Text = cure;
                                floater.SetCure(cure);
                                //only need one announcement
                                // announce if the previous one is old, or if the total changed
                                if (announced < start || announcedTotal != added)
                                {
                                    if(!importing)
                                        ActGlobals.oFormActMain.TTS(cure);
                                    announced = mostRecent;
                                    announcedTotal = added;
                                    ActGlobals.oFormActMain.SendToMacroFile(macroFile, cure, "shout ");
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
        }

        private void textBoxSeconds_TextChanged(object sender, EventArgs e)
        {
            if (!initializing)
            {
                int secs;
                if (!int.TryParse(textBoxSeconds.Text, out secs))
                    secs = 20;
                timeWindow = new TimeSpan(0, 0, secs);
                SaveSettings();
            }
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
                    if (floater.Visible == false && gridData_.Count > 0)
                    {
                        floater.Visible = true;
                        floater.TopMost = true;
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
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
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
            this.buttonClear.TabIndex = 7;
            this.buttonClear.Text = "Clear";
            this.toolTip1.SetToolTip(this.buttonClear, "Clear the current data");
            this.buttonClear.UseVisualStyleBackColor = true;
            this.buttonClear.Click += new System.EventHandler(this.buttonClear_Click);
            // 
            // dataGridView1
            // 
            this.dataGridView1.AllowUserToAddRows = false;
            this.dataGridView1.AllowUserToDeleteRows = false;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Location = new System.Drawing.Point(0, 0);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.Size = new System.Drawing.Size(399, 210);
            this.dataGridView1.TabIndex = 8;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(126, 219);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(34, 13);
            this.label3.TabIndex = 2;
            this.label3.Text = "within";
            // 
            // textBoxSeconds
            // 
            this.textBoxSeconds.Location = new System.Drawing.Point(166, 216);
            this.textBoxSeconds.Name = "textBoxSeconds";
            this.textBoxSeconds.Size = new System.Drawing.Size(47, 20);
            this.textBoxSeconds.TabIndex = 3;
            this.textBoxSeconds.Text = "30";
            this.textBoxSeconds.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.textBoxSeconds.TextChanged += new System.EventHandler(this.textBoxSeconds_TextChanged);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(219, 219);
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
            this.checkBoxPop.Location = new System.Drawing.Point(337, 244);
            this.checkBoxPop.Name = "checkBoxPop";
            this.checkBoxPop.Size = new System.Drawing.Size(57, 17);
            this.checkBoxPop.TabIndex = 10;
            this.checkBoxPop.Text = "Popup";
            this.toolTip1.SetToolTip(this.checkBoxPop, "Check to show pop-up window");
            this.checkBoxPop.UseVisualStyleBackColor = true;
            this.checkBoxPop.CheckedChanged += new System.EventHandler(this.checkBoxPop_CheckedChanged);
            // 
            // Adder
            // 
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
            this.Size = new System.Drawing.Size(402, 305);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
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
            dataGridView1.Columns["count"].Width = 40;
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

        // save changes to size/loc by telling the main form about them
        private void Floater_ResizeEnd(object sender, EventArgs e)
        {
            if (GeometryEvent != null)
            {
                GeometryEventArgs args = new GeometryEventArgs { size = this.Size, location = this.Location };
                GeometryEvent.Invoke(this, args);
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
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.textBoxCure = new System.Windows.Forms.TextBox();
            this.panel1 = new System.Windows.Forms.Panel();
            this.panel2 = new System.Windows.Forms.Panel();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.panel1.SuspendLayout();
            this.panel2.SuspendLayout();
            this.SuspendLayout();
            // 
            // dataGridView1
            // 
            this.dataGridView1.AllowUserToAddRows = false;
            this.dataGridView1.AllowUserToDeleteRows = false;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGridView1.Location = new System.Drawing.Point(0, 0);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.RowHeadersVisible = false;
            this.dataGridView1.Size = new System.Drawing.Size(166, 159);
            this.dataGridView1.TabIndex = 0;
            // 
            // textBoxCure
            // 
            this.textBoxCure.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBoxCure.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.textBoxCure.ForeColor = System.Drawing.SystemColors.ControlText;
            this.textBoxCure.Location = new System.Drawing.Point(3, 3);
            this.textBoxCure.Name = "textBoxCure";
            this.textBoxCure.ReadOnly = true;
            this.textBoxCure.Size = new System.Drawing.Size(158, 20);
            this.textBoxCure.TabIndex = 1;
            this.textBoxCure.TabStop = false;
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.textBoxCure);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
            this.panel1.Location = new System.Drawing.Point(0, 0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(166, 26);
            this.panel1.TabIndex = 2;
            // 
            // panel2
            // 
            this.panel2.Controls.Add(this.dataGridView1);
            this.panel2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel2.Location = new System.Drawing.Point(0, 26);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(166, 159);
            this.panel2.TabIndex = 3;
            // 
            // Floater
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(166, 185);
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
            this.panel2.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.TextBox textBoxCure;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Panel panel2;
    }
}
#endregion Floater.Designer.cs
