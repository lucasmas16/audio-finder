using System;
using System.IO;
namespace AudioFinder
{
    internal static class DeviceIdentityTests
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS: " + message); }
        static Settings Fixture() { var s = new Settings(); s.Devices.Add(new SavedDevice { Id = "old", Name = "Stereo", InstanceId = "physical-a", ContainerId = "container-a", IconPreset = "headset" }); s.Slots[2] = new Slot { DeviceId = "old", Key = 114, Modifiers = 3 }; return s; }
        static Endpoint Target(string id, string container) { return new Endpoint { Id = id, Name = "Stereo", InstanceId = "physical-a", ContainerId = container, State = 1 }; }
        static int Main(string[] args)
        {
            try {
                var s = Fixture(); Check(s.Reconcile(new[] { Target("new", "container-a") }) && s.Slots[2].DeviceId == "new" && s.Slots[2].Key == 114 && s.Devices[0].IconPreset == "headset", "Endpoint recriado preserva posição, atalho e ícone");
                Check(!s.Reconcile(new[] { Target("new", "container-a") }), "Reconciliação repetida é idempotente");
                s = Fixture(); Check(!s.Reconcile(new[] { Target("new", "container-b") }) && s.Devices[0].Id == "old", "Mesmo nome e instance com container distinto não combina hardware");
                s = Fixture(); s.Devices[0].InstanceId = s.Devices[0].ContainerId = ""; var unknown = Target("new", ""); unknown.InstanceId = ""; Check(!s.Reconcile(new[] { unknown }), "Nome isolado nunca migra cadastro legado");
                s = Fixture(); Check(!s.Reconcile(new[] { Target("one", "container-a"), Target("two", "container-a") }), "Dois destinos possíveis permanecem separados");
                s = Fixture(); s.Devices.Add(new SavedDevice { Id = "new", Name = "Stereo" }); Check(!s.Reconcile(new[] { Target("new", "container-a") }) || s.Devices[0].Id == "old", "Destino cadastrado não absorve histórico antigo");
                s = Fixture(); var handsFree = Target("new", "container-a"); handsFree.Name = "Hands-free"; Check(!s.Reconcile(new[] { handsFree }), "Perfis de áudio diferentes permanecem separados");
                s = Fixture(); s.Devices[0].ContainerId = ""; Check(s.Reconcile(new[] { Target("new", "") }) && s.Devices[0].Id == "new", "Interface física exata permite recuperação sem container disponível");
                s = Fixture(); Check(!s.Reconcile(new[] { Target("old", "container-a"), Target("new", "container-a") }) && s.Devices[0].Id == "old", "Endpoint original ainda ativo mantém sua associação");
                s = Fixture(); s.Devices[0].InstanceId = s.Devices[0].ContainerId = ""; var original = Target("old", "container-a"); original.State = 8; Check(s.Reconcile(new[] { original, Target("new", "container-a") }) && s.Devices[0].Id == "new", "Endpoint original inativo fornece prova para migrar cadastro legado");
                s = Fixture(); var copy = s.Copy(); copy.Devices[0].IconPath = "image.png"; copy.Devices[0].InstanceId = "other"; Check(s.Devices[0].IconPath == "" && s.Devices[0].InstanceId == "physical-a", "Edição de identidade e aparência usa cópia independente");
                string directory = Path.GetFullPath(args[0]); Directory.CreateDirectory(directory); var store = new SettingsStore(Path.Combine(directory, "identity.xml")); store.Save(s); var loaded = store.Load(); Check(loaded.Devices[0].ContainerId == "container-a" && loaded.Devices[0].IconPreset == "headset", "Identidade e ícone persistem no XML");
                Console.WriteLine("12 verificações de identidade concluídas sem alterar áudio."); return 0;
            } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
