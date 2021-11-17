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
