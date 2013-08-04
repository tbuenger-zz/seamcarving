using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SeamCarving
{
    public partial class SCForm : Form
    {
        public SCForm()
        {
            InitializeComponent();
        }

        private SeamCarving sc;

        private Bitmap showImage;

        public Bitmap ShowImage
        {
            get { return showImage; }
            set 
            {
                if (value == null) return;

                showImage = value;
                if (sc == null || sc.SourceHeight != value.Height || sc.SourceWidth != sc.SourceWidth)
                {
                    sc = new SeamCarving(value);
                }
                else
                {
                    sc.SetSourceImage(value);
                }

                //Breite anpassen:
                int widthpadding = this.Width - pictureBox1.Width;
                this.Width = value.Width + widthpadding;
                //Höhe anpassen:
                int heightpadding = this.Height - pictureBox1.Height;
                this.Height = value.Height + heightpadding;

                this.MinimumSize = new Size(0, this.Height);
                this.MaximumSize = this.Size;

                pictureBox1_SizeChanged(this, null);
            }
        }

        private void pictureBox1_SizeChanged(object sender, EventArgs e)
        {
            if(showImage == null) return;

            Image oldPic = pictureBox1.Image;
            int seamsDelta = sc.CurrentWidth - pictureBox1.Width;

            Image newPic;
            if(seamsDelta > 0)  //Seams sollen entfernt werden
            {
                newPic = sc.RemoveSeams(seamsDelta);
            }
            else //Seams sollen wieder rekonstruiert werden
            {
                newPic = sc.InsertSeams(-seamsDelta);
            }

            pictureBox1.Image = newPic;
            if(oldPic != null)
                oldPic.Dispose();
            
        }
    }
}