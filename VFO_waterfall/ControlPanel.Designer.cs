namespace SDRSharp.VFO_waterfall
{
    partial class ControlPanel
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.waterfallPictureBox = new System.Windows.Forms.PictureBox();
            this.contrastTrackBar = new System.Windows.Forms.TrackBar();
            ((System.ComponentModel.ISupportInitialize)(this.waterfallPictureBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.contrastTrackBar)).BeginInit();
            this.SuspendLayout();
            // 
            // waterfallPictureBox
            // 
            this.waterfallPictureBox.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.waterfallPictureBox.Location = new System.Drawing.Point(5, 5);
            this.waterfallPictureBox.Name = "waterfallPictureBox";
            this.waterfallPictureBox.Size = new System.Drawing.Size(545, 340);
            this.waterfallPictureBox.MinimumSize = new System.Drawing.Size(50, 50);
            this.waterfallPictureBox.TabIndex = 1;
            this.waterfallPictureBox.TabStop = false;
            // 
            // contrastTrackBar
            // 
            this.contrastTrackBar.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.contrastTrackBar.Location = new System.Drawing.Point(555, 5);
            this.contrastTrackBar.Minimum = 1;
            this.contrastTrackBar.Maximum = 100;
            this.contrastTrackBar.Name = "contrastTrackBar";
            this.contrastTrackBar.Orientation = System.Windows.Forms.Orientation.Vertical;
            this.contrastTrackBar.Size = new System.Drawing.Size(45, 340);
            this.contrastTrackBar.MinimumSize = new System.Drawing.Size(20, 50);
            this.contrastTrackBar.TabIndex = 6;
            this.contrastTrackBar.TickFrequency = 10;
            this.contrastTrackBar.Value = 40;
            // this.contrastTrackBar.ValueChanged += new System.EventHandler(this.contrastTrackBar_ValueChanged);
            // 
            // ControlPanel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.Controls.Add(this.contrastTrackBar);
            this.Controls.Add(this.waterfallPictureBox);
            this.Name = "ControlPanel";
            this.Size = new System.Drawing.Size(600, 350);
            ((System.ComponentModel.ISupportInitialize)(this.waterfallPictureBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.contrastTrackBar)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.PictureBox waterfallPictureBox;
        private System.Windows.Forms.TrackBar contrastTrackBar;
    }
}
