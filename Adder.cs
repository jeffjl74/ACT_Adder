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
// reference:System.Core.dll

namespace ACT_Adder
{
    public partial class Adder : UserControl, IActPluginV1
    {
        Label lblStatus;
        string settingsFile = Path.Combine(ActGlobals.oFormActMain.AppDataFolder.FullName, "Config\\Adder.config.xml");
        SettingsSerializer xmlSettings;
        bool initializing = true;

        const int logTimeStampLength = 39; //# of chars in the timestamp
        const string logTimeStampRegexStr = @"^\(\d{10}\)\[.{24}\] ";
        const string playerOrYou = @"((?<player>You)|\\aPC [^ ]+ (?<player>[^:]+):\w+\\/a) ";
        const string groupSay = playerOrYou + @"says? to the group, """;
        const string numSay = @"(?<count>\d+)""";
        const string targetSay = @"need (?<target>\d+)""";
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

            timeWindow = new TimeSpan(0, 0, 20);

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
                Player found = gridData_.SingleOrDefault(x => x.name == p.name);
                if (found == null)
                { 
                    gridData_.Add(p);
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
                }
                foreach (Player player in gridData_)
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
            gridData_.Clear();
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
    }
}
