using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace AudioFinder
{
    internal static class Program
    {
        [STAThread] private static void Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            ConnectionTrace.Enabled = args.Contains("--diagnostics");
            bool first;
            using (var mutex = new Mutex(true, "Local\\AudioFinder." + Environment.UserName, out first))
            {
                if (!first) { NativeMethods.PostMessage(new IntPtr(0xffff), NativeMethods.ShowMessage, IntPtr.Zero, IntPtr.Zero); return; }
                try
                {
                    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioFinder", "settings.xml");
                    using (var context = new TrayApp(new SettingsStore(path), args.Contains("--tray"))) Application.Run(context);
                }
                catch (Exception ex) { MessageBox.Show(L.T("Não foi possível iniciar o Audio Finder.", "Audio Finder could not start.") + "\n\n" + ex.Message, "Audio Finder", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
    internal static class NativeMethods
    {
        internal static readonly uint ShowMessage = RegisterWindowMessage("AudioFinder.Show.1");
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        internal static Icon MakeIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                using (var brush = new SolidBrush(Color.FromArgb(26, 37, 32))) g.FillEllipse(brush, 0, 0, 31, 31);
                using (var pen = new Pen(Color.FromArgb(153, 223, 190), 3))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    for (int i = 0; i < 5; i++) { int height = new[] { 6, 14, 22, 14, 6 }[i]; g.DrawLine(pen, 7 + i * 4.5f, 16 - height / 2, 7 + i * 4.5f, 16 + height / 2); }
                }
                IntPtr handle = bitmap.GetHicon(); try { using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); } finally { DestroyIcon(handle); }
            }
        }
    }
    internal sealed class DispatchForm : Form
    {
        internal Action ShowRequested;
        internal DispatchForm() { ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.None; Size = new Size(1, 1); var handle = Handle; }
        protected override void WndProc(ref Message m) { if ((uint)m.Msg == NativeMethods.ShowMessage && ShowRequested != null) ShowRequested(); base.WndProc(ref m); }
    }
    internal sealed class TrayApp : ApplicationContext
    {
        internal Settings Config;
        internal System.Collections.Generic.List<Endpoint> Devices;
        internal string DefaultId = "", LastError = "";
        internal readonly Hotkeys Shortcuts;
        private readonly SettingsStore store;
        private readonly AudioService audio;
        private readonly DispatchForm dispatch;
        private readonly NotifyIcon tray;
        private readonly Icon icon;
        private readonly System.Windows.Forms.Timer debounce;
        private MainForm settingsForm;
        private OutputToast toast;
        private bool closing;
        private int refreshQueued;
        private int operationSequence;
        private CancellationTokenSource connectionCancellation;
        private readonly Func<SavedDevice, CancellationToken, Task<Endpoint>> connectBluetooth;
        private readonly Func<System.Collections.Generic.List<Endpoint>> readEndpoints;
        private readonly Func<string> readDefault;
        private readonly Action<string> selectOutput;
        internal event Action Updated;
        internal TrayApp(SettingsStore settingsStore, bool startHidden, Func<SavedDevice, CancellationToken, Task<Endpoint>> connect = null,
            Func<System.Collections.Generic.List<Endpoint>> enumerate = null, Func<string> defaultOutput = null, Action<string> select = null)
        {
            ConnectionTrace.Write("startup build=" + ConnectionTrace.Build);
            connectBluetooth = connect ?? BluetoothConnection.ConnectAsync;
            store = settingsStore; Config = store.Load(); L.Apply(Config.Language);
            dispatch = new DispatchForm { ShowRequested = ShowSettings };
            Shortcuts = new Hotkeys(); Shortcuts.Pressed += SelectSlot;
            audio = new AudioService(enumerate == null);
            readEndpoints = enumerate ?? (Func<System.Collections.Generic.List<Endpoint>>)audio.Enumerate;
            readDefault = defaultOutput ?? (() => audio.Default(1));
            selectOutput = select ?? (Action<string>)audio.Select;
            debounce = new System.Windows.Forms.Timer { Interval = 180 };
            debounce.Tick += delegate { debounce.Stop(); Refresh(); };
            audio.Changed += QueueRefresh;
            icon = NativeMethods.MakeIcon(); tray = new NotifyIcon { Icon = icon, Text = "Audio Finder", Visible = true };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSettings(); };
            Shortcuts.Apply(Config); Refresh();
            if (!String.IsNullOrEmpty(store.Warning)) LastError = store.Warning;
            if (Shortcuts.Errors.Count > 0) LastError = BusyShortcuts;
            if (!startHidden || LastError.Length > 0) ShowSettings();
        }
        private static string BusyShortcuts { get { return L.T("Há atalhos ocupados. Abra Editar para escolher outra combinação.", "Some shortcuts are in use. Open Edit to choose another combination."); } }
        // The settings window builds its texts once; reopen it in the new language after the current event finishes.
        private void RebuildSettings()
        {
            var old = settingsForm;
            if (old == null || old.IsDisposed || closing) return;
            dispatch.BeginInvoke((Action)delegate
            {
                if (closing || old.IsDisposed) return;
                var bounds = old.Bounds; var state = old.WindowState;
                settingsForm = new MainForm(this) { StartPosition = FormStartPosition.Manual, Bounds = bounds, WindowState = state };
                settingsForm.Show(); old.Close(); old.Dispose(); settingsForm.Activate();
            });
        }
        internal void QueueRefresh()
        {
            if (closing || Interlocked.Exchange(ref refreshQueued, 1) != 0) return;
            try { dispatch.BeginInvoke((Action)delegate { Interlocked.Exchange(ref refreshQueued, 0); if (!closing) { debounce.Stop(); debounce.Start(); } }); }
            catch (InvalidOperationException) { Interlocked.Exchange(ref refreshQueued, 0); }
        }
        internal void Refresh()
        {
            ConnectionTrace.Write("refresh begin");
            try
            {
                Devices = readEndpoints(); DefaultId = readDefault();
                var reconciled = Config.Copy();
                if (reconciled.Reconcile(Devices))
                {
                    // Commit only after persistence succeeds; a failed save must not lose the old association.
                    try { store.Save(reconciled); Config = reconciled; }
                    catch (Exception ex)
                    {
                        if (!(ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)) throw;
                        LastError = L.T("Não foi possível salvar a identidade dos dispositivos: ", "The device identity could not be saved: ") + ex.Message;
                    }
                }
            }
            catch (Exception ex) { LastError = L.T("Não foi possível consultar o áudio: ", "Audio devices could not be read: ") + ex.Message; Devices = new System.Collections.Generic.List<Endpoint>(); DefaultId = ""; }
            UpdateMenu(); if (Updated != null) Updated();
        }
        private void UpdateMenu()
        {
            var menu = new ContextMenuStrip { Renderer = new ThemeMenuRenderer(new Palette(Config.Theme)), ShowImageMargin = false };
            menu.Items.Add(L.T("Audio Finder · abrir", "Audio Finder · open"), null, delegate { ShowSettings(); }); menu.Items.Add(new ToolStripSeparator());
            for (int i = 0; i < 8; i++)
            {
                int index = i; var slot = Config.Slots[i]; var saved = Config.Devices.FirstOrDefault(d => d.Id == slot.DeviceId);
                if (saved == null) continue;
                bool active = Devices.Any(d => d.Id == slot.DeviceId && d.Active);
                var item = new ToolStripMenuItem((i + 1) + "  " + saved.Name + (active ? "" : L.T(" · desconectado", " · disconnected")) + "    " + slot.Shortcut, null, delegate { SelectSlot(index); }) { Checked = DefaultId == slot.DeviceId };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Atualizar dispositivos", "Refresh devices"), null, delegate { Refresh(); });
            menu.Items.Add(Config.Enabled ? L.T("Pausar atalhos", "Pause shortcuts") : L.T("Ativar atalhos", "Enable shortcuts"), null, delegate { var next = Config.Copy(); next.Enabled = !next.Enabled; string error; if (!Apply(next, out error)) Fail(error); });
            menu.Items.Add(L.T("Sair", "Exit"), null, delegate { ExitThread(); });
            var old = tray.ContextMenuStrip; tray.ContextMenuStrip = menu; if (old != null) old.Dispose();
        }
        internal bool Apply(Settings next, out string error)
        {
            error = "";
            // Reject collisions before persisting, and restore previous registrations on failure.
            bool registered = Shortcuts.Apply(next);
            if (!registered && (next.Enabled != Config.Enabled || Shortcuts.Errors.Keys.Any(i => next.Slots[i].Key != Config.Slots[i].Key || next.Slots[i].Modifiers != Config.Slots[i].Modifiers)))
            {
                error = String.Join("\n", Shortcuts.Errors.Select(p => L.F("Dispositivo {0}: {1}", "Device {0}: {1}", p.Key + 1, p.Value)));
                Shortcuts.Apply(Config); return false;
            }
            try { store.Save(next); }
            catch (Exception ex) { Shortcuts.Apply(Config); error = L.T("Não foi possível salvar: ", "Could not save: ") + ex.Message; return false; }
            CancelConnection();
            bool languageChanged = next.Language != Config.Language;
            Config = next; LastError = Shortcuts.Errors.Count > 0 ? BusyShortcuts : "";
            if (languageChanged) { L.Apply(Config.Language); RebuildSettings(); }
            if (!Config.ShowErrors && toast != null) { toast.Close(); toast = null; }
            UpdateMenu(); if (Updated != null) Updated(); return true;
        }
        internal async void SelectSlot(int index)
        {
            if (index < 0 || index > 7) return;
            int operation = ++operationSequence;
            ConnectionTrace.Write("operation=" + operation + " slot=" + (index + 1) + " begin context=" + (SynchronizationContext.Current == null ? "none" : SynchronizationContext.Current.GetType().Name));
            CancelConnection();
            Refresh();
            // COM calls and Updated subscribers can dispatch another selection while refreshing.
            if (closing || operation != operationSequence) return;
            var slot = Config.Slots[index]; var device = Config.Devices.FirstOrDefault(d => d.Id == slot.DeviceId);
            if (device == null) { Fail(L.F("O dispositivo {0} ainda não foi cadastrado.", "Device {0} has not been set up yet.", index + 1)); return; }
            var target = device.Copy();
            var cancellation = new CancellationTokenSource(); connectionCancellation = cancellation;
            bool attemptedConnection = false;
            OutputToast progress = null;
            try
            {
                var endpoint = BluetoothConnection.Resolve(target, Devices, true);
                if (endpoint == null)
                {
                    if (!target.Bluetooth) throw new InvalidOperationException(L.T("Esta saída não está conectada ao computador.", "This output is not connected to the computer."));
                    attemptedConnection = true;
                    progress = OutputToast.Connecting(index, Config, Config.Theme);
                    progress.CancelRequested += delegate { if (Object.ReferenceEquals(connectionCancellation, cancellation)) CancelConnection(); };
                    ShowToast(progress);
                    ConnectionTrace.Write("operation=" + operation + " awaiting-backend");
                    endpoint = await connectBluetooth(target, cancellation.Token);
                    ConnectionTrace.Write("operation=" + operation + " backend-completed");
                }
                if (closing || cancellation.IsCancellationRequested || !Object.ReferenceEquals(connectionCancellation, cancellation)) { ConnectionTrace.Write("operation=" + operation + " obsolete closing=" + closing + " canceled=" + cancellation.IsCancellationRequested); return; }
                Refresh();
                if (!IsCurrentOperation(operation, cancellation)) return;
                // Do not trust a driver request (or stale worker result) as proof of usable audio.
                var confirmed = BluetoothConnection.Resolve(target, Devices, true);
                if (endpoint == null || confirmed == null || endpoint.Id != confirmed.Id) throw new InvalidOperationException(L.T("O Windows ainda não confirmou esta saída como conectada.", "Windows has not confirmed this output as connected yet."));
                var current = Config.Devices.FirstOrDefault(d => d.Id == Config.Slots[index].DeviceId);
                if (current == null || Config.Devices.Any(d => d != current && String.Equals(d.Id, confirmed.Id, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException(L.T("Esta saída está associada a outro cadastro. Atualize os dispositivos antes de ativar.", "This output is linked to another entry. Refresh the devices before activating it."));
                ConnectionTrace.Write("operation=" + operation + " select-output");
                selectOutput(confirmed.Id); Refresh();
                if (!IsCurrentOperation(operation, cancellation)) return;
                if (!String.Equals(DefaultId, confirmed.Id, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(L.T("O Windows não confirmou esta saída como padrão.", "Windows did not confirm this output as the default."));
                LastError = "";
                ShowToast(attemptedConnection ? OutputToast.Connected(index, Config, Config.Theme) : OutputToast.Selection(index, Config, Devices, Config.Theme));
            }
            catch (Exception ex)
            {
                if (closing || cancellation.IsCancellationRequested || !Object.ReferenceEquals(connectionCancellation, cancellation)) { ConnectionTrace.Write("operation=" + operation + " obsolete closing=" + closing + " canceled=" + cancellation.IsCancellationRequested); return; }
                ConnectionTrace.Write("operation=" + operation + " failed type=" + ex.GetType().Name);
                LastError = device.Name + "\n" + (ex is OperationCanceledException ? L.T("A tentativa terminou sem confirmar a conexão. Tente novamente.", "The attempt ended without confirming the connection. Try again.") : ex is COMException ? L.T("O Windows não conseguiu ativar esta saída. Verifique a conexão.", "Windows could not activate this output. Check the connection.") : ex.Message);
                Refresh();
                if (!IsCurrentOperation(operation, cancellation)) return;
                ShowToast(OutputToast.DeviceFailure(index, Config, LastError, Config.Theme));
            }
            finally
            {
                if (Object.ReferenceEquals(connectionCancellation, cancellation))
                {
                    connectionCancellation = null;
                    // Cleanup owns only this request's progress, never the terminal result.
                    if (progress != null && Object.ReferenceEquals(toast, progress)) { toast = null; progress.Close(); progress.Dispose(); }
                }
                ConnectionTrace.Write("operation=" + operation + " finally current=" + Object.ReferenceEquals(connectionCancellation, cancellation));
                cancellation.Dispose();
            }
        }
        private bool IsCurrentOperation(int operation, CancellationTokenSource cancellation)
        {
            return !closing && operation == operationSequence && !cancellation.IsCancellationRequested && Object.ReferenceEquals(connectionCancellation, cancellation);
        }
        private void CancelConnection()
        {
            ConnectionTrace.Write("cancel current=" + (connectionCancellation != null));
            var cancellation = connectionCancellation; connectionCancellation = null;
            if (cancellation != null) cancellation.Cancel();
            if (toast != null && toast.IsConnecting) { var previous = toast; toast = null; previous.Close(); previous.Dispose(); }
        }
        internal void Fail(string message)
        {
            LastError = message; if (Updated != null) Updated();
            if (!Config.ShowErrors) return;
            ShowToast(OutputToast.Failure(message, Config.Theme));
        }
        private void ShowToast(OutputToast next)
        {
            ConnectionTrace.Write("toast request kind=" + next.Heading + " enabled=" + Config.ShowErrors);
            if (!Config.ShowErrors) { next.Dispose(); return; }
            var previous = toast; toast = null;
            if (previous != null) { previous.Close(); previous.Dispose(); }
            toast = next;
            next.FormClosed += delegate { if (Object.ReferenceEquals(toast, next)) toast = null; };
            next.Show();
            ConnectionTrace.Write("toast shown kind=" + next.Heading + " visible=" + next.Visible + " handle=" + next.Handle.ToInt64());
        }
        internal void ShowSettings()
        {
            if (closing) return;
            if (settingsForm == null || settingsForm.IsDisposed) { settingsForm = new MainForm(this); settingsForm.Show(); }
            else { settingsForm.WindowState = FormWindowState.Normal; settingsForm.Show(); settingsForm.Activate(); }
        }
        internal void BeginCapture() { Shortcuts.Clear(); }
        internal void EndCapture() { Shortcuts.Apply(Config); if (Updated != null) Updated(); }
        protected override void ExitThreadCore() { Dispose(); base.ExitThreadCore(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !closing)
            {
                closing = true; CancelConnection(); debounce.Stop(); debounce.Dispose(); audio.Dispose(); Shortcuts.Dispose();
                if (toast != null) toast.Dispose(); if (settingsForm != null) settingsForm.Dispose();
                tray.Visible = false; var menu = tray.ContextMenuStrip; tray.Dispose(); if (menu != null) menu.Dispose(); icon.Dispose(); dispatch.Dispose();
            }
            base.Dispose(disposing);
        }
    }
    internal static class Startup
    {
        private const string Key = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        internal static bool Enabled { get { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && key.GetValue("AudioFinder") != null; } }
        internal static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
            { if (enabled) key.SetValue("AudioFinder", "\"" + Application.ExecutablePath + "\" --tray"); else key.DeleteValue("AudioFinder", false); }
        }
    }
}
