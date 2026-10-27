using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("旅店排名")]
[assembly: System.Reflection.AssemblyDescription("炉石传说官方榜单桌面悬浮窗")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace HearthstoneRank
{
    public static class Art
    {
        public static readonly Color Background = Color.FromArgb(38, 29, 24);
        public static readonly Color Gold = Color.FromArgb(239, 201, 127);
        public static readonly Color Text = Color.FromArgb(230, 213, 183);
        public static readonly Color Muted = Color.FromArgb(175, 153, 123);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        public static Icon MakeIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var brush = new LinearGradientBrush(new Rectangle(1, 1, 30, 30), Gold, Color.FromArgb(93, 55, 25), 60f))
                        g.FillPolygon(brush, new[] { new Point(16, 1), new Point(30, 10), new Point(27, 27), new Point(9, 31), new Point(1, 17), new Point(5, 6) });
                    using (var brush = new SolidBrush(Color.FromArgb(11, 108, 169)))
                        g.FillPolygon(brush, new[] { new Point(16, 5), new Point(26, 12), new Point(23, 24), new Point(11, 27), new Point(5, 17), new Point(8, 9) });
                    using (var pen = new Pen(Color.FromArgb(173, 242, 255), 2.3f))
                        g.DrawLines(pen, new[] { new PointF(12, 22), new PointF(20, 17), new PointF(20, 12), new PointF(14, 10), new PointF(10, 15), new PointF(15, 18), new PointF(17, 14) });
                }
                IntPtr handle = bitmap.GetHicon();
                try { using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
    }

    public sealed class OverlayForm : Form
    {
        readonly RankContext context;
        readonly Font nameFont = new Font("Microsoft YaHei UI", 9, FontStyle.Bold, GraphicsUnit.Pixel);
        readonly Font rankFont = new Font("Georgia", 28, FontStyle.Bold, GraphicsUnit.Pixel);
        readonly Font smallFont = new Font("Microsoft YaHei UI", 8, FontStyle.Regular, GraphicsUnit.Pixel);
        readonly Font modeFont = new Font("Microsoft YaHei UI", 9, FontStyle.Regular, GraphicsUnit.Pixel);
        readonly ToolTip tooltip = new ToolTip();
        readonly System.Windows.Forms.Timer staleTimer = new System.Windows.Forms.Timer();
        Point dragOffset;
        bool dragging;
        public Snapshot Snapshot = new Snapshot { Status = RankStatus.Paused, Message = "请先填写玩家名称" };

        public OverlayForm(RankContext context)
        {
            this.context = context;
            Text = "旅店排名 · 悬浮窗";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ClientSize = new Size(112, 112);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(new[] { new Point(12, 0), new Point(100, 0), new Point(112, 12), new Point(112, 100),
                    new Point(100, 112), new Point(12, 112), new Point(0, 100), new Point(0, 12) });
                Region = new Region(path);
            }
            staleTimer.Interval = 1000;
            staleTimer.Tick += delegate { if (Visible && Snapshot.CheckedAt != DateTime.MinValue &&
                (DateTime.Now - Snapshot.CheckedAt).TotalSeconds > 20) Invalidate(); };
            staleTimer.Start();
            MouseDown += OnDown;
            MouseMove += OnMove;
            MouseUp += delegate { if (dragging) { dragging = false; context.Config.X = Left; context.Config.Y = Top; context.Save(); } };
            MouseDoubleClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left && e.Y > 25) context.OpenSettings(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (!context.Exiting) { e.Cancel = true; context.CloseOverlay(); }
            };
        }
        protected override CreateParams CreateParams
        { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }
        public void ApplyConfig()
        {
            TopMost = context.Config.Topmost;
            if (!Visible)
            {
                var desired = new Rectangle(context.Config.X, context.Config.Y, Width, Height);
                if (context.Config.X < 0 || !Screen.AllScreens.Any(s => Rectangle.Intersect(s.WorkingArea, desired).Width > 30 && Rectangle.Intersect(s.WorkingArea, desired).Height > 30))
                { var area = Screen.PrimaryScreen.WorkingArea; Location = new Point(area.Right - Width - 24, area.Top + 100); }
                else Location = desired.Location;
            }
            if (context.Config.OverlayVisible) Show(); else Hide();
            Invalidate();
        }
        public void SetSnapshot(Snapshot snapshot)
        {
            Snapshot = snapshot;
            string details = snapshot.Message + (snapshot.CheckedAt == DateTime.MinValue ? "" : "\n最近查询：" + snapshot.CheckedAt.ToString("HH:mm:ss"));
            if (snapshot.Candidates.Count == 1) details += "\n" + snapshot.Candidates[0];
            if (snapshot.Error.Length > 0) details += "\n" + snapshot.Error;
            tooltip.SetToolTip(this, details + "\n右键打开设置；拖动移动位置");
            if (Visible) Invalidate();
        }
        void OnDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { context.Menu.Show(this, e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            if (e.Y < 24 && e.X < 28) { context.Config.Topmost = !context.Config.Topmost; context.Apply(false); return; }
            if (e.Y < 24 && e.X > 86) { context.CloseOverlay(); return; }
            dragging = true; dragOffset = e.Location;
        }
        void OnMove(object sender, MouseEventArgs e)
        {
            if (dragging) { Point cursor = Cursor.Position; Location = new Point(cursor.X - dragOffset.X, cursor.Y - dragOffset.Y); }
        }
        static void Polygon(Graphics g, Color color, params PointF[] points)
        { using (var brush = new SolidBrush(color)) g.FillPolygon(brush, points); }
        static void Line(Graphics g, Color color, float x1, float y1, float x2, float y2)
        { using (var pen = new Pen(color)) g.DrawLine(pen, x1, y1, x2, y2); }
        static void Center(Graphics g, string text, Font font, Color color, RectangleF rect)
        {
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text, font, brush, rect, format);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(24, 15, 10));
            using (var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(223, 181, 100), Color.FromArgb(102, 66, 31), 60f))
                g.FillPolygon(brush, new[] { new Point(12, 2), new Point(99, 2), new Point(109, 13), new Point(109, 99), new Point(99, 109), new Point(13, 109), new Point(3, 99), new Point(3, 14) });
            Polygon(g, Color.FromArgb(57, 36, 26), new PointF(15, 7), new PointF(96, 7), new PointF(104, 16), new PointF(104, 96), new PointF(96, 104), new PointF(15, 104), new PointF(8, 96), new PointF(8, 16));
            for (int y = 20; y < 101; y += 13) Line(g, Color.FromArgb(69, 44, 29), 9, y, 103, y - 3);
            using (var brush = new SolidBrush(Color.FromArgb(32, 25, 21))) g.FillRectangle(brush, 15, 25, 82, 63);
            Line(g, Color.FromArgb(165, 128, 69), 22, 25, 90, 25);
            g.DrawIcon(context.Icon, new Rectangle(44, 2, 24, 24));
            Center(g, String.IsNullOrEmpty(context.Config.Player) ? "旅店排名" : context.Config.Player, nameFont, Art.Text, new RectangleF(17, 27, 78, 15));
            bool fresh = Snapshot.Status == RankStatus.Ready && (DateTime.Now - Snapshot.CheckedAt).TotalSeconds < 20;
            string rank = "···";
            if (Snapshot.Status == RankStatus.Ambiguous) rank = "重名";
            else if (Snapshot.Status == RankStatus.IdentityUnavailable) rank = "待核验";
            else if (Snapshot.Status == RankStatus.Paused) rank = "暂停";
            else if (Snapshot.Candidates.Count == 1) rank = Snapshot.Candidates[0].Rank.ToString();
            else if (Snapshot.Status == RankStatus.NotFound) rank = "未上榜";
            Center(g, rank, rank.Length > 5 ? modeFont : rankFont, Color.FromArgb(8, 6, 4), new RectangleF(13, 39, 88, 36));
            Center(g, rank, rank.Length > 5 ? modeFont : rankFont, fresh ? Color.FromArgb(250, 216, 132) : Color.FromArgb(190, 169, 131), new RectangleF(12, 38, 88, 36));
            string detail = Labels.Region(context.Config.Region) + " · " + Labels.Mode(context.Config.Mode);
            if (Snapshot.Candidates.Count == 1 && Snapshot.Candidates[0].Score.HasValue)
                detail = "积分 " + Snapshot.Candidates[0].Score.Value.ToString("0.##");
            if (Snapshot.Status == RankStatus.Ambiguous) detail = Snapshot.Candidates.Count + " 个同名结果";
            Center(g, detail, smallFont, Art.Muted, new RectangleF(12, 74, 88, 13));
            Line(g, Color.FromArgb(112, 78, 41), 22, 88, 90, 88);
            string status = fresh ? Snapshot.CheckedAt.ToString("HH:mm:ss") + " · 5s" : "右键设置";
            if (Snapshot.Status == RankStatus.Searching) status = Snapshot.CompletedPages + "/" + Snapshot.TotalPages + " · 核对同名";
            else if (Snapshot.Status == RankStatus.Offline) status = "连接失败 · 重试中";
            else if (Snapshot.Status == RankStatus.Ambiguous) status = "右键查看同名";
            else if (Snapshot.Status == RankStatus.NotFound) status = "仅含官网公开玩家";
            else if (Snapshot.Status == RankStatus.IdentityUnavailable) status = "官网未公开编号";
            Center(g, status, smallFont, fresh ? Color.FromArgb(132, 199, 180) : Art.Gold, new RectangleF(12, 90, 88, 14));
            Color pin = context.Config.Topmost ? Art.Gold : Art.Muted;
            Line(g, pin, 14, 10, 20, 10); Line(g, pin, 17, 10, 17, 20); Line(g, pin, 13, 16, 21, 16);
            Line(g, Art.Muted, 94, 10, 101, 17); Line(g, Art.Muted, 101, 10, 94, 17);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { staleTimer.Dispose(); tooltip.Dispose(); nameFont.Dispose(); rankFont.Dispose(); smallFont.Dispose(); modeFont.Dispose(); }
            base.Dispose(disposing);
        }
    }

    public static class Labels
    {
        public static string Region(string id)
        { switch (id) { case "CN": return "国服"; case "EU": return "欧服"; case "US": return "美服"; default: return "亚服"; } }
        public static string Mode(string id)
        { switch (id) { case "wild": return "狂野"; case "battlegrounds": return "酒馆单打"; case "battlegroundsduo": return "战棋双打"; default: return "标准"; } }
    }

    public sealed class SettingsForm : Form
    {
        readonly RankContext context;
        readonly Font uiFont = new Font("Microsoft YaHei UI", 9);
        readonly Font headingFont = new Font("Microsoft YaHei UI", 19, FontStyle.Bold);
        readonly TextBox player = new TextBox();
        readonly ComboBox region = new ComboBox();
        readonly ComboBox mode = new ComboBox();
        readonly NumericUpDown season = new NumericUpDown();
        readonly NumericUpDown hint = new NumericUpDown();
        readonly CheckBox monitoring = new CheckBox();
        readonly CheckBox visible = new CheckBox();
        readonly CheckBox topmost = new CheckBox();
        readonly ComboBox closeAction = new ComboBox();
        readonly ListView results = new ListView();
        readonly Label status = new Label();
        readonly Label source = new Label();
        string identityKey;
        readonly string[] regions = { "AP", "US", "EU", "CN" };
        readonly string[] modes = { "standard", "wild", "battlegrounds", "battlegroundsduo" };
        public SettingsForm(RankContext context)
        {
            this.context = context;
            Text = "旅店排名 · 设置";
            Icon = context.Icon;
            ClientSize = new Size(560, 688);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = uiFont;
            BackColor = Art.Background; ForeColor = Art.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            var title = new Label { Text = "旅店排名", Location = new Point(22, 17), Size = new Size(250, 34), Font = headingFont, ForeColor = Art.Gold };
            Controls.Add(title);
            AddLabel(this, "官方榜单，每 5 秒查询；暂停后停止联网。", 24, 55, 510, 22);
            var target = Group("监测对象", 88, 208);
            AddLabel(target, "玩家名称", 15, 29, 78, 24);
            player.SetBounds(105, 26, 391, 26); player.MaxLength = 80; player.AccessibleName = "玩家名称"; target.Controls.Add(player);
            AddLabel(target, "服务器", 15, 65, 78, 24);
            region.SetBounds(105, 62, 140, 26); region.DropDownStyle = ComboBoxStyle.DropDownList;
            region.Items.AddRange(new object[] { "亚太（AP）", "美洲（US）", "欧洲（EU）", "中国大陆（CN）" });
            region.AccessibleName = "服务器"; target.Controls.Add(region);
            mode.SetBounds(320, 62, 176, 26); mode.DropDownStyle = ComboBoxStyle.DropDownList;
            mode.Items.AddRange(new object[] { "标准模式", "狂野模式", "酒馆战棋（单打）", "酒馆战棋（双打）" });
            mode.AccessibleName = "游戏模式"; target.Controls.Add(mode); AddLabel(target, "模式", 265, 65, 52, 24);
            AddLabel(target, "赛季 ID", 15, 102, 78, 24);
            season.SetBounds(105, 99, 90, 26); season.Maximum = 9999; season.AccessibleName = "赛季 ID，0 表示当前赛季"; target.Controls.Add(season);
            AddLabel(target, "0 = 当前赛季；酒馆显示赛季号可能与 ID 不同", 208, 103, 295, 35);
            AddLabel(target, "参考排名", 15, 140, 78, 24);
            hint.SetBounds(105, 137, 140, 26); hint.Maximum = 10000000; hint.AccessibleName = "定位参考排名"; target.Controls.Add(hint);
            AddLabel(target, "仅加快定位，不用来区分同名账号", 259, 140, 243, 25);
            monitoring.Text = "开启监测"; monitoring.SetBounds(105, 174, 180, 24); target.Controls.Add(monitoring);

            var desktop = Group("桌面与关闭", 308, 104);
            visible.Text = "显示悬浮窗"; visible.SetBounds(16, 24, 150, 25); desktop.Controls.Add(visible);
            topmost.Text = "悬浮窗置顶"; topmost.SetBounds(180, 24, 150, 25); desktop.Controls.Add(topmost);
            AddLabel(desktop, "点击悬浮窗 ×", 16, 64, 110, 24);
            closeAction.SetBounds(140, 59, 354, 27); closeAction.DropDownStyle = ComboBoxStyle.DropDownList;
            closeAction.Items.AddRange(new object[] { "隐藏到托盘，继续监测", "退出整个程序" }); desktop.Controls.Add(closeAction);

            var matches = Group("查询结果与同名", 424, 196);
            source.SetBounds(15, 24, 480, 23); matches.Controls.Add(source);
            results.SetBounds(15, 49, 480, 80); results.View = View.Details; results.FullRowSelect = true; results.MultiSelect = false;
            results.Columns.Add("排名", 75); results.Columns.Add("玩家昵称 / 公开 BattleTag", 290); results.Columns.Add("积分", 95);
            results.BackColor = Color.FromArgb(52, 42, 34); results.ForeColor = Art.Text; results.AccessibleName = "同名候选结果";
            matches.Controls.Add(results);
            status.SetBounds(15, 135, 480, 50); status.ForeColor = Art.Muted; matches.Controls.Add(status);
            AddButton(this, "重新扫描", 23, 631, 99, delegate { context.Refresh(); });
            AddButton(this, "打开官网", 130, 631, 99, delegate { context.OpenWebsite(); });
            AddButton(this, "保存并应用", 237, 631, 132, SaveChanges);
            AddButton(this, "关闭设置", 377, 631, 99, delegate { Close(); });
            var exit = new LinkLabel { Text = "退出程序", Location = new Point(477, 643), AutoSize = true, LinkColor = Art.Muted };
            exit.LinkClicked += delegate { context.Quit(); }; Controls.Add(exit);
            region.SelectedIndexChanged += delegate { UpdateSourceHint(); };
            mode.SelectedIndexChanged += delegate { UpdateSourceHint(); };
            LoadConfig();
        }
        GroupBox Group(string text, int y, int height)
        {
            var group = new GroupBox { Text = text, Location = new Point(23, y), Size = new Size(514, height), ForeColor = Art.Gold };
            Controls.Add(group); return group;
        }
        static void AddLabel(Control parent, string text, int x, int y, int width, int height)
        { parent.Controls.Add(new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), ForeColor = Art.Muted }); }
        static void AddButton(Control parent, string text, int x, int y, int width, Action action)
        {
            var button = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, 32), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(77, 55, 35), ForeColor = Art.Text };
            button.FlatAppearance.BorderColor = Color.FromArgb(129, 94, 52);
            button.Click += delegate { action(); }; parent.Controls.Add(button);
        }
        public void LoadConfig()
        {
            Config c = context.Config;
            player.Text = c.Player; region.SelectedIndex = Array.IndexOf(regions, c.Region);
            mode.SelectedIndex = Array.IndexOf(modes, c.Mode); season.Value = c.Season; hint.Value = c.HintRank;
            monitoring.Checked = c.Monitoring; visible.Checked = c.OverlayVisible; topmost.Checked = c.Topmost;
            closeAction.SelectedIndex = c.CloseToTray ? 0 : 1; identityKey = c.IdentityKey;
            SetSnapshot(context.Latest);
        }
        void UpdateSourceHint()
        {
            source.Text = region.SelectedIndex == 3 ? "国服独立官方接口 · 仅含官网公开排名" : "暴雪国际服官方接口 · AP / US / EU 独立榜单";
        }
        void SaveChanges()
        {
            var copy = context.Config.Copy();
            copy.Player = player.Text.Trim(); copy.Region = regions[region.SelectedIndex]; copy.Mode = modes[mode.SelectedIndex];
            copy.Season = (int)season.Value; copy.HintRank = (int)hint.Value;
            copy.Monitoring = monitoring.Checked; copy.OverlayVisible = visible.Checked;
            copy.Topmost = topmost.Checked; copy.CloseToTray = closeAction.SelectedIndex == 0;
            try
            {
                copy.Validate();
                if (copy.Monitoring && copy.Player.Length == 0) throw new ArgumentException("请填写要监测的玩家名称，或关闭监测。");
                bool reset = copy.IdentityKey != context.Config.IdentityKey || copy.Monitoring != context.Config.Monitoring || copy.HintRank != context.Config.HintRank;
                context.Config = copy; identityKey = copy.IdentityKey;
                if (context.Apply(reset)) status.Text = "设置已保存。";
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "无法保存设置", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        public void SetSnapshot(Snapshot snapshot)
        {
            if (snapshot == null || identityKey != context.Config.IdentityKey) return;
            results.BeginUpdate(); results.Items.Clear();
            foreach (var row in snapshot.Candidates)
            {
                var item = new ListViewItem(row.Rank.ToString()); item.SubItems.Add(row.Name);
                item.SubItems.Add(row.Score.HasValue ? row.Score.Value.ToString("0.##") : "—"); results.Items.Add(item);
            }
            results.EndUpdate();
            string progress = snapshot.Status == RankStatus.Searching ? "（" + snapshot.CompletedPages + "/" + snapshot.TotalPages + " 页）" : "";
            source.Text = Labels.Region(context.Config.Region) + " · " + Labels.Mode(context.Config.Mode)
                + (snapshot.Season > 0 ? " · 赛季 ID " + snapshot.Season : "") + (snapshot.Total > 0 ? " · 公开 " + snapshot.Total + " 人" : "");
            status.Text = snapshot.Message + progress + "\n" + (snapshot.Error.Length > 0 ? snapshot.Error :
                "同名无法凭排名或积分唯一锁定；只有官网公开完整 BattleTag 时才能核验编号。");
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { uiFont.Dispose(); headingFont.Dispose(); }
        }
    }

    public sealed class RankContext : ApplicationContext
    {
        public Config Config;
        public Snapshot Latest;
        public readonly Icon Icon;
        public readonly ContextMenuStrip Menu = new ContextMenuStrip();
        public bool Exiting;
        readonly string configFile;
        readonly OverlayForm overlay;
        SettingsForm settings;
        readonly NotifyIcon tray;
        readonly RankMonitor monitor;
        readonly ToolStripMenuItem visibilityItem, pinItem, monitorItem;
        public RankContext(string directory, bool showSettings)
        {
            configFile = Path.Combine(directory, "settings.json");
            Config = new Config();
            string loadError = null;
            if (File.Exists(configFile))
            {
                try { Config = Json.Load<Config>(configFile); Config.Validate(); }
                catch (Exception error) { Config = new Config(); loadError = error.Message; }
            }
            Icon = Art.MakeIcon();
            overlay = new OverlayForm(this);
            IntPtr handle = overlay.Handle;
            Menu.Items.Add("打开设置…", null, delegate { OpenSettings(); });
            visibilityItem = new ToolStripMenuItem("显示悬浮窗", null, delegate { Config.OverlayVisible = !Config.OverlayVisible; Apply(false); });
            pinItem = new ToolStripMenuItem("悬浮窗置顶", null, delegate { Config.Topmost = !Config.Topmost; Apply(false); });
            monitorItem = new ToolStripMenuItem("开启监测", null, delegate { Config.Monitoring = !Config.Monitoring; Apply(true); });
            Menu.Items.Add(visibilityItem); Menu.Items.Add(pinItem); Menu.Items.Add(monitorItem);
            Menu.Items.Add("重新扫描全榜", null, delegate { Refresh(); });
            Menu.Items.Add("打开官方榜单", null, delegate { OpenWebsite(); });
            Menu.Items.Add(new ToolStripSeparator()); Menu.Items.Add("退出程序", null, delegate { Quit(); });
            tray = new NotifyIcon { Icon = Icon, Text = "旅店排名 · 右键打开设置", Visible = true, ContextMenuStrip = Menu };
            tray.DoubleClick += delegate { OpenSettings(); };
            monitor = new RankMonitor(new BoardApi());
            monitor.Changed += snapshot => {
                try { if (!Exiting && !overlay.IsDisposed) overlay.BeginInvoke((Action)(() => UpdateSnapshot(snapshot))); }
                catch (InvalidOperationException) { }
            };
            Apply(false); monitor.Apply(Config);
            if (showSettings || Config.Player.Length == 0) OpenSettings();
            if (loadError != null) MessageBox.Show("旧设置无法读取，已恢复默认值。\n" + loadError, "旅店排名");
        }
        void UpdateSnapshot(Snapshot snapshot)
        {
            if (Exiting) return;
            if (snapshot.QueryKey.Length > 0 && snapshot.QueryKey != Config.IdentityKey) return;
            if (!Config.Monitoring && snapshot.Status != RankStatus.Paused) return;
            Latest = snapshot; overlay.SetSnapshot(snapshot);
            string tip = "旅店排名 · " + Config.Player + " · " + snapshot.Message;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            if (settings != null && !settings.IsDisposed) settings.SetSnapshot(snapshot);
        }
        public bool Save()
        {
            try { Json.Save(configFile, Config); return true; }
            catch (Exception error) { MessageBox.Show("设置未能保存：" + error.Message, "旅店排名"); return false; }
        }
        public bool Apply(bool reset)
        {
            if (!Save()) return false;
            overlay.ApplyConfig();
            visibilityItem.Checked = Config.OverlayVisible; pinItem.Checked = Config.Topmost; monitorItem.Checked = Config.Monitoring;
            if (reset)
            {
                UpdateSnapshot(new Snapshot { Status = RankStatus.Searching, Message = "正在查询新设置" });
                monitor.Apply(Config);
            }
            return true;
        }
        public void CloseOverlay()
        { if (Config.CloseToTray) { Config.OverlayVisible = false; Apply(false); } else Quit(); }
        public void OpenSettings()
        {
            if (settings == null || settings.IsDisposed) settings = new SettingsForm(this);
            settings.Show(); if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
            settings.Activate();
        }
        public void PostOpenSettings()
        { try { overlay.BeginInvoke((Action)OpenSettings); } catch (InvalidOperationException) { } }
        public void PostQuit()
        { try { overlay.BeginInvoke((Action)Quit); } catch (InvalidOperationException) { } }
        public void Refresh()
        { monitor.Refresh(); }
        public void OpenWebsite()
        {
            int page = Latest != null && Latest.Candidates.Count > 0 ? Latest.Candidates[0].Page : 1;
            int season = Latest != null ? Latest.Season : Config.Season;
            Process.Start(BoardApi.WebsiteUrl(Config, page, season));
        }
        public void Quit()
        {
            if (Exiting) return;
            Exiting = true; Save(); monitor.Dispose(); tray.Visible = false; tray.Dispose(); Menu.Dispose();
            if (settings != null) settings.Dispose(); overlay.Dispose(); Icon.Dispose(); ExitThread();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = 4;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HearthstoneRank");
            int dataArg = Array.IndexOf(args, "--data-dir");
            if (dataArg >= 0 && dataArg + 1 < args.Length) directory = Path.GetFullPath(args[dataArg + 1]);
            string instance;
            using (var hash = SHA256.Create()) instance = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(directory.ToLowerInvariant()))).Replace("-", "").Substring(0, 24);
            if (args.Contains("--shutdown"))
            {
                EventWaitHandle existing;
                if (EventWaitHandle.TryOpenExisting("Local\\HearthstoneRank.Quit." + instance, out existing))
                { using (existing) existing.Set(); }
                return;
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\HearthstoneRank." + instance, out created))
            using (var openEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\HearthstoneRank.Open." + instance))
            using (var quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\HearthstoneRank.Quit." + instance))
            {
                if (!created) { openEvent.Set(); return; }
                RankContext context = null;
                RegisteredWaitHandle registration = null;
                RegisteredWaitHandle quitRegistration = null;
                try
                {
                    context = new RankContext(directory, args.Contains("--settings"));
                    registration = ThreadPool.RegisterWaitForSingleObject(openEvent, delegate { context.PostOpenSettings(); }, null, Timeout.Infinite, false);
                    quitRegistration = ThreadPool.RegisterWaitForSingleObject(quitEvent, delegate { context.PostQuit(); }, null, Timeout.Infinite, false);
                    Application.Run(context);
                }
                catch (Exception error) { MessageBox.Show(error.Message, "旅店排名", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { if (registration != null) registration.Unregister(null); if (quitRegistration != null) quitRegistration.Unregister(null); if (context != null && !context.Exiting) context.Quit(); mutex.ReleaseMutex(); }
            }
        }
    }
}
