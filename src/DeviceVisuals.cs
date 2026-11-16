using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Diagnostics;

namespace AudioFinder
{
    internal static class DeviceVisuals
    {
        internal static readonly string[] Keys = { "generic", "airpods-pro", "airpods", "edifier-monitors", "kuba-disco", "sony-pulse", "edifier-w820nb", "monitor-tv", "kz-az09pro", "shokz-openrun-pro-2" };
        private static readonly string[] NamesPortuguese = { "Ícone padrão", "Fone In-Ear", "Fone Earbud", "Monitor de Áudio", "Fone Premium", "Fone Gamer", "Fone Bluetooth OverEar", "Monitor/Tv", "Fone Esportivo", "Fone Esportivo de Condução Óssea" };
        private static readonly string[] NamesEnglish = { "Default icon", "In-ear earbuds", "Open-fit earbuds", "Studio speakers", "Premium headphones", "Gaming headset", "Wireless over-ear", "Monitor/TV", "Sports earbuds", "Bone conduction" };
        // Display names follow the UI language; Keys stay fixed because they are stored in settings.
        internal static string[] Names { get { return L.English ? NamesEnglish : NamesPortuguese; } }
        internal static Image Load(string preset, string custom)
        {
            Image image = TryLoad(custom);
            if (image != null) return image;
            return TryLoad(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "devices", (Array.IndexOf(Keys, preset) < 0 ? "generic" : preset) + ".png"));
        }
        internal static Image LoadCustom(string path) { return TryLoad(path); }
        private static Image TryLoad(string path)
        {
            if (String.IsNullOrEmpty(path)) return null;
            try { using (var source = Image.FromFile(path)) return new Bitmap(source); }
            catch (Exception e) { if (e is IOException || e is ArgumentException || e is UnauthorizedAccessException || e is OutOfMemoryException) return null; throw; }
        }
        internal static string Import(string source)
        {
            using (var image = Image.FromFile(source))
            {
                if (image.RawFormat.Guid != System.Drawing.Imaging.ImageFormat.Png.Guid) throw new ArgumentException(L.T("Escolha um arquivo PNG válido.", "Choose a valid PNG file."));
                if (image.Width > 8192 || image.Height > 8192) throw new ArgumentException(L.T("Use um PNG de até 8192 × 8192 pixels.", "Use a PNG up to 8192 × 8192 pixels."));
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioFinder", "device-images");
                Directory.CreateDirectory(folder);
                string target = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png");
                image.Save(target, System.Drawing.Imaging.ImageFormat.Png); return target;
            }
        }
    }
    internal sealed class DeviceArtwork : Control, IThemed
    {
        private Image image;
        private Palette palette = new Palette(Appearance.Dark);
        internal DeviceArtwork() { DoubleBuffered = true; SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
        internal void SetImage(string preset, string custom) { if (image != null) image.Dispose(); image = DeviceVisuals.Load(preset, custom); Invalidate(); }
        public void SetPalette(Palette value) { palette = value; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Draw(e.Graphics, ClientRectangle, image, palette);
        }
        // Without an image (the generic model has no PNG), a line-art headphone is drawn.
        internal static void Draw(Graphics g, Rectangle bounds, Image image, Palette palette)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            if (image != null)
            {
                float ratio = Math.Min((float)bounds.Width / image.Width, (float)bounds.Height / image.Height);
                int w = (int)(image.Width * ratio), h = (int)(image.Height * ratio);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, bounds.X + (bounds.Width - w) / 2, bounds.Y + (bounds.Height - h) / 2, w, h);
            }
            else
            {
                int size = Math.Min(bounds.Width, bounds.Height) * 3 / 4;
                using (var pen = new Pen(palette.Text, Math.Max(2, size / 18f)))
                {
                    int x = bounds.X + (bounds.Width - size) / 2, y = bounds.Y + (bounds.Height - size) / 2;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawArc(pen, x, y, size, size, 180, 180);
                    g.DrawRectangle(pen, x, y + size / 2, size / 5, size / 3);
                    g.DrawRectangle(pen, x + size * 4 / 5, y + size / 2, size / 5, size / 3);
                }
            }
        }
        protected override void Dispose(bool disposing) { if (disposing && image != null) image.Dispose(); base.Dispose(disposing); }
    }
    // Grid of image models: every option is visible as a thumbnail. Arrows move focus, Space/Enter selects,
    // and only one tile is a tab stop so Tab enters and leaves the grid in a single step.
    internal sealed class IconPicker : Panel, IThemed
    {
        internal const int Columns = 6;
        internal Palette Palette = new Palette(Appearance.Dark);
        private readonly List<IconTile> tiles = new List<IconTile>();
        private readonly IconTile custom;
        private readonly SoftButton change;
        private readonly ToolTip tips = new ToolTip();
        private int preset;
        private bool useCustom;
        private string customImage = "";
        internal event EventHandler CustomRequested;
        internal IconPicker()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            AccessibleName = L.T("Imagem do dispositivo", "Device image"); AccessibleRole = AccessibleRole.Grouping; TabStop = false;
            for (int i = 0; i < DeviceVisuals.Keys.Length; i++) AddTile(new IconTile(this, i, DeviceVisuals.Names[i], DeviceVisuals.Load(DeviceVisuals.Keys[i], "")));
            custom = new IconTile(this, -1, L.T("Imagem própria", "Custom image"), null); AddTile(custom);
            change = new SoftButton { Text = L.T("Trocar…", "Change…"), AccessibleName = L.T("Trocar imagem própria", "Change custom image"), Visible = false };
            change.Click += delegate { RequestCustom(); }; tips.SetToolTip(change, L.T("Escolher outro arquivo PNG", "Choose another PNG file"));
            Controls.Add(change);
            UpdateTiles();
        }
        private void AddTile(IconTile tile) { tiles.Add(tile); tips.SetToolTip(tile, tile.Caption); Controls.Add(tile); }
        internal IList<IconTile> Tiles { get { return tiles.AsReadOnly(); } }
        internal IconTile SelectedTile { get { return tiles.Single(t => t.Selected); } }
        internal string Preset { get { return DeviceVisuals.Keys[preset]; } }
        internal string CustomPath { get { return useCustom ? customImage : ""; } }
        internal bool HasCustom { get { return customImage.Length > 0; } }
        internal void Load(string presetKey, string customPath)
        {
            preset = Math.Max(0, Array.IndexOf(DeviceVisuals.Keys, presetKey));
            var image = DeviceVisuals.LoadCustom(customPath);
            customImage = image == null ? "" : customPath; useCustom = image != null;
            custom.SetImage(image); UpdateTiles();
        }
        internal void SetCustom(string path)
        {
            var image = DeviceVisuals.LoadCustom(path);
            if (image == null) throw new ArgumentException(L.T("Não foi possível abrir a imagem.", "The image could not be opened."));
            customImage = path; useCustom = true; custom.SetImage(image); UpdateTiles();
        }
        internal void SelectPreset(int index) { preset = Math.Max(0, Math.Min(DeviceVisuals.Keys.Length - 1, index)); useCustom = false; UpdateTiles(); }
        internal void Activate(IconTile tile)
        {
            if (tile != custom) { SelectPreset(tile.Index); return; }
            if (HasCustom) { useCustom = true; UpdateTiles(); } else RequestCustom();
        }
        private void RequestCustom() { var handler = CustomRequested; if (handler != null) handler(this, EventArgs.Empty); }
        internal void MoveFocus(IconTile from, Keys key)
        {
            int at = tiles.IndexOf(from), next = at;
            if (key == Keys.Left) next = at - 1; else if (key == Keys.Right) next = at + 1;
            else if (key == Keys.Up) next = at - Columns; else if (key == Keys.Down) next = at + Columns;
            if (next < 0 || next >= tiles.Count) return;
            foreach (var tile in tiles) tile.TabStop = tile == tiles[next];
            tiles[next].Focus();
        }
        private void UpdateTiles()
        {
            foreach (var tile in tiles) tile.Selected = tile == custom ? useCustom : !useCustom && tile.Index == preset;
            if (!ContainsFocus) foreach (var tile in tiles) tile.TabStop = tile.Selected;
            change.Visible = HasCustom;
        }
        protected override void OnLeave(EventArgs e) { base.OnLeave(e); foreach (var tile in tiles) tile.TabStop = tile.Selected; }
        public void SetPalette(Palette value) { Palette = value; BackColor = value.Background; foreach (var tile in tiles) tile.Invalidate(); Invalidate(); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (tiles.Count == 0) return;
            int gap = PaintUi.S(this, 8), rows = (tiles.Count + 1 + Columns - 1) / Columns;
            int width = (Width - gap * (Columns - 1)) / Columns, height = (Height - gap * (rows - 1)) / rows;
            for (int i = 0; i < tiles.Count; i++) tiles[i].SetBounds((i % Columns) * (width + gap), (i / Columns) * (height + gap), width, height);
            int cell = tiles.Count, buttonHeight = PaintUi.S(this, 42);
            if (change != null) change.SetBounds((cell % Columns) * (width + gap), (cell / Columns) * (height + gap) + (height - buttonHeight) / 2, width, buttonHeight);
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }
    internal sealed class IconTile : Control
    {
        private static readonly Font CaptionFont = new Font("Segoe UI", 8f);
        internal readonly int Index;
        internal readonly string Caption;
        private readonly IconPicker owner;
        private Image image;
        private bool selected, hover;
        internal IconTile(IconPicker picker, int index, string caption, Image picture)
        {
            owner = picker; Index = index; Caption = caption; image = picture;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand; TabStop = false;
            AccessibleName = caption; AccessibleRole = AccessibleRole.RadioButton;
        }
        internal bool Selected
        {
            get { return selected; }
            set { if (selected == value) return; selected = value; Invalidate(); if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1); }
        }
        internal void SetImage(Image value) { if (image != null && image != value) image.Dispose(); image = value; Invalidate(); }
        protected override bool IsInputKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            if ((keyData & Keys.Modifiers) == Keys.None && (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Enter || key == Keys.Space)) return true;
            return base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e); if (e.Modifiers != Keys.None) return;
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { owner.Activate(this); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) { owner.MoveFocus(this, e.KeyCode); e.Handled = true; }
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Focus(); base.OnMouseDown(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); owner.Activate(this); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var p = owner.Palette; var g = e.Graphics; float s = PaintUi.Scale(this);
            var r = new RectangleF(s, s, Width - 5 * s, Height - 6 * s);
            PaintUi.Surface(g, r, p, false, selected || hover, s);
            if (selected) using (var path = PaintUi.Round(r, 13 * s)) using (var pen = new Pen(p.Accent, 2.4f * s)) g.DrawPath(pen, path);
            int caption = PaintUi.S(this, 30);
            var art = Rectangle.Round(new RectangleF(r.X + 10 * s, r.Y + 7 * s, r.Width - 20 * s, r.Height - caption - 10 * s));
            if (Index < 0 && image == null)
            {
                float size = Math.Min(art.Width, art.Height) * 0.5f;
                PaintUi.Glyph(g, "plus", new RectangleF(art.X + (art.Width - size) / 2, art.Y + (art.Height - size) / 2, size, size), p.Muted, 1.6f);
            }
            else DeviceArtwork.Draw(g, art, image, p);
            // Two centred lines keep long names readable; anything longer still has the tooltip.
            PaintUi.Text(g, Caption, CaptionFont, selected ? p.Accent : p.Text, new Rectangle((int)(r.X + 3 * s), (int)(r.Bottom - caption - 3 * s), (int)(r.Width - 6 * s), caption), TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            if (selected)
            {
                float badge = 18 * s; var dot = new RectangleF(r.Right - badge - 6 * s, r.Y + 6 * s, badge, badge);
                using (var brush = new SolidBrush(p.Accent)) g.FillEllipse(brush, dot);
                var mark = dot; mark.Inflate(-3 * s, -3 * s); PaintUi.Glyph(g, "check", mark, p.AccentText, 2.2f);
            }
            if (Focused && ShowFocusCues)
            {
                var focus = r; focus.Inflate(-4 * s, -4 * s);
                using (var path = PaintUi.Round(focus, 9 * s)) using (var pen = new Pen(p.Text, s) { DashStyle = DashStyle.Dot }) g.DrawPath(pen, path);
            }
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new TileAccessible(this); }
        protected override void Dispose(bool disposing) { if (disposing && image != null) { image.Dispose(); image = null; } base.Dispose(disposing); }
        private sealed class TileAccessible : ControlAccessibleObject
        {
            private readonly IconTile tile;
            internal TileAccessible(IconTile owner) : base(owner) { tile = owner; }
            public override AccessibleStates State { get { var state = base.State | AccessibleStates.Selectable; return tile.Selected ? state | AccessibleStates.Checked | AccessibleStates.Selected : state; } }
            public override string DefaultAction { get { return "Selecionar"; } }
            public override void DoDefaultAction() { tile.owner.Activate(tile); }
        }
    }
    internal sealed class OutputBar : Panel, IThemed
    {
        private readonly List<OutputCard> cards = new List<OutputCard>();
        internal OutputBar(IEnumerable<ConfiguredOutput> outputs, int selected)
        {
            BackColor = Color.Transparent;
            foreach (var output in outputs) { var card = new OutputCard(output, output.Position == selected); cards.Add(card); Controls.Add(card); }
        }
        public void SetPalette(Palette palette) { foreach (var card in cards) card.SetPalette(palette); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (cards == null || cards.Count == 0) return;
            int margin = PaintUi.S(this, 12), gap = PaintUi.S(this, 6);
            float unit = (Width - 2 * margin - gap * (cards.Count - 1)) / (cards.Count + 0.32f); int x = margin;
            foreach (var card in cards) { int width = (int)(unit * (card.Selected ? 1.32f : 1)); int top = PaintUi.S(this, card.Selected ? 0 : 22); card.SetBounds(x, top, Math.Max(24, width), Height - top - PaintUi.S(this, 12)); x += width + gap; }
        }
    }
    internal sealed class ConnectionVisual : Control, IThemed
    {
        private readonly bool connecting;
        private readonly bool connected;
        private readonly DeviceArtwork artwork;
        private readonly Timer animation;
        private readonly Stopwatch clock = new Stopwatch();
        private Palette palette = new Palette(Appearance.Dark);
        internal ConnectionVisual(ConfiguredOutput output, bool inProgress, bool success = false)
        {
            connecting = inProgress; connected = success; DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true); BackColor = Color.Transparent;
            AccessibleName = (connecting ? L.F("Computador tentando conectar à posição {0}: {1}", "Computer connecting to position {0}: {1}", output.Position, output.Name) : connected ? L.F("Computador conectado à posição {0}: {1}", "Computer connected to position {0}: {1}", output.Position, output.Name) : L.F("Computador sem conexão com a posição {0}: {1}", "Computer not connected to position {0}: {1}", output.Position, output.Name));
            AccessibleRole = AccessibleRole.Graphic;
            artwork = new DeviceArtwork(); artwork.SetImage(output.IconPreset, output.IconPath); Controls.Add(artwork);
            animation = new Timer { Interval = 40 }; animation.Tick += delegate {
                var form = FindForm();
                if (!Visible || form == null || !form.Visible) { SetAnimationVisible(false); return; }
                Invalidate();
            };
        }
        internal void SetAnimationVisible(bool visible)
        {
            if (animation == null || IsDisposed) return;
            if (connecting && visible) { clock.Start(); animation.Start(); } else { clock.Stop(); animation.Stop(); }
        }
        public void SetPalette(Palette value) { palette = value; artwork.SetPalette(value); Invalidate(); }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e); if (animation == null) return;
            SetAnimationVisible(Visible);
        }
        protected override void OnHandleDestroyed(EventArgs e) { if (animation != null) animation.Stop(); base.OnHandleDestroyed(e); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (artwork == null) return;
            int size = Math.Min(Height - PaintUi.S(this, 30), Width / 4);
            artwork.SetBounds((int)(Width * 0.76f) - size / 2, PaintUi.S(this, 5), size, size);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = PaintUi.Scale(this), center = Height * 0.43f;
            float size = Math.Min(Width * 0.19f, Height * 0.60f), left = Width * 0.23f - size / 2;
            var screen = new RectangleF(left, center - size * 0.42f, size, size * 0.67f);
            using (var path = PaintUi.Round(screen, 5 * scale))
            using (var fill = new SolidBrush(palette.Inset))
            using (var pen = new Pen(palette.Text, 2 * scale))
            { g.FillPath(fill, path); g.DrawPath(pen, path); g.DrawLine(pen, left + size / 2, screen.Bottom, left + size / 2, screen.Bottom + size * 0.14f); g.DrawLine(pen, left + size * 0.30f, screen.Bottom + size * 0.15f, left + size * 0.70f, screen.Bottom + size * 0.15f); }
            using (var pen = new Pen(palette.Accent, 2 * scale))
            { g.DrawLine(pen, screen.Left + size * 0.23f, screen.Top + size * 0.32f, screen.Left + size * 0.40f, screen.Top + size * 0.32f); g.DrawLine(pen, screen.Left + size * 0.40f, screen.Top + size * 0.32f, screen.Left + size * 0.49f, screen.Top + size * 0.18f); g.DrawLine(pen, screen.Left + size * 0.49f, screen.Top + size * 0.18f, screen.Left + size * 0.60f, screen.Top + size * 0.45f); g.DrawLine(pen, screen.Left + size * 0.60f, screen.Top + size * 0.45f, screen.Left + size * 0.76f, screen.Top + size * 0.45f); }
            if (connecting)
            {
                double phase = clock.Elapsed.TotalMilliseconds / 1000.0;
                for (int i = 0; i < 4; i++)
                {
                    double pulse = (Math.Sin(phase * 6 - i * 0.85) + 1) / 2;
                    float dot = (float)(3 + pulse * 3) * scale;
                    using (var brush = new SolidBrush(Color.FromArgb((int)(70 + pulse * 185), palette.Accent))) g.FillEllipse(brush, Width * (0.42f + i * 0.055f) - dot / 2, center - dot / 2, dot, dot);
                }
                float ring = 11 * scale;
                using (var pen = new Pen(palette.Accent, 2 * scale)) g.DrawArc(pen, Width * 0.51f - ring, center + 16 * scale, ring * 2, ring * 2, (float)(phase * 200 % 360), 230);
            }
            else
            {
                Color error = connected ? palette.Accent : SystemInformation.HighContrast ? palette.Text : palette.Dark ? Color.FromArgb(245, 146, 136) : Color.FromArgb(177, 50, 43);
                float r = 13 * scale, x = Width * 0.51f;
                using (var pen = new Pen(error, 2.3f * scale))
                {
                    g.DrawEllipse(pen, x - r, center - r, r * 2, r * 2);
                    if (connected) g.DrawLines(pen, new[] { new PointF(x - r * 0.55f, center), new PointF(x - r * 0.12f, center + r * 0.4f), new PointF(x + r * 0.55f, center - r * 0.42f) });
                    else { g.DrawLine(pen, x - r * 0.4f, center - r * 0.4f, x + r * 0.4f, center + r * 0.4f); g.DrawLine(pen, x + r * 0.4f, center - r * 0.4f, x - r * 0.4f, center + r * 0.4f); }
                }
            }
            using (var font = new Font("Segoe UI", 8.5f))
            {
                TextRenderer.DrawText(g, L.T("Computador", "Computer"), font, new Rectangle((int)left - 20, Height - PaintUi.S(this, 26), (int)size + 40, PaintUi.S(this, 22)), palette.Muted, TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, connecting ? "Bluetooth" : connected ? L.T("Conectado", "Connected") : L.T("Sem conexão", "Not connected"), font, new Rectangle((int)(Width * 0.38f), Height - PaintUi.S(this, 26), (int)(Width * 0.26f), PaintUi.S(this, 22)), connecting ? palette.Muted : palette.Text, TextFormatFlags.HorizontalCenter);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) { animation.Stop(); animation.Dispose(); clock.Stop(); } base.Dispose(disposing); }
    }
    internal sealed class OutputCard : Control, IThemed
    {
        internal readonly bool Selected;
        private readonly ConfiguredOutput output;
        private readonly DeviceArtwork artwork;
        private Palette palette = new Palette(Appearance.Dark);
        internal OutputCard(ConfiguredOutput value, bool selected)
        {
            output = value; Selected = selected; DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent;
            AccessibleName = selected ? L.F("Posição {0}, saída em uso: {1}", "Position {0}, output in use: {1}", value.Position, value.Name) : L.F("Posição {0}, saída configurada: {1}", "Position {0}, configured output: {1}", value.Position, value.Name);
            artwork = new DeviceArtwork(); artwork.SetImage(value.IconPreset, value.IconPath); Controls.Add(artwork);
        }
        public void SetPalette(Palette value) { palette = value; artwork.SetPalette(value); Invalidate(); }
        protected override void OnLayout(LayoutEventArgs e)
        { base.OnLayout(e); if (artwork != null) artwork.SetBounds(8, PaintUi.S(this, 29), Math.Max(8, Width - 16), Math.Max(8, Height - PaintUi.S(this, 80))); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); float scale = PaintUi.Scale(this);
            PaintUi.Surface(e.Graphics, new RectangleF(1, 1, Width - 5, Height - 5), palette, false, Selected, scale);
            Color color = Selected ? palette.Accent : palette.Muted;
            using (var font = new Font("Segoe UI Semibold", Selected ? 14 : 11)) TextRenderer.DrawText(e.Graphics, output.Position.ToString(), font, new Rectangle(5, 3, Width - 10, PaintUi.S(this, 25)), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            using (var font = new Font("Segoe UI", 8))
            {
                TextRenderer.DrawText(e.Graphics, output.Name, font, new Rectangle(6, Height - PaintUi.S(this, 47), Width - 12, PaintUi.S(this, 24)), palette.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(e.Graphics, Selected ? L.T("✓ Em uso", "✓ In use") : output.Active ? L.T("Disponível", "Available") : L.T("Indisponível", "Unavailable"), font, new Rectangle(4, Height - PaintUi.S(this, 24), Width - 8, PaintUi.S(this, 20)), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
