using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace AudioFinder
{
    internal static class BluetoothConnectionTests
    {
        private static int checks;
        private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
        private static void Main(string[] args)
        {
            var target = new SavedDevice { Id = "original", Name = "Stereo", Bluetooth = true, ContainerId = "physical-a" };
            var active = new Endpoint { Id = "replacement", Name = "Stereo", Bluetooth = true, State = 1, ContainerId = "physical-a" };
            Check(BluetoothConnection.Resolve(target, new[] { active }, true) == active, "proven identity reconnect");
            Check(BluetoothConnection.Resolve(target, new[] { active, new Endpoint { Id = "other", Name = "Stereo", Bluetooth = true, State = 1, ContainerId = "physical-a" } }, true) == null, "ambiguous endpoint rejected");
            Check(BluetoothConnection.Resolve(target, new[] { new Endpoint { Id = "different-hardware", Name = "Stereo", Bluetooth = true, State = 1, ContainerId = "physical-b" } }, true) == null, "same name rejected");
            Check(BluetoothConnection.Resolve(target, new[] { new Endpoint { Id = "handsfree", Name = "Handsfree", Bluetooth = true, State = 1, ContainerId = "physical-a" } }, true) == null, "different profile rejected");
            Check(BluetoothConnection.Resolve(target, new[] { new Endpoint { Id = "original", State = 8 } }, true) == null, "inactive exact rejected");
            Check(BluetoothConnection.RunAsync(target, CancellationToken.None, (d, t) => active, 1000).GetAwaiter().GetResult() == active, "successful fake result");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); bool called = false;
                try { BluetoothConnection.RunAsync(target, cancellation.Token, (d, t) => { called = true; return active; }, 1000).GetAwaiter().GetResult(); throw new Exception("Expected cancellation"); }
                catch (OperationCanceledException) { Check(!called, "cancel before request"); }
            }
            using (var entered = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            {
                var attempt = BluetoothConnection.RunAsync(target, CancellationToken.None, (d, t) => { entered.Set(); release.WaitOne(); t.ThrowIfCancellationRequested(); return active; }, 80);
                Check(entered.WaitOne(1000), "worker started");
                try { attempt.GetAwaiter().GetResult(); throw new Exception("Expected timeout"); }
                catch (TimeoutException) { Check(true, "bounded timeout while native fake blocked"); }
                release.Set();
                Thread.Sleep(30);
            }
            using (var cancellation = new CancellationTokenSource())
            using (var entered = new ManualResetEvent(false))
            {
                var attempt = BluetoothConnection.RunAsync(target, cancellation.Token, (d, t) => { entered.Set(); while (!t.IsCancellationRequested) Thread.Sleep(5); t.ThrowIfCancellationRequested(); return active; }, 1000);
                Check(entered.WaitOne(1000), "cancel worker started"); cancellation.Cancel();
                try { attempt.GetAwaiter().GetResult(); throw new Exception("Expected cancellation"); }
                catch (OperationCanceledException) { Check(true, "cancel pending request"); }
            }
            Check(Marshal.SizeOf(typeof(KsProperty)) == 24, "native KSPROPERTY layout");
            Check(BluetoothDriver.PropertySet == new Guid("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D"), "SDK property set");
            Check(typeof(IKsControl).GUID == new Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), "SDK KS control IID");
            Console.WriteLine("Bluetooth deterministic checks: " + checks + " passed; no native reconnect or audio selection.");
            if (args.Contains("--probe"))
            {
                int bluetooth = 0, supported = 0, unavailable = 0;
                using (var audio = new AudioService(false))
                    foreach (var endpoint in audio.Enumerate().Where(e => e.Bluetooth))
                    {
                        bluetooth++;
                        Console.WriteLine("Read-only endpoint " + bluetooth + "; state=" + endpoint.State);
                        try { if (BluetoothDriver.Request(endpoint.Id, true, CancellationToken.None, Console.WriteLine)) supported++; else unavailable++; }
                        catch (COMException) { unavailable++; }
                    }
                Console.WriteLine("Read-only KS capability probe: Bluetooth endpoints=" + bluetooth + "; reconnect supported=" + supported + "; unavailable=" + unavailable + ". No reconnect GET sent.");
            }
        }
    }
}
