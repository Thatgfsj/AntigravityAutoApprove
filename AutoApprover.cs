// ============================================================================
//  AntigravityAutoApprove  v5
//  后台静默自动点击 Antigravity(Google 反重力 IDE) 的 Agent 权限批准弹窗。
//  策略: 一直选第 1 项 "Yes, allow this time"(选A); 两段式弹窗自动补点 Submit。
//  双管道:
//   [UIA]  每400ms扫描窗口树, 仅点击支持 Invoke/Selection/Toggle 模式的元素
//          (绝不抢焦点、绝不移动鼠标、绝不改前台窗口 —— 无感知);
//   [CDP]  经 Antigravity 自带调试协议(DevToolsActivePort)在页面内执行 JS 点击,
//          窗口最小化/遮挡/虚拟桌面均有效, 且自动 reload 加载失败的错误页。
//  编译: build.cmd (系统自带 .NET Framework 4.x 编译器, 零依赖)
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AntigravityAutoApprove
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "AntigravityAutoApprove_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Antigravity 自动批准程序已在运行中（请查看系统托盘图标）。",
                        "Antigravity 自动批准", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool minimized = args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
                bool selftest = args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase));
                if (selftest) { SelfTest.Run(); return; }
                Application.Run(new MainForm(minimized));
            }
        }
    }

    // ------------------------------ UI 基础 ------------------------------
    internal static class Ui
    {
        public static readonly Color Primary = Color.FromArgb(37, 99, 235);
        public static readonly Color PrimaryHover = Color.FromArgb(29, 78, 216);
        public static readonly Color PrimaryLight = Color.FromArgb(219, 234, 254);
        public static readonly Color Bg = Color.FromArgb(247, 250, 255);
        public static readonly Color TextMain = Color.FromArgb(15, 23, 42);
        public static readonly Color TextSub = Color.FromArgb(100, 116, 139);
        public static readonly Color Ok = Color.FromArgb(22, 163, 74);
        public static readonly Color OffTrack = Color.FromArgb(203, 213, 225);

        public static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    public class ToggleSwitch : Control
    {
        private bool _checked;
        public Color OnColor = Ui.Primary;
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                    Invalidate();
                }
            }
        }

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(440, 30);
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9.5f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Color.White);
            Rectangle track = new Rectangle(2, Height / 2 - 11, 44, 22);
            using (SolidBrush b = new SolidBrush(_checked ? OnColor : Ui.OffTrack))
            using (GraphicsPath gp = Ui.RoundedPath(track, 11))
            { g.FillPath(b, gp); }
            int kx = _checked ? track.Right - 20 : track.X + 2;
            using (SolidBrush b = new SolidBrush(Color.White))
            { g.FillEllipse(b, kx, track.Y + 2, 18, 18); }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(58, 0, Width - 60, Height),
                Ui.TextMain, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    }

    public class RoundedButton : Control
    {
        public Color NormalColor = Ui.Primary;
        public Color HoverColor = Ui.PrimaryHover;
        public Color LabelColor = Color.White;
        private bool _hover;

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9f);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Color.White);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath gp = Ui.RoundedPath(r, 8))
            {
                if (LabelColor == Ui.TextMain)
                {
                    using (SolidBrush b = new SolidBrush(_hover ? Ui.PrimaryLight : Color.FromArgb(241, 245, 249)))
                    using (Pen pen = new Pen(Ui.PrimaryLight))
                    { g.FillPath(b, gp); g.DrawPath(pen, gp); }
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(_hover ? HoverColor : NormalColor))
                    { g.FillPath(b, gp); }
                }
            }
            TextRenderer.DrawText(g, Text, Font, r, LabelColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
    }

    // 标题栏小按钮: 底色必须与渐变右端一致, 否则出现花屏
    public class HeaderButton : Control
    {
        private bool _hover;
        public HeaderButton(string text, int width)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = text;
            Size = new Size(width, 54);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 10f);
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.FromArgb(56, 125, 244));   // 渐变在 x≈420-500 处的颜色
            if (_hover)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(50, 255, 255, 255)))
                { g.FillRectangle(b, ClientRectangle); }
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
    }

    // ------------------------------ 配置持久化 ------------------------------
    public class AppConfig
    {
        public bool Enabled;
        public bool Verbose;

        private static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AntigravityAutoApprove"); }
        }
        private static string FilePath
        {
            get { return Path.Combine(Dir, "config.ini"); }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, "enabled=" + (Enabled ? "1" : "0") + "\r\n");
            }
            catch { }
        }

        public static AppConfig Load()
        {
            AppConfig c = new AppConfig();
            c.Enabled = false;
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        string[] kv = line.Split('=');
                        if (kv.Length < 2) continue;
                        if (kv[0] == "enabled") c.Enabled = kv[1].Trim() == "1";
                        else if (kv[0] == "verbose") c.Verbose = kv[1].Trim() == "1";
                    }
                }
            }
            catch { }
            return c;
        }

        public static string LogPath
        {
            get { return Path.Combine(Dir, "log.txt"); }
        }

        public static void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg + "\r\n");
            }
            catch { }
        }
    }

    // ------------------------------ 监视器 ------------------------------
    public class Watcher
    {
        private System.Threading.Timer _timer;
        private int _busy;
        private int _tick;
        public bool EnabledFlag;
        public bool SelfTest;
        public int Clicked;
        public Action<string> StatusChanged;
        public Action ClickedChanged;

        // 在页面里执行: 错误页自愈 / 穿透 shadow DOM 找第1个 Yes 选项点击 / 两段式 Submit
        private const string CdpJs =
            "(function(){" +
            "if(location.href.indexOf('chrome-error')===0){if(!window.__agyH||Date.now()-window.__agyH>5000){window.__agyH=Date.now();location.reload();return 'HEAL';}return 'N';}" +
            "function deep(r,o){var e=r.querySelectorAll('*');for(var i=0;i<e.length;i++){o.push(e[i]);if(e[i].shadowRoot)deep(e[i].shadowRoot,o);}return o;}" +
            "var a=deep(document,[]);" +
            "function row(pre){for(var i=a.length-1;i>=0;i--){var t=(a[i].textContent||'').replace(/\\s+/g,' ').trim();if(t.indexOf(pre)>=0){var c=a[i].closest('a,button,[role=button],[role=option],[role=listitem],li,div');return c||a[i];}}return null;}" +
            "var y=row('Yes, allow');if(y){y.click();window.__agyT=Date.now();return 'C1';}" +
            "if(window.__agyT&&Date.now()-window.__agyT<1500){var s=row('Submit');if(s){s.click();window.__agyT=0;return 'S1';}return 'W';}" +
            "return 'N';})()";

        public void Start()
        {
            if (_timer != null) return;
            _timer = new System.Threading.Timer(delegate { Tick(); }, null, 400, 400);
        }

        public void Stop()
        {
            if (_timer != null) { _timer.Dispose(); _timer = null; }
        }

        private void SetStatus(string s)
        {
            Action<string> h = StatusChanged;
            if (h != null) h(s);
        }

        private void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                if (!EnabledFlag) return;
                _tick++;

                bool anyWindow = UiaPass();
                if (!SelfTest) CdpPass();

                if (Clicked == 0)
                    SetStatus(anyWindow ? "运行中 · 正在监视 Antigravity（含后台/最小化）…" : "运行中 · 等待 Antigravity 启动…");
            }
            catch (Exception ex)
            {
                AppConfig.Log("TICK ERROR: " + ex.Message);
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        // ---------- UIA 管道: 仅模式点击, 零干扰 ----------
        private bool UiaPass()
        {
            HashSet<int> pidSet = new HashSet<int>();
            if (SelfTest) { pidSet.Add(Process.GetCurrentProcess().Id); return UiaScan(pidSet); }
            Process[] ps = Process.GetProcessesByName("Antigravity");
            if (ps.Length == 0) return false;
            foreach (Process p in ps) pidSet.Add(p.Id);
            return UiaScan(pidSet);
        }

        private bool UiaScan(HashSet<int> pidSet)
        {
            int windowsSeen = 0;
            AutomationElementCollection roots = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement win in roots)
            {
                int pid;
                try { pid = win.Current.ProcessId; } catch { continue; }
                if (!pidSet.Contains(pid)) continue;
                string wname;
                try { wname = win.Current.Name ?? ""; } catch { continue; }
                if (!SelfTest && wname.Contains("自动批准")) continue;
                windowsSeen++;
                ApproveInWindow(win);
            }
            return windowsSeen > 0;
        }

        // 找第 1 个 "Yes, allow"(=选项A) 与 Submit; Yes 优先选支持点击模式的元素
        private static void ScanOnce(AutomationElement win, out AutomationElement firstYes, out AutomationElement submit, out string yesName)
        {
            firstYes = null; submit = null; yesName = "";
            AutomationElementCollection all;
            try { all = win.FindAll(TreeScope.Descendants, Condition.TrueCondition); }
            catch (Exception ex) { if (Watcher.Verbose) AppConfig.Log("SCAN ERR: " + ex.Message); return; }
            if (all == null) return;

            List<AutomationElement> yesCandidates = new List<AutomationElement>();
            for (int i = 0; i < all.Count; i++)
            {
                AutomationElement e = all[i];
                string n;
                try { n = e.Current.Name; } catch { continue; }
                if (string.IsNullOrEmpty(n)) continue;
                if (n.IndexOf("Yes, allow", StringComparison.OrdinalIgnoreCase) >= 0) yesCandidates.Add(e);
                else if (submit == null && n.IndexOf("Submit", StringComparison.OrdinalIgnoreCase) >= 0) submit = e;
            }
            if (yesCandidates.Count == 0) return;

            // 优先: 有 Invoke/Selection/Toggle 模式的元素(可无感点击)
            foreach (AutomationElement e in yesCandidates)
            {
                object p;
                try { if (e.TryGetCurrentPattern(InvokePattern.Pattern, out p)) { firstYes = e; break; } } catch { }
                try { if (e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out p)) { firstYes = e; break; } } catch { }
                try { if (e.TryGetCurrentPattern(TogglePattern.Pattern, out p)) { firstYes = e; break; } } catch { }
            }
            if (firstYes == null) firstYes = yesCandidates[0];

            yesName = "";
            try { string n = firstYes.Current.Name; yesName = n.Length > 60 ? n.Substring(0, 60) + "…" : n; } catch { }
            if (Watcher.Verbose) AppConfig.Log("SCAN yes=" + (firstYes != null) + " sub=" + (submit != null) + " candidates=" + yesCandidates.Count);
        }

        private void ApproveInWindow(AutomationElement win)
        {
            AutomationElement firstYes, submit; string yesName;
            ScanOnce(win, out firstYes, out submit, out yesName);
            if (firstYes == null) return;

            if (Click(firstYes))
            {
                Clicked++;
                AppConfig.Log("APPROVED(#" + Clicked + "): " + yesName);
                Action ch = ClickedChanged; if (ch != null) ch();
                SetStatus("运行中 · 已自动批准 " + Clicked + " 次 ✓");
                if (submit != null)
                {
                    Thread.Sleep(250);
                    try { if (Click(submit)) AppConfig.Log("SUBMIT clicked"); } catch { }
                }
            }
            else
            {
                AppConfig.Log("UIA CLICK FAILED(留给CDP管道): " + yesName);
            }
        }

        // 仅无障碍模式点击: 不动鼠标、不改前台、不判坐标 —— 任何失败都交给 CDP 管道
        private static bool Click(AutomationElement el)
        {
            object p;
            try { p = el.GetCurrentPattern(InvokePattern.Pattern); if (p is InvokePattern) { ((InvokePattern)p).Invoke(); return true; } } catch { }
            try { p = el.GetCurrentPattern(SelectionItemPattern.Pattern); if (p is SelectionItemPattern) { ((SelectionItemPattern)p).Select(); return true; } } catch { }
            try { p = el.GetCurrentPattern(TogglePattern.Pattern); if (p is TogglePattern) { ((TogglePattern)p).Toggle(); return true; } } catch { }
            return false;
        }

        // ---------- CDP 管道: 后台/最小化/遮挡均有效 + 错误页自愈 ----------
        private DateTime _lastHeal;
        private int _nullCount;

        private void CdpPass()
        {
            string r = Cdp.Evaluate(CdpJs);
            if (Verbose)
            {
                _nullCount = (r == null) ? _nullCount + 1 : 0;
                if (r != null) AppConfig.Log("CDP EVAL: " + r);
                else if (_nullCount % 25 == 1) AppConfig.Log("CDP EVAL: null (x" + _nullCount + ")");
            }
            if (r == "C1")
            {
                Clicked++;
                AppConfig.Log("CDP APPROVED(#" + Clicked + ")");
                Action ch = ClickedChanged; if (ch != null) ch();
                SetStatus("运行中 · 已自动批准 " + Clicked + " 次 ✓");
            }
            else if (r == "S1") AppConfig.Log("CDP SUBMIT clicked");
            else if (r == "HEAL")
            {
                if ((DateTime.UtcNow - _lastHeal).TotalSeconds > 6)
                {
                    _lastHeal = DateTime.UtcNow;
                    AppConfig.Log("CDP: 检测到错误页, 已自动重载 Antigravity 界面");
                }
            }
        }

        public static bool Verbose;
    }

    // ------------------------------ 主窗口(蓝白主题) ------------------------------
    public class MainForm : Form
    {
        private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string RunValueName = "AntigravityAutoApprove";

        private readonly AppConfig _cfg = AppConfig.Load();
        private readonly Watcher _watcher = new Watcher();
        private Panel _header;
        private Panel _card;
        private ToggleSwitch _tglEnabled;
        private ToggleSwitch _tglAutostart;
        private NotifyIcon _tray;
        private bool _allowClose;
        private string _statusText = "未启动";
        private bool _watching;

        public MainForm(bool minimized)
        {
            Text = "Antigravity 自动批准";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 268);
            BackColor = Ui.Bg;
            DoubleBuffered = true;
            Font = new Font("Microsoft YaHei UI", 9f);

            BuildHeader();
            BuildBody();
            BuildTray();

            Paint += delegate (object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Color.FromArgb(226, 232, 240)))
                using (GraphicsPath gp = Ui.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 12))
                { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.DrawPath(pen, gp); }
            };
            Load += delegate { ApplyRounded(); };
            Resize += delegate { ApplyRounded(); };

            ApplyEnabledState();

            _watcher.StatusChanged = delegate (string s) { UpdateStatus(s); };
            _watcher.ClickedChanged = delegate { _header.Refresh(); };
            _watcher.EnabledFlag = _cfg.Enabled;
            Watcher.Verbose = _cfg.Verbose;
            if (_cfg.Enabled) _watcher.Start();

            if (minimized) HideToTray();
        }

        private void ApplyRounded()
        {
            using (GraphicsPath gp = Ui.RoundedPath(new Rectangle(0, 0, Width, Height), 12))
            { Region = new Region(gp); }
        }

        private void BuildHeader()
        {
            _header = new Panel { Left = 0, Top = 0, Width = 500, Height = 54 };
            _header.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (LinearGradientBrush b = new LinearGradientBrush(_header.ClientRectangle,
                    Color.FromArgb(37, 99, 235), Color.FromArgb(59, 130, 246), 0f))
                { g.FillRectangle(b, _header.ClientRectangle); }
                // logo: 白色圆角方块 + 蓝色 A
                using (GraphicsPath gp = Ui.RoundedPath(new Rectangle(14, 15, 24, 24), 6))
                { g.FillPath(Brushes.White, gp); }
                TextRenderer.DrawText(g, "A", new Font("Segoe UI", 11f, FontStyle.Bold),
                    new Rectangle(14, 13, 24, 26), Ui.Primary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, "Antigravity 自动批准",
                    new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
                    new Rectangle(46, 0, 240, 54), Color.White,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                string cnt = "已批准 " + _watcher.Clicked + " 次";
                TextRenderer.DrawText(g, cnt, new Font("Microsoft YaHei UI", 8.5f),
                    new Rectangle(_header.Width - 268, 0, 168, 54),
                    Color.FromArgb(219, 234, 254),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            };

            HeaderButton btnMin = new HeaderButton("—", 40);
            btnMin.Location = new Point(500 - 84, 0);
            btnMin.Click += delegate { HideToTray(); };
            HeaderButton btnClose = new HeaderButton("✕", 40);
            btnClose.Location = new Point(500 - 44, 0);
            btnClose.Click += delegate { HideToTray(); };
            _header.Controls.Add(btnMin);
            _header.Controls.Add(btnClose);
            _header.MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); }
            };
            Controls.Add(_header);
            _header.BringToFront();
        }

        private void BuildBody()
        {
            _card = new Panel { Left = 16, Top = 68, Width = 468, Height = 62 };
            _card.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, _card.Width - 1, _card.Height - 1);
                using (GraphicsPath gp = Ui.RoundedPath(r, 10))
                {
                    using (SolidBrush b = new SolidBrush(Color.White)) g.FillPath(b, gp);
                    using (Pen p = new Pen(Ui.PrimaryLight)) g.DrawPath(p, gp);
                }
                using (SolidBrush dot = new SolidBrush(_watching ? Ui.Ok : Ui.OffTrack))
                { g.FillEllipse(dot, 16, _card.Height / 2 - 5, 10, 10); }
                TextRenderer.DrawText(g, _statusText, new Font("Microsoft YaHei UI", 9.5f),
                    new Rectangle(36, 0, 300, _card.Height), Ui.TextMain,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                TextRenderer.DrawText(g, "已自动批准", new Font("Microsoft YaHei UI", 8f),
                    new Rectangle(_card.Width - 120, 11, 104, 16), Ui.TextSub, TextFormatFlags.Right);
                TextRenderer.DrawText(g, _watcher.Clicked + " 次", new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
                    new Rectangle(_card.Width - 120, 25, 104, 30), Ui.Primary, TextFormatFlags.Right);
            };
            Controls.Add(_card);

            _tglEnabled = new ToggleSwitch { Left = 18, Top = 146, Width = 464, Height = 30, Text = "自动批准（后台静默 · 一直选第 1 项 Yes, allow this time）" };
            _tglEnabled.Checked = _cfg.Enabled;
            _tglEnabled.CheckedChanged += delegate
            {
                _cfg.Enabled = _tglEnabled.Checked;
                _cfg.Save();
                ApplyEnabledState();
            };
            Controls.Add(_tglEnabled);

            _tglAutostart = new ToggleSwitch { Left = 18, Top = 182, Width = 464, Height = 30, Text = "开机自动启动（静默模式，直接最小化到托盘）" };
            _tglAutostart.Checked = IsAutostartOn();
            _tglAutostart.CheckedChanged += delegate { SetAutostart(_tglAutostart.Checked); };
            Controls.Add(_tglAutostart);

            Label lblHint = new Label
            {
                Left = 18, Top = 216, Width = 460, Height = 16,
                Text = "最小化/遮挡/虚拟桌面均可自动点击（UIA + CDP 双通道）",
                ForeColor = Ui.TextSub, Font = new Font("Microsoft YaHei UI", 8f), BackColor = Color.Transparent
            };
            Controls.Add(lblHint);

            RoundedButton btnHide = new RoundedButton { Left = 16, Top = 236, Width = 170, Height = 32, Text = "隐藏到托盘继续运行" };
            btnHide.Click += delegate { HideToTray(); };
            RoundedButton btnLog = new RoundedButton { Left = 196, Top = 236, Width = 110, Height = 32, Text = "打开日志", NormalColor = Color.FromArgb(241, 245, 249), HoverColor = Ui.PrimaryLight, LabelColor = Ui.TextMain };
            btnLog.Click += delegate
            {
                try
                {
                    if (!File.Exists(AppConfig.LogPath)) AppConfig.Log("(日志创建)");
                    Process.Start("notepad.exe", AppConfig.LogPath);
                }
                catch { }
            };
            RoundedButton btnExit = new RoundedButton { Left = 316, Top = 236, Width = 110, Height = 32, Text = "退出程序", NormalColor = Color.FromArgb(241, 245, 249), HoverColor = Ui.PrimaryLight, LabelColor = Ui.TextMain };
            btnExit.Click += delegate { _allowClose = true; Close(); };
            Controls.Add(btnHide);
            Controls.Add(btnLog);
            Controls.Add(btnExit);
        }

        private void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = SystemIcons.Shield;
            _tray.Text = "Antigravity 自动批准";
            _tray.Visible = true;
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("显示窗口", null, delegate { ShowFromTray(); });
            menu.Items.Add("退出程序", null, delegate { _allowClose = true; Close(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowFromTray(); };
        }

        private void ApplyEnabledState()
        {
            _watching = _cfg.Enabled;
            _watcher.EnabledFlag = _cfg.Enabled;
            if (_cfg.Enabled) { _watcher.Start(); UpdateStatus("运行中 · 正在监视 Antigravity…"); }
            else { _watcher.Stop(); UpdateStatus("已停用（打开上方开关以启用）"); }
            _card.Invalidate();
        }

        private void UpdateStatus(string s)
        {
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    _statusText = s;
                    string trayText = "Antigravity 自动批准 · " + s;
                    _tray.Text = trayText.Length > 63 ? trayText.Substring(0, 63) : trayText;
                    _card.Invalidate();
                    _header.Refresh();
                });
            }
            catch { }
        }

        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
        }

        private void ShowFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            _tray.Visible = false;
            base.OnFormClosing(e);
        }

        private static bool IsAutostartOn()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
            {
                return k != null && k.GetValue(RunValueName) != null;
            }
        }

        private static void SetAutostart(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (k == null) return;
                if (on) k.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\" --minimized");
                else { try { k.DeleteValue(RunValueName, false); } catch { } }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr h, int m, int w, int l);
    }

    // ------------------------------ 自检模式 ------------------------------
    internal static class SelfTest
    {
        public static void Run()
        {
            string log = Path.Combine(Path.GetTempPath(), "agy_autoapprove_test.log");
            File.WriteAllText(log, "");

            Form form = new Form();
            form.Text = "selftest-dialog";
            form.ClientSize = new Size(680, 280);
            form.StartPosition = FormStartPosition.Manual;
            form.Left = 40; form.Top = 40;

            string[] names = new string[]
            {
                "1 Yes, allow this time",
                "2 Yes, and always allow python \"C:\\x\\inspect.py\" in this conversation",
                "3 Yes, and always allow python \"C:\\x\\inspect.py\" in this project",
                "5 No (tell the agent what to do instead)",
                "Submit"
            };
            int y = 10;
            foreach (string n in names)
            {
                string captured = n;
                Button b = new Button { Left = 10, Top = y, Width = 650, Height = 34, Text = n };
                b.Click += delegate { File.AppendAllText(log, "CLICKED::" + captured + Environment.NewLine); };
                form.Controls.Add(b);
                y += 45;
            }

            Watcher w = new Watcher();
            w.SelfTest = true;
            Watcher.Verbose = true;
            w.EnabledFlag = true;
            w.Start();

            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 9000;
            t.Tick += delegate { t.Stop(); form.Close(); };
            t.Start();

            Application.Run(form);
            Environment.Exit(0);
        }
    }
}
