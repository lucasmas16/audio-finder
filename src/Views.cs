using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;

namespace AudioFinder
{
    internal static class Ui
    {
        internal static Label Label(string text, float size, bool muted)
        { return new Label { Text = text, Font = new Font("Segoe UI", size), BackColor = Color.Transparent, Tag = muted ? "muted" : null, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft }; }
        internal static SoftButton Button(string text, EventHandler click)
        { var b = new SoftButton { Text = text }; b.Click += click; return b; }
        internal static void Bounds(Control c, Control scale, int x, int y, int width, int height)
        { c.SetBounds(PaintUi.S(scale, x), PaintUi.S(scale, y), PaintUi.S(scale, width), PaintUi.S(scale, height)); }
        internal static void Init(Form form, string title, int width, int height)
        {
            form.SuspendLayout();
            form.Text = title; form.Font = new Font("Segoe UI", 9.5f); form.AutoScaleDimensions = new SizeF(96, 96); form.AutoScaleMode = AutoScaleMode.Dpi;
            form.ClientSize = new Size(width, height); form.StartPosition = FormStartPosition.CenterScreen; form.MaximizeBox = false;
            form.Icon = NativeMethods.MakeIcon();
        }
        internal static void Ready(Form form) { form.ResumeLayout(true); }
        internal static void Theme(Form form, Appearance appearance)
        {
            var palette = new Palette(appearance); form.BackColor = palette.Background; form.ForeColor = palette.Text;
            PaintUi.Apply(form, palette); PaintUi.TitleBar(form, palette);
        }
    }
    internal sealed class MainForm : Form
    {
        private readonly TrayApp app;
        private readonly Label title, subtitle, caption, hint, footer, status;
        private readonly ThemeToggle theme;
        private readonly LanguageToggle language;
        private readonly GlassPanel sidebar;
        private readonly Label currentTitle, currentName, currentState, shortcutTitle, shortcutHelp, errorsTitle, errorsHelp, startupTitle, startupHelp;
        private readonly Toggle enabled, errors, startup;
        private readonly SoftButton history;
        private readonly DeviceRow[] rows = new DeviceRow[8];
        private bool updating;
        internal MainForm(TrayApp application)
        {
            app = application; Ui.Init(this, "Audio Finder", 1000, 782); MinimumSize = Size;
            title = Ui.Label("Audio Finder", 25, false); title.Font = new Font("Segoe UI Semibold", 25);
            subtitle = Ui.Label(L.T("Seu som. No atalho certo.", "Your sound. On the right shortcut."), 10, true);
            theme = new ThemeToggle { Checked = app.Config.Theme == Appearance.Dark };
            theme.CheckedChanged += delegate { if (!updating) Change(c => c.Theme = theme.Checked ? Appearance.Dark : Appearance.Light); };
            language = new LanguageToggle { Checked = L.English };
            language.CheckedChanged += delegate { if (!updating) Change(c => c.Language = language.Checked ? Language.English : Language.Portuguese); };
            caption = Ui.Label(L.T("SUAS SAÍDAS", "YOUR OUTPUTS"), 9, true);
            hint = Ui.Label(L.T("Até 8 dispositivos · escolha a saída e grave seu atalho", "Up to 8 devices · pick an output and record your shortcut"), 9, true);
            sidebar = new GlassPanel();
            currentTitle = Ui.Label(L.T("TOCANDO AGORA", "NOW PLAYING"), 9, true);
            currentName = Ui.Label(NoActiveOutput, 18, false); currentName.Font = new Font("Segoe UI Semibold", 18);
            currentState = Ui.Label(WindowsDefault, 9, true);
            shortcutTitle = Ui.Label(L.T("Atalhos ativos", "Shortcuts on"), 10, false); shortcutHelp = Ui.Label(L.T("Funcionam com o app\nna bandeja do Windows.", "They work while the app\nis in the Windows tray."), 9, true);
            errorsTitle = Ui.Label(L.T("Avisos na tela", "On-screen notices"), 10, false); errorsHelp = Ui.Label(L.T("Mostra trocas e falhas.\nFecha após 3 segundos.", "Shows switches and failures.\nCloses after 3 seconds."), 9, true);
            startupTitle = Ui.Label(L.T("Iniciar com Windows", "Start with Windows"), 10, false); startupHelp = Ui.Label(L.T("Abre direto na bandeja.", "Opens straight to the tray."), 9, true);
            enabled = new Toggle { AccessibleName = L.T("Ativar atalhos globais", "Enable global shortcuts") }; errors = new Toggle { AccessibleName = L.T("Mostrar avisos de troca por 3 segundos", "Show switch notices for 3 seconds") }; startup = new Toggle { AccessibleName = L.T("Iniciar com Windows", "Start with Windows") };
            enabled.CheckedChanged += delegate { if (!updating) Change(c => c.Enabled = enabled.Checked); };
            errors.CheckedChanged += delegate { if (!updating) Change(c => c.ShowErrors = errors.Checked); };
            startup.CheckedChanged += delegate { if (!updating) { try { Startup.Set(startup.Checked); } catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Não foi possível alterar a inicialização", "Could not change startup")); } UpdateView(); } };
            history = Ui.Button(L.T("Histórico Bluetooth", "Bluetooth history"), delegate { using (var form = new HistoryForm(app)) form.ShowDialog(this); });
            sidebar.Controls.AddRange(new Control[] { currentTitle, currentName, currentState, shortcutTitle, shortcutHelp, errorsTitle, errorsHelp, startupTitle, startupHelp, enabled, errors, startup, history });
            status = Ui.Label("", 9, true);
            footer = Ui.Label(L.T("Fechar esta janela mantém o Audio Finder na bandeja. Para encerrar, use Sair no ícone perto do relógio.", "Closing this window keeps Audio Finder in the tray. To quit, use Exit on the icon next to the clock."), 9, true);
            Controls.AddRange(new Control[] { title, subtitle, language, theme, caption, hint, sidebar, status, footer });
            for (int i = 0; i < 8; i++)
            {
                int index = i; rows[i] = new DeviceRow(i + 1);
                rows[i].Edit.Click += delegate { app.Refresh(); app.BeginCapture(); try { using (var editor = new SlotEditor(app, index)) editor.ShowDialog(this); } finally { app.EndCapture(); } };
                rows[i].Activate.Click += delegate { app.SelectSlot(index); };
                Controls.Add(rows[i]);
            }
            app.Updated += UpdateView; UpdateView();
            Shown += delegate { Ui.Theme(this, app.Config.Theme); };
            Ui.Ready(this); MinimumSize = Size;
        }
        private void Change(Action<Settings> action)
        { var next = app.Config.Copy(); action(next); string error; if (!app.Apply(next, out error)) { MessageBox.Show(this, error, L.T("Não foi possível salvar", "Could not save"), MessageBoxButtons.OK, MessageBoxIcon.Warning); UpdateView(); } }
        private static string NoActiveOutput { get { return L.T("Nenhuma saída ativa", "No active output"); } }
        private static string WindowsDefault { get { return L.T("Saída padrão do Windows", "Windows default output"); } }
        internal void UpdateView()
        {
            if (IsDisposed) return;
            updating = true;
            try
            {
                theme.Checked = app.Config.Theme == Appearance.Dark; language.Checked = L.English; enabled.Checked = app.Config.Enabled; errors.Checked = app.Config.ShowErrors; startup.Checked = Startup.Enabled;
                var current = app.Devices.FirstOrDefault(d => d.Id == app.DefaultId);
                currentName.Text = current == null ? NoActiveOutput : current.Name;
                currentState.Text = current != null && current.Bluetooth ? L.T("Bluetooth · conectado", "Bluetooth · connected") : WindowsDefault;
                for (int i = 0; i < 8; i++) rows[i].Update(app, i);
                status.Text = app.LastError.Length > 0 ? app.LastError.Replace("\n", " · ") : L.T("Grave a combinação que o teclado envia. Sugestão: Ctrl + Alt + F1 até F8.", "Record the combination your keyboard sends. Suggestion: Ctrl + Alt + F1 to F8.");
                Ui.Theme(this, app.Config.Theme);
            }
            finally { updating = false; }
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (sidebar == null) return;
            float scale = PaintUi.Scale(this); int w = (int)(ClientSize.Width / scale), h = (int)(ClientSize.Height / scale);
            Ui.Bounds(title, this, 28, 19, 500, 44); Ui.Bounds(subtitle, this, 30, 62, 500, 26); Ui.Bounds(theme, this, w - 204, 30, 172, 38); Ui.Bounds(language, this, w - 328, 30, 112, 38);
            int side = 250, right = 302, rw = w - right - 26;
            Ui.Bounds(sidebar, this, 26, 110, side, 554);
            Ui.Bounds(currentTitle, this, 22, 22, 205, 22); Ui.Bounds(currentName, this, 20, 56, 210, 114); Ui.Bounds(currentState, this, 22, 174, 210, 38);
            Ui.Bounds(shortcutTitle, this, 22, 231, 159, 25); Ui.Bounds(enabled, this, 182, 229, 50, 30); Ui.Bounds(shortcutHelp, this, 22, 260, 211, 43);
            Ui.Bounds(errorsTitle, this, 22, 322, 154, 25); Ui.Bounds(errors, this, 182, 319, 50, 30); Ui.Bounds(errorsHelp, this, 22, 350, 215, 44);
            Ui.Bounds(startupTitle, this, 22, 410, 160, 26); Ui.Bounds(startup, this, 182, 409, 50, 30); Ui.Bounds(startupHelp, this, 22, 440, 213, 28);
            Ui.Bounds(history, this, 18, 485, 214, 44);
            Ui.Bounds(caption, this, right + 3, 104, 140, 27); Ui.Bounds(hint, this, right + 3, 131, rw, 25);
            for (int i = 0; i < 8; i++) if (rows[i] != null) Ui.Bounds(rows[i], this, right, 167 + 63 * i, rw, 59);
            Ui.Bounds(status, this, 30, 688, w - 60, 40); Ui.Bounds(footer, this, 30, h - 43, w - 60, 30);
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { app.Updated -= UpdateView; base.OnFormClosed(e); }
        protected override void Dispose(bool disposing) { if (disposing) { app.Updated -= UpdateView; if (Icon != null) Icon.Dispose(); } base.Dispose(disposing); }
    }
    internal sealed class DeviceRow : GlassPanel
    {
        private readonly Label number, name, detail;
        internal readonly SoftButton Activate, Edit;
        internal DeviceRow(int index)
        {
            number = Ui.Label(index.ToString("00"), 13, true); name = Ui.Label(L.F("Dispositivo {0}", "Device {0}", index), 10, false); detail = Ui.Label(L.T("Nenhuma saída cadastrada", "No output set up"), 8.5f, true);
            Activate = Ui.Button(L.T("Ativar", "Activate"), delegate { }); Edit = Ui.Button(L.T("Cadastrar", "Set up"), delegate { });
            Edit.AccessibleName = L.F("Editar dispositivo {0}", "Edit device {0}", index); Activate.AccessibleName = L.F("Ativar dispositivo {0}", "Activate device {0}", index);
            Controls.AddRange(new Control[] { number, name, detail, Activate, Edit });
        }
        internal void Update(TrayApp app, int index)
        {
            var slot = app.Config.Slots[index]; var saved = app.Config.Devices.FirstOrDefault(d => d.Id == slot.DeviceId);
            bool active = app.Devices.Any(d => d.Id == slot.DeviceId && d.Active), current = saved != null && app.DefaultId == slot.DeviceId;
            name.Text = saved == null ? L.F("Dispositivo {0}", "Device {0}", index + 1) : saved.Name;
            detail.Text = saved == null ? L.T("Escolha uma saída e um atalho", "Pick an output and a shortcut") : (current ? L.T("Em uso", "In use") : active ? L.T("Disponível", "Available") : L.T("Desconectado", "Disconnected")) + (saved.Bluetooth ? " · Bluetooth" : "") + "   /   " + slot.Shortcut;
            if (app.Shortcuts.Errors.ContainsKey(index)) detail.Text = L.T("Atalho ocupado · edite a combinação", "Shortcut in use · edit the combination");
            else if (!app.Config.Enabled && saved != null) detail.Text += L.T(" · pausado", " · paused");
            Activate.Text = current ? L.T("Em uso", "In use") : L.T("Ativar", "Activate"); Activate.Selected = current; Activate.Enabled = saved != null;
            Edit.Text = saved == null ? L.T("Cadastrar", "Set up") : L.T("Editar", "Edit");
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (number == null) return;
            int w = (int)(Width / PaintUi.Scale(this));
            Ui.Bounds(number, this, 14, 12, 34, 28); Ui.Bounds(name, this, 54, 6, w - 250, 25); Ui.Bounds(detail, this, 54, 29, w - 250, 22);
            Ui.Bounds(Activate, this, w - 186, 9, 78, 39); Ui.Bounds(Edit, this, w - 105, 9, 95, 39);
        }
    }
    internal sealed class CaptureBox : TextBox
    {
        internal uint Key, Mods;
        internal string Error = "";
        internal CaptureBox() { ReadOnly = true; ShortcutsEnabled = false; TextAlign = HorizontalAlignment.Center; AccessibleName = L.T("Gravar atalho: pressione a combinação desejada", "Record shortcut: press the combination you want"); }
        internal void Set(uint key, uint mods) { Error = ""; Key = key; Mods = mods; Text = HotkeyText.Format(key, mods); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Tab) return base.ProcessCmdKey(ref msg, keyData);
            if (key == Keys.Escape) { Parent.SelectNextControl(this, true, true, true, true); return true; }
            uint mods = ((keyData & Keys.Control) != 0 ? 2u : 0) | ((keyData & Keys.Alt) != 0 ? 1u : 0) | ((keyData & Keys.Shift) != 0 ? 4u : 0);
            if (HotkeyText.Valid((uint)key, mods)) Set((uint)key, mods);
            else if (key == Keys.F12) { Error = L.T("F12 é reservada pelo Windows. Escolha outra tecla.", "F12 is reserved by Windows. Choose another key."); Text = L.T("F12 é reservada pelo Windows", "F12 is reserved by Windows"); }
            return true;
        }
    }
    internal sealed class DeviceChoice
    {
        internal Endpoint Endpoint;
        internal bool Saved;
        public override string ToString() { return Endpoint.ToString(); }
        // Offer only outputs that can play now. An unavailable output stays listed only when kept
        // (the slot's own device or the current selection), so existing slots are never altered.
        internal static List<DeviceChoice> Build(IEnumerable<Endpoint> endpoints, IEnumerable<SavedDevice> saved, IEnumerable<string> keep)
        {
            var current = (endpoints ?? Enumerable.Empty<Endpoint>()).ToList();
            var known = (saved ?? Enumerable.Empty<SavedDevice>()).ToList();
            var result = new List<DeviceChoice>();
            foreach (var endpoint in current.Where(e => e.Active))
                if (!result.Any(c => Same(c.Endpoint.Id, endpoint.Id))) result.Add(new DeviceChoice { Endpoint = endpoint, Saved = known.Any(s => Same(s.Id, endpoint.Id)) });
            foreach (var id in (keep ?? Enumerable.Empty<string>()).Where(k => !String.IsNullOrWhiteSpace(k)))
            {
                if (result.Any(c => Same(c.Endpoint.Id, id))) continue;
                var remembered = known.FirstOrDefault(s => Same(s.Id, id));
                if (remembered != null) { result.Add(new DeviceChoice { Endpoint = new Endpoint { Id = remembered.Id, Name = remembered.Name, Bluetooth = remembered.Bluetooth, State = 0 }, Saved = true }); continue; }
                var enumerated = current.FirstOrDefault(e => Same(e.Id, id));
                if (enumerated != null) result.Add(new DeviceChoice { Endpoint = enumerated, Saved = false });
            }
            return result;
        }
        internal static bool Same(string a, string b) { return !String.IsNullOrWhiteSpace(a) && String.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }
    internal sealed class SlotEditor : Form
    {
        private readonly TrayApp app;
        private readonly int index;
        private readonly ComboBox devices;
        private readonly CaptureBox capture;
        private readonly CheckBox bluetooth;
        private readonly Label feedback;
        private readonly IconPicker picker;
        private bool loading;
        private string status = "";
        internal static string NoOutputMessage { get { return L.T("Nenhuma saída conectada. Ligue o dispositivo e ele aparece aqui.", "No output connected. Turn the device on and it shows up here."); } }
        internal static string KeptOfflineMessage { get { return L.T("Este dispositivo está desconectado. Você pode manter ou escolher outro conectado.", "This device is disconnected. You can keep it or pick another connected one."); } }
        internal static string NewOfflineMessage { get { return L.T("Este dispositivo desconectou. Conecte-o de novo ou escolha outro para salvar.", "This device disconnected. Reconnect it or pick another one to save."); } }
        internal SlotEditor(TrayApp application, int slotIndex)
        {
            app = application; index = slotIndex;
            Ui.Init(this, L.F("Dispositivo {0} · Audio Finder", "Device {0} · Audio Finder", index + 1), 654, 743); FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            var title = Ui.Label(L.F("Configure seu dispositivo {0}", "Set up your device {0}", index + 1), 18, false); title.SetBounds(26, 20, 600, 38);
            var label = Ui.Label(L.T("SAÍDA DE ÁUDIO", "AUDIO OUTPUT"), 9, true); label.SetBounds(28, 74, 500, 25);
            devices = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, DropDownWidth = 590, AccessibleName = L.T("Saída de áudio", "Audio output") }; devices.SetBounds(28, 104, 594, 30);
            devices.DrawMode = DrawMode.OwnerDrawFixed;
            devices.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                var p = new Palette(app.Config.Theme); bool selected = (e.State & DrawItemState.Selected) != 0;
                using (var brush = new SolidBrush(selected ? p.Selection : p.Inset)) e.Graphics.FillRectangle(brush, e.Bounds);
                if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, devices.Items[e.Index].ToString(), devices.Font, Rectangle.Inflate(e.Bounds, -4, 0), p.Text, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
                if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
            };
            bluetooth = new CheckBox { Text = L.T("Guardar como fone Bluetooth", "Remember as a Bluetooth headset"), AutoSize = true }; bluetooth.SetBounds(28, 147, 590, 30);
            devices.SelectedIndexChanged += delegate { if (!loading) { ApplyChoice(); UpdateStatus(); } };
            var imageLabel = Ui.Label(L.T("IMAGEM DO DISPOSITIVO", "DEVICE IMAGE"), 9, true); imageLabel.SetBounds(28, 180, 400, 25);
            picker = new IconPicker(); picker.SetBounds(26, 208, 598, 210);
            picker.CustomRequested += delegate { ChooseImage(); };
            var keyLabel = Ui.Label(L.T("ATALHO GLOBAL", "GLOBAL SHORTCUT"), 9, true); keyLabel.SetBounds(28, 436, 500, 24);
            capture = new CaptureBox(); capture.SetBounds(28, 467, 357, 31);
            var slot = app.Config.Slots[index]; capture.Set(slot.Key, slot.Modifiers);
            var suggested = Ui.Button(L.F("Usar Ctrl + Alt + F{0}", "Use Ctrl + Alt + F{0}", index + 1), delegate { capture.Set((uint)Keys.F1 + (uint)index, 3); }); suggested.SetBounds(400, 458, 225, 43);
            var help = Ui.Label(L.T("Clique no campo e pressione o atalho. Tab avança; Esc sai da gravação.\nTeclados com tecla Fn podem reservar algumas combinações para funções internas.\nO cadastro permanece salvo quando o dispositivo é desconectado.", "Click the field and press the shortcut. Tab moves on; Esc stops recording.\nKeyboards with an Fn key may reserve some combinations for built-in functions.\nThe entry stays saved when the device is disconnected."), 9, true); help.SetBounds(28, 513, 594, 70);
            feedback = Ui.Label("", 9, true); feedback.SetBounds(28, 587, 594, 44);
            var clear = Ui.Button(L.T("Limpar posição", "Clear position"), delegate { Save(true); }); clear.SetBounds(26, 670, 145, 44);
            var noKey = Ui.Button(L.T("Sem atalho", "No shortcut"), delegate { capture.Set(0, 0); }); noKey.SetBounds(178, 670, 130, 44);
            var cancel = Ui.Button(L.T("Cancelar", "Cancel"), delegate { DialogResult = DialogResult.Cancel; }); cancel.SetBounds(361, 670, 118, 44);
            var save = Ui.Button(L.T("Salvar", "Save"), delegate { Save(false); }); save.Primary = true; save.SetBounds(489, 670, 137, 44);
            Controls.AddRange(new Control[] { title, label, devices, bluetooth, imageLabel, picker, keyLabel, capture, suggested, help, feedback, clear, noKey, cancel, save });
            LoadChoices();
            app.Updated += OnDevicesUpdated;
            Ui.Theme(this, app.Config.Theme); devices.BackColor = new Palette(app.Config.Theme).Inset; devices.ForeColor = ForeColor;
            Shown += delegate { PaintUi.TitleBar(this, new Palette(app.Config.Theme)); };
            Ui.Ready(this);
            devices.ItemHeight = PaintUi.S(this, 25);
            Shown += delegate {
                var area = Screen.FromControl(this).WorkingArea;
                if (Height > area.Height - 24) {
                    AutoScroll = true; AutoScrollMinSize = ClientSize;
                    Height = area.Height - 24; Top = area.Top + 12;
                }
            };
        }
        private void Save(bool clear)
        {
            var next = app.Config.Copy(); var slot = next.Slots[index];
            if (clear) { slot.DeviceId = ""; slot.Key = slot.Modifiers = 0; }
            else
            {
                if (capture.Error.Length > 0) { feedback.Text = capture.Error; return; }
                app.Refresh();
                var choice = devices.SelectedItem as DeviceChoice;
                if (choice == null) { feedback.Text = L.T("Selecione uma saída de áudio.", "Select an audio output."); return; }
                if (!choice.Saved && !app.Devices.Any(d => d.Id == choice.Endpoint.Id && d.Active)) { feedback.Text = L.T("Conecte o dispositivo antes de cadastrá-lo pela primeira vez.", "Connect the device before setting it up for the first time."); return; }
                if (capture.Key != 0 && next.Slots.Where((s, i) => i != index).Any(s => s.Key == capture.Key && s.Modifiers == capture.Mods)) { feedback.Text = L.T("Esse atalho já está cadastrado em outra posição.", "That shortcut is already used by another position."); return; }
                slot.DeviceId = choice.Endpoint.Id; slot.Key = capture.Key; slot.Modifiers = capture.Mods;
                var saved = next.Devices.FirstOrDefault(d => d.Id == slot.DeviceId);
                if (saved == null) { saved = new SavedDevice { Id = slot.DeviceId }; next.Devices.Add(saved); }
                saved.Name = choice.Endpoint.Name; saved.Bluetooth = bluetooth.Checked; saved.RememberIdentity(choice.Endpoint);
                saved.IconPreset = picker.Preset; saved.IconPath = picker.CustomPath;
            }
            string error;
            if (!app.Apply(next, out error)) { app.BeginCapture(); feedback.Text = error; return; }
            DialogResult = DialogResult.OK;
        }
        private void OnDevicesUpdated() { if (!IsDisposed && !Disposing) LoadChoices(); }
        // Rebuilds the output list without touching the user's in-progress choices.
        private void LoadChoices()
        {
            var previous = devices.SelectedItem as DeviceChoice;
            string slotId = app.Config.Slots[index].DeviceId;
            var keep = new List<string> { slotId }; if (previous != null) keep.Add(previous.Endpoint.Id);
            var choices = DeviceChoice.Build(app.Devices, app.Config.Devices, keep);
            string target = previous != null ? previous.Endpoint.Id : slotId;
            loading = true;
            try
            {
                devices.BeginUpdate();
                devices.Items.Clear(); devices.Items.AddRange(choices.Cast<object>().ToArray());
                devices.SelectedIndex = choices.FindIndex(c => DeviceChoice.Same(c.Endpoint.Id, target));
                devices.EndUpdate();
            }
            finally { loading = false; }
            if (devices.SelectedIndex < 0 && choices.Count > 0) devices.SelectedIndex = 0;
            else if (previous == null) ApplyChoice();
            UpdateStatus();
        }
        private void ApplyChoice()
        {
            var choice = devices.SelectedItem as DeviceChoice;
            bluetooth.Checked = choice != null && (choice.Endpoint.Bluetooth || app.Config.Devices.Any(d => d.Id == choice.Endpoint.Id && d.Bluetooth));
            var remembered = choice == null ? null : app.Config.Devices.FirstOrDefault(d => d.Id == choice.Endpoint.Id);
            picker.Load(remembered == null ? "generic" : remembered.IconPreset, remembered == null ? "" : remembered.IconPath);
        }
        // Status messages never replace an error the user still needs to read.
        private void UpdateStatus()
        {
            var choice = devices.SelectedItem as DeviceChoice;
            string next = devices.Items.Count == 0 ? NoOutputMessage : choice != null && !choice.Endpoint.Active ? (choice.Saved ? KeptOfflineMessage : NewOfflineMessage) : "";
            if (feedback.Text.Length == 0 || feedback.Text == status) feedback.Text = next;
            status = next;
        }
        private void ChooseImage()
        {
            using (var dialog = new OpenFileDialog { Filter = L.T("Imagem PNG|*.png", "PNG image|*.png") })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { picker.SetCustom(DeviceVisuals.Import(dialog.FileName)); }
                catch (Exception ex) { feedback.Text = L.T("Não foi possível importar: ", "Could not import: ") + ex.Message; }
            }
        }
        internal string Status { get { return status; } }
        protected override void Dispose(bool disposing) { if (disposing) { app.Updated -= OnDevicesUpdated; if (Icon != null) Icon.Dispose(); } base.Dispose(disposing); }
    }
    internal sealed class HistoryForm : Form
    {
        internal HistoryForm(TrayApp app)
        {
            Ui.Init(this, L.T("Histórico Bluetooth · Audio Finder", "Bluetooth history · Audio Finder"), 620, 420); FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            var title = Ui.Label(L.T("Seus fones ficam lembrados", "Your headsets are remembered"), 18, false); title.SetBounds(26, 20, 564, 35);
            var help = Ui.Label(L.T("Selecione os fones que deseja esquecer. Seus atalhos serão liberados.\nIsso limpa apenas o Audio Finder; não desfaz o pareamento do Windows.", "Select the headsets you want to forget. Their shortcuts will be freed.\nThis only clears Audio Finder; it does not undo the Windows pairing."), 9, true); help.SetBounds(28, 66, 564, 50);
            var list = new CheckedListBox { CheckOnClick = true, BorderStyle = BorderStyle.None, IntegralHeight = false, AccessibleName = L.T("Fones Bluetooth salvos", "Saved Bluetooth headsets") }; list.SetBounds(28, 132, 564, 176);
            var saved = app.Config.Devices.Where(d => d.Bluetooth).ToList();
            foreach (var device in saved) list.Items.Add(device.Name + (app.Devices.Any(d => d.Id == device.Id && d.Active) ? L.T(" · conectado", " · connected") : L.T(" · desconectado", " · disconnected")));
            var empty = Ui.Label(saved.Count == 0 ? L.T("Nenhum fone Bluetooth cadastrado.", "No Bluetooth headset saved.") : L.T("O histórico só é apagado quando você pede.", "History is only cleared when you ask."), 9, true); empty.SetBounds(28, 312, 564, 28);
            var all = Ui.Button(L.T("Marcar todos", "Select all"), delegate { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); }); all.SetBounds(26, 359, 144, 40);
            var remove = Ui.Button(L.T("Esquecer selecionados", "Forget selected"), delegate
            {
                var selected = list.CheckedIndices.Cast<int>().Select(i => saved[i]).ToList(); if (selected.Count == 0) return;
                if (MessageBox.Show(this, L.T("Esquecer os fones e liberar suas posições e atalhos?", "Forget these headsets and free their positions and shortcuts?") + "\n\n" + String.Join("\n", selected.Select(d => d.Name)), L.T("Limpar histórico Bluetooth", "Clear Bluetooth history"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                var next = app.Config.Copy(); next.Forget(selected.Select(d => d.Id)); string error;
                if (app.Apply(next, out error)) Close(); else MessageBox.Show(this, error, L.T("Não foi possível salvar", "Could not save"));
            }); remove.SetBounds(184, 359, 239, 40);
            var close = Ui.Button(L.T("Fechar", "Close"), delegate { Close(); }); close.SetBounds(443, 359, 149, 40);
            Controls.AddRange(new Control[] { title, help, list, empty, all, remove, close }); Ui.Theme(this, app.Config.Theme);
            list.BackColor = new Palette(app.Config.Theme).Inset; list.ForeColor = ForeColor; Shown += delegate { PaintUi.TitleBar(this, new Palette(app.Config.Theme)); };
            Ui.Ready(this);
        }
        protected override void Dispose(bool disposing) { if (disposing && Icon != null) Icon.Dispose(); base.Dispose(disposing); }
    }
    internal sealed class ConfiguredOutput
    {
        internal int Position;
        internal string DeviceId;
        internal string Name;
        internal bool Active;
        internal string IconPreset, IconPath;
    }
    internal sealed class OutputToast : Form
    {
        internal const int Duration = 3000;
        internal const int ConnectedDuration = 4000;
        internal readonly string Heading;
        internal readonly string SelectedId;
        internal readonly int SelectedPosition;
        internal readonly List<string> ListedEndpointIds;
        internal readonly List<int> ListedPositions;
        internal readonly List<string> ListedNames;
        private readonly Timer timer;
        internal readonly bool IsConnecting;
        internal event Action CancelRequested;
        private bool shaping;
        private readonly ConnectionVisual connectionVisual;
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80; return cp; } }
        internal static OutputToast Selection(int selectedIndex, Settings settings, IEnumerable<Endpoint> endpoints, Appearance theme)
        {
            var enumerated = (endpoints ?? Enumerable.Empty<Endpoint>()).Where(d => d != null).ToList();
            var configured = new List<ConfiguredOutput>();
            if (settings != null)
            {
                for (int index = 0; index < settings.Slots.Count && index < 8; index++)
                {
                    var slot = settings.Slots[index];
                    if (slot == null || String.IsNullOrWhiteSpace(slot.DeviceId)) continue;
                    var current = enumerated.FirstOrDefault(d => String.Equals(d.Id, slot.DeviceId, StringComparison.OrdinalIgnoreCase));
                    var saved = settings.Devices.FirstOrDefault(d => String.Equals(d.Id, slot.DeviceId, StringComparison.OrdinalIgnoreCase));
                    string name = current != null && !String.IsNullOrWhiteSpace(current.Name) ? current.Name : saved != null && !String.IsNullOrWhiteSpace(saved.Name) ? saved.Name : L.T("Saída de áudio", "Audio output");
                    configured.Add(new ConfiguredOutput { Position = index + 1, DeviceId = slot.DeviceId, Name = name, Active = current != null && current.Active, IconPreset = saved == null ? "generic" : saved.IconPreset, IconPath = saved == null ? "" : saved.IconPath });
                }
            }
            int selectedPosition = selectedIndex + 1;
            var selected = configured.FirstOrDefault(d => d.Position == selectedPosition);
            return new OutputToast(L.T("Saída selecionada", "Output selected"), selected == null ? L.T("Saída de áudio", "Audio output") : selected.Name, selectedPosition, configured, theme);
        }
        internal static OutputToast Failure(string text, Appearance theme)
        { return new OutputToast(Unavailable, text, 0, Enumerable.Empty<ConfiguredOutput>(), theme); }
        private static string Unavailable { get { return L.T("Saída indisponível", "Output unavailable"); } }
        private static ConfiguredOutput SlotOutput(int index, Settings settings)
        {
            var slot = settings != null && index >= 0 && index < settings.Slots.Count ? settings.Slots[index] : null;
            var saved = slot == null ? null : settings.Devices.FirstOrDefault(d => d.Id == slot.DeviceId);
            return new ConfiguredOutput { Position = index + 1, DeviceId = slot == null ? "" : slot.DeviceId, Name = saved == null ? L.F("Dispositivo {0}", "Device {0}", index + 1) : saved.Name, IconPreset = saved == null ? "generic" : saved.IconPreset, IconPath = saved == null ? "" : saved.IconPath };
        }
        internal static OutputToast Connecting(int selectedIndex, Settings settings, Appearance theme)
        {
            var output = SlotOutput(selectedIndex, settings);
            return new OutputToast(L.T("Conectando…", "Connecting…"), output.Name, selectedIndex + 1, new[] { output }, theme, true, true);
        }
        internal static OutputToast DeviceFailure(int selectedIndex, Settings settings, string text, Appearance theme)
        {
            var output = SlotOutput(selectedIndex, settings);
            var toast = new OutputToast(Unavailable, output.Name, selectedIndex + 1, new[] { output }, theme, false, true);
            toast.AccessibleDescription = text; return toast;
        }
        internal static OutputToast Connected(int selectedIndex, Settings settings, Appearance theme)
        {
            var output = SlotOutput(selectedIndex, settings);
            return new OutputToast(L.T("Conectado", "Connected"), output.Name, selectedIndex + 1, new[] { output }, theme, false, true, true);
        }
        private OutputToast(string heading, string text, int selectedPosition, IEnumerable<ConfiguredOutput> outputs, Appearance theme, bool connecting = false, bool connectionView = false, bool connected = false)
        {
            Heading = heading; SelectedPosition = selectedPosition; IsConnecting = connecting;
            var configured = (outputs ?? Enumerable.Empty<ConfiguredOutput>()).Where(d => d != null).OrderBy(d => d.Position).ToList();
            var selected = configured.FirstOrDefault(d => d.Position == SelectedPosition);
            SelectedId = selected == null ? "" : selected.DeviceId;
            ListedEndpointIds = configured.Select(d => d.DeviceId).ToList();
            ListedPositions = configured.Select(d => d.Position).ToList();
            ListedNames = configured.Select(d => d.Name).ToList();
            bool showList = configured.Count > 0 && !connectionView;
            int width = showList ? Math.Max(360, configured.Count * 116 + 64) : 440;
            Ui.Init(this, "Audio Finder · " + heading.ToLowerInvariant(), width, connectionView ? 224 : showList ? 244 : 134); FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            var panel = new GlassPanel { Dock = DockStyle.Fill, Size = ClientSize };
            var title = Ui.Label(connectionView && !connecting && !connected ? L.T("Sem conexão", "Not connected") : heading, 12, false); title.SetBounds(18, 12, connecting ? width - 150 : width - 36, 25);
            var body = Ui.Label(connectionView ? selectedPosition + " · " + text : text, 9, !showList); body.SetBounds(18, 39, width - 36, showList || connectionView ? 24 : 67);
            body.AccessibleName = connectionView ? L.F("Posição {0}: {1}", "Position {0}: {1}", selectedPosition, text) : text;
            title.Anchor = body.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.AddRange(new Control[] { title, body });
            if (showList) panel.Controls.Add(new OutputBar(configured, SelectedPosition) { Dock = DockStyle.Bottom, Height = 175 });
            if (connectionView)
            {
                connectionVisual = new ConnectionVisual(selected ?? SlotOutput(selectedPosition - 1, null), connecting, connected); connectionVisual.SetBounds(12, 70, width - 24, 140); connectionVisual.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; panel.Controls.Add(connectionVisual);
                if (connecting)
                {
                    var cancel = Ui.Button(L.T("Cancelar", "Cancel"), delegate { var handler = CancelRequested; if (handler != null) handler(); else Close(); }); cancel.SetBounds(width - 119, 11, 100, 31); cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right; cancel.AccessibleName = L.T("Cancelar tentativa de conexão", "Cancel connection attempt"); panel.Controls.Add(cancel);
                }
            }
            Controls.Add(panel); Ui.Theme(this, theme);
            // Child-control transparency only exposes the opaque parent Form.
            // Clip the native window to the painted surface, including its border.
            BackColor = new Palette(theme).Surface;
            timer = new Timer { Interval = connected ? ConnectedDuration : Duration }; timer.Tick += delegate { ConnectionTrace.Write("toast timer kind=" + Heading); timer.Stop(); Close(); };
            Ui.Ready(this);
            UpdateShape();
        }
        private void UpdateShape()
        {
            if (shaping || Disposing || IsDisposed || FormBorderStyle != FormBorderStyle.None || ClientSize.Width < 8 || ClientSize.Height < 8) return;
            shaping = true;
            try
            {
                float scale = PaintUi.Scale(this);
                var bounds = new RectangleF(1, 1, ClientSize.Width - 5 * scale, ClientSize.Height - 6 * scale);
                bounds.Inflate(scale / 2, scale / 2);
                using (var path = PaintUi.Round(bounds, 13 * scale + scale / 2))
                {
                    var old = Region; Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            }
            finally { shaping = false; }
        }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); UpdateShape(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateShape(); }
        protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); UpdateShape(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); ConnectionTrace.Write("toast visibility kind=" + Heading + " visible=" + Visible); if (connectionVisual != null) connectionVisual.SetAnimationVisible(Visible); }
        protected override void OnFormClosed(FormClosedEventArgs e) { ConnectionTrace.Write("toast closed kind=" + Heading + " reason=" + e.CloseReason); base.OnFormClosed(e); }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e); var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Width = Math.Min(Width, area.Width - 24); Height = Math.Min(Height, area.Height - 24);
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 20);
            // Reassert the native topmost position after replacing the previous no-activate window.
            // Form.TopMost alone can stay true while the replacement is behind the foreground app.
            NativeMethods.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);
            Update();
            if (!IsConnecting) timer.Start();
            ConnectionTrace.Write("toast placed kind=" + Heading + " bounds=" + Bounds + " screen=" + area + " topmost=" + TopMost);
        }
        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); if (Icon != null) Icon.Dispose(); } base.Dispose(disposing); }
    }
}
