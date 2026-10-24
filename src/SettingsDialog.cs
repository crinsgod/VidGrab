using System;
using System.Drawing;
using System.Windows.Forms;

namespace VidGrab
{
    internal sealed class SettingsDialog : Form
    {
        private readonly NumericUpDown _numThreads;
        private readonly TextBox _txtProxy;

        public SettingsDialog(Settings settings)
        {
            Text = "设置";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 240);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            var lblThreads = new Label
            {
                Text = "下载线程数（直链分块，1-16）",
                Location = new Point(18, 22),
                AutoSize = true
            };
            int threads = settings.Threads <= 0 ? 4 : settings.Threads;
            _numThreads = new NumericUpDown
            {
                Location = new Point(18, 46),
                Size = new Size(80, 26),
                Minimum = 1,
                Maximum = 16,
                Value = Math.Max(1, Math.Min(16, threads))
            };

            var lblProxy = new Label
            {
                Text = "代理地址（留空 = 跟随系统）",
                Location = new Point(18, 86),
                AutoSize = true
            };
            _txtProxy = new TextBox
            {
                Location = new Point(18, 110),
                Size = new Size(424, 26),
                Text = settings.Proxy ?? ""
            };
            var lblHint = new Label
            {
                Text = "例如 http://127.0.0.1:7890 或 socks5://127.0.0.1:1080",
                Location = new Point(18, 142),
                AutoSize = true,
                ForeColor = Color.Gray
            };

            var btnOk = new Button
            {
                Text = "确定",
                Location = new Point(266, 192),
                Size = new Size(84, 30),
                DialogResult = DialogResult.OK
            };
            var btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(358, 192),
                Size = new Size(84, 30),
                DialogResult = DialogResult.Cancel
            };

            Controls.AddRange(new Control[] { lblThreads, _numThreads, lblProxy, _txtProxy, lblHint, btnOk, btnCancel });
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        public int ThreadCount { get { return (int)_numThreads.Value; } }
        public string ProxyText { get { return _txtProxy.Text.Trim(); } }
    }
}
