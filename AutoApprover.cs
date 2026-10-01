// ============================================================================
//  AntigravityAutoApprove  v3
//  后台静默自动点击 Antigravity(Google 反重力 IDE) 的 Agent 权限批准弹窗。
//  策略: 一直选第 1 项 "Yes, allow this time"(选A); 两段式弹窗自动补点 Submit。
//  v3: 蓝白主题自绘UI / 400ms 哨兵轮询提速 / 后台(遮挡/最小化)安全点击 /
//      坐标兜底前校验落点归属, 被遮挡时短暂置前并还原 / 队列弹窗连点 / 打开日志。
//  编译: build.cmd (系统自带 .NET Framework 4.x 编译器, 零依赖)
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

    // ------------------------------ UI 基础控件 ------------------------------
    internal static class Ui
    {
        public static readonly Color Primary = Color.FromArgb(37, 99, 235);      // #2563EB
        public static readonly Color PrimaryHover = Color.FromArgb(29, 78, 216); // #1D4ED8
        public static readonly Color PrimaryLight = Color.FromArgb(219, 234, 254); // #DBEAFE
        public static readonly Color Bg = Color.FromArgb(247, 250, 255);         // #F7FAFF
        public static readonly Color TextMain = Color.FromArgb(15, 23, 42);      // #0F172A
        public static readonly Color TextSub = Color.FromArgb(100, 116, 139);    // #64748B
        public static readonly Color Ok = Color.FromArgb(22, 163, 74);           // #16A34A
        public static readonly Color OffTrack = Color.FromArgb(203, 213, 225);   // #CBD5E1

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

    // 现代拨动开关(轨道+文字, 整体可点击)
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
                Enabled ? Ui.TextMain : Ui.TextSub,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    }

    // 圆角按钮
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
                if (LabelColor == Ui.TextMain) // 浅色按钮
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

    // ------------------------------ 配置持久化 ------------------------------
    public class AppConfig
    {
        public bool Enabled;

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
        public static bool Verbose;
        public int Clicked;
        public Action<string> StatusChanged;
        public Action ClickedChanged;

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

                HashSet<int> pidSet = new HashSet<int>();
                if (SelfTest)
                {
                    pidSet.Add(Process.GetCurrentProcess().Id);
                }
                else
                {
                    Process[] ps = Process.GetProcessesByName("Antigravity");
                    if (ps.Length == 0) { SetStatus("运行中 · 等待 Antigravity 启动…"); return; }
                    foreach (Process p in ps) pidSet.Add(p.Id);
                }

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

                if (windowsSeen > 0 && Clicked == 0)
                    SetStatus("运行中 · 正在监视 Antigravity，等待权限弹窗…");
                else if (windowsSeen == 0)
                    SetStatus("运行中 · 未发现 Antigravity 窗口");
            }
            catch (Exception ex)
            {
                AppConfig.Log("TICK ERROR: " + ex.Message);
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        // 全量扫描: 找第 1 个 "Yes, allow"(=选项A) 与 Submit
        // 注: 不用 CacheRequest(实测其缓存引用会让 Invoke 慢至秒级并卡死后续轮次)
        private static void ScanOnce(AutomationElement win, out AutomationElement firstYes, out AutomationElement submit, out string yesName)
        {
            firstYes = null; submit = null; yesName = "";
            AutomationElementCollection all;
            try { all = win.FindAll(TreeScope.Descendants, Condition.TrueCondition); }
            catch (Exception ex) { if (Verbose) AppConfig.Log("SCAN ERR: " + ex.Message); return; }
            if (all == null) return;
            foreach (AutomationElement e in all)
            {
                string n;
                try { n = e.Current.Name; } catch { continue; }
                if (string.IsNullOrEmpty(n)) continue;
                if (firstYes == null && n.IndexOf("Yes, allow", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    firstYes = e;
                    yesName = n.Length > 60 ? n.Substring(0, 60) + "…" : n;
                }
                else if (submit == null && n.IndexOf("Submit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    submit = e;
                }
                if (firstYes != null && submit != null) break;
            }
            if (Verbose) AppConfig.Log("SCAN total=" + all.Count + " yes=" + (firstYes != null) + " sub=" + (submit != null));
        }

        // 哨兵查询(原生侧过滤, 只回传命中元素, 空闲时近零开销)。
        // 注: 已实测 Chromium 提供方 Invoke 仅 ~55ms; 自检里 WinForms 控件同进程
        //     作为提供方时 Invoke 会延迟 4s+, 那是测试夹具的怪癖, 与真实目标无关。
        private static bool DialogProbablyOpen(AutomationElement win)
        {
            try
            {
                OrCondition cond = new OrCondition(
                    new PropertyCondition(AutomationElement.NameProperty, "Skip"),
                    new PropertyCondition(AutomationElement.NameProperty, "Submit"),
                    new PropertyCondition(AutomationElement.NameProperty, "Yes, allow this time"));
                return win.FindAll(TreeScope.Descendants, cond).Count > 0;
            }
            catch { return false; }
        }

        // 批准窗口内的弹窗: 哨兵命中才全量扫描; 每 5 个 tick(约2s)强制扫一次兜底,
        // 防止弹窗元素的可访问名与哨兵词不完全一致时漏检。
        private void ApproveInWindow(AutomationElement win)
        {
            if (!DialogProbablyOpen(win) && _tick % 5 != 0) return;

            AutomationElement firstYes, submit; string yesName;
            ScanOnce(win, out firstYes, out submit, out yesName);
            if (firstYes == null)
            {
                if (Verbose) AppConfig.Log("ROUND: no dialog elements");
                return;
            }

            if (Click(firstYes, win))
            {
                Clicked++;
                AppConfig.Log("APPROVED(#" + Clicked + "): " + yesName);
                Action ch = ClickedChanged; if (ch != null) ch();
                SetStatus("运行中 · 已自动批准 " + Clicked + " 次 ✓");
                // 两段式弹窗: 若 Submit 仍在, 250ms 后补点
                if (submit != null)
                {
                    Thread.Sleep(250);
                    try { if (Click(submit, win)) AppConfig.Log("SUBMIT clicked"); } catch { }
                }
            }
            else
            {
                AppConfig.Log("CLICK FAILED: " + yesName);
            }
        }

        // 静默点击: UIA 模式优先(无需前台, 后台/遮挡/最小化均可), 坐标兜底(校验落点归属)
        private static bool Click(AutomationElement el, AutomationElement win)
        {
            object p;
            if (Verbose) AppConfig.Log("CLICK: enter");
            try
            {
                p = el.GetCurrentPattern(InvokePattern.Pattern);
                if (p is InvokePattern)
                {
                    if (Verbose) AppConfig.Log("CLICK: got InvokePattern");
                    ((InvokePattern)p).Invoke();
                    if (Verbose) AppConfig.Log("CLICK: invoked ok");
                    return true;
                }
            }
            catch (Exception ex) { if (Verbose) AppConfig.Log("INVOKE ERR: " + ex.Message); }
            try { p = el.GetCurrentPattern(SelectionItemPattern.Pattern); if (p is SelectionItemPattern) { ((SelectionItemPattern)p).Select(); return true; } } catch { }
            try { p = el.GetCurrentPattern(TogglePattern.Pattern); if (p is TogglePattern) { ((TogglePattern)p).Toggle(); return true; } } catch { }

            // 坐标兜底: 先确认落点属于 Antigravity 顶层窗口, 被遮挡时短暂置前
            try
            {
                System.Windows.Point pt;
                try { pt = el.GetClickablePoint(); }
                catch
                {
                    System.Windows.Rect r = el.Current.BoundingRectangle;
                    if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) return false;
                    pt = new System.Windows.Point(r.X + r.Width / 2, r.Y + r.Height / 2);
                }
                IntPtr root = IntPtr.Zero;
                try { root = (IntPtr)win.Current.NativeWindowHandle; } catch { }
                if (root == IntPtr.Zero) return false;

                if (!PointBelongsToRoot(pt.X, pt.Y, root))
                {
                    IntPtr prev = GetForegroundWindow();
                    try
                    {
                        ShowWindow(root, 9); // SW_RESTORE
                        SetForegroundWindow(root);
                        Thread.Sleep(120);
                    }
                    catch { }
                    bool owned = PointBelongsToRoot(pt.X, pt.Y, root);
                    if (!owned)
                    {
                        if (prev != IntPtr.Zero) { try { SetForegroundWindow(prev); } catch { } }
                        AppConfig.Log("COORD CLICK SKIPPED: point covered by another window");
                        return false; // 不盲点, 避免点到别的程序
                    }
                    PhysicalClick(pt.X, pt.Y);
                    if (prev != IntPtr.Zero) { try { SetForegroundWindow(prev); } catch { } }
                    return true;
                }
                PhysicalClick(pt.X, pt.Y);
                return true;
            }
            catch { return false; }
        }

        private static bool PointBelongsToRoot(double x, double y, IntPtr root)
        {
            POINT p = new POINT(); p.X = (int)x; p.Y = (int)y;
            IntPtr hit = WindowFromPoint(p);
            IntPtr hitRoot = GetAncestor(hit, 2); // GA_ROOT
            return hitRoot == root;
        }

        private static void PhysicalClick(double x, double y)
        {
            POINT old;
            GetCursorPos(out old);
            SetCursorPos((int)x, (int)y);
            mouse_event(0x02, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(30);
            mouse_event(0x04, 0, 0, 0, UIntPtr.Zero);
            SetCursorPos(old.X, old.Y);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr h, int cmd);
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
        private Label _lblHint;
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

            _tglEnabled.Checked = _cfg.Enabled;
            ApplyEnabledState(false);
            _tglAutostart.Checked = IsAutostartOn();

            _watcher.StatusChanged = delegate (string s) { UpdateStatus(s); };
            _watcher.ClickedChanged = delegate { _header.Refresh(); };
            _watcher.EnabledFlag = _cfg.Enabled;
            if (_cfg.Enabled) _watcher.Start();

            if (minimized) HideToTray();
        }

        private void ApplyRounded()
        {
            using (GraphicsPath gp = Ui.RoundedPath(new Rectangle(0, 0, Width, Height), 12))
            { Region = new Region(gp); }
        }

        // ------- 标题栏(蓝色渐变, 可拖动) -------
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
                g.FillEllipse(Brushes.White, 16, 19, 16, 16);
                g.FillEllipse(Brushes.White, 20, 23, 8, 8);
                TextRenderer.DrawText(g, "Antigravity 自动批准",
                    new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
                    new Rectangle(42, 0, 300, 54), Color.White,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                string cnt = "已批准 " + _watcher.Clicked + " 次";
                TextRenderer.DrawText(g, cnt, new Font("Microsoft YaHei UI", 8.5f),
                    new Rectangle(_header.Width - 170, 0, 140, 54),
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
            foreach (Control c in _header.Controls)
            {
                c.MouseDown += delegate (object s, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); }
                };
            }
            Controls.Add(_header);
            _header.BringToFront();
        }

        // ------- 主体 -------
        private void BuildBody()
        {
            // 状态卡片
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
                    new Rectangle(_card.Width - 120, 11, 104, 16), Ui.TextSub,
                    TextFormatFlags.Right);
                TextRenderer.DrawText(g, _watcher.Clicked + " 次", new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
                    new Rectangle(_card.Width - 120, 25, 104, 30), Ui.Primary,
                    TextFormatFlags.Right);
            };
            Controls.Add(_card);

            // 开关
            _tglEnabled = new ToggleSwitch { Left = 18, Top = 146, Width = 464, Height = 30, Text = "自动批准（后台静默 · 一直选第 1 项 Yes, allow this time）" };
            _tglEnabled.CheckedChanged += delegate
            {
                _cfg.Enabled = _tglEnabled.Checked;
                _cfg.Save();
                ApplyEnabledState(true);
            };
            Controls.Add(_tglEnabled);

            _tglAutostart = new ToggleSwitch { Left = 18, Top = 182, Width = 464, Height = 30, Text = "开机自动启动（静默模式，直接最小化到托盘）" };
            _tglAutostart.CheckedChanged += delegate { SetAutostart(_tglAutostart.Checked); };
            Controls.Add(_tglAutostart);

            _lblHint = new Label
            {
                Left = 18,
                Top = 216,
                Width = 300,
                Height = 16,
                Text = "Antigravity 在后台/被遮挡时同样可以自动点击",
                ForeColor = Ui.TextSub,
                Font = new Font("Microsoft YaHei UI", 8f),
                BackColor = Color.Transparent
            };
            Controls.Add(_lblHint);

            // 底部按钮
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

        private void ApplyEnabledState(bool announce)
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
                    _header.Invalidate();
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
                HideToTray();   // 点 ✕ = 静默后台继续运行
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

    // 标题栏小按钮
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
            if (_hover)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
                { g.FillRectangle(b, ClientRectangle); }
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
    }

    // ------------------------------ 自检模式 ------------------------------
    // --selftest: 生成仿真弹窗(数字前缀选项 + Submit + No), 验证自动批准全流程
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

            string deliveryLog = Path.Combine(Path.GetTempPath(), "agy_delivery_test.log");
            File.WriteAllText(deliveryLog, "");
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
                b.Click += delegate
                {
                    File.AppendAllText(log, "CLICKED::" + captured + Environment.NewLine);
                    File.AppendAllText(deliveryLog, "DELIVERED " + DateTime.Now.ToString("HH:mm:ss.fff") + " " + captured + Environment.NewLine);
                };
                form.Controls.Add(b);
                y += 45;
            }
            // UI 线程心跳: 验证消息泵是否畅通
            System.Windows.Forms.Timer hb = new System.Windows.Forms.Timer();
            hb.Interval = 500;
            hb.Tick += delegate
            {
                File.AppendAllText(deliveryLog, "UI-PUMP " + DateTime.Now.ToString("HH:mm:ss.fff") + Environment.NewLine);
            };
            hb.Start();

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
