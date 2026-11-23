namespace OpenLoop.App
{
    partial class TiledView
    {
        private System.ComponentModel.IContainer components = null;

        private Panel _host;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tick?.Stop();
                _tick?.Dispose();
            }
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            _host = new Panel();

            SuspendLayout();

            _host.Dock = DockStyle.Fill;
            _host.Margin = new Padding(0);
            _host.BackColor = UiStyle.Page;

            AutoScaleMode = AutoScaleMode.Font;
            BackColor = UiStyle.Page;
            Dock = DockStyle.Fill;
            Controls.Add(_host);

            ResumeLayout(false);
        }
    }
}
