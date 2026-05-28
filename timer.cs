using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ProgressBarTimer
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TimerForm());
        }
    }

    sealed class TimerForm : Form
    {
        const int NUM_SEGS = 16;
        const int DEFAULT_SECS = 600;
        const int MIN_SECS = 60;
        const int MAX_SECS = 5999;
        const int TICK_MS = 100;
        const int CHROME_H = 26;
        const int RESIZE_GRIP = 7;
        const double WARN_RATIO = 0.30;

        const int WM_NCHITTEST = 0x0084;
        const int WM_NCLBUTTONDOWN = 0x00A1;
        const int WM_EXITSIZEMOVE = 0x0232;
        const int HTCAPTION = 2;
        const int HTLEFT = 10;
        const int HTRIGHT = 11;
        const int HTTOP = 12;
        const int HTTOPLEFT = 13;
        const int HTTOPRIGHT = 14;
        const int HTBOTTOM = 15;
        const int HTBOTTOMLEFT = 16;
        const int HTBOTTOMRIGHT = 17;

        static readonly Color C_BG = Color.FromArgb(18, 20, 24);
        static readonly Color C_PANEL = Color.FromArgb(28, 31, 37);
        static readonly Color C_PANEL_EDGE = Color.FromArgb(58, 64, 74);
        static readonly Color C_TRACK = Color.FromArgb(43, 48, 57);
        static readonly Color C_TRACK_LINE = Color.FromArgb(67, 74, 86);
        static readonly Color C_SEG_IDLE = Color.FromArgb(50, 56, 66);
        static readonly Color C_TXT = Color.FromArgb(239, 244, 248);
        static readonly Color C_TXT_MUTED = Color.FromArgb(143, 153, 166);
        static readonly Color C_ACCENT = Color.FromArgb(76, 201, 240);
        static readonly Color C_ACCENT_2 = Color.FromArgb(72, 149, 239);
        static readonly Color C_WARN = Color.FromArgb(255, 190, 96);
        static readonly Color C_WARN_2 = Color.FromArgb(255, 138, 76);
        static readonly Color C_OT = Color.FromArgb(255, 84, 112);
        static readonly Color C_OT_2 = Color.FromArgb(239, 56, 92);
        static readonly Color C_BTN = Color.FromArgb(42, 47, 56);
        static readonly Color C_BTN_HOVER = Color.FromArgb(55, 62, 74);
        static readonly Color C_BTN_DOWN = Color.FromArgb(36, 41, 49);

        int setSeconds;
        double remaining;
        double overtimeElapsed;
        bool isRunning;
        bool isOvertime;
        DateTime lastTick;
        string keyBuf = "";
        bool repeatUp;

        System.Windows.Forms.Timer ticker;
        System.Windows.Forms.Timer keyTimer;
        System.Windows.Forms.Timer repeatTimer;
        IconButton btnUp, btnDn, btnStartStop, btnMinimize, btnClose;
        Image imgMinus, imgPlay, imgPause, imgPlus;
        Font timeFont;
        Font labelFont;

        Rectangle rcPanel, rcBar, rcTime, rcStatus;

        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public TimerForm()
        {
            setSeconds = DEFAULT_SECS;
            remaining = setSeconds;

            Text = "ProgressBarTimer";
            Icon = LoadAppIcon();
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = true;
            BackColor = C_BG;
            FormBorderStyle = FormBorderStyle.None;
            MinimumSize = new Size(360, 86);
            StartPosition = FormStartPosition.Manual;
            Size = new Size(460, 112);
            Location = ClampToScreen(new Point(200, 200));

            LoadButtonImages();

            btnDn = MakeButton("-");
            btnStartStop = MakeButton(">");
            btnUp = MakeButton("+");
            btnMinimize = MakeChromeButton("-");
            btnClose = MakeChromeButton("x");

            btnDn.ButtonImage = imgMinus;
            btnStartStop.ButtonImage = imgPlay;
            btnUp.ButtonImage = imgPlus;

            btnUp.MouseDown += (s, e) => BeginRepeat(true);
            btnDn.MouseDown += (s, e) => BeginRepeat(false);
            btnUp.MouseUp += (s, e) => EndRepeat();
            btnDn.MouseUp += (s, e) => EndRepeat();
            btnUp.MouseLeave += (s, e) => EndRepeat();
            btnDn.MouseLeave += (s, e) => EndRepeat();
            btnStartStop.Click += (s, e) => Toggle();
            btnMinimize.Click += (s, e) => WindowState = FormWindowState.Minimized;
            btnClose.Click += (s, e) => Close();

            Controls.Add(btnDn);
            Controls.Add(btnStartStop);
            Controls.Add(btnUp);
            Controls.Add(btnMinimize);
            Controls.Add(btnClose);

            ticker = new System.Windows.Forms.Timer();
            ticker.Interval = TICK_MS;
            ticker.Tick += OnTick;

            keyTimer = new System.Windows.Forms.Timer();
            keyTimer.Interval = 1200;
            keyTimer.Tick += delegate { keyTimer.Stop(); CommitKey(); };

            KeyDown += OnKeyDown;
            MouseDown += OnFormMouseDown;
            MouseDoubleClick += OnFormMouseDoubleClick;
            Resize += delegate { DoLayout(); Invalidate(); };
            Paint += OnPaint;

            labelFont = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            DoLayout();
        }

        IconButton MakeButton(string text)
        {
            IconButton b = new IconButton();
            b.Text = text;
            b.Font = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
            b.ForeColor = C_TXT;
            b.BackColor = C_BTN;
            b.HoverColor = C_BTN_HOVER;
            b.DownColor = C_BTN_DOWN;
            b.BorderColor = Color.FromArgb(75, 84, 98);
            b.Cursor = Cursors.Hand;
            b.TabStop = false;
            return b;
        }

        IconButton MakeChromeButton(string text)
        {
            IconButton b = MakeButton(text);
            b.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
            b.BackColor = Color.FromArgb(30, 34, 40);
            b.HoverColor = Color.FromArgb(52, 58, 68);
            b.DownColor = Color.FromArgb(40, 45, 53);
            b.BorderColor = Color.FromArgb(66, 74, 86);
            b.ShowAccent = false;
            return b;
        }

        void LoadButtonImages()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "buttons");
            imgMinus = LoadButtonImage(Path.Combine(dir, "btn_minus.png"), "ProgressBarTimer.assets.buttons.btn_minus.png");
            imgPlay = LoadButtonImage(Path.Combine(dir, "btn_play.png"), "ProgressBarTimer.assets.buttons.btn_play.png");
            imgPause = LoadButtonImage(Path.Combine(dir, "btn_pause.png"), "ProgressBarTimer.assets.buttons.btn_pause.png");
            imgPlus = LoadButtonImage(Path.Combine(dir, "btn_plus.png"), "ProgressBarTimer.assets.buttons.btn_plus.png");
        }

        static Image LoadButtonImage(string path, string resourceName)
        {
            if (File.Exists(path))
            {
                try
                {
                    using (Image src = Image.FromFile(path))
                        return new Bitmap(src);
                }
                catch { }
            }

            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    using (Image src = Image.FromStream(stream))
                        return new Bitmap(src);
                }
            }
            catch
            {
                return null;
            }
        }

        static Icon LoadAppIcon()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "app-icon.ico");
            if (File.Exists(path))
            {
                try { return new Icon(path); }
                catch { }
            }

            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream stream = asm.GetManifestResourceStream("ProgressBarTimer.assets.app-icon.ico"))
                {
                    if (stream == null) return null;
                    return new Icon(stream);
                }
            }
            catch
            {
                return null;
            }
        }

        void DoLayout()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            int pad = Math.Max(7, Math.Min(12, h / 12));
            int gap = Math.Max(6, Math.Min(10, w / 50));
            int chromeButtonW = 28;
            btnClose.Bounds = new Rectangle(w - chromeButtonW - 6, 4, chromeButtonW, 18);
            btnMinimize.Bounds = new Rectangle(btnClose.Left - chromeButtonW - 5, 4, chromeButtonW, 18);

            rcPanel = new Rectangle(pad, CHROME_H + 2, Math.Max(4, w - pad * 2), Math.Max(4, h - CHROME_H - pad - 2));

            int contentH = rcPanel.Height;
            int mainH = Math.Max(28, Math.Min(38, contentH - 18));
            int mainY = rcPanel.Y + Math.Max(18, (contentH - mainH) / 2 + 7);
            int btnH = Math.Max(26, Math.Min(34, mainH));
            int smallW = Math.Max(28, Math.Min(34, w / 13));
            int playW = Math.Max(34, Math.Min(42, w / 10));
            int controlsW = smallW * 2 + playW + gap * 2;
            int controlsX = rcPanel.Right - 12 - controlsW;

            btnDn.Bounds = new Rectangle(controlsX, mainY, smallW, btnH);
            btnStartStop.Bounds = new Rectangle(btnDn.Right + gap, mainY, playW, btnH);
            btnUp.Bounds = new Rectangle(btnStartStop.Right + gap, mainY, smallW, btnH);

            int timeW = Math.Max(86, Math.Min(124, rcPanel.Width * 27 / 100));
            rcTime = new Rectangle(controlsX - gap - timeW, mainY - 2, timeW, btnH + 4);

            int barX = rcPanel.X + 14;
            int barRight = rcTime.X - gap;
            rcStatus = new Rectangle(barX, rcPanel.Y + 8, Math.Max(4, barRight - barX), 15);
            rcBar = new Rectangle(barX, mainY + btnH / 2 - 8, Math.Max(4, barRight - barX), 16);

            Font old = timeFont;
            float fs = Math.Max(20f, Math.Min(34f, rcTime.Height * 0.72f));
            timeFont = new Font("Segoe UI", fs, FontStyle.Bold, GraphicsUnit.Pixel);
            if (old != null) old.Dispose();
        }

        void OnPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(C_BG);
            PaintChrome(g);

            using (GraphicsPath shadow = RoundRect(Offset(rcPanel, 0, 2), 16))
            using (Brush b = new SolidBrush(Color.FromArgb(52, 0, 0, 0)))
                g.FillPath(b, shadow);

            using (GraphicsPath panel = RoundRect(rcPanel, 16))
            using (Brush b = new SolidBrush(C_PANEL))
            using (Pen p = new Pen(C_PANEL_EDGE))
            {
                g.FillPath(b, panel);
                g.DrawPath(p, panel);
            }

            PaintStatus(g);
            PaintProgress(g);
            PaintTime(g);
        }

        void PaintChrome(Graphics g)
        {
            Rectangle chrome = new Rectangle(0, 0, ClientSize.Width, CHROME_H);
            using (LinearGradientBrush b = new LinearGradientBrush(
                chrome,
                Color.FromArgb(24, 27, 32),
                Color.FromArgb(17, 19, 23),
                LinearGradientMode.Vertical))
                g.FillRectangle(b, chrome);

            using (Pen p = new Pen(Color.FromArgb(44, 50, 60)))
                g.DrawLine(p, 0, CHROME_H - 1, ClientSize.Width, CHROME_H - 1);

            using (Brush dot = new SolidBrush(C_ACCENT))
                g.FillEllipse(dot, 10, 9, 7, 7);

            using (Brush b = new SolidBrush(C_TXT_MUTED))
                g.DrawString("ProgressBarTimer", labelFont, b, 23, 6);
        }

        void PaintStatus(Graphics g)
        {
            string status;
            Color col;

            if (isOvertime)
            {
                status = "OVERTIME";
                col = C_OT;
            }
            else if (setSeconds > 0 && remaining / setSeconds <= WARN_RATIO)
            {
                status = isRunning ? "ALMOST THERE" : "READY";
                col = C_WARN;
            }
            else
            {
                status = isRunning ? "RUNNING" : "READY";
                col = isRunning ? C_ACCENT : C_TXT_MUTED;
            }

            using (Brush b = new SolidBrush(col))
                g.DrawString(status, labelFont, b, rcStatus.X, rcStatus.Y);

            string total = string.Format("{0:00}:{1:00}", setSeconds / 60, setSeconds % 60);
            SizeF sz = g.MeasureString(total, labelFont);
            using (Brush b = new SolidBrush(C_TXT_MUTED))
                g.DrawString(total, labelFont, b, rcStatus.Right - sz.Width, rcStatus.Y);
        }

        void PaintProgress(Graphics g)
        {
            Rectangle r = rcBar;
            if (r.Width < 8 || r.Height < 8) return;

            using (GraphicsPath track = RoundRect(r, r.Height / 2))
            using (Brush b = new SolidBrush(C_TRACK))
            using (Pen p = new Pen(C_TRACK_LINE))
            {
                g.FillPath(b, track);
                g.DrawPath(p, track);
            }

            double ratio;
            Color c1;
            Color c2;
            bool reverse;

            if (!isOvertime)
            {
                ratio = (setSeconds > 0) ? Math.Max(0.0, Math.Min(1.0, remaining / setSeconds)) : 0.0;
                c1 = (ratio <= WARN_RATIO) ? C_WARN : C_ACCENT;
                c2 = (ratio <= WARN_RATIO) ? C_WARN_2 : C_ACCENT_2;
                reverse = false;
            }
            else
            {
                ratio = (setSeconds > 0) ? Math.Max(0.0, Math.Min(1.0, overtimeElapsed / setSeconds)) : 0.0;
                c1 = C_OT;
                c2 = C_OT_2;
                reverse = false;
            }

            int fillW = Math.Max(0, (int)Math.Round(r.Width * ratio));
            if (fillW > 0)
            {
                Rectangle fill = reverse
                    ? new Rectangle(r.Right - fillW, r.Y, fillW, r.Height)
                    : new Rectangle(r.X, r.Y, fillW, r.Height);

                using (GraphicsPath clipPath = RoundRect(r, r.Height / 2))
                using (Region oldClip = g.Clip.Clone())
                using (LinearGradientBrush brush = new LinearGradientBrush(fill, c1, c2, LinearGradientMode.Horizontal))
                {
                    g.SetClip(clipPath);
                    g.FillRectangle(brush, fill);
                    g.Clip = oldClip;
                }
            }

            PaintSegmentTicks(g, r);
        }

        void PaintSegmentTicks(Graphics g, Rectangle r)
        {
            using (Pen p = new Pen(C_SEG_IDLE, 1f))
            {
                for (int i = 1; i < NUM_SEGS; i++)
                {
                    int x = r.X + r.Width * i / NUM_SEGS;
                    g.DrawLine(p, x, r.Y + 3, x, r.Bottom - 3);
                }
            }
        }

        void PaintTime(Graphics g)
        {
            if (rcTime.Width < 4 || rcTime.Height < 4 || timeFont == null) return;

            int val;
            Color col;

            if (!isOvertime)
            {
                val = (int)Math.Ceiling(remaining);
                col = C_TXT;
            }
            else
            {
                val = (int)overtimeElapsed;
                col = C_OT;
            }

            string text = string.Format("{0:00}:{1:00}", val / 60, val % 60);
            SizeF sz = g.MeasureString(text, timeFont);
            float tx = rcTime.X + (rcTime.Width - sz.Width) / 2f;
            float ty = rcTime.Y + (rcTime.Height - sz.Height) / 2f - 1f;

            using (Brush sh = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                g.DrawString(text, timeFont, sh, tx + 1, ty + 1);

            using (Brush tb = new SolidBrush(col))
                g.DrawString(text, timeFont, tb, tx, ty);
        }

        void OnTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            double delta = (now - lastTick).TotalSeconds;
            lastTick = now;

            if (!isOvertime)
            {
                remaining -= delta;
                if (remaining <= 0)
                {
                    remaining = 0;
                    isOvertime = true;
                    overtimeElapsed = 0;
                }
            }
            else
            {
                overtimeElapsed += delta;
                if (overtimeElapsed >= setSeconds)
                {
                    overtimeElapsed = setSeconds;
                    ticker.Stop();
                    isRunning = false;
                    UpdateStartButton();
                }
            }

            Invalidate();
        }

        void Toggle()
        {
            if (isRunning)
            {
                ticker.Stop();
                isRunning = false;
            }
            else
            {
                if (isOvertime && overtimeElapsed >= setSeconds) Reset();
                lastTick = DateTime.UtcNow;
                ticker.Start();
                isRunning = true;
            }
            UpdateStartButton();
        }

        void Reset()
        {
            ticker.Stop();
            isRunning = false;
            remaining = setSeconds;
            overtimeElapsed = 0;
            isOvertime = false;
            UpdateStartButton();
            Invalidate();
        }

        void ResetToDefault()
        {
            if (isRunning) return;
            setSeconds = DEFAULT_SECS;
            Reset();
        }

        void UpdateStartButton()
        {
            if (btnStartStop == null) return;
            btnStartStop.Text = isRunning ? "||" : ">";
            btnStartStop.ButtonImage = isRunning ? imgPause : imgPlay;
            btnStartStop.AccentColor = isRunning ? C_WARN : C_ACCENT;
            btnStartStop.Invalidate();
        }

        void AdjustTime(int delta)
        {
            if (isRunning) return;
            setSeconds = Clamp(setSeconds + delta);
            Reset();
        }

        static int Clamp(int v)
        {
            return Math.Max(MIN_SECS, Math.Min(MAX_SECS, v));
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Home)
            {
                CenterOnCurrentScreen();
                e.Handled = true;
                return;
            }

            switch (e.KeyCode)
            {
                case Keys.Space:
                case Keys.Return:
                    Toggle();
                    return;
                case Keys.R:
                    Reset();
                    return;
                case Keys.Up:
                    AdjustTime(+60);
                    return;
                case Keys.Down:
                    AdjustTime(-60);
                    return;
            }

            int digit = -1;
            if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9)
                digit = e.KeyCode - Keys.D0;
            else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9)
                digit = e.KeyCode - Keys.NumPad0;

            if (digit >= 0 && !isRunning)
            {
                keyBuf += digit.ToString();
                keyTimer.Stop();
                if (keyBuf.Length >= 2) CommitKey();
                else keyTimer.Start();
            }
        }

        void CommitKey()
        {
            if (keyBuf.Length == 0) return;
            int mins = int.Parse(keyBuf);
            keyBuf = "";
            if (mins < 1) mins = 1;
            setSeconds = Clamp(mins * 60);
            Reset();
        }

        void BeginRepeat(bool up)
        {
            if (isRunning) return;
            repeatUp = up;
            EndRepeat();
            AdjustTime(repeatUp ? +60 : -60);
            System.Windows.Forms.Timer rt = new System.Windows.Forms.Timer();
            rt.Interval = 400;
            bool first = true;
            rt.Tick += delegate
            {
                if (first) { rt.Interval = 100; first = false; }
                AdjustTime(repeatUp ? +60 : -60);
            };
            rt.Start();
            repeatTimer = rt;
        }

        void EndRepeat()
        {
            if (repeatTimer != null)
            {
                repeatTimer.Stop();
                repeatTimer.Dispose();
                repeatTimer = null;
            }
        }

        static Point ClampToScreen(Point p)
        {
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            return new Point(
                Math.Max(screen.Left, Math.Min(p.X, screen.Right - 100)),
                Math.Max(screen.Top, Math.Min(p.Y, screen.Bottom - 50))
            );
        }

        void CenterOnCurrentScreen()
        {
            Rectangle screen = Screen.FromControl(this).WorkingArea;
            Location = new Point(
                screen.Left + (screen.Width - Width) / 2,
                screen.Top + (screen.Height - Height) / 2);
        }

        void KeepWindowOnScreen()
        {
            if (WindowState != FormWindowState.Normal) return;

            Rectangle screen = Screen.FromRectangle(Bounds).WorkingArea;
            int w = Math.Min(Width, screen.Width);
            int h = Math.Min(Height, screen.Height);
            int x = Math.Max(screen.Left, Math.Min(Left, screen.Right - w));
            int y = Math.Max(screen.Top, Math.Min(Top, screen.Bottom - h));

            if (Width != w || Height != h || Left != x || Top != y)
                Bounds = new Rectangle(x, y, w, h);
        }

        void OnFormMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Y >= CHROME_H) return;
            if (btnMinimize.Bounds.Contains(e.Location) || btnClose.Bounds.Contains(e.Location)) return;

            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }

        void OnFormMouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && rcTime.Contains(e.Location))
                ResetToDefault();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_EXITSIZEMOVE)
            {
                KeepWindowOnScreen();
                return;
            }

            if (m.Msg != WM_NCHITTEST || WindowState == FormWindowState.Maximized) return;
            if ((int)m.Result != 1) return;

            Point p = PointToClient(new Point(
                unchecked((short)(long)m.LParam),
                unchecked((short)((long)m.LParam >> 16))));

            bool left = p.X <= RESIZE_GRIP;
            bool right = p.X >= ClientSize.Width - RESIZE_GRIP;
            bool top = p.Y <= RESIZE_GRIP;
            bool bottom = p.Y >= ClientSize.Height - RESIZE_GRIP;

            if (left && top) m.Result = (IntPtr)HTTOPLEFT;
            else if (right && top) m.Result = (IntPtr)HTTOPRIGHT;
            else if (left && bottom) m.Result = (IntPtr)HTBOTTOMLEFT;
            else if (right && bottom) m.Result = (IntPtr)HTBOTTOMRIGHT;
            else if (left) m.Result = (IntPtr)HTLEFT;
            else if (right) m.Result = (IntPtr)HTRIGHT;
            else if (top) m.Result = (IntPtr)HTTOP;
            else if (bottom) m.Result = (IntPtr)HTBOTTOM;
        }

        static Rectangle Offset(Rectangle r, int dx, int dy)
        {
            return new Rectangle(r.X + dx, r.Y + dy, r.Width, r.Height);
        }

        static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = Math.Max(1, radius * 2);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (ticker != null) { ticker.Dispose(); ticker = null; }
                if (keyTimer != null) { keyTimer.Dispose(); keyTimer = null; }
                if (repeatTimer != null) { repeatTimer.Dispose(); repeatTimer = null; }
                if (imgMinus != null) { imgMinus.Dispose(); imgMinus = null; }
                if (imgPlay != null) { imgPlay.Dispose(); imgPlay = null; }
                if (imgPause != null) { imgPause.Dispose(); imgPause = null; }
                if (imgPlus != null) { imgPlus.Dispose(); imgPlus = null; }
                if (timeFont != null) { timeFont.Dispose(); timeFont = null; }
                if (labelFont != null) { labelFont.Dispose(); labelFont = null; }
            }
            base.Dispose(disposing);
        }

        sealed class IconButton : Button
        {
            bool hovering;
            bool pressing;

            public Color HoverColor { get; set; }
            public Color DownColor { get; set; }
            public Color BorderColor { get; set; }
            public Color AccentColor { get; set; }
            public bool ShowAccent { get; set; }
            public Image ButtonImage { get; set; }

            public IconButton()
            {
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                UseVisualStyleBackColor = false;
                AccentColor = C_ACCENT;
                ShowAccent = true;
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                hovering = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                hovering = false;
                pressing = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs mevent)
            {
                pressing = true;
                Invalidate();
                base.OnMouseDown(mevent);
            }

            protected override void OnMouseUp(MouseEventArgs mevent)
            {
                pressing = false;
                Invalidate();
                base.OnMouseUp(mevent);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                Color fill = pressing ? DownColor : (hovering ? HoverColor : BackColor);
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                int radius = Math.Max(8, Math.Min(14, Height / 3));

                if (ButtonImage != null)
                {
                    int size = Math.Min(Width, Height) + 4;
                    Rectangle dst = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
                    g.DrawImage(ButtonImage, dst);

                    if (hovering || pressing)
                    {
                        Color overlay = pressing
                            ? Color.FromArgb(72, 0, 0, 0)
                            : Color.FromArgb(26, 255, 255, 255);
                        using (GraphicsPath clip = RoundRect(new Rectangle(1, 1, Width - 3, Height - 3), radius))
                        using (Brush b = new SolidBrush(overlay))
                            g.FillPath(b, clip);
                    }
                    return;
                }

                using (GraphicsPath path = RoundRect(r, radius))
                using (Brush b = new SolidBrush(fill))
                using (Pen border = new Pen(BorderColor))
                {
                    g.FillPath(b, path);
                    g.DrawPath(border, path);
                }

                if (ShowAccent)
                {
                    using (Pen accent = new Pen(AccentColor, 2f))
                        g.DrawLine(accent, 8, Height - 4, Width - 8, Height - 4);
                }

                TextRenderer.DrawText(
                    g,
                    Text,
                    Font,
                    ClientRectangle,
                    ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
