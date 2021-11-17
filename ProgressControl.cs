using System.ComponentModel;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

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
