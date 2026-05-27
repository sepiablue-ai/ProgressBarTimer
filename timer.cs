// ProgressBarTimer.cs
// Windows standalone timer with segmented progress bar
//
// ビルド方法 → build.bat を実行
//   .NET SDK がある場合: dotnet publish (完全スタンドアロン EXE)
//   .NET Framework のみ: csc.exe でコンパイル (要 .NET Framework 4.x)
//
// 操作:
//   Space / Enter  … スタート / 一時停止
//   R              … リセット
//   ↑ / ↓          … 設定時間 ±1分
//   数字キー 2桁   … 分を直接入力 (例: 1→5 で 15分)
//   ▲ / ▼ ボタン   … 設定時間 ±1分 (長押しで連続変更)

using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using System.IO;

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
        //──────────────────── 定数 ────────────────────────────────
        const int    NUM_SEGS     = 16;
        const int    DEFAULT_SECS = 600;   // デフォルト 10分
        const int    MIN_SECS     = 60;    // 最小 1分
        const int    MAX_SECS     = 5999;  // 最大 99:59
        const int    TICK_MS      = 100;   // 更新間隔 ms
        const double WARN_RATIO   = 0.30;  // 警告しきい値 (30%)

        static readonly string CFG = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "timer_config.ini");

        //──────────────────── パレット ────────────────────────────
        static readonly Color C_OUTER   = Color.FromArgb(210, 210, 210);
        static readonly Color C_INNER   = Color.FromArgb(22,  22,  22 );
        static readonly Color C_SEG_OFF = Color.FromArgb(48,  48,  48 );
        static readonly Color C_SEG_ON  = Color.FromArgb(15,  15,  15 );  // ほぼ黒
        static readonly Color C_WARN    = Color.FromArgb(200, 165, 0  );  // 黄色
        static readonly Color C_OT      = Color.FromArgb(210, 28,  0  );  // 赤
        static readonly Color C_TXT     = Color.FromArgb(218, 218, 218);  // 通常テキスト
        static readonly Color C_TXT_OT  = Color.FromArgb(255, 48,  16 );  // 超過テキスト
        static readonly Color C_BTN     = Color.FromArgb(185, 185, 185);  // ボタン
        static readonly Color C_DIV     = Color.FromArgb(80,  80,  80 );  // 区切り線

        //──────────────────── 状態 ────────────────────────────────
        int      setSeconds;
        double   remaining;
        double   overtimeElapsed;
        bool     isRunning;
        bool     isOvertime;
        DateTime lastTick;
        string   keyBuf  = "";
        bool     repeatUp;

        //──────────────────── コントロール / リソース ─────────────
        System.Windows.Forms.Timer ticker;
        System.Windows.Forms.Timer keyTimer;
        System.Windows.Forms.Timer repeatTimer;
        Button   btnUp, btnDn;
        Font     timeFont;

        //──────────────────── レイアウトキャッシュ ───────────────
        Rectangle rcBar, rcDiv, rcTime;

        //══════════════════════════════════════════════════════════
        public TimerForm()
        {
            LoadConfig();
            remaining = setSeconds;

            // ── ウィンドウ設定 ──
            Text            = "ProgressBarTimer";
            TopMost         = true;
            DoubleBuffered  = true;
            KeyPreview      = true;
            BackColor       = C_OUTER;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize     = new Size(300, 80);
            StartPosition   = FormStartPosition.Manual;
            Size            = LoadSize();
            Location        = ClampToScreen(LoadLocation());

            // ── ▲▼ ボタン ──
            btnUp = MakeButton("▲");
            btnDn = MakeButton("▼");
            btnUp.MouseDown += (s, e) => BeginRepeat(true);
            btnDn.MouseDown += (s, e) => BeginRepeat(false);
            btnUp.MouseUp   += (s, e) => EndRepeat();
            btnDn.MouseUp   += (s, e) => EndRepeat();
            btnUp.MouseLeave += (s, e) => EndRepeat();
            btnDn.MouseLeave += (s, e) => EndRepeat();
            Controls.Add(btnUp);
            Controls.Add(btnDn);

            // ── メインタイマー ──
            ticker          = new System.Windows.Forms.Timer();
            ticker.Interval = TICK_MS;
            ticker.Tick    += OnTick;

            // ── キー入力確定タイマー ──
            keyTimer          = new System.Windows.Forms.Timer();
            keyTimer.Interval = 1200;
            keyTimer.Tick    += delegate { keyTimer.Stop(); CommitKey(); };

            // ── イベント ──
            KeyDown     += OnKeyDown;
            Resize      += delegate { DoLayout(); Invalidate(); };
            Paint       += OnPaint;
            FormClosed  += delegate { SaveConfig(); };

            DoLayout();
        }

        Button MakeButton(string text)
        {
            Button b = new Button();
            b.Text      = text;
            b.Font      = new Font("Segoe UI", 7f, FontStyle.Regular, GraphicsUnit.Point);
            b.BackColor = C_BTN;
            b.FlatStyle = FlatStyle.Flat;
            b.Cursor    = Cursors.Hand;
            b.TabStop   = false;
            b.FlatAppearance.BorderSize  = 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(140, 140, 140);
            return b;
        }

        void DoLayout()
        {
            int W = ClientSize.Width;
            int H = ClientSize.Height;
            const int PAD   = 6;
            const int DIV_W = 2;
            int iw = W - PAD * 2;
            int ih = H - PAD * 2;

            int btnW = Math.Max(18, Math.Min(26, iw / 10));
            int btnH = Math.Max(12, (ih - 4) / 2);
            int timeW = Math.Max(72, iw * 32 / 100);
            int barW  = iw - DIV_W - timeW - btnW - 8;

            rcBar  = new Rectangle(PAD + 3, PAD + 3, Math.Max(4, barW), Math.Max(4, ih - 6));
            rcDiv  = new Rectangle(PAD + 3 + barW, PAD, DIV_W, ih);
            rcTime = new Rectangle(rcDiv.Right + 2, PAD + 3, Math.Max(4, timeW - 4), Math.Max(4, ih - 6));

            int btnX = rcTime.Right + 2;
            btnUp.Bounds = new Rectangle(btnX, PAD + 2,            btnW - 2, btnH);
            btnDn.Bounds = new Rectangle(btnX, PAD + 2 + btnH + 2, btnW - 2, btnH);

            Font old = timeFont;
            float fs = Math.Max(8f, rcTime.Height * 0.52f);
            timeFont = new Font("Courier New", fs, FontStyle.Bold, GraphicsUnit.Pixel);
            if (old != null) old.Dispose();
        }

        void OnPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            g.Clear(C_OUTER);

            const int PAD = 6;
            Rectangle inner = new Rectangle(PAD, PAD,
                ClientSize.Width - PAD * 2, ClientSize.Height - PAD * 2);

            using (Brush b = new SolidBrush(C_INNER))
                g.FillRectangle(b, inner);

            PaintSegments(g);

            using (Brush b = new SolidBrush(C_DIV))
                g.FillRectangle(b, rcDiv);

            PaintTime(g);
        }

        void PaintSegments(Graphics g)
        {
            Rectangle r = rcBar;
            if (r.Width < 4 || r.Height < 4) return;

            int   n   = NUM_SEGS;
            int   gap = Math.Max(2, r.Width / 50 + 2);
            float sw  = Math.Max(1f, (float)(r.Width - gap * (n - 1)) / n);

            int   active;
            Color activeCol;
            bool  reverseDir;

            if (!isOvertime)
            {
                double ratio = (setSeconds > 0)
                    ? Math.Max(0.0, Math.Min(1.0, remaining / setSeconds)) : 0.0;
                active       = (int)Math.Round(ratio * n);
                activeCol    = (ratio <= WARN_RATIO) ? C_WARN : C_SEG_ON;
                reverseDir   = false;
            }
            else
            {
                double ratio = (setSeconds > 0)
                    ? Math.Max(0.0, Math.Min(1.0, overtimeElapsed / setSeconds)) : 0.0;
                active       = (int)Math.Round(ratio * n);
                activeCol    = C_OT;
                reverseDir   = true;
            }

            using (Brush onBrush  = new SolidBrush(activeCol))
            using (Brush offBrush = new SolidBrush(C_SEG_OFF))
            {
                for (int i = 0; i < n; i++)
                {
                    int x  = r.X + (int)(i * (sw + gap));
                    int iw = Math.Max(1, (int)sw);
                    Rectangle seg = new Rectangle(x, r.Y, iw, r.Height);
                    bool lit = reverseDir ? (i >= n - active) : (i < active);
                    g.FillRectangle(lit ? onBrush : offBrush, seg);
                }
            }
        }

        void PaintTime(Graphics g)
        {
            Rectangle r = rcTime;
            if (r.Width < 4 || r.Height < 4 || timeFont == null) return;

            int   val;
            Color col;

            if (!isOvertime)
            {
                val = (int)Math.Ceiling(remaining);
                col = C_TXT;
            }
            else
            {
                val = (int)overtimeElapsed;
                col = C_TXT_OT;
            }

            string text = string.Format("{0:00}:{1:00}", val / 60, val % 60);
            SizeF  sz   = g.MeasureString(text, timeFont);
            float  tx   = r.X + (r.Width  - sz.Width)  / 2f;
            float  ty   = r.Y + (r.Height - sz.Height) / 2f;

            using (Brush sh = new SolidBrush(Color.Black))
                g.DrawString(text, timeFont, sh, tx + 1, ty + 1);

            using (Brush tb = new SolidBrush(col))
                g.DrawString(text, timeFont, tb, tx, ty);
        }

        void OnTick(object sender, EventArgs e)
        {
            DateTime now   = DateTime.UtcNow;
            double   delta = (now - lastTick).TotalSeconds;
            lastTick = now;

            if (!isOvertime)
            {
                remaining -= delta;
                if (remaining <= 0)
                {
                    remaining       = 0;
                    isOvertime      = true;
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
                lastTick  = DateTime.UtcNow;
                ticker.Start();
                isRunning = true;
            }
        }

        void Reset()
        {
            ticker.Stop();
            isRunning       = false;
            remaining       = setSeconds;
            overtimeElapsed = 0;
            isOvertime      = false;
            Invalidate();
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
            if (e.KeyCode >= Keys.D0      && e.KeyCode <= Keys.D9)
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

        void LoadConfig()
        {
            setSeconds = DEFAULT_SECS;
            if (!File.Exists(CFG)) return;
            try
            {
                foreach (string line in File.ReadAllLines(CFG))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    int parsed;
                    if (k == "set_seconds" && int.TryParse(v, out parsed))
                        setSeconds = Clamp(parsed);
                }
            }
            catch { }
        }

        Size LoadSize()
        {
            if (File.Exists(CFG))
            {
                try
                {
                    foreach (string line in File.ReadAllLines(CFG))
                    {
                        int eq = line.IndexOf('=');
                        if (eq < 1) continue;
                        if (line.Substring(0, eq).Trim() == "size")
                        {
                            string[] p = line.Substring(eq + 1).Trim().Split(',');
                            int w, h;
                            if (p.Length == 2 && int.TryParse(p[0], out w)
                                              && int.TryParse(p[1], out h))
                                return new Size(Math.Max(300, w), Math.Max(80, h));
                        }
                    }
                }
                catch { }
            }
            return new Size(440, 104);
        }

        Point LoadLocation()
        {
            if (File.Exists(CFG))
            {
                try
                {
                    foreach (string line in File.ReadAllLines(CFG))
                    {
                        int eq = line.IndexOf('=');
                        if (eq < 1) continue;
                        if (line.Substring(0, eq).Trim() == "location")
                        {
                            string[] p = line.Substring(eq + 1).Trim().Split(',');
                            int x, y;
                            if (p.Length == 2 && int.TryParse(p[0], out x)
                                              && int.TryParse(p[1], out y))
                                return new Point(x, y);
                        }
                    }
                }
                catch { }
            }
            return new Point(200, 200);
        }

        static Point ClampToScreen(Point p)
        {
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            return new Point(
                Math.Max(screen.Left, Math.Min(p.X, screen.Right  - 100)),
                Math.Max(screen.Top,  Math.Min(p.Y, screen.Bottom - 50))
            );
        }

        void SaveConfig()
        {
            try
            {
                File.WriteAllLines(CFG, new string[]
                {
                    "set_seconds=" + setSeconds,
                    "size="        + Width  + "," + Height,
                    "location="    + Location.X + "," + Location.Y
                });
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (ticker      != null) { ticker.Dispose();      ticker      = null; }
                if (keyTimer    != null) { keyTimer.Dispose();    keyTimer    = null; }
                if (repeatTimer != null) { repeatTimer.Dispose(); repeatTimer = null; }
                if (timeFont    != null) { timeFont.Dispose();    timeFont    = null; }
            }
            base.Dispose(disposing);
        }
    }
}
