using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AudioFinder
{
    internal static class Tests
    {
        private static int count;
        private static readonly List<string> log = new List<string>();
        private static void Check(bool condition, string description) { if (!condition) throw new Exception("FAIL: " + description); count++; log.Add("PASS: " + description); Console.WriteLine(log.Last()); }
        private static void Pump(int milliseconds) { var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); } }
        private static void Snapshot(Form form, string path)
        {
            form.Show(); Pump(250);
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(path); }
        }
        [STAThread] private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool safe = args.Any(a => String.Equals(a, "--safe", StringComparison.OrdinalIgnoreCase));
            string output = args.Length > 0 ? args[0] : Path.GetFullPath("artifacts"); Directory.CreateDirectory(output);
            string temporary = Path.Combine(output, "test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
            try
            {
                L.Apply(Language.Portuguese);
                var config = new Settings(); Check(config.Slots.Count == 8, "Oito posições fixas");
                config.Devices.Add(new SavedDevice { Id = "offline-bluetooth", Name = "Fone Bluetooth de teste", Bluetooth = true });
                config.Devices.Add(new SavedDevice { Id = "wired", Name = "Alto-falantes", Bluetooth = false });
                config.Slots[7] = new Slot { DeviceId = "offline-bluetooth", Key = (uint)Keys.F8, Modifiers = 3 };
                config.Slots[0] = new Slot { DeviceId = "wired", Key = (uint)Keys.F1, Modifiers = 3 };
                var store = new SettingsStore(Path.Combine(temporary, "settings.xml")); store.Save(config);
                var restored = store.Load(); Check(restored.Devices.Count == 2 && restored.Slots[7].DeviceId == "offline-bluetooth" && restored.Slots[7].Key == (uint)Keys.F8, "Histórico e atalho 8 persistem sem dispositivo conectado");
                var removed = restored.Copy(); removed.Forget(new[] { "offline-bluetooth" });
                Check(removed.Slots[7].Key == 0 && removed.Slots[7].DeviceId == "" && removed.Devices.Count == 1 && removed.Slots[0].Key != 0, "Limpeza Bluetooth libera posição/atalho e preserva saída cabeada");
                Check(restored.Devices.Count == 2, "Edição usa cópia independente");
                restored.ShowErrors = false; restored.Theme = Appearance.Light; store.Save(restored);
                Check(!store.Load().ShowErrors && store.Load().Theme == Appearance.Light, "Toggle de avisos e tema persistentes");
                File.WriteAllText(store.FilePath, "broken");
                Check(store.Load().Devices.Count == 2 && store.Warning != null && File.ReadAllText(store.FilePath) == "broken", "Recuperação por backup preserva arquivo corrompido");
                Check(!HotkeyText.Valid((uint)Keys.F12, 0) && !HotkeyText.Valid((uint)Keys.ControlKey, 0) && !HotkeyText.Valid((uint)Keys.F1, 8), "Atalhos inválidos e reservados rejeitados");
                using (var first = new Hotkeys()) using (var second = new Hotkeys())
                {
                    var shortcutSettings = config.Copy(); foreach (var slot in shortcutSettings.Slots) slot.Key = 0; shortcutSettings.Slots[0].Key = (uint)Keys.F23; shortcutSettings.Slots[0].Modifiers = 0;
                    Check(first.Apply(shortcutSettings), "Registro nativo de atalho global");
                    Check(!second.Apply(shortcutSettings) && second.Errors.ContainsKey(0), "Conflito real entre registros detectado");
                    int received = 0; first.Pressed += delegate(int index) { if (index == 0) received++; };
                    PostMessage(first.Handle, 0x312, new IntPtr(1), new IntPtr((int)Keys.F23 << 16)); Pump(200);
                    Check(received == 1, "Mensagem nativa WM_HOTKEY aciona posição com janela oculta");
                    first.Clear(); Check(second.Apply(shortcutSettings), "Atalho liberado após remoção");
                }
                List<Endpoint> endpoints; string[] defaults;
                using (var audio = new AudioService(true))
                {
                    endpoints = audio.Enumerate(); defaults = Enumerable.Range(0, 3).Select(audio.Default).ToArray();
                    Check(endpoints.Count > 0, "Enumeração Core Audio no PC real");
                    File.WriteAllLines(Path.Combine(output, "devices.txt"), endpoints.Select(d => d.Name + " | state=" + d.State + " | bluetooth=" + d.Bluetooth + " | " + d.Id));
                    bool failed = false; try { audio.Select("missing-device-test"); } catch (InvalidOperationException) { failed = true; }
                    Check(failed && Enumerable.Range(0, 3).Select(audio.Default).SequenceEqual(defaults), "Dispositivo inexistente falha sem mudar saída");
                    if (!safe && !String.IsNullOrEmpty(defaults[1]))
                    {
                        try { audio.Select(defaults[1]); Check(Enumerable.Range(0, 3).All(role => audio.Default(role) == defaults[1]), "Seleção nativa confirmada nas três funções de áudio"); }
                        finally { audio.RestoreDefaults(defaults); }
                        Check(Enumerable.Range(0, 3).Select(audio.Default).SequenceEqual(defaults), "Saídas originais restauradas após teste");
                    }
                }
                var appStore = new SettingsStore(Path.Combine(temporary, "app.xml")); var demo = new Settings { Language = Language.Portuguese };
                var available = endpoints.FirstOrDefault(d => d.Active && d.Id == defaults[1]) ?? endpoints.FirstOrDefault(d => d.Active);
                if (available != null) { demo.Devices.Add(new SavedDevice { Id = available.Id, Name = available.Name, Bluetooth = available.Bluetooth }); demo.Slots[0].DeviceId = available.Id; }
                demo.Devices.Add(config.Devices[0]); demo.Slots[1].DeviceId = "offline-bluetooth";
                appStore.Save(demo);
                var toastSettings = new Settings();
                toastSettings.Devices.AddRange(new[] {
                    new SavedDevice { Id = "duplicate", Name = "Nome salvo duplicado", IconPreset = "kuba-disco" },
                    new SavedDevice { Id = "active-b", Name = "Nome salvo B", IconPreset = "airpods-pro" },
                    new SavedDevice { Id = "offline", Name = "Saída offline salva", IconPreset = "sony-pulse" },
                    new SavedDevice { Id = "active-d", Name = "Nome salvo D", IconPreset = "edifier-monitors" },
                    new SavedDevice { Id = "active-e", Name = "Nome salvo E", IconPreset = "edifier-w820nb" }
                });
                toastSettings.Slots[0].DeviceId = "duplicate";
                toastSettings.Slots[1].DeviceId = "active-b";
                toastSettings.Slots[3].DeviceId = "offline";
                toastSettings.Slots[4].DeviceId = "duplicate";
                toastSettings.Slots[6].DeviceId = "active-d";
                toastSettings.Slots[7].DeviceId = "active-e";
                using (var preview = OutputToast.Selection(4, toastSettings, new[] {
                    new Endpoint { Id = "duplicate", Name = "Nome atual duplicado", State = 1 },
                    new Endpoint { Id = "active-b", Name = "Nome atual B", State = 1 },
                    new Endpoint { Id = "active-d", Name = "Monitor", State = 1 },
                    new Endpoint { Id = "active-e", Name = "Saída HDMI com um nome deliberadamente longo para validar truncamento", State = 1 },
                    new Endpoint { Id = "not-configured", Name = "Saída do Windows não cadastrada", State = 1 }
                }, Appearance.Dark))
                {
                    var rows = preview.Controls.Cast<Control>().SelectMany(Descendants).OfType<OutputCard>().Where(l => l.AccessibleName != null && l.AccessibleName.StartsWith("Posição ")).ToList();
                    Check(preview.Heading == "Saída selecionada" && preview.SelectedId == "duplicate" && preview.SelectedPosition == 5, "Aviso de sucesso identifica a posição selecionada");
                    Check(preview.ListedPositions.SequenceEqual(new[] { 1, 2, 4, 5, 7, 8 }), "Aviso ordena slots ocupados e preserva lacunas na numeração");
                    Check(preview.ListedEndpointIds.SequenceEqual(new[] { "duplicate", "active-b", "offline", "duplicate", "active-d", "active-e" }), "Aviso lista somente slots configurados e preserva dispositivos duplicados");
                    Check(preview.ListedNames.SequenceEqual(new[] { "Nome atual duplicado", "Nome atual B", "Saída offline salva", "Nome atual duplicado", "Monitor", "Saída HDMI com um nome deliberadamente longo para validar truncamento" }), "Aviso usa nome atual com fallback para o cadastro salvo");
                    Check(!preview.ListedEndpointIds.Contains("not-configured") && rows.All(l => !l.AccessibleName.Contains("não cadastrada")), "Saídas ativas do Windows não cadastradas ficam fora do aviso");
                    Check(rows.Count(l => l.Selected) == 1 && rows.Single(l => l.Selected).AccessibleName.Contains("Posição 5"), "Destaque se aplica somente à posição acionada, mesmo com DeviceId duplicado");
                    Check(rows.Count == 6 && rows.Any(l => l.AccessibleName.Contains("Posição 4")), "Slot offline permanece na barra numerada");
                    preview.Show(); Pump(50);
                    var selectedCard = rows.Single(l => l.Selected);
                    Check(rows.All(l => l.Selected || (l.Width < selectedCard.Width && l.Height < selectedCard.Height)), "Selecionado cresce e se eleva em relação aos demais");
                    Check(rows.All(l => l.Left >= 0 && l.Right <= l.Parent.Width), "Seis cartões cabem na barra sem corte");
                    preview.Width = 640; Pump(50);
                    Check(rows.All(l => l.Right <= l.Parent.Width), "Barra se adapta a largura reduzida");
                    Check(preview.Region != null && !preview.Region.IsVisible(0, 0) && !preview.Region.IsVisible(preview.Width - 1, preview.Height - 1) && preview.Region.IsVisible(preview.Width / 2, preview.Height / 2), "Janela da barra acompanha resize com cantos recortados e centro visível");
                    preview.Width = 800; Pump(50);
                    Snapshot(preview, Path.Combine(output, "audio-finder-toast.png"));
                }
                Check(DeviceVisuals.Keys.Take(7).SequenceEqual(new[] { "generic", "airpods-pro", "airpods", "edifier-monitors", "kuba-disco", "sony-pulse", "edifier-w820nb" }), "Renomear modelos preserva chaves e ordem dos cadastros existentes");
                Check(DeviceVisuals.Names.SequenceEqual(new[] { "Ícone padrão", "Fone In-Ear", "Fone Earbud", "Monitor de Áudio", "Fone Premium", "Fone Gamer", "Fone Bluetooth OverEar", "Monitor/Tv", "Fone Esportivo", "Fone Esportivo de Condução Óssea" }) && DeviceVisuals.Keys.Skip(7).SequenceEqual(new[] { "monitor-tv", "kz-az09pro", "shokz-openrun-pro-2" }), "Catálogo preserva modelos existentes e inclui os dois fones esportivos");
                foreach (Appearance toastTheme in new[] { Appearance.Dark, Appearance.Light }) using (var failure = OutputToast.Failure("Saída de teste indisponível", toastTheme)) {
                    failure.Show(); Pump(30);
                    Check(failure.Region != null && !failure.Region.IsVisible(0, 0) && !failure.Region.IsVisible(failure.Width / 2, failure.Height - 1) && failure.Region.IsVisible(failure.Width / 2, failure.Height / 2), "Aviso de erro recorta cantos e margem externa: " + toastTheme);
                }
                var connectionSettings = new Settings();
                connectionSettings.Devices.Add(new SavedDevice { Id = "connection-ui-only", Name = "Fone de teste", Bluetooth = true, IconPreset = "kuba-disco", IconPath = "missing.png" }); connectionSettings.Slots[2].DeviceId = "connection-ui-only";
                using (var progress = OutputToast.Connecting(2, connectionSettings, Appearance.Dark)) {
                    progress.Show(); Pump(80);
                    var animation = progress.Controls.Cast<Control>().SelectMany(Descendants).OfType<ConnectionVisual>().Single();
                    int invalidations = 0; animation.Invalidated += delegate { invalidations++; };
                    byte[] first, second;
                    using (var bitmap = new Bitmap(animation.Width, animation.Height)) using (var bytes = new MemoryStream()) { animation.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(bytes, System.Drawing.Imaging.ImageFormat.Png); first = bytes.ToArray(); }
                    Pump(180);
                    using (var bitmap = new Bitmap(animation.Width, animation.Height)) using (var bytes = new MemoryStream()) { animation.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(bytes, System.Drawing.Imaging.ImageFormat.Png); second = bytes.ToArray(); }
                    Check(!first.SequenceEqual(second) && invalidations >= 2, "Conexão redesenha movimento temporal real sem bloquear eventos");
                    Check(animation.Controls.Cast<Control>().All(c => c.Right <= animation.Width) && progress.Controls.Cast<Control>().SelectMany(Descendants).OfType<SoftButton>().Single().Right <= progress.ClientSize.Width, "Ícone e botão cancelar cabem em DPI ampliado");
                    Pump(OutputToast.Duration);
                    Check(!progress.IsDisposed && progress.Visible && progress.SelectedPosition == 3 && progress.SelectedId == "connection-ui-only", "Progresso mantém posição e fica visível além do timer de avisos comuns");
                    progress.Hide(); Pump(30); int paused = invalidations; Pump(120); Check(invalidations == paused, "Animação para enquanto aviso está oculto");
                    progress.Show(); bool cancelled = false; progress.CancelRequested += delegate { cancelled = true; progress.Close(); };
                    var cancelButton = progress.Controls.Cast<Control>().SelectMany(Descendants).OfType<SoftButton>().Single(); cancelButton.PerformClick(); Pump(90);
                    Check(cancelled && progress.IsDisposed, "Cancelar notifica controlador e encerra aviso e timer");
                }
                using (var deviceError = OutputToast.DeviceFailure(2, connectionSettings, "Detalhe completo da falha", Appearance.Light)) {
                    Check(deviceError.SelectedPosition == 3 && deviceError.SelectedId == "connection-ui-only" && deviceError.AccessibleDescription.Contains("Detalhe completo"), "Falha iconográfica mantém identidade, posição e detalhe acessível");
                }
                var visualSettings = new Settings();
                visualSettings.Devices.Add(new SavedDevice { Id = "visual", Name = "Imagem de teste", IconPreset = "airpods", IconPath = "missing-image.png" });
                var visualStore = new SettingsStore(Path.Combine(output, "visual-settings.xml")); visualStore.Save(visualSettings);
                var visualCopy = visualSettings.Copy(); visualCopy.Devices[0].IconPreset = "sony-pulse";
                Check(visualStore.Load().Devices[0].IconPreset == "airpods" && visualStore.Load().Devices[0].IconPath == "missing-image.png" && visualSettings.Devices[0].IconPreset == "airpods", "Preset e PNG persistem e edição visual usa cópia independente");
                foreach (var model in DeviceVisuals.Keys.Skip(1)) using (var image = DeviceVisuals.Load(model, "missing-image.png")) Check(image != null, "PNG ausente usa preset: " + model);
                using (var fallback = new DeviceArtwork()) { fallback.SetImage("invalid-model", "missing-image.png"); using (var bitmap = new Bitmap(64, 64)) { fallback.Size = bitmap.Size; fallback.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); } }
                Check(true, "Modelo inválido e arquivo ausente desenham ícone padrão sem falha");
                var listed = new[] {
                    new Endpoint { Id = "active-new", Name = "Novo ativo", State = 1 },
                    new Endpoint { Id = "active-saved", Name = "Salvo ativo", State = 1 },
                    new Endpoint { Id = "active-saved", Name = "Salvo ativo repetido", State = 1 },
                    new Endpoint { Id = "other-offline", Name = "Fone de outro atalho", State = 8, Bluetooth = true },
                    new Endpoint { Id = "own-offline", Name = "Nome atual do Windows", State = 8, Bluetooth = true }
                };
                var known = new[] {
                    new SavedDevice { Id = "active-saved", Name = "Salvo ativo" },
                    new SavedDevice { Id = "other-offline", Name = "Fone de outro atalho", Bluetooth = true },
                    new SavedDevice { Id = "own-offline", Name = "Fone do atalho", Bluetooth = true },
                    new SavedDevice { Id = "absent", Name = "Fora do Windows" }
                };
                var fresh = DeviceChoice.Build(listed, known, new[] { "" });
                Check(fresh.Select(c => c.Endpoint.Id).SequenceEqual(new[] { "active-new", "active-saved" }), "Nova configuração lista somente saídas ativas e oculta indisponíveis de outros atalhos");
                Check(!fresh[0].Saved && fresh[1].Saved && fresh[1].Endpoint.Name == "Salvo ativo", "Saída ativa já cadastrada aparece uma única vez e marcada como salva");
                var own = DeviceChoice.Build(listed, known, new[] { "own-offline" });
                Check(own.Select(c => c.Endpoint.Id).SequenceEqual(new[] { "active-new", "active-saved", "own-offline" }) && !own[2].Endpoint.Active && own[2].Saved && own[2].Endpoint.Bluetooth && own[2].Endpoint.Name == "Fone do atalho", "Dispositivo indisponível do próprio atalho continua listado com os dados do cadastro");
                Check(DeviceChoice.Build(listed, known, new[] { "absent" }).Last().Endpoint.Id == "absent", "Dispositivo do atalho ausente do Windows continua listado");
                var droppedNew = DeviceChoice.Build(new[] { new Endpoint { Id = "active-new", Name = "Novo ativo", State = 8 } }, known, new[] { "", "active-new" });
                Check(droppedNew.Count == 1 && !droppedNew[0].Saved && !droppedNew[0].Endpoint.Active, "Seleção nova que desconecta continua visível até ser trocada");
                string legacyPath = Path.Combine(temporary, "legacy.xml");
                File.WriteAllText(legacyPath, "<?xml version=\"1.0\"?><Settings><Theme>Light</Theme><ShowErrors>true</ShowErrors><Enabled>true</Enabled></Settings>");
                Check(new SettingsStore(legacyPath).Load().Language == Language.Auto, "Configuração antiga sem idioma usa Automático");
                var originalCulture = Thread.CurrentThread.CurrentUICulture;
                try
                {
                    Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("en-US"); L.Apply(Language.Auto); bool englishWindows = L.English;
                    Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("pt-BR"); L.Apply(Language.Auto); bool portugueseWindows = !L.English;
                    L.Apply(Language.English); bool forcedEnglish = L.English;
                    Check(englishWindows && portugueseWindows && forcedEnglish, "Automático segue o Windows e a escolha explícita prevalece");
                }
                finally { Thread.CurrentThread.CurrentUICulture = originalCulture; L.Apply(Language.Portuguese); }
                var liveEndpoints = new List<Endpoint> { new Endpoint { Id = "live-other", Name = "Fone de outro atalho", State = 8, Bluetooth = true } };
                var liveSettings = new Settings { Language = Language.Portuguese };
                liveSettings.Devices.Add(new SavedDevice { Id = "live-other", Name = "Fone de outro atalho", Bluetooth = true });
                liveSettings.Devices.Add(new SavedDevice { Id = "live-own", Name = "Fone do atalho", Bluetooth = true, IconPreset = "kz-az09pro" });
                liveSettings.Slots[0].DeviceId = "live-other"; liveSettings.Slots[2].DeviceId = "live-own";
                var liveStore = new SettingsStore(Path.Combine(temporary, "live.xml")); liveStore.Save(liveSettings);
                using (var liveApp = new TrayApp(liveStore, true, null, () => new List<Endpoint>(liveEndpoints), () => "", delegate(string id) { }))
                {
                    liveApp.BeginCapture();
                    using (var editor = new SlotEditor(liveApp, 4))
                    {
                        var combo = editor.Controls.OfType<ComboBox>().Single(c => c.AccessibleName == "Saída de áudio");
                        Check(combo.Items.Count == 0 && editor.Status == SlotEditor.NoOutputMessage, "Sem saída ativa: lista vazia e aviso para ligar o dispositivo");
                        liveEndpoints.Add(new Endpoint { Id = "live-new", Name = "Fone recém-conectado", State = 1, Bluetooth = true }); liveApp.Refresh();
                        Check(combo.Items.Count == 1 && ((DeviceChoice)combo.SelectedItem).Endpoint.Id == "live-new" && editor.Status == "", "Lista atualiza com o editor aberto e oferece a saída que conectou");
                    }
                    using (var editor = new SlotEditor(liveApp, 2))
                    {
                        var combo = editor.Controls.OfType<ComboBox>().Single(c => c.AccessibleName == "Saída de áudio");
                        var picker = editor.Controls.OfType<IconPicker>().Single();
                        var ids = combo.Items.Cast<DeviceChoice>().Select(c => c.Endpoint.Id).ToList();
                        Check(ids.SequenceEqual(new[] { "live-new", "live-own" }) && ((DeviceChoice)combo.SelectedItem).Endpoint.Id == "live-own" && editor.Status == SlotEditor.KeptOfflineMessage, "Atalho configurado mantém o dispositivo indisponível selecionado e avisa");
                        Check(picker.Preset == "kz-az09pro" && picker.CustomPath == "" && picker.SelectedTile.Index == Array.IndexOf(DeviceVisuals.Keys, "kz-az09pro"), "Grade abre no modelo salvo do dispositivo");
                        Check(picker.Tiles.Count == DeviceVisuals.Keys.Length + 1 && picker.Tiles.Select(t => t.AccessibleName).SequenceEqual(DeviceVisuals.Names.Concat(new[] { "Imagem própria" })) && picker.Tiles.Count(t => t.Selected) == 1 && picker.Tiles.Count(t => t.TabStop) == 1, "Grade tem um bloco acessível por modelo, mais imagem própria, com uma seleção e uma parada de Tab");
                        picker.SelectPreset(Array.IndexOf(DeviceVisuals.Keys, "sony-pulse"));
                        liveEndpoints.Add(new Endpoint { Id = "live-late", Name = "Saída que chegou depois", State = 1 }); liveApp.Refresh();
                        ids = combo.Items.Cast<DeviceChoice>().Select(c => c.Endpoint.Id).ToList();
                        Check(ids.Contains("live-late") && !ids.Contains("live-other") && ((DeviceChoice)combo.SelectedItem).Endpoint.Id == "live-own" && picker.Preset == "sony-pulse", "Atualização ao vivo preserva seleção e imagem escolhidas");
                        Snapshot(editor, Path.Combine(output, "audio-finder-editor-grid-dark.png"));
                        foreach (var tile in picker.Tiles) Check(tile.Left >= 0 && tile.Right <= picker.ClientSize.Width && tile.Bottom <= picker.ClientSize.Height, "Bloco cabe na grade: " + tile.Caption);
                        editor.Controls.OfType<SoftButton>().Single(b => b.Text == "Salvar").PerformClick(); Pump(50);
                        Check(liveApp.Config.Slots[2].DeviceId == "live-own" && liveApp.Config.Slots[0].DeviceId == "live-other" && liveApp.Config.Devices.Count == 2 && liveApp.Config.Devices.Single(d => d.Id == "live-own").IconPreset == "sony-pulse", "Salvar sem trocar mantém o dispositivo e não altera outros atalhos");
                    }
                    var liveLight = liveApp.Config.Copy(); liveLight.Theme = Appearance.Light; string liveError;
                    Check(liveApp.Apply(liveLight, out liveError), "Tema claro aplicado ao editor de teste");
                    using (var editor = new SlotEditor(liveApp, 2))
                    {
                        var picker = editor.Controls.OfType<IconPicker>().Single();
                        string ownImage = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "devices", "kuba-disco.png");
                        picker.SetCustom(ownImage);
                        Snapshot(editor, Path.Combine(output, "audio-finder-editor-grid-light.png"));
                        Check(picker.CustomPath == ownImage && picker.SelectedTile.Index < 0 && picker.Controls.OfType<SoftButton>().Single().Visible, "Imagem própria vira bloco selecionado e mostra Trocar");
                        picker.Activate(picker.Tiles[1]);
                        Check(picker.CustomPath == "" && picker.Preset == DeviceVisuals.Keys[1] && picker.HasCustom, "Clicar em modelo usa o modelo e mantém a imagem própria disponível na sessão");
                    }
                    liveApp.EndCapture();
                    liveApp.ShowSettings(); Pump(150);
                    var portugueseMain = Application.OpenForms.OfType<MainForm>().Single();
                    Check(portugueseMain.Controls.OfType<Label>().Any(l => l.Text == "SUAS SAÍDAS") && portugueseMain.Controls.OfType<LanguageToggle>().Single().Checked == false, "Janela em português com seletor PT marcado");
                    var toEnglish = liveApp.Config.Copy(); toEnglish.Language = Language.English;
                    Check(liveApp.Apply(toEnglish, out liveError) && L.English && liveStore.Load().Language == Language.English, "Escolher inglês aplica e persiste o idioma");
                    Pump(250);
                    var englishMain = Application.OpenForms.OfType<MainForm>().Single();
                    Check(!Object.ReferenceEquals(englishMain, portugueseMain) && portugueseMain.IsDisposed && englishMain.Controls.OfType<Label>().Any(l => l.Text == "YOUR OUTPUTS") && englishMain.Controls.OfType<LanguageToggle>().Single().Checked, "Troca de idioma reabre a janela já traduzida");
                    Snapshot(englishMain, Path.Combine(output, "audio-finder-english.png"));
                    liveApp.BeginCapture();
                    using (var editor = new SlotEditor(liveApp, 2))
                    {
                        Check(editor.Text == "Device 3 · Audio Finder" && editor.Controls.OfType<IconPicker>().Single().Tiles[1].Caption == "In-ear earbuds" && editor.Status == SlotEditor.KeptOfflineMessage && SlotEditor.KeptOfflineMessage.StartsWith("This device"), "Editor, modelos e avisos em inglês");
                        Snapshot(editor, Path.Combine(output, "audio-finder-editor-english.png"));
                    }
                    liveApp.EndCapture();
                    using (var englishToast = OutputToast.Selection(2, liveApp.Config, liveEndpoints, Appearance.Dark)) Check(englishToast.Heading == "Output selected", "Aviso de troca em inglês");
                    Check(HotkeyText.Format(0, 0) == "No shortcut" && DeviceVisuals.Keys.SequenceEqual(new[] { "generic", "airpods-pro", "airpods", "edifier-monitors", "kuba-disco", "sony-pulse", "edifier-w820nb", "monitor-tv", "kz-az09pro", "shokz-openrun-pro-2" }), "Inglês traduz textos sem mudar as chaves salvas");
                    var toPortuguese = liveApp.Config.Copy(); toPortuguese.Language = Language.Portuguese;
                    Check(liveApp.Apply(toPortuguese, out liveError) && !L.English, "Voltar ao português");
                    Pump(250);
                    Check(Application.OpenForms.OfType<MainForm>().Single().Controls.OfType<Label>().Any(l => l.Text == "SUAS SAÍDAS"), "Janela volta ao português");
                    foreach (var form in Application.OpenForms.OfType<MainForm>().ToList()) form.Close();
                    Pump(100);
                }
                var eight = new Settings();
                for (int i = 0; i < 8; i++) { string id = "eight-" + i; eight.Devices.Add(new SavedDevice { Id = id, Name = "Dispositivo " + (i + 1), IconPreset = DeviceVisuals.Keys[1 + i % (DeviceVisuals.Keys.Length - 1)] }); eight.Slots[i].DeviceId = id; }
                using (var compact = OutputToast.Selection(7, eight, Enumerable.Empty<Endpoint>(), Appearance.Light))
                {
                    compact.Show(); Pump(40); compact.Width = 640; Pump(40);
                    var cards = compact.Controls.Cast<Control>().SelectMany(Descendants).OfType<OutputCard>().ToList();
                    Check(cards.Count == 8 && cards.All(c => c.Right <= c.Parent.Width) && cards.Single(c => c.Selected).AccessibleName.Contains("Posição 8"), "Oito saídas e selecionado final cabem em 640 pixels");
                    Snapshot(compact, Path.Combine(output, "audio-finder-toast-light-eight.png"));
                }
                using (var app = new TrayApp(appStore, true, delegate(SavedDevice device, CancellationToken token) { var failed = new TaskCompletionSource<Endpoint>(); failed.SetException(new InvalidOperationException("Dispositivo indisponível no backend de teste")); return failed.Task; }))
                {
                    app.ShowSettings(); Pump(200); var main = Application.OpenForms.OfType<MainForm>().Single();
                    Snapshot(main, Path.Combine(output, "audio-finder-dark.png"));
                    var light = app.Config.Copy(); light.Theme = Appearance.Light; string error; Check(app.Apply(light, out error), "Tema aplicado sem alterar cadastros");
                    Snapshot(main, Path.Combine(output, "audio-finder-light.png"));
                    var dark = app.Config.Copy(); dark.Theme = Appearance.Dark; app.Apply(dark, out error);
                    app.BeginCapture(); using (var editor = new SlotEditor(app, 1)) {
                        Snapshot(editor, Path.Combine(output, "audio-finder-editor.png"));
                        var modelChoice = editor.Controls.OfType<IconPicker>().Single();
                        foreach (var model in new[] { "monitor-tv", "kz-az09pro", "shokz-openrun-pro-2" }) {
                            modelChoice.SelectPreset(Array.IndexOf(DeviceVisuals.Keys, model));
                            Snapshot(editor, Path.Combine(output, "audio-finder-editor-" + (model == "monitor-tv" ? "monitor" : model) + ".png"));
                        }
                    } app.EndCapture();
                    using (var history = new HistoryForm(app)) Snapshot(history, Path.Combine(output, "audio-finder-history.png"));
                    if (!safe && available != null)
                    {
                        app.SelectSlot(0); Pump(100); var selectedToast = Application.OpenForms.OfType<OutputToast>().Single();
                        Check(selectedToast.Heading == "Saída selecionada" && selectedToast.SelectedId == available.Id && selectedToast.SelectedPosition == 1, "Troca bem-sucedida exibe aviso para o slot acionado");
                        Check(selectedToast.ListedPositions.SequenceEqual(new[] { 1, 2 }) && selectedToast.ListedEndpointIds.SequenceEqual(new[] { available.Id, "offline-bluetooth" }), "Aviso de sucesso contém somente os slots configurados, incluindo o offline");
                    }
                    app.SelectSlot(1); Pump(100);
                    var watch = Stopwatch.StartNew(); app.SelectSlot(1); Pump(100);
                    Check(Application.OpenForms.OfType<OutputToast>().Count() == 1 && Application.OpenForms.OfType<OutputToast>().Single().Heading == "Saída indisponível", "Falha substitui aviso anterior sem sobreposição");
                    while (Application.OpenForms.OfType<OutputToast>().Any() && watch.ElapsedMilliseconds < 4000) Pump(20);
                    Check(watch.ElapsedMilliseconds >= 2900 && watch.ElapsedMilliseconds < 3500, "Aviso fecha automaticamente em 3 segundos (" + watch.ElapsedMilliseconds + " ms)");
                    var silent = app.Config.Copy(); silent.ShowErrors = false; app.Apply(silent, out error); app.SelectSlot(1); Pump(100);
                    if (!safe && available != null) { app.SelectSlot(0); Pump(100); }
                    Check(!Application.OpenForms.OfType<OutputToast>().Any(), "Toggle desativado suprime avisos de sucesso e falha");
                    var shortcut = app.Config.Copy(); shortcut.Slots[0].Key = (uint)Keys.F23; shortcut.Slots[0].Modifiers = 0; Check(app.Apply(shortcut, out error), "Configuração registra atalho e salva");
                    using (var competitor = new Hotkeys())
                    {
                        var occupied = shortcut.Copy(); occupied.Slots[0].Key = (uint)Keys.F24; Check(competitor.Apply(occupied), "Atalho concorrente preparado");
                        Check(!app.Apply(occupied, out error) && app.Config.Slots[0].Key == (uint)Keys.F23 && appStore.Load().Slots[0].Key == (uint)Keys.F23, "Conflito preserva cadastro e arquivo anteriores");
                    }
                    main.Close(); Pump(100); Check(!Application.OpenForms.OfType<MainForm>().Any(), "Fechar configurações descarta janela e mantém contexto da bandeja");
                    var before = Process.GetCurrentProcess().TotalProcessorTime; Pump(1500); var cpu = Process.GetCurrentProcess().TotalProcessorTime - before;
                    log.Add("Informativo: CPU em teste ocioso (inclui loop de testes): " + cpu.TotalMilliseconds + " ms / 1500 ms");
                }
                using (var icon = NativeMethods.MakeIcon()) using (var outputIcon = File.Create(Path.Combine(output, "AudioFinder.ico"))) icon.Save(outputIcon);
                File.WriteAllLines(Path.Combine(output, "test-results.txt"), log.Concat(new[] { count + " verificações concluídas." }));
                Console.WriteLine(count + " verificações concluídas."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); File.WriteAllText(Path.Combine(output, "test-failure.txt"), ex.ToString()); return 1; }
        }
        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
