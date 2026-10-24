using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VidGrab
{
    internal static class AppInfo
    {
        public const string Version = "1.1.0";
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string startup = args != null && args.Length > 0 ? string.Join(Environment.NewLine, args) : null;
            Application.Run(new MainForm(startup));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }

    internal sealed class MainForm : Form
    {
        private readonly Settings _settings = Settings.Load();
        private readonly List<DownloadTask> _tasks = new List<DownloadTask>();
        private readonly string _startupText;

        private TextBox _txtUrls;
        private Button _btnParse;
        private ComboBox _cmbQuality;
        private Button _btnApplyAll;
        private CheckBox _chkMerge;
        private CheckBox _chkSubs;
        private CheckBox _chkCookie;
        private Button _btnSettings;
        private Button _btnImportCookie;
        private Button _btnManageCookie;
        private Label _lblCookie;
        private DataGridView _grid;
        private TextBox _txtSave;
        private Button _btnBrowse;
        private Button _btnOpenDir;
        private Button _btnStart;
        private Button _btnStop;
        private ProgressBar _progress;
        private Label _lblStatus;

        private CancellationTokenSource _cts;
        private bool _busy;
        private bool _stopRequested;
        private bool _suppressCellEvents;

        public MainForm(string startupText = null)
        {
            _startupText = startupText;
            Text = "VidGrab v" + AppInfo.Version + " · 视频下载器";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1040, 720);
            MinimumSize = new Size(900, 640);
            Font = new Font("Microsoft YaHei UI", 9F);

            BuildUi();
            CookieStore.Configure(_settings.CookiePath, _settings.UseCookie);
            ApplyEngineSettings();
            UpdateCookieLabel();
            if (!string.IsNullOrEmpty(_startupText)) _txtUrls.Text = _startupText;
            Shown += async (s, e) =>
            {
                await PrepareAsync();
                if (!string.IsNullOrEmpty(_startupText)) await ParseAsync();
            };
        }

        private void BuildUi()
        {
            var lblInput = new Label
            {
                Text = "链接（每行一个；也可整段粘贴抖音分享文案，自动提取）",
                Location = new Point(18, 14),
                AutoSize = true
            };

            _txtUrls = new TextBox
            {
                Location = new Point(18, 38),
                Size = new Size(880, 74),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Microsoft YaHei UI", 9.5F)
            };

            _btnParse = new Button
            {
                Text = "解析全部",
                Location = new Point(910, 38),
                Size = new Size(112, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnParse.Click += async (s, e) => await ParseAsync();

            var lblQuality = new Label { Text = "统一清晰度", Location = new Point(18, 124), AutoSize = true };

            _cmbQuality = new ComboBox
            {
                Location = new Point(84, 120),
                Size = new Size(96, 26),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbQuality.Items.AddRange(new object[] { "最高", "1080p", "720p", "480p", "最低", "仅音频" });
            _cmbQuality.SelectedItem = _settings.Quality;
            if (_cmbQuality.SelectedIndex < 0) _cmbQuality.SelectedIndex = 0;

            _btnApplyAll = new Button
            {
                Text = "应用到全部",
                Location = new Point(188, 118),
                Size = new Size(86, 28)
            };
            _btnApplyAll.Click += (s, e) => ApplyQualityToAll();

            _chkMerge = new CheckBox
            {
                Text = "合并为 MP4",
                Location = new Point(284, 122),
                AutoSize = true,
                Checked = _settings.MergeMp4
            };

            _chkSubs = new CheckBox
            {
                Text = "下载字幕",
                Location = new Point(396, 122),
                AutoSize = true,
                Checked = _settings.DownloadSubs
            };

            _chkCookie = new CheckBox
            {
                Text = "使用 Cookie",
                Location = new Point(496, 122),
                AutoSize = true,
                Checked = _settings.UseCookie
            };
            _chkCookie.CheckedChanged += (s, e) =>
            {
                _settings.UseCookie = _chkCookie.Checked;
                _settings.Save();
                CookieStore.Configure(_settings.CookiePath, _chkCookie.Checked);
                UpdateCookieLabel();
            };

            _btnImportCookie = new Button
            {
                Text = "导入…",
                Location = new Point(604, 118),
                Size = new Size(64, 28)
            };
            _btnImportCookie.Click += (s, e) => ImportCookie();

            _btnManageCookie = new Button
            {
                Text = "管理…",
                Location = new Point(674, 118),
                Size = new Size(64, 28)
            };
            _btnManageCookie.Click += (s, e) => ManageCookie();

            _btnSettings = new Button
            {
                Text = "设置…",
                Location = new Point(744, 118),
                Size = new Size(64, 28)
            };
            _btnSettings.Click += (s, e) => OpenSettings();

            _lblCookie = new Label
            {
                Text = "未导入",
                Location = new Point(816, 124),
                Size = new Size(206, 20),
                AutoEllipsis = true,
                ForeColor = Color.Gray
            };

            _grid = new DataGridView
            {
                Location = new Point(18, 154),
                Size = new Size(1004, 400),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                EditMode = DataGridViewEditMode.EditOnEnter,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            _grid.Columns.Add(MakeTextColumn("#", 36, true));
            _grid.Columns.Add(MakeTextColumn("平台", 64, true));
            _grid.Columns.Add(MakeTextColumn("标题", 330, true));

            var qualityColumn = new DataGridViewComboBoxColumn
            {
                HeaderText = "清晰度",
                Name = "quality",
                Width = 120,
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox
            };
            _grid.Columns.Add(qualityColumn);

            _grid.Columns.Add(MakeTextColumn("大小", 90, true));
            _grid.Columns.Add(MakeTextColumn("状态", 78, true));
            _grid.Columns.Add(MakeTextColumn("进度", 58, true));

            _grid.Columns[0].FillWeight = 30;
            _grid.Columns[1].FillWeight = 55;
            _grid.Columns[2].FillWeight = 300;
            _grid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            _grid.Columns[3].Width = 120;
            _grid.Columns[4].FillWeight = 70;
            _grid.Columns[5].FillWeight = 60;
            _grid.Columns[6].FillWeight = 45;

            _grid.CellValueChanged += (s, e) => OnQualityChanged(e.RowIndex, e.ColumnIndex);
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.DataError += (s, e) => { e.ThrowException = false; };

            var lblSave = new Label { Text = "保存到", Location = new Point(18, 566), AutoSize = true, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };

            _txtSave = new TextBox
            {
                Location = new Point(72, 562),
                Size = new Size(730, 26),
                Text = _settings.SaveDir,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            _btnBrowse = new Button
            {
                Text = "浏览…",
                Location = new Point(810, 560),
                Size = new Size(80, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            _btnBrowse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.SelectedPath = System.IO.Directory.Exists(_txtSave.Text) ? _txtSave.Text : _settings.SaveDir;
                    if (dlg.ShowDialog(this) == DialogResult.OK) _txtSave.Text = dlg.SelectedPath;
                }
            };

            _btnOpenDir = new Button
            {
                Text = "打开目录",
                Location = new Point(896, 560),
                Size = new Size(116, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            _btnOpenDir.Click += (s, e) => OpenSaveDir();

            _btnStart = new Button
            {
                Text = "开始下载",
                Location = new Point(780, 598),
                Size = new Size(110, 34),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = true
            };
            _btnStart.Click += async (s, e) => await StartQueueAsync();

            _btnStop = new Button
            {
                Text = "停止",
                Location = new Point(896, 598),
                Size = new Size(116, 34),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = false
            };
            _btnStop.Click += (s, e) => StopQueue();

            _progress = new ProgressBar
            {
                Location = new Point(18, 642),
                Size = new Size(1004, 16),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            _lblStatus = new Label
            {
                Text = "就绪",
                Location = new Point(18, 664),
                Size = new Size(1004, 26),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoEllipsis = true
            };

            Controls.AddRange(new Control[]
            {
                lblInput, _txtUrls, _btnParse, lblQuality, _cmbQuality, _btnApplyAll, _chkMerge,
                _chkSubs, _chkCookie, _btnImportCookie, _btnManageCookie, _btnSettings, _lblCookie,
                _grid, lblSave, _txtSave, _btnBrowse, _btnOpenDir,
                _btnStart, _btnStop, _progress, _lblStatus
            });
        }

        private static DataGridViewTextBoxColumn MakeTextColumn(string header, int width, bool readOnly)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                Width = width,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private async Task PrepareAsync()
        {
            _btnParse.Enabled = false;
            SetStatus("首次启动，正在释放下载组件…");
            try
            {
                await Task.Run(() => Binaries.Ensure());
                SetStatus("就绪 · 粘贴多个链接后点「解析全部」，再逐行选清晰度，最后「开始下载」");
            }
            catch (Exception ex)
            {
                SetStatus("组件释放失败：" + ex.Message);
                MessageBox.Show(this, ex.Message, "VidGrab", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnParse.Enabled = true;
            }
        }

        private async Task ParseAsync()
        {
            if (_busy) return;

            var urls = ExtractUrls(_txtUrls.Text);
            if (urls.Count == 0)
            {
                MessageBox.Show(this, "没有识别到有效链接。每行一个，或直接粘贴整段分享文案。", "VidGrab");
                return;
            }

            _busy = true;
            _btnParse.Enabled = false;
            _btnStart.Enabled = false;
            _tasks.Clear();
            _grid.Rows.Clear();
            _progress.Value = 0;

            int ok = 0, failed = 0;
            try
            {
                for (int i = 0; i < urls.Count; i++)
                {
                    string url = urls[i];
                    SetStatus(string.Format("解析中 {0}/{1}：{2}", i + 1, urls.Count, ShortUrl(url)));

                    var task = new DownloadTask
                    {
                        Url = url,
                        Platform = PlatformName(url)
                    };

                    try
                    {
                        MediaInfo info = await ParseOneAsync(url);
                        task.Info = info;
                        task.Title = string.IsNullOrWhiteSpace(info.Title) ? ShortUrl(url) : info.Title;
                        task.Format = PickFormat(info, Convert.ToString(_cmbQuality.SelectedItem));
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        task.State = "解析失败";
                        task.Message = FirstLine(ex.Message);
                        task.Title = ShortUrl(url);
                        failed++;
                    }

                    _tasks.Add(task);
                    AddTaskRow(task);
                }

                _btnStart.Enabled = ok > 0;
                _progress.Value = 0;
                SetStatus(string.Format("解析完成：{0} 个可下载，{1} 个失败。可在表格中逐行调整清晰度，然后点「开始下载」。", ok, failed));
            }
            finally
            {
                _busy = false;
                _btnParse.Enabled = true;
            }
        }

        private static async Task<MediaInfo> ParseOneAsync(string url)
        {
            if (IsDouyin(url)) return await Douyin.ExtractAsync(url, null);
            if (IsX(url))
            {
                MediaInfo viaApi = null;
                try { viaApi = await XTwitter.ExtractAsync(url, null); }
                catch { viaApi = null; }
                return viaApi ?? await YtDlp.ProbeAsync(url);
            }
            return await YtDlp.ProbeAsync(url);
        }

        private void AddTaskRow(DownloadTask task)
        {
            int index = _grid.Rows.Add();
            var row = _grid.Rows[index];
            task.RowIndex = index;

            row.Cells[0].Value = (index + 1).ToString();
            row.Cells[1].Value = task.Platform;
            row.Cells[2].Value = Truncate(task.Title, 60);

            _suppressCellEvents = true;
            try
            {
                var cell = new DataGridViewComboBoxCell();
                if (task.Info != null)
                {
                    foreach (var f in task.Info.Formats) cell.Items.Add(f.Title);
                    if (task.Format != null) cell.Value = task.Format.Title;
                }
                row.Cells[3] = cell;
            }
            finally
            {
                _suppressCellEvents = false;
            }

            row.Cells[4].Value = task.Format != null ? task.Format.SizeText : "";
            row.Cells[5].Value = task.State;
            row.Cells[6].Value = task.Progress + "%";

            if (task.State == "解析失败")
                row.DefaultCellStyle.ForeColor = Color.Gray;
        }

        private void OnQualityChanged(int rowIndex, int columnIndex)
        {
            if (_suppressCellEvents || columnIndex != 3) return;
            if (rowIndex < 0 || rowIndex >= _tasks.Count) return;

            var task = _tasks[rowIndex];
            if (task.Info == null) return;

            string title = Convert.ToString(_grid.Rows[rowIndex].Cells[3].Value);
            var fmt = task.Info.Formats.FirstOrDefault(f => f.Title == title);
            if (fmt == null) return;

            task.Format = fmt;
            _grid.Rows[rowIndex].Cells[4].Value = fmt.SizeText;
        }

        private void ApplyQualityToAll()
        {
            string quality = Convert.ToString(_cmbQuality.SelectedItem);
            _suppressCellEvents = true;
            try
            {
                for (int i = 0; i < _tasks.Count; i++)
                {
                    var task = _tasks[i];
                    if (task.Info == null) continue;
                    var fmt = PickFormat(task.Info, quality);
                    if (fmt == null) continue;
                    task.Format = fmt;
                    if (i < _grid.Rows.Count)
                    {
                        var cell = _grid.Rows[i].Cells[3] as DataGridViewComboBoxCell;
                        if (cell != null && cell.Items.Contains(fmt.Title)) cell.Value = fmt.Title;
                        _grid.Rows[i].Cells[4].Value = fmt.SizeText;
                    }
                }
            }
            finally
            {
                _suppressCellEvents = false;
            }
            SetStatus("已把「" + quality + "」应用到全部任务");
        }

        private async Task StartQueueAsync()
        {
            if (_busy) return;

            // First click with nothing parsed behaves like "analyze everything, then let me choose".
            if (_tasks.Count == 0)
            {
                await ParseAsync();
                if (_tasks.Count > 0)
                    SetStatus("解析完成：请先在表格里确认/调整清晰度，再点一次「开始下载」。");
                return;
            }

            var queue = _tasks.Where(t => t.State == "待下载" && t.Format != null && t.Info != null).ToList();
            if (queue.Count == 0)
            {
                MessageBox.Show(this, "没有待下载的任务。", "VidGrab");
                return;
            }

            string saveDir = _txtSave.Text.Trim();
            try { System.IO.Directory.CreateDirectory(saveDir); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存目录不可用：" + ex.Message, "VidGrab");
                return;
            }

            _settings.SaveDir = saveDir;
            _settings.Quality = Convert.ToString(_cmbQuality.SelectedItem);
            _settings.MergeMp4 = _chkMerge.Checked;
            _settings.DownloadSubs = _chkSubs.Checked;
            _settings.Save();

            _busy = true;
            _stopRequested = false;
            _btnParse.Enabled = false;
            _btnStart.Enabled = false;
            _btnStop.Enabled = true;
            _progress.Value = 0;

            int total = queue.Count;
            int finished = 0;

            try
            {
                foreach (var task in queue)
                {
                    if (_stopRequested) break;

                    task.State = "下载中";
                    task.Progress = 0;
                    UpdateTaskRow(task, "下载中");
                    int taskIndex = finished + 1;
                    SetStatus(string.Format("({0}/{1}) {2}", taskIndex, total, Truncate(task.Title, 60)));

                    _cts = new CancellationTokenSource();
                    try
                    {
                        Action<int, string> onProgress = (pct, text) =>
                        {
                            task.Progress = pct;
                            UpdateTaskRow(task, null);
                            double overall = (finished + pct / 100.0) / total * 100.0;
                            SetOverall((int)overall);
                            SetStatus(string.Format("({0}/{1}) {2}", taskIndex, total, text));
                        };

                        if (!string.IsNullOrEmpty(task.Format.DirectUrl))
                        {
                            if (task.Format.IsDouyin)
                                await Douyin.DownloadAsync(task.Info, task.Format, saveDir, onProgress, _cts.Token);
                            else
                                await XTwitter.DownloadAsync(task.Info, task.Format, saveDir, onProgress, _cts.Token);
                        }
                        else
                        {
                            Exception lastError = null;
                            for (int attempt = 0; attempt < 3; attempt++)
                            {
                                bool retry = false;
                                try
                                {
                                    await YtDlp.DownloadAsync(task.Info, task.Format, saveDir, _chkMerge.Checked,
                                        _chkSubs.Checked, onProgress,
                                        s => SetStatus(string.Format("({0}/{1}) {2}", taskIndex, total, s)), _cts.Token);
                                    lastError = null;
                                    break;
                                }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception ex)
                                {
                                    lastError = ex;
                                    retry = attempt < 2;
                                }

                                if (retry)
                                {
                                    SetStatus(string.Format("({0}/{1}) 下载失败，重试 {2}/2…", taskIndex, total, attempt + 1));
                                    await Task.Delay(1500 * (attempt + 1), _cts.Token);
                                }
                            }
                            if (lastError != null) throw lastError;
                        }

                        task.State = "完成";
                        task.Progress = 100;
                        UpdateTaskRow(task, "完成");
                    }
                    catch (OperationCanceledException)
                    {
                        task.State = "已取消";
                        UpdateTaskRow(task, "已取消");
                        finished++;
                        break;
                    }
                    catch (Exception ex)
                    {
                        task.State = "失败";
                        task.Message = FirstLine(ex.Message);
                        UpdateTaskRow(task, "失败");
                    }
                    finally
                    {
                        _cts.Dispose();
                        _cts = null;
                    }

                    finished++;
                    SetOverall((int)(finished * 100.0 / total));
                }

                if (_stopRequested)
                    SetStatus(string.Format("已停止：完成 {0}/{1}", finished, total));
                else
                    SetStatus(string.Format("全部完成：{0} 个任务，保存到 {1}", total, saveDir));
            }
            finally
            {
                _busy = false;
                _btnParse.Enabled = true;
                _btnStart.Enabled = true;
                _btnStop.Enabled = false;
                _stopRequested = false;
            }
        }

        private void StopQueue()
        {
            _stopRequested = true;
            if (_cts != null)
            {
                try { _cts.Cancel(); } catch { }
            }
            SetStatus("正在停止…");
        }

        private static FormatItem PickFormat(MediaInfo info, string quality)
        {
            var formats = info.Formats;
            if (formats == null || formats.Count == 0) return null;

            if (quality == "仅音频")
            {
                var audio = formats.FirstOrDefault(f => f.Title == "仅音频");
                return audio ?? formats[formats.Count - 1];
            }

            var videos = formats.Where(f => f.Title != "仅音频").ToList();
            if (videos.Count == 0) return formats[0];

            if (quality == "最高") return videos[0];
            if (quality == "最低") return videos[videos.Count - 1];

            int target;
            if (int.TryParse(quality.Replace("p", ""), out target))
            {
                var exact = videos.FirstOrDefault(f => f.Height > 0 && f.Height <= target);
                if (exact != null) return exact;
                return videos[videos.Count - 1];
            }
            return videos[0];
        }

        private void UpdateTaskRow(DownloadTask task, string state)
        {
            RunOnUi(() =>
            {
                if (task.RowIndex < 0 || task.RowIndex >= _grid.Rows.Count) return;
                var row = _grid.Rows[task.RowIndex];
                if (state != null) row.Cells[5].Value = state;
                row.Cells[6].Value = task.Progress + "%";
            });
        }

        private void SetOverall(int percent)
        {
            RunOnUi(() => _progress.Value = Math.Max(0, Math.Min(100, percent)));
        }

        private void SetStatus(string text)
        {
            RunOnUi(() => _lblStatus.Text = text);
        }

        private void RunOnUi(Action action)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(action); } catch { } return; }
            action();
        }

        private void ApplyEngineSettings()
        {
            int threads = _settings.Threads <= 0 ? 4 : _settings.Threads;
            threads = Math.Max(1, Math.Min(16, threads));
            DirectDownloader.Threads = threads;
            DirectDownloader.ProxyUrl = _settings.Proxy ?? "";
            YtDlp.ProxyUrl = _settings.Proxy ?? "";
        }

        private void OpenSettings()
        {
            using (var dlg = new SettingsDialog(_settings))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _settings.Proxy = dlg.ProxyText;
                _settings.Threads = dlg.ThreadCount;
                _settings.Save();
                ApplyEngineSettings();
                SetStatus("设置已保存（线程数 " + _settings.Threads + "，代理：" +
                          (string.IsNullOrWhiteSpace(_settings.Proxy) ? "系统默认" : _settings.Proxy) + "）");
            }
        }

        private void UpdateCookieLabel()
        {
            bool active = CookieStore.Enabled && System.IO.File.Exists(CookieStore.Path);
            if (active)
            {
                var domains = CookieStore.Domains();
                _lblCookie.Text = domains.Count == 0
                    ? "未导入"
                    : "已启用 · " + domains.Count + " 个站点";
            }
            else
            {
                _lblCookie.Text = string.IsNullOrEmpty(CookieStore.Path) ? "未导入" : "已导入但未启用";
            }
            _lblCookie.ForeColor = active ? Color.FromArgb(0, 120, 0) : Color.Gray;
        }

        private void ImportCookie()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择 cookies.txt（Netscape 格式，可多次导入不同站点）";
                dlg.Filter = "cookies.txt|*.txt|所有文件|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    int merged = CookieStore.MergeFrom(dlg.FileName);
                    if (merged == 0)
                    {
                        MessageBox.Show(this, "这个文件里没有识别到有效 Cookie。", "VidGrab");
                        return;
                    }
                    _settings.CookiePath = CookieStore.MasterPath;
                    _settings.UseCookie = true;
                    _settings.Save();
                    CookieStore.Configure(CookieStore.MasterPath, true);
                    _chkCookie.Checked = true;
                    UpdateCookieLabel();
                    SetStatus(string.Format("已合并 {0} 条 Cookie（不会覆盖其它站点）。当前 {1}", merged, CookieStore.Summary()));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导入失败：" + ex.Message, "VidGrab");
                }
            }
        }

        private void ManageCookie()
        {
            using (var dlg = new CookieDialog(() =>
            {
                CookieStore.Configure(_settings.CookiePath, _settings.UseCookie);
                UpdateCookieLabel();
            }))
            {
                dlg.ShowDialog(this);
            }
            CookieStore.Configure(_settings.CookiePath, _settings.UseCookie);
            UpdateCookieLabel();
        }

        private void OpenSaveDir()
        {
            try
            {
                string dir = _txtSave.Text.Trim();
                if (System.IO.Directory.Exists(dir)) Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch { }
        }

        private static List<string> ExtractUrls(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;

            foreach (Match m in Regex.Matches(text, @"https?://[^\s\""'<>【】]+", RegexOptions.IgnoreCase))
            {
                string url = m.Value.TrimEnd('.', ',', '，', '。', '！', '!', '）', ')', ']', '】');
                if (url.Length < 10) continue;
                if (!result.Contains(url)) result.Add(url);
            }
            return result;
        }

        private static string PlatformName(string url)
        {
            if (IsDouyin(url)) return "抖音";
            if (IsX(url)) return "X";
            if (url.IndexOf("youtube.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                url.IndexOf("youtu.be", StringComparison.OrdinalIgnoreCase) >= 0) return "YouTube";
            if (url.IndexOf("tiktok.com", StringComparison.OrdinalIgnoreCase) >= 0) return "TikTok";
            if (url.IndexOf("instagram.com", StringComparison.OrdinalIgnoreCase) >= 0) return "Instagram";
            return "其它";
        }

        private static bool IsDouyin(string url)
        {
            return url.IndexOf("douyin.com", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsX(string url)
        {
            return url.IndexOf("x.com/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("twitter.com/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ShortUrl(string url)
        {
            return url.Length <= 50 ? url : url.Substring(0, 47) + "…";
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "未知错误";
            string[] lines = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[lines.Length - 1].Trim() : s;
        }
    }
}
