using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using System.Windows.Forms;

namespace AudioFinder
{
    public class SavedDevice
    {
        public string Id = "";
        public string Name = "";
        public bool Bluetooth;
        public string InstanceId = "", ContainerId = "";
        public string IconPreset = "generic", IconPath = "";
        internal SavedDevice Copy() { return (SavedDevice)MemberwiseClone(); }
        internal bool RememberIdentity(Endpoint endpoint)
        {
            bool changed = false;
            if (!String.IsNullOrWhiteSpace(endpoint.InstanceId) && InstanceId != endpoint.InstanceId) { InstanceId = endpoint.InstanceId; changed = true; }
            if (!String.IsNullOrWhiteSpace(endpoint.ContainerId) && ContainerId != endpoint.ContainerId) { ContainerId = endpoint.ContainerId; changed = true; }
            return changed;
        }
    }
    public class Slot
    {
        public string DeviceId = "";
        public uint Key;
        public uint Modifiers;
        public Slot Copy() { return (Slot)MemberwiseClone(); }
        public string Shortcut { get { return HotkeyText.Format(Key, Modifiers); } }
    }
    public class Settings
    {
        public Appearance Theme = Appearance.Dark;
        public Language Language = Language.Auto;
        public bool ShowErrors = true;
        public bool Enabled = true;
        public List<SavedDevice> Devices = new List<SavedDevice>();
        [XmlIgnore] public List<Slot> Slots = Enumerable.Range(0, 8).Select(i => new Slot()).ToList();
        [XmlArray("Slots")] public Slot[] SerializedSlots
        {
            get { return Slots.ToArray(); }
            set { Slots = value == null ? new List<Slot>() : value.ToList(); }
        }
        public Settings Copy()
        {
            var copy = (Settings)MemberwiseClone();
            copy.Slots = Slots.Select(s => s.Copy()).ToList();
            copy.Devices = Devices.Select(d => d.Copy()).ToList();
            return copy;
        }
        public void Normalize()
        {
            if (!Enum.IsDefined(typeof(Appearance), Theme)) Theme = Appearance.Dark;
            if (!Enum.IsDefined(typeof(Language), Language)) Language = Language.Auto;
            Devices = (Devices ?? new List<SavedDevice>()).Where(d => d != null && !String.IsNullOrWhiteSpace(d.Id)).GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            Slots = (Slots ?? new List<Slot>()).Take(8).Select(s => s ?? new Slot()).ToList();
            while (Slots.Count < 8) Slots.Add(new Slot());
            var used = new HashSet<string>();
            foreach (var slot in Slots)
            {
                if (!Devices.Any(d => d.Id == slot.DeviceId)) slot.DeviceId = "";
                if (String.IsNullOrEmpty(slot.DeviceId) || !HotkeyText.Valid(slot.Key, slot.Modifiers) || !used.Add(slot.Key + ":" + slot.Modifiers)) slot.Key = slot.Modifiers = 0;
            }
        }
        public void Forget(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(ids);
            Devices.RemoveAll(d => set.Contains(d.Id));
            foreach (var slot in Slots) if (set.Contains(slot.DeviceId)) { slot.DeviceId = ""; slot.Key = slot.Modifiers = 0; }
        }
        // Endpoint IDs can change after a driver reinstall. Never infer physical identity from a name alone.
        internal bool Reconcile(IEnumerable<Endpoint> endpoints)
        {
            var current = endpoints.ToList(); bool changed = false;
            foreach (var saved in Devices)
            {
                var exact = current.FirstOrDefault(e => Same(e.Id, saved.Id));
                if (exact != null) changed |= saved.RememberIdentity(exact);
            }
            foreach (var saved in Devices)
            {
                if (current.Any(e => Same(e.Id, saved.Id) && e.Active)) continue;
                var candidates = current.Where(e => e.Active && Compatible(saved, e)).ToList();
                if (candidates.Count != 1) continue;
                var target = candidates[0];
                // A second saved profile or configured endpoint makes migration ambiguous.
                if (Devices.Any(d => d != saved && (Same(d.Id, target.Id) || Compatible(d, target)))) continue;
                string previous = saved.Id; saved.Id = target.Id; saved.RememberIdentity(target);
                foreach (var slot in Slots) if (Same(slot.DeviceId, previous)) slot.DeviceId = target.Id;
                changed = true;
            }
            return changed;
        }
        private static bool Same(string a, string b) { return !String.IsNullOrWhiteSpace(a) && String.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        private static bool Compatible(SavedDevice saved, Endpoint endpoint)
        {
            // Retain the endpoint profile (stereo, hands-free, optical, etc.) within one physical device.
            if (!Same(saved.Name, endpoint.Name)) return false;
            if (!String.IsNullOrWhiteSpace(saved.ContainerId) && !String.IsNullOrWhiteSpace(endpoint.ContainerId)) return Same(saved.ContainerId, endpoint.ContainerId);
            return Same(saved.InstanceId, endpoint.InstanceId);
        }
    }
    internal static class HotkeyText
    {
        internal static bool Valid(uint key, uint mods)
        {
            return key > 0 && key <= 254 && key != (uint)Keys.F12 && key != 16 && key != 17 && key != 18 && key != 91 && key != 92 && key != 229 && (mods & ~7u) == 0;
        }
        internal static string Format(uint key, uint mods)
        {
            if (key == 0) return L.T("Sem atalho", "No shortcut");
            string text = ((mods & 2) != 0 ? "Ctrl + " : "") + ((mods & 1) != 0 ? "Alt + " : "") + ((mods & 4) != 0 ? "Shift + " : "");
            string name = ((Keys)key).ToString();
            if (key >= 48 && key <= 57) name = ((char)key).ToString();
            return text + name;
        }
    }
    internal sealed class SettingsStore
    {
        internal readonly string FilePath;
        internal string Warning;
        internal SettingsStore(string path) { FilePath = path; }
        internal Settings Load()
        {
            if (!File.Exists(FilePath)) return new Settings();
            try { return Read(FilePath); }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException)) throw;
                Warning = L.T("Não foi possível ler as configurações. O arquivo original foi preservado.", "The settings could not be read. The original file was preserved.");
                try { var backup = Read(FilePath + ".bak"); Warning = L.T("As configurações foram recuperadas da cópia de segurança.", "The settings were restored from the backup copy."); return backup; }
                catch (Exception backupError) { if (!(backupError is IOException || backupError is InvalidOperationException || backupError is UnauthorizedAccessException)) throw; return new Settings(); }
            }
        }
        private static Settings Read(string path)
        {
            using (var input = File.OpenRead(path))
            using (var reader = System.Xml.XmlReader.Create(input, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null }))
            { var settings = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(reader); settings.Normalize(); return settings; }
        }
        internal void Save(Settings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = FilePath + ".tmp";
            try
            {
                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { new XmlSerializer(typeof(Settings)).Serialize(output, settings); output.Flush(true); }
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak"); else File.Move(temp, FilePath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
