using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

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
