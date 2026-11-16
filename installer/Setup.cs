using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

// Per-user installer without external tooling. The application payload is a ZIP embedded at build time.
// It never requests elevation and only deletes folders that carry its own installation marker.
namespace AudioFinderSetup
{
    internal static class Product
    {
        internal const string Name = "Audio Finder";
        internal const string Exe = "AudioFinder.exe";
        internal const string Uninstaller = "Uninstall.exe";
        internal const string Marker = "AudioFinder.install";
        internal const string Payload = "AudioFinder.Payload.zip";
        internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioFinder";
        internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string RunValue = "AudioFinder";
        internal const string Shortcut = "Audio Finder.lnk";
        internal const int RequiredFramework = 528040; // .NET Framework 4.8
        internal static string Version
        {
            get
            {
                var info = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyInformationalVersionAttribute));
                return info != null ? info.InformationalVersion : Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            }
        }
        internal static string LocalAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        internal static string DefaultDir { get { return Path.Combine(LocalAppData, "Programs", "AudioFinder"); } }
        internal static string DataDir { get { return Path.Combine(LocalAppData, "AudioFinder"); } }
        internal static string StartMenuShortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Shortcut); } }
        internal static string DesktopShortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Shortcut); } }
    }

    internal sealed class Options
    {
        internal bool Silent, Uninstall, NoIntegration, NoLaunch, RemoveData, Desktop, FromTemp;
        internal string Dir;
        internal int Parent;
        internal static Options Parse(string[] args)
        {
            var o = new Options();
            foreach (var raw in args)
            {
                string arg = raw.Trim(); string lower = arg.ToLowerInvariant();
                if (lower == "/silent" || lower == "/s") o.Silent = true;
                else if (lower == "/uninstall") o.Uninstall = true;
                else if (lower == "/nointegration") o.NoIntegration = true;
                else if (lower == "/nolaunch") o.NoLaunch = true;
                else if (lower == "/removedata") o.RemoveData = true;
                else if (lower == "/desktop") o.Desktop = true;
                else if (lower == "/fromtemp") o.FromTemp = true;
                else if (lower.StartsWith("/dir=")) o.Dir = arg.Substring(5).Trim('"');
                else if (lower.StartsWith("/parent=") && Int32.TryParse(arg.Substring(8), out o.Parent)) { }
                else throw new ArgumentException("Opção desconhecida: " + arg);
            }
            return o;
        }
        internal string ToArguments()
        {
            var list = new List<string> { "/uninstall" };
            if (Silent) list.Add("/silent"); if (NoIntegration) list.Add("/nointegration"); if (RemoveData) list.Add("/removedata");
            if (FromTemp) list.Add("/fromtemp"); if (!String.IsNullOrEmpty(Dir)) list.Add("/dir=" + Dir);
            if (Parent > 0) list.Add("/parent=" + Parent);
            return String.Join(" ", list.Select(Quote));
        }
        private static string Quote(string value) { return value.IndexOfAny(new[] { ' ', '\t' }) < 0 ? value : "\"" + value.Replace("\"", "") + "\""; }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Options options;
            try { options = Options.Parse(args); }
            catch (ArgumentException ex) { MessageBox.Show(ex.Message + "\n\nUso: /silent /dir=<pasta> /nolaunch /desktop /uninstall /removedata", Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1; }
            try
            {
                if (options.Uninstall) return RunUninstall(options);
                if (options.Silent) { Installer.Install(Resolve(options.Dir), options, () => true); return 0; }
                using (var form = new SetupForm(options, false)) { Application.Run(form); return form.ExitCode; }
            }
            catch (Exception ex)
            {
                if (!options.Silent) MessageBox.Show(ex.Message, Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                else Log(ex);
                return 1;
            }
        }
        // Silent runs have no window: failures go to %TEMP%\AudioFinderSetup.log (paths and messages only).
        internal static void Log(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "AudioFinderSetup.log"), DateTime.Now.ToString("s") + " " + Product.Version + " " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine, Encoding.UTF8); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        internal static string Resolve(string dir) { return Path.GetFullPath(String.IsNullOrWhiteSpace(dir) ? Product.DefaultDir : Environment.ExpandEnvironmentVariables(dir)).TrimEnd('\\'); }
        private static int RunUninstall(Options options)
        {
            string self = Application.ExecutablePath;
            if (String.IsNullOrEmpty(options.Dir)) options.Dir = Path.GetDirectoryName(self);
            string dir = Resolve(options.Dir);
            // A running executable cannot delete itself: continue from a temporary copy.
            if (!options.FromTemp && Installer.IsInside(self, dir))
            {
                string temp = Path.Combine(Path.GetTempPath(), "AudioFinderUninstall-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);
                string copy = Path.Combine(temp, Product.Uninstaller);
                File.Copy(self, copy);
                options.FromTemp = true; options.Dir = dir; options.Parent = Process.GetCurrentProcess().Id;
                Process.Start(new ProcessStartInfo(copy, options.ToArguments()) { UseShellExecute = false, WorkingDirectory = temp });
                return 0;
            }
            if (options.Parent > 0)
            {
                try { using (var parent = Process.GetProcessById(options.Parent)) parent.WaitForExit(15000); }
                catch (ArgumentException) { } catch (InvalidOperationException) { }
            }
            try
            {
                if (options.Silent) { Installer.Uninstall(dir, options, () => true); return 0; }
                using (var form = new SetupForm(options, true)) { Application.Run(form); return form.ExitCode; }
            }
            finally { if (options.FromTemp) Installer.ScheduleSelfCleanup(Path.GetDirectoryName(self)); }
        }
    }

    internal static class Installer
    {
        internal static bool IsInside(string path, string dir)
        {
            try
            {
                string full = Path.GetFullPath(path), root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }
        // Command lines such as "C:\...\AudioFinder.exe" --tray: keep only the executable path.
        internal static string ExecutableOf(string command)
        {
            string value = (command ?? "").Trim();
            if (value.StartsWith("\"")) { int end = value.IndexOf('"', 1); return end > 0 ? value.Substring(1, end - 1) : value.Trim('"'); }
            int exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe > 0 ? value.Substring(0, exe + 4) : value;
        }
        internal static bool IsInstallation(string dir) { return File.Exists(Path.Combine(dir, Product.Marker)) && File.Exists(Path.Combine(dir, Product.Exe)); }
        internal static string InstalledVersion()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(Product.UninstallKey)) return key == null ? null : key.GetValue("DisplayVersion") as string;
        }
        internal static string FrameworkProblem()
        {
            if (!Environment.Is64BitOperatingSystem) return "O Audio Finder requer Windows 10 ou 11 de 64 bits.";
            using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
            {
                object release = key == null ? null : key.GetValue("Release");
                return release is int && (int)release >= Product.RequiredFramework ? null : "O Audio Finder requer o .NET Framework 4.8, incluído no Windows 10 (1903 ou mais recente) e no Windows 11.";
            }
        }
        internal static List<Process> RunningCopies(string dir)
        {
            var result = new List<Process>(); string exe = Path.Combine(dir, Product.Exe);
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Product.Exe)))
            {
                try { if (String.Equals(Path.GetFullPath(process.MainModule.FileName), exe, StringComparison.OrdinalIgnoreCase)) { result.Add(process); continue; } }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
                process.Dispose();
            }
            return result;
        }
        private static void StopCopies(string dir, Func<bool> confirmClose)
        {
            var running = RunningCopies(dir);
            if (running.Count == 0) return;
            if (!confirmClose()) throw new OperationCanceledException("O Audio Finder continua aberto.");
            foreach (var process in running) using (process) { try { process.Kill(); process.WaitForExit(5000); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
            if (RunningCopies(dir).Count > 0) throw new IOException("Não foi possível encerrar o Audio Finder. Use Sair no ícone da bandeja e tente novamente.");
        }

        internal static void Install(string dir, Options options, Func<bool> confirmClose)
        {
            string framework = FrameworkProblem(); if (framework != null) throw new InvalidOperationException(framework);
            if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any() && !IsInstallation(dir))
                throw new InvalidOperationException("A pasta escolhida já contém arquivos que não são de uma instalação do Audio Finder:\n" + dir);
            StopCopies(dir, confirmClose);
            string parent = Path.GetDirectoryName(dir); Directory.CreateDirectory(parent);
            string staging = dir + ".new-" + Guid.NewGuid().ToString("N"), previous = dir + ".old-" + Guid.NewGuid().ToString("N");
            try
            {
                Extract(staging);
                File.Copy(Application.ExecutablePath, Path.Combine(staging, Product.Uninstaller), true);
                File.WriteAllText(Path.Combine(staging, Product.Marker), Product.Name + " " + Product.Version + Environment.NewLine, Encoding.UTF8);
                // Swap whole folders so a failure never leaves a half-updated installation.
                if (Directory.Exists(dir)) Directory.Move(dir, previous);
                try { Directory.Move(staging, dir); }
                catch { if (Directory.Exists(previous) && !Directory.Exists(dir)) Directory.Move(previous, dir); throw; }
            }
            finally
            {
                TryDelete(staging);
                TryDelete(previous);
            }
            if (!options.NoIntegration)
            {
                Register(dir);
                ShellLink.Create(Product.StartMenuShortcut, Path.Combine(dir, Product.Exe), dir, "Alterna a saída de áudio por atalhos globais");
                if (options.Desktop) ShellLink.Create(Product.DesktopShortcut, Path.Combine(dir, Product.Exe), dir, "Alterna a saída de áudio por atalhos globais");
            }
            if (!options.NoLaunch && !options.Silent) Process.Start(new ProcessStartInfo(Path.Combine(dir, Product.Exe)) { UseShellExecute = true, WorkingDirectory = dir });
        }
        private static void Extract(string target)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Product.Payload))
            {
                if (stream == null) throw new InvalidDataException("O pacote de instalação está incompleto.");
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    string root = Path.GetFullPath(target).TrimEnd('\\') + "\\";
                    Directory.CreateDirectory(root);
                    foreach (var entry in archive.Entries)
                    {
                        string name = entry.FullName.Replace('/', '\\');
                        string path = Path.GetFullPath(Path.Combine(root, name));
                        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Entrada inválida no pacote: " + entry.FullName);
                        if (name.EndsWith("\\")) { Directory.CreateDirectory(path); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        entry.ExtractToFile(path, true);
                    }
                }
            }
            if (!File.Exists(Path.Combine(target, Product.Exe))) throw new InvalidDataException("O pacote de instalação não contém o aplicativo.");
        }
        private static void Register(string dir)
        {
            long size = new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024;
            string uninstaller = Path.Combine(dir, Product.Uninstaller);
            using (var key = Registry.CurrentUser.CreateSubKey(Product.UninstallKey))
            {
                key.SetValue("DisplayName", Product.Name);
                key.SetValue("DisplayVersion", Product.Version);
                key.SetValue("Publisher", Product.Name);
                key.SetValue("DisplayIcon", Path.Combine(dir, Product.Exe) + ",0");
                key.SetValue("InstallLocation", dir);
                key.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                key.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /silent");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, size), RegistryValueKind.DWord);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
        }

        internal static void Uninstall(string dir, Options options, Func<bool> confirmClose)
        {
            if (!IsInstallation(dir)) throw new InvalidOperationException("Nenhuma instalação do Audio Finder foi encontrada em:\n" + dir);
            StopCopies(dir, confirmClose);
            if (!options.NoIntegration)
            {
                foreach (var link in new[] { Product.StartMenuShortcut, Product.DesktopShortcut })
                    if (File.Exists(link)) { string target = ShellLink.Target(link); if (target == null || IsInside(target, dir)) File.Delete(link); }
                using (var run = Registry.CurrentUser.OpenSubKey(Product.RunKey, true))
                {
                    string value = run == null ? null : run.GetValue(Product.RunValue) as string;
                    if (value != null && IsInside(ExecutableOf(value), dir)) run.DeleteValue(Product.RunValue, false);
                }
                using (var key = Registry.CurrentUser.OpenSubKey(Product.UninstallKey))
                {
                    string location = key == null ? null : key.GetValue("InstallLocation") as string;
                    if (location != null && String.Equals(location.Trim().TrimEnd('\\'), dir, StringComparison.OrdinalIgnoreCase)) Registry.CurrentUser.DeleteSubKeyTree(Product.UninstallKey, false);
                }
            }
            Directory.Delete(dir, true);
            if (options.RemoveData && Directory.Exists(Product.DataDir)) Directory.Delete(Product.DataDir, true);
        }
        private static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        // The temporary uninstaller removes its own folder shortly after it exits.
        internal static void ScheduleSelfCleanup(string folder)
        {
            if (!Path.GetFileName(folder).StartsWith("AudioFinderUninstall-", StringComparison.Ordinal)) return;
            string command = "/d /c ping 127.0.0.1 -n 4 > nul & rmdir /s /q \"" + folder + "\"";
            try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), command) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetTempPath() }); }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }

    internal sealed class SetupForm : Form
    {
        internal int ExitCode = 2;
        private readonly Options options;
        private readonly bool uninstall;
        private readonly string dir;
        private readonly CheckBox first, second;
        private readonly Label status;
        private readonly Button primary, cancel;
        private bool finished;
        internal SetupForm(Options value, bool removing)
        {
            options = value; uninstall = removing; dir = Program.Resolve(options.Dir);
            Text = (uninstall ? "Desinstalar " : "Instalar ") + Product.Name;
            Font = new Font("Segoe UI", 9.5f); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 300);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (ArgumentException) { }
            var title = new Label { Text = uninstall ? "Desinstalar o " + Product.Name : Product.Name + " " + Product.Version, Font = new Font("Segoe UI Semibold", 15f), AutoSize = false };
            title.SetBounds(24, 18, 472, 34);
            string installed = Installer.InstalledVersion();
            string detail;
            if (uninstall) detail = "O aplicativo será removido de:\n" + dir;
            else
            {
                detail = "Instalação só para o seu usuário, sem pedir administrador, em:\n" + dir;
                Version current, next;
                if (installed != null && System.Version.TryParse(installed, out current) && System.Version.TryParse(Product.Version, out next))
                    detail = (next < current ? "Atenção: a versão instalada (" + installed + ") é mais nova que esta. " : next == current ? "Esta versão já está instalada e será reinstalada. " : "Atualiza a versão " + installed + ". ") + "Configurações e atalhos são mantidos.\n" + dir;
            }
            var info = new Label { Text = detail, AutoSize = false }; info.SetBounds(24, 62, 472, 66);
            first = new CheckBox { AutoSize = false }; first.SetBounds(24, 136, 472, 26);
            second = new CheckBox { AutoSize = false }; second.SetBounds(24, 166, 472, 26);
            if (uninstall) { first.Text = "Remover também configurações, histórico e imagens salvas"; second.Visible = false; }
            else { first.Text = "Criar atalho na área de trabalho"; second.Text = "Abrir o Audio Finder ao concluir"; second.Checked = true; }
            status = new Label { AutoSize = false, ForeColor = SystemColors.GrayText }; status.SetBounds(24, 202, 472, 40);
            primary = new Button { Text = uninstall ? "Desinstalar" : "Instalar" }; primary.SetBounds(290, 252, 100, 32);
            cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel }; cancel.SetBounds(398, 252, 100, 32);
            primary.Click += delegate { if (finished) { Close(); return; } Run(); };
            cancel.Click += delegate { Close(); };
            AcceptButton = primary; CancelButton = cancel;
            Controls.AddRange(new Control[] { title, info, first, second, status, primary, cancel });
            string problem = uninstall ? null : Installer.FrameworkProblem();
            if (problem != null) { status.Text = problem; primary.Enabled = false; }
        }
        private bool ConfirmClose()
        {
            return MessageBox.Show(this, "O Audio Finder está aberto. Ele será fechado para continuar; suas configurações já estão salvas.", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK;
        }
        private void Run()
        {
            primary.Enabled = cancel.Enabled = first.Enabled = second.Enabled = false; UseWaitCursor = true;
            status.Text = uninstall ? "Removendo…" : "Instalando…"; status.Refresh();
            try
            {
                if (uninstall) { options.RemoveData = first.Checked; Installer.Uninstall(dir, options, ConfirmClose); status.Text = "O Audio Finder foi removido."; }
                else { options.Desktop = first.Checked; options.NoLaunch = !second.Checked; Installer.Install(dir, options, ConfirmClose); status.Text = "Instalação concluída. O Audio Finder fica no Menu Iniciar e no ícone perto do relógio."; }
                ExitCode = 0; finished = true; primary.Text = "Concluir"; primary.Enabled = true; cancel.Visible = false;
            }
            catch (OperationCanceledException ex) { status.Text = ex.Message; primary.Enabled = cancel.Enabled = first.Enabled = second.Enabled = true; }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is InvalidDataException || ex is COMException)) throw;
                ExitCode = 1; status.ForeColor = Color.Firebrick; status.Text = "Não foi possível concluir: " + ex.Message; cancel.Enabled = true; cancel.Text = "Fechar";
            }
            finally { UseWaitCursor = false; }
        }
    }

    internal static class ShellLink
    {
        internal static void Create(string path, string target, string workingDirectory, string description)
        {
            var link = (IShellLinkW)new CShellLink();
            try
            {
                link.SetPath(target); link.SetWorkingDirectory(workingDirectory); link.SetDescription(description); link.SetIconLocation(target, 0);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                ((IPersistFile)link).Save(path, true);
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }
        internal static string Target(string path)
        {
            var link = (IShellLinkW)new CShellLink();
            try
            {
                ((IPersistFile)link).Load(path, 0);
                var buffer = new StringBuilder(1024); link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0x4);
                return buffer.Length == 0 ? null : buffer.ToString();
            }
            catch (COMException) { return null; }
            finally { Marshal.FinalReleaseComObject(link); }
        }
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class CShellLink { }
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, uint flags);
            void GetIDList(out IntPtr idList);
            void SetIDList(IntPtr idList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int show);
            void SetShowCmd(int show);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int icon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int icon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr hwnd, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }
    }
}
