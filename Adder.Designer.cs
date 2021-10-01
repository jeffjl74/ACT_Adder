using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

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
