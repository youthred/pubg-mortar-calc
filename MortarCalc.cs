using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Runtime.InteropServices;

public class MortarApp : Form
{
    // ---------- 状态（单位：米） ----------
    double selfX = 2800, selfY = 2800;
    double tgtX = 3000, tgtY = 2950;
    bool selectedSelf = true;
    bool useMil = false;
    bool snap = false;
    double rmax = 700;

    // 视图变换（地图世界坐标 0..8000 米）
    double Cx = 4000, Cy = 4000;
    double ViewScale = 0.043;
    const double MAX_SCALE = 0.8;

    MapPanel map;
    TextBox selfBox, tgtBox;
    Label distLabel, angleLabel;
    RadioButton rbSelf, rbTgt, rbDeg, rbMil;
    Button btnSwap, btnClear, btnFit;
    CheckBox snapChk;
    NumericUpDown rmaxBox;

    bool syncing = false;
    Point? downPos = null;
    bool panning = false, draggingMarker = false, dragWhich = false;

    // 热键
    const int HOTKEY_ID = 0x5A10;
    const uint MOD_CTRL = 0x0002, MOD_SHIFT = 0x0004;
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public MortarApp()
    {
        this.Text = "PUBG 迫击炮测距 v1.0   (Ctrl+Shift+M 显示/隐藏)";
        this.FormBorderStyle = FormBorderStyle.FixedToolWindow;
        this.TopMost = true;
        this.Opacity = 0.94;
        this.BackColor = Color.FromArgb(22, 27, 34);
        this.ForeColor = Color.FromArgb(230, 237, 243);
        this.Font = new Font("Microsoft YaHei", 9f);
        this.ClientSize = new Size(360, 548);
        this.StartPosition = FormStartPosition.Manual;
        var wa = Screen.PrimaryScreen.WorkingArea;
        this.Location = new Point(wa.Right - 380, 80);
        this.ShowInTaskbar = true;

        // 选择 自己/目标
        Panel selPanel = new Panel();
        selPanel.Bounds = new Rectangle(6, 6, 122, 24);
        selPanel.BackColor = Color.Transparent;
        rbSelf = new RadioButton(); rbSelf.Text = "自己"; rbSelf.Bounds = new Rectangle(0, 2, 58, 20); rbSelf.Checked = true;
        rbTgt = new RadioButton(); rbTgt.Text = "目标"; rbTgt.Bounds = new Rectangle(60, 2, 58, 20);
        selPanel.Controls.Add(rbSelf); selPanel.Controls.Add(rbTgt);
        this.Controls.Add(selPanel);
        rbSelf.CheckedChanged += delegate { selectedSelf = rbSelf.Checked; };

        btnSwap = MakeButton("交换", new Rectangle(134, 5, 48, 24));
        btnClear = MakeButton("清空", new Rectangle(186, 5, 48, 24));
        btnFit = MakeButton("适配", new Rectangle(238, 5, 48, 24));

        snapChk = new CheckBox();
        snapChk.Text = "吸附";
        snapChk.Bounds = new Rectangle(292, 7, 60, 22);
        snapChk.ForeColor = Color.FromArgb(230, 237, 243);
        snapChk.Font = new Font("Microsoft YaHei", 8.5f);
        snapChk.Checked = false;
        snapChk.CheckedChanged += delegate { snap = snapChk.Checked; };
        this.Controls.Add(snapChk);

        map = new MapPanel(this);
        map.Bounds = new Rectangle(8, 34, 344, 344);
        map.BorderStyle = BorderStyle.FixedSingle;
        this.Controls.Add(map);

        this.Controls.Add(MakeLabel("自己", new Rectangle(10, 386, 40, 20)));
        selfBox = MakeBox(new Rectangle(52, 384, 300, 22));
        this.Controls.Add(MakeLabel("目标", new Rectangle(10, 414, 40, 20)));
        tgtBox = MakeBox(new Rectangle(52, 412, 300, 22));

        distLabel = new Label();
        distLabel.Bounds = new Rectangle(10, 440, 340, 26);
        distLabel.Font = new Font("Microsoft YaHei", 15f, FontStyle.Bold);
        distLabel.ForeColor = Color.FromArgb(255, 176, 46);
        distLabel.Text = "距离 — m";
        this.Controls.Add(distLabel);

        angleLabel = new Label();
        angleLabel.Bounds = new Rectangle(10, 470, 340, 22);
        angleLabel.Font = new Font("Microsoft YaHei", 11f, FontStyle.Bold);
        angleLabel.ForeColor = Color.FromArgb(74, 222, 128);
        angleLabel.Text = "仰角 —";
        this.Controls.Add(angleLabel);

        Panel unitPanel = new Panel();
        unitPanel.Bounds = new Rectangle(10, 498, 130, 22);
        unitPanel.BackColor = Color.Transparent;
        rbDeg = new RadioButton(); rbDeg.Text = "度"; rbDeg.Bounds = new Rectangle(0, 1, 50, 20); rbDeg.Checked = true;
        rbMil = new RadioButton(); rbMil.Text = "密位"; rbMil.Bounds = new Rectangle(54, 1, 76, 20);
        unitPanel.Controls.Add(rbDeg); unitPanel.Controls.Add(rbMil);
        this.Controls.Add(unitPanel);
        rbMil.CheckedChanged += delegate { useMil = rbMil.Checked; UpdateResult(); };

        this.Controls.Add(MakeLabel("射程", new Rectangle(150, 500, 36, 18)));
        rmaxBox = new NumericUpDown();
        rmaxBox.Bounds = new Rectangle(188, 497, 64, 20);
        rmaxBox.Minimum = 400; rmaxBox.Maximum = 900; rmaxBox.Value = 700; rmaxBox.Increment = 10;
        this.Controls.Add(rmaxBox);
        rmaxBox.ValueChanged += delegate { rmax = (double)rmaxBox.Value; UpdateResult(); };
        this.Controls.Add(MakeLabel("m", new Rectangle(256, 500, 30, 18)));

        Label hint = MakeLabel("滚轮缩放 · 拖拽平移 · 单击放点 · 吸附=贴齐50m格", new Rectangle(10, 524, 344, 18));
        hint.ForeColor = Color.FromArgb(110, 120, 130);
        hint.Font = new Font("Microsoft YaHei", 8f);

        btnSwap.Click += delegate { double tx = selfX, ty = selfY; selfX = tgtX; selfY = tgtY; tgtX = tx; tgtY = ty; SyncBoxes(); UpdateResult(); map.Invalidate(); };
        btnClear.Click += delegate { tgtX = selfX; tgtY = selfY; SyncBoxes(); UpdateResult(); map.Invalidate(); };
        btnFit.Click += delegate { FitView(); };

        selfBox.TextChanged += delegate { if (syncing) return; double x, y; if (TryParseCoord(selfBox.Text, out x, out y)) { selfX = x; selfY = y; UpdateResult(); FitView(); } };
        tgtBox.TextChanged += delegate { if (syncing) return; double x, y; if (TryParseCoord(tgtBox.Text, out x, out y)) { tgtX = x; tgtY = y; UpdateResult(); FitView(); } };
        selfBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) tgtBox.Focus(); };

        SyncBoxes();
        FitView();
        UpdateResult();
    }

    Button MakeButton(string t, Rectangle b)
    {
        var x = new Button();
        x.Text = t; x.Bounds = b; x.FlatStyle = FlatStyle.Flat;
        x.FlatAppearance.BorderColor = Color.FromArgb(45, 51, 59);
        x.BackColor = Color.FromArgb(28, 33, 40); x.ForeColor = Color.FromArgb(230, 237, 243);
        x.Font = new Font("Microsoft YaHei", 8.5f); x.UseVisualStyleBackColor = false;
        this.Controls.Add(x); return x;
    }
    Label MakeLabel(string t, Rectangle b)
    {
        var x = new Label();
        x.Text = t; x.Bounds = b; x.ForeColor = Color.FromArgb(139, 148, 158);
        x.TextAlign = ContentAlignment.MiddleLeft;
        this.Controls.Add(x); return x;
    }
    TextBox MakeBox(Rectangle b)
    {
        var x = new TextBox();
        x.Bounds = b; x.BackColor = Color.FromArgb(13, 17, 23); x.ForeColor = Color.FromArgb(230, 237, 243);
        x.BorderStyle = BorderStyle.FixedSingle; x.Font = new Font("Consolas", 9.5f);
        this.Controls.Add(x); return x;
    }

    // ---------- 物理 ----------
    double AngleForDistance(double d)
    {
        double ratio = d / rmax;
        if (ratio > 1.000001) return double.NaN;
        ratio = Math.Min(Math.Max(ratio, 0.0), 1.0);
        double asinDeg = Math.Asin(ratio) * 180.0 / Math.PI;
        return 90.0 - asinDeg / 2.0;
    }
    string FmtAngle(double deg)
    {
        if (useMil) return ((int)Math.Round(deg * 6400.0 / 360.0)) + " mil";
        return deg.ToString("0.0") + "°";
    }
    double Distance()
    {
        double dx = tgtX - selfX, dy = tgtY - selfY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // ---------- 坐标解析 ----------
    bool TryParseCoord(string s, out double x, out double y)
    {
        x = 0; y = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        string t = s.Trim().ToUpperInvariant();
        var m = Regex.Match(t, @"^([A-H])\s*([1-8])\s*(?:[-.,，]\s*(\d)\s*[-.,，]?\s*(\d)\s*)?$");
        if (!m.Success) return false;
        int col = m.Groups[1].Value[0] - 'A';
        int row = int.Parse(m.Groups[2].Value) - 1;
        double xkm, ykm;
        if (m.Groups[3].Success)
        {
            int sx = int.Parse(m.Groups[3].Value);
            int sy = int.Parse(m.Groups[4].Value);
            if (sx < 0 || sx > 9 || sy < 0 || sy > 9) return false;
            xkm = col + (sx + 0.5) / 10.0;
            ykm = row + (sy + 0.5) / 10.0;
        }
        else
        {
            xkm = col + 0.5;
            ykm = row + 0.5;
        }
        x = xkm * 1000.0;
        y = ykm * 1000.0;
        return true;
    }
    string ToGridStr(double x, double y)
    {
        x = Math.Max(0, Math.Min(8000, Math.Round(x)));
        y = Math.Max(0, Math.Min(8000, Math.Round(y)));
        int col = (int)(x / 1000);
        int row = (int)(y / 1000) + 1;
        int sx = (int)((x % 1000) / 100);
        int sy = (int)((y % 1000) / 100);
        return string.Format("{0}{1}-{2}{3}", (char)('A' + col), row, sx, sy);
    }

    // ---------- 视图与坐标换算 ----------
    double MinScale() { return Math.Min(map.ClientSize.Width, map.ClientSize.Height) / 8000.0; }
    double ClampM(double m) { return m < -1000 ? -1000 : (m > 9000 ? 9000 : m); }
    double SnapCell(double m)
    {
        // 每个 100m 格内 9 点：四角(0/100)、四边中点(50)、格子中心(50,50)
        m = Math.Max(0, Math.Min(8000, m));
        double cell = Math.Floor(m / 100.0);
        double within = m - cell * 100.0;   // 格内位置 0..100
        if (within < 25) within = 0;
        else if (within < 75) within = 50;
        else within = 100;
        double r = cell * 100.0 + within;
        return r > 8000 ? 8000 : r;
    }
    double PlaceCoord(double m)
    {
        m = Math.Max(0, Math.Min(8000, m));
        if (snap) return SnapCell(m);
        return m;
    }
    int Sx(double m) { return (int)Math.Round((m - Cx) * ViewScale + map.ClientSize.Width / 2.0); }
    int Sy(double m) { return (int)Math.Round((m - Cy) * ViewScale + map.ClientSize.Height / 2.0); }
    double Wx(int px) { return (px - map.ClientSize.Width / 2.0) / ViewScale + Cx; }
    double Wy(int py) { return (py - map.ClientSize.Height / 2.0) / ViewScale + Cy; }
    Point ToScreen(double mx, double my) { return new Point(Sx(mx), Sy(my)); }

    void FitView()
    {
        double dx = tgtX - selfX, dy = tgtY - selfY;
        double span = Math.Max(Math.Abs(dx), Math.Abs(dy));
        span = Math.Max(span, 400) + 600;
        double s = Math.Min(map.ClientSize.Width, map.ClientSize.Height) / span;
        s = Math.Max(MinScale(), Math.Min(MAX_SCALE, s));
        ViewScale = s;
        Cx = ClampM((selfX + tgtX) / 2.0);
        Cy = ClampM((selfY + tgtY) / 2.0);
        map.Invalidate();
    }

    // ---------- 交互 ----------
    void MapWheel(MouseEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
        double ns = Math.Max(MinScale(), Math.Min(MAX_SCALE, ViewScale * factor));
        if (Math.Abs(ns - ViewScale) < 1e-9) return;
        double wx = Wx(e.X), wy = Wy(e.Y);
        double cx2 = wx - (e.X - map.ClientSize.Width / 2.0) / ns;
        double cy2 = wy - (e.Y - map.ClientSize.Height / 2.0) / ns;
        ViewScale = ns;
        Cx = ClampM(cx2); Cy = ClampM(cy2);
        map.Invalidate();
    }
    void MapDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        downPos = e.Location;
        panning = false; draggingMarker = false;
        Point p1 = ToScreen(selfX, selfY), p2 = ToScreen(tgtX, tgtY);
        if (Dist2(e.Location, p1) <= 100) { draggingMarker = true; dragWhich = true; }
        else if (Dist2(e.Location, p2) <= 100) { draggingMarker = true; dragWhich = false; }
    }
    void MapMove(MouseEventArgs e)
    {
        if (!downPos.HasValue) return;
        if (draggingMarker)
        {
            double px = PlaceCoord(Wx(e.X)), py = PlaceCoord(Wy(e.Y));
            if (dragWhich) { selfX = px; selfY = py; } else { tgtX = px; tgtY = py; }
            SyncBoxes(); UpdateResult(); map.Invalidate();
        }
        else if (e.Button == MouseButtons.Left)
        {
            int ddx = e.X - downPos.Value.X, ddy = e.Y - downPos.Value.Y;
            if (!panning && (ddx * ddx + ddy * ddy) > 9) panning = true;
            if (panning)
            {
                Cx = ClampM(Cx - ddx / ViewScale);
                Cy = ClampM(Cy - ddy / ViewScale);
                downPos = e.Location;
                map.Invalidate();
            }
        }
    }
    void MapUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) { downPos = null; return; }
        if (draggingMarker) { draggingMarker = false; downPos = null; return; }
        if (!panning && downPos.HasValue)
        {
            double px = PlaceCoord(Wx(downPos.Value.X)), py = PlaceCoord(Wy(downPos.Value.Y));
            if (selectedSelf) { selfX = px; selfY = py; } else { tgtX = px; tgtY = py; }
            SyncBoxes(); UpdateResult(); map.Invalidate();
        }
        downPos = null; panning = false;
    }
    static int Dist2(Point a, Point b)
    {
        int dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    // ---------- 结果与同步 ----------
    void SyncBoxes()
    {
        syncing = true;
        selfBox.Text = ToGridStr(selfX, selfY);
        tgtBox.Text = ToGridStr(tgtX, tgtY);
        syncing = false;
    }
    void UpdateResult()
    {
        double dist = Distance();
        distLabel.Text = "距离 " + ((int)Math.Round(dist)) + " m";
        if (dist > rmax)
        {
            angleLabel.Text = "⚠ 超出最大射程 " + ((int)rmax) + " m";
            angleLabel.ForeColor = Color.FromArgb(250, 204, 21);
        }
        else if (dist < 121)
        {
            angleLabel.Text = "⚠ 低于最小射程约 121 m";
            angleLabel.ForeColor = Color.FromArgb(251, 113, 133);
        }
        else
        {
            angleLabel.Text = "仰角 " + FmtAngle(AngleForDistance(dist));
            angleLabel.ForeColor = Color.FromArgb(74, 222, 128);
        }
    }

    // ---------- 绘制 ----------
    void DrawMap(Graphics g)
    {
        int w = map.ClientSize.Width, h = map.ClientSize.Height;
        g.Clear(Color.FromArgb(13, 17, 23));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        double leftM = Wx(0), rightM = Wx(w), topM = Wy(0), bottomM = Wy(h);

        // 50m 细格（更高倍率，显示吸附点）
        if (ViewScale >= 0.1)
        {
            using (Pen p = new Pen(Color.FromArgb(22, 27, 33), 0.6f))
            {
                long c0 = (long)Math.Floor(leftM / 50.0);
                for (long i = c0; i * 50.0 <= rightM; i++)
                {
                    double m = i * 50.0;
                    if (m < 0 || m > 8000) continue;
                    int px = Sx(m);
                    g.DrawLine(p, px, 0, px, h);
                }
                long r0 = (long)Math.Floor(topM / 50.0);
                for (long i = r0; i * 50.0 <= bottomM; i++)
                {
                    double m = i * 50.0;
                    if (m < 0 || m > 8000) continue;
                    int py = Sy(m);
                    g.DrawLine(p, 0, py, w, py);
                }
            }
        }

        // 100m 细格
        if (ViewScale >= 0.05)
        {
            using (Pen p = new Pen(Color.FromArgb(32, 38, 46), 1f))
            {
                long c0 = (long)Math.Floor(leftM / 100.0);
                for (long i = c0; i * 100.0 <= rightM; i++)
                {
                    double m = i * 100.0;
                    if (m < 0 || m > 8000) continue;
                    int px = Sx(m);
                    g.DrawLine(p, px, 0, px, h);
                }
                long r0 = (long)Math.Floor(topM / 100.0);
                for (long i = r0; i * 100.0 <= bottomM; i++)
                {
                    double m = i * 100.0;
                    if (m < 0 || m > 8000) continue;
                    int py = Sy(m);
                    g.DrawLine(p, 0, py, w, py);
                }
            }
        }

        // 1km 粗格
        using (Pen p = new Pen(Color.FromArgb(58, 67, 77), 1.4f))
        {
            for (long c = 0; c <= 8; c++)
            {
                double m = c * 1000.0;
                if (m < leftM - 1 || m > rightM + 1) continue;
                int px = Sx(m);
                g.DrawLine(p, px, 0, px, h);
            }
            for (long r = 0; r <= 8; r++)
            {
                double m = r * 1000.0;
                if (m < topM - 1 || m > bottomM + 1) continue;
                int py = Sy(m);
                g.DrawLine(p, 0, py, w, py);
            }
        }

        // 地图边界 0..8000
        using (Pen p = new Pen(Color.FromArgb(90, 100, 110), 2f))
        {
            int x0 = Sx(0), y0 = Sy(0), x1 = Sx(8000), y1 = Sy(8000);
            g.DrawRectangle(p, Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        }

        // 概览时的 A-H / 1-8 标签
        if (ViewScale <= MinScale() * 1.15)
        {
            using (Font f = new Font("Segoe UI", 8f, FontStyle.Bold))
            using (Brush b = new SolidBrush(Color.FromArgb(154, 164, 175)))
            {
                for (int c = 0; c < 8; c++)
                    g.DrawString(((char)('A' + c)).ToString(), f, b, Sx(c * 1000.0 + 500.0) - 5, 3);
                for (int r = 0; r < 8; r++)
                    g.DrawString((r + 1).ToString(), f, b, 3, Sy(r * 1000.0 + 500.0) - 6);
            }
        }

        // 连线 + 距离
        Point p1 = ToScreen(selfX, selfY), p2 = ToScreen(tgtX, tgtY);
        using (Pen pl = new Pen(Color.FromArgb(148, 163, 184), 1.5f)) { pl.DashStyle = DashStyle.Dash; g.DrawLine(pl, p1, p2); }

        string ds = ((int)Math.Round(Distance())) + " m";
        using (Font f = new Font("Segoe UI", 8.5f, FontStyle.Bold))
        {
            SizeF sz = g.MeasureString(ds, f);
            float tx = (p1.X + p2.X) / 2f - sz.Width / 2f;
            float ty = (p1.Y + p2.Y) / 2f - 10f - sz.Height / 2f;
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(200, 13, 17, 23)))
                g.FillRectangle(bg, tx - 3, ty - 2, sz.Width + 6, sz.Height + 4);
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(255, 210, 125)))
                g.DrawString(ds, f, tb, tx, ty);
        }

        DrawMarker(g, p1, Color.FromArgb(56, 189, 248), "自己");
        DrawMarker(g, p2, Color.FromArgb(251, 113, 133), "目标");

        using (Font f = new Font("Microsoft YaHei", 7.5f))
        using (Brush b = new SolidBrush(Color.FromArgb(110, 120, 130)))
            g.DrawString("滚轮缩放 · 拖拽平移 · 单击放点", f, b, 6, h - 16);
    }

    void DrawMarker(Graphics g, Point p, Color c, string label)
    {
        using (Pen halo = new Pen(c, 2.5f)) g.DrawEllipse(halo, p.X - 6, p.Y - 6, 12, 12);
        using (SolidBrush dot = new SolidBrush(c)) g.FillEllipse(dot, p.X - 3, p.Y - 3, 7, 7);
        using (Font f = new Font("Microsoft YaHei", 8f, FontStyle.Bold))
        {
            SizeF sz = g.MeasureString(label, f);
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(180, 13, 17, 23)))
                g.FillRectangle(bg, p.X - sz.Width / 2f - 2, p.Y - 16 - sz.Height, sz.Width + 4, sz.Height + 2);
            using (SolidBrush tb = new SolidBrush(c))
                g.DrawString(label, f, tb, p.X - sz.Width / 2f, p.Y - 16 - sz.Height);
        }
    }

    // ---------- 热键 ----------
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterHotKey(this.Handle, HOTKEY_ID, MOD_CTRL | MOD_SHIFT, 0x4D);
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(this.Handle, HOTKEY_ID);
        base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message m)
    {
        const int WM_HOTKEY = 0x0312;
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
        {
            if (this.Visible) this.Hide();
            else { this.Show(); this.Activate(); }
            return;
        }
        base.WndProc(ref m);
    }

    class MapPanel : Panel
    {
        MortarApp o;
        public MapPanel(MortarApp owner) { o = owner; this.DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e) { o.DrawMap(e.Graphics); base.OnPaint(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { o.MapWheel(e); base.OnMouseWheel(e); }
        protected override void OnMouseDown(MouseEventArgs e) { o.MapDown(e); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { o.MapMove(e); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { o.MapUp(e); base.OnMouseUp(e); }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { o.FitView(); base.OnMouseDoubleClick(e); }
    }
}

public static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MortarApp());
    }
}
