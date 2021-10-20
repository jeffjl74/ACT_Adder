using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

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
