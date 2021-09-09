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
using System.Collections.Generic;
using System.Threading.Tasks;
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
[assembly: AssemblyVersion("0.9.2.0")]
[assembly: AssemblyFileVersion("0.9.2.0")]
#endregion Properties\AssemblyInfo.cs
#region Adder.cs
// reference:System.Core.dll

namespace ACT_Adder
{
    public partial class Adder : UserControl, IActPluginV1
    {
        Label lblStatus;
        string settingsFile = Path.Combine(ActGlobals.oFormActMain.AppDataFolder.FullName, "Config\\Adder.config.xml");
        SettingsSerializer xmlSettings;
        bool initializing = true;

        string macroFile;

        const int logTimeStampLength = 39; //# of chars in the timestamp
        const string logTimeStampRegexStr = @"^\(\d{10}\)\[.{24}\] ";
        const string playerOrYou = @"((?<player>You)|\\aPC [^ ]+ (?<player>[^:]+):\w+\\/a) ";
        const string groupSay = playerOrYou + @"says? to the group, """;
        const string numSay = @"(?<count>\d+)""";
        const string targetSay = @"n[^ ]* (?<target>\d+)""";
        Regex reCount = new Regex(logTimeStampRegexStr + groupSay + numSay, RegexOptions.Compiled);
        Regex reTarget = new Regex(logTimeStampRegexStr + groupSay + targetSay, RegexOptions.Compiled);

        BindingList<Player> gridData_;
        public BindingList<Player> gridData { get { return gridData_; } }
        Player Need = new Player { count = "0", when = DateTime.MinValue, name = "Target" };

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

            timeWindow = new TimeSpan(0, 0, 30);

            macroFile = Path.Combine(ActGlobals.oFormActMain.GameMacroFolder, "lab-macro.txt");

            ActGlobals.oFormActMain.OnLogLineRead += OFormActMain_OnLogLineRead;

            pluginStatusText.Text = "Plugin Started";
        }

        private void OFormActMain_OnLogLineRead(bool isImport, LogLineEventArgs logInfo)
        {
            if (logInfo.detectedType == 0 && logInfo.logLine.Length > logTimeStampLength)
            {
                Match match = reCount.Match(logInfo.logLine);
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
            if(o != null)
            {
                Player found = gridData.SingleOrDefault(x => x.name == p.name);
                if (found == null)
                { 
                    gridData.Add(p);
                    mostRecent = p.when;
                }
                else
                {
                    found.count = p.count;
                    found.when = p.when;
                    mostRecent = p.when;
                }

                //remove "old" tells
                DateTime start = mostRecent - timeWindow;
                if (Need.when < start)
                {
                    textBoxTarget.Text = string.Empty;
                    textBoxCures.Text = string.Empty;
                    ActGlobals.oFormActMain.SendToMacroFile(macroFile, "cure not available", "say ");
                }
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
            textBoxCures.Text = string.Empty;
            ActGlobals.oFormActMain.SendToMacroFile(macroFile, "cure not available", "say ");
            SearchForTarget();
        }

        // UI thread.
        // If there is a recent "need" total,
        // add the players' numbers, 2 at a time, looking for the needed total.
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
                                textBoxCures.Text = "cure " + gridData_[i].name + " and " + gridData_[j].name;
                                //only need one announcement
                                if (announced < start || announcedTotal != added)
                                {
                                    ActGlobals.oFormActMain.TTS(textBoxCures.Text);
                                    announced = mostRecent;
                                    announcedTotal = added;
                                    ActGlobals.oFormActMain.SendToMacroFile(macroFile, textBoxCures.Text, "shout ");
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
            textBoxTarget.Text = string.Empty;
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
            this.label1 = new System.Windows.Forms.Label();
            this.textBoxTarget = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.textBoxCures = new System.Windows.Forms.TextBox();
            this.buttonClear = new System.Windows.Forms.Button();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.whenDataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.nameDataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.countDataGridViewTextBoxColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.playerBindingSource = new System.Windows.Forms.BindingSource(this.components);
            this.label3 = new System.Windows.Forms.Label();
            this.textBoxSeconds = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.linkLabel1 = new System.Windows.Forms.LinkLabel();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.playerBindingSource)).BeginInit();
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
            this.textBoxCures.Size = new System.Drawing.Size(303, 20);
            this.textBoxCures.TabIndex = 6;
            // 
            // buttonClear
            // 
            this.buttonClear.Location = new System.Drawing.Point(138, 268);
            this.buttonClear.Name = "buttonClear";
            this.buttonClear.Size = new System.Drawing.Size(75, 23);
            this.buttonClear.TabIndex = 7;
            this.buttonClear.Text = "Clear";
            this.buttonClear.UseVisualStyleBackColor = true;
            this.buttonClear.Click += new System.EventHandler(this.buttonClear_Click);
            // 
            // dataGridView1
            // 
            this.dataGridView1.AllowUserToAddRows = false;
            this.dataGridView1.AllowUserToDeleteRows = false;
            this.dataGridView1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dataGridView1.AutoGenerateColumns = false;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.whenDataGridViewTextBoxColumn,
            this.nameDataGridViewTextBoxColumn,
            this.countDataGridViewTextBoxColumn});
            this.dataGridView1.DataSource = this.playerBindingSource;
            this.dataGridView1.Location = new System.Drawing.Point(0, 0);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.Size = new System.Drawing.Size(353, 210);
            this.dataGridView1.TabIndex = 8;
            // 
            // whenDataGridViewTextBoxColumn
            // 
            this.whenDataGridViewTextBoxColumn.DataPropertyName = "when";
            dataGridViewCellStyle1.Format = "h:mm:ss";
            this.whenDataGridViewTextBoxColumn.DefaultCellStyle = dataGridViewCellStyle1;
            this.whenDataGridViewTextBoxColumn.HeaderText = "when";
            this.whenDataGridViewTextBoxColumn.Name = "whenDataGridViewTextBoxColumn";
            this.whenDataGridViewTextBoxColumn.Width = 60;
            // 
            // nameDataGridViewTextBoxColumn
            // 
            this.nameDataGridViewTextBoxColumn.DataPropertyName = "name";
            this.nameDataGridViewTextBoxColumn.HeaderText = "name";
            this.nameDataGridViewTextBoxColumn.Name = "nameDataGridViewTextBoxColumn";
            // 
            // countDataGridViewTextBoxColumn
            // 
            this.countDataGridViewTextBoxColumn.DataPropertyName = "count";
            this.countDataGridViewTextBoxColumn.HeaderText = "count";
            this.countDataGridViewTextBoxColumn.Name = "countDataGridViewTextBoxColumn";
            // 
            // playerBindingSource
            // 
            this.playerBindingSource.DataSource = typeof(ACT_Adder.Player);
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
            this.linkLabel1.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabel1_LinkClicked);
            // 
            // Adder
            // 
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
            this.Size = new System.Drawing.Size(356, 305);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.playerBindingSource)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }
        private Label label1;
        private TextBox textBoxTarget;
        private Label label2;
        private TextBox textBoxCures;
        private Button buttonClear;
        private DataGridView dataGridView1;
        private BindingSource playerBindingSource;
        private System.ComponentModel.IContainer components;
        private DataGridViewTextBoxColumn whenDataGridViewTextBoxColumn;
        private DataGridViewTextBoxColumn nameDataGridViewTextBoxColumn;
        private DataGridViewTextBoxColumn countDataGridViewTextBoxColumn;
        private Label label3;
        private TextBox textBoxSeconds;
        private Label label4;
        private LinkLabel linkLabel1;
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
