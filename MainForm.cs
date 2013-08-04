using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SeamCarving
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        SCForm SubForm;

        private void button1_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.ShowDialog();
            if (ofd.FileName != null)
            {
                Bitmap img = new Bitmap(ofd.FileName);
                if (img != null)
                {
                    if (SubForm == null)
                    {
                        SubForm = new SCForm();
                    }
                    SubForm.ShowImage = img;
                    SubForm.Show();
                }
            }
        }


    }
}