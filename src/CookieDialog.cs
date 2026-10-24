using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VidGrab
{
    internal sealed class CookieDialog : Form
    {
        private readonly Action _onChanged;
        private ListView _list;
        private Label _lblHint;
        private Button _btnCheck;

        public CookieDialog(Action onChanged)
        {
            _onChanged = onChanged;
            Text = "Cookie 管理";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 430);
            MinimumSize = new Size(500, 380);
            Font = new Font("Microsoft YaHei UI", 9F);

            var lblTitle = new Label
            {
                Text = "一个文件可以放多个站点的 Cookie；导入是合并，不会覆盖其它站点。",
                Location = new Point(16, 14),
                Size = new Size(528, 34),
                ForeColor = Color.FromArgb(80, 80, 80)
            };

            _list = new ListView
            {
                Location = new Point(16, 52),
                Size = new Size(528, 280),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false
            };
            _list.Columns.Add("站点（Cookie 域名）", 300);
            _list.Columns.Add("Cookie 数", 75);
            _list.Columns.Add("登录状态", 140);

            _lblHint = new Label
            {
                Text = "",
                Location = new Point(16, 340),
                Size = new Size(528, 24),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.Gray
            };

            var btnImport = new Button
            {
                Text = "导入并合并…",
                Location = new Point(16, 374),
                Size = new Size(112, 32),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnImport.Click += (s, e) => Import();

            _btnCheck = new Button
            {
                Text = "检测有效性",
                Location = new Point(136, 374),
                Size = new Size(104, 32),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            _btnCheck.Click += async (s, e) => await CheckAllAsync();

            var btnRemove = new Button
            {
                Text = "删除选中站点",
                Location = new Point(248, 374),
                Size = new Size(116, 32),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnRemove.Click += (s, e) => RemoveSelected();

            var btnClear = new Button
            {
                Text = "清空全部",
                Location = new Point(372, 374),
                Size = new Size(90, 32),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnClear.Click += (s, e) => ClearAll();

            var btnClose = new Button
            {
                Text = "关闭",
                Location = new Point(470, 374),
                Size = new Size(74, 32),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.OK
            };

            Controls.AddRange(new Control[] { lblTitle, _list, _lblHint, btnImport, _btnCheck, btnRemove, btnClear, btnClose });
            AcceptButton = btnClose;
            CancelButton = btnClose;

            RefreshList();
        }

        private void RefreshList()
        {
            _list.Items.Clear();
            var domains = CookieStore.Domains();
            foreach (var d in domains)
            {
                var row = new ListViewItem(new[] { d.Key, d.Value.ToString(), "未检测" });
                row.Tag = d.Key;
                _list.Items.Add(row);
            }

            int total = 0;
            foreach (var d in domains) total += d.Value;
            _lblHint.Text = domains.Count == 0
                ? "还没有 Cookie。点「导入并合并」选择浏览器导出的 cookies.txt。"
                : string.Format("共 {0} 个站点、{1} 条 Cookie。文件：{2}", domains.Count, total, CookieStore.MasterPath);
        }

        private void Import()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择 cookies.txt（Netscape 格式）";
                dlg.Filter = "cookies.txt|*.txt|所有文件|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    int merged = CookieStore.MergeFrom(dlg.FileName);
                    RefreshList();
                    if (_onChanged != null) _onChanged();
                    _lblHint.Text = merged == 0
                        ? "这个文件里没有识别到有效 Cookie。"
                        : string.Format("已合并 {0} 条 Cookie。共 {1} 个站点。", merged, CookieStore.Domains().Count);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导入失败：" + ex.Message, "VidGrab");
                }
            }
        }

        private async Task CheckAllAsync()
        {
            if (_list.Items.Count == 0) return;
            _btnCheck.Enabled = false;
            try
            {
                foreach (ListViewItem row in _list.Items)
                {
                    string domain = Convert.ToString(row.Tag);
                    row.SubItems[2].Text = "检测中…";
                    row.SubItems[2].ForeColor = Color.Gray;

                    var entries = CookieStore.Read(CookieStore.MasterPath)
                        .Where(e => string.Equals(CookieStore.Normalize(e.Domain), CookieStore.Normalize(domain), StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var result = await Task.Run(() => CookieChecker.Check(domain, entries));
                    row.SubItems[2].Text = result.Status;
                    row.SubItems[2].ForeColor = result.Status == "有效" ? Color.FromArgb(0, 130, 0)
                                                 : (result.Status.StartsWith("未登录") || result.Status == "已过期") ? Color.Firebrick
                                                 : Color.Gray;
                    if (!string.IsNullOrEmpty(result.Detail))
                        _lblHint.Text = domain + "：" + result.Detail;
                }
            }
            finally
            {
                _btnCheck.Enabled = true;
            }
        }

        private void RemoveSelected()
        {
            if (_list.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "请先在列表里选中一个站点。", "VidGrab");
                return;
            }
            string domain = Convert.ToString(_list.SelectedItems[0].Tag);
            if (MessageBox.Show(this, "删除 " + domain + " 的所有 Cookie？", "VidGrab",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            CookieStore.RemoveDomain(domain);
            RefreshList();
            if (_onChanged != null) _onChanged();
        }

        private void ClearAll()
        {
            if (CookieStore.Domains().Count == 0) return;
            if (MessageBox.Show(this, "清空全部 Cookie？此操作不可撤销。", "VidGrab",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            CookieStore.ClearAll();
            RefreshList();
            if (_onChanged != null) _onChanged();
        }
    }
}
