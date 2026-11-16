using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AudioFinder
{
    // A successful KS request starts a real driver attempt; only an active endpoint completes it.
    internal sealed class BluetoothConnection
    {
        private static readonly SemaphoreSlim NativeGate = new SemaphoreSlim(1, 1);
        internal const int TimeoutMilliseconds = 15000;
        internal static Task<Endpoint> ConnectAsync(SavedDevice target, CancellationToken cancellation)
        { return RunAsync(target, cancellation, Connect, TimeoutMilliseconds); }
        internal static async Task<Endpoint> RunAsync(SavedDevice target, CancellationToken cancellation, Func<SavedDevice, CancellationToken, Endpoint> operation, int timeout)
        {
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var token = lifetime.Token;
            var worker = Task.Run(() => { token.ThrowIfCancellationRequested(); return operation(target, token); });
            try
            {
                var deadline = Task.Delay(timeout, cancellation);
                var completed = await Task.WhenAny(worker, deadline);
                if (completed == worker) return await worker;
                lifetime.Cancel();
                // Observe a late native failure without allowing it to activate an output.
                ObserveLateFailure(worker);
                cancellation.ThrowIfCancellationRequested();
                throw new TimeoutException(L.T("O dispositivo Bluetooth não confirmou a conexão a tempo. Verifique se está ligado e próximo do computador.", "The Bluetooth device did not confirm the connection in time. Check that it is on and close to the computer."));
            }
            finally
            {
                // A blocked native call can outlive the UI timeout. Keep its token and wait handle alive.
                if (worker.IsCompleted) lifetime.Dispose();
                else DisposeWhenCompleted(worker, lifetime);
            }
        }
        private static void DisposeWhenCompleted(Task task, IDisposable lifetime)
        { task.ContinueWith(t => lifetime.Dispose(), TaskScheduler.Default); }
        private static void ObserveLateFailure(Task task)
        { task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        private static Endpoint Connect(SavedDevice target, CancellationToken cancellation)
        {
            NativeGate.Wait(cancellation);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                // All COM objects are created, used and released on this one worker thread.
                using (var audio = new AudioService(false))
                {
                    var endpoints = audio.Enumerate();
                    var endpoint = Resolve(target, endpoints, false);
                    if (endpoint == null) throw new InvalidOperationException(L.T("O Windows não encontrou uma saída Bluetooth com a identidade cadastrada. Conecte o dispositivo nas configurações do Windows.", "Windows could not find a Bluetooth output with the saved identity. Connect the device in Windows settings."));
                    if (endpoint.Active) return endpoint;
                    if (!endpoint.Bluetooth || endpoint.State != 8) throw new InvalidOperationException(L.T("Esta saída Bluetooth está desabilitada ou não está presente no Windows.", "This Bluetooth output is disabled or not present in Windows."));
                    cancellation.ThrowIfCancellationRequested();
                    BluetoothDriver.Request(endpoint.Id, false, cancellation);
                    while (true)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var active = Resolve(target, audio.Enumerate(), true);
                        if (active != null) return active;
                        if (cancellation.WaitHandle.WaitOne(250)) cancellation.ThrowIfCancellationRequested();
                    }
                }
            }
            finally { NativeGate.Release(); }
        }
        internal static Endpoint Resolve(SavedDevice target, IEnumerable<Endpoint> endpoints, bool activeOnly)
        {
            var all = endpoints.ToList();
            var exact = all.FirstOrDefault(e => Same(e.Id, target.Id) && (!activeOnly || e.Active));
            if (exact != null) return exact;
            var candidates = all.Where(e => (!activeOnly || e.Active) && e.Bluetooth && Same(target.Name, e.Name) &&
                ((!String.IsNullOrWhiteSpace(target.ContainerId) && !String.IsNullOrWhiteSpace(e.ContainerId)) ? Same(target.ContainerId, e.ContainerId) : Same(target.InstanceId, e.InstanceId))).ToList();
            return candidates.Count == 1 ? candidates[0] : null;
        }
        private static bool Same(string a, string b) { return !String.IsNullOrWhiteSpace(a) && String.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    internal static class BluetoothDriver
    {
        // ksmedia.h: KSPROPSETID_BtAudio, KSPROPERTY_ONESHOT_RECONNECT = 0.
        internal static readonly Guid PropertySet = new Guid("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
        internal static bool Request(string endpointId, bool probeOnly, CancellationToken cancellation = default(CancellationToken), Action<string> diagnostic = null)
        {
            IMMDeviceEnumerator enumerator = null; IMMDevice endpoint = null; object topologyObject = null;
            bool supported = false, accepted = false;
            var filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
                Marshal.ThrowExceptionForHR(enumerator.GetDevice(endpointId, out endpoint));
                var iid = typeof(IDeviceTopology).GUID;
                int topologyHr = endpoint.Activate(ref iid, 23, IntPtr.Zero, out topologyObject);
                if (topologyHr < 0)
                {
                    if (diagnostic != null) diagnostic("Topology activation: 0x" + topologyHr.ToString("X8"));
                    if (probeOnly) return false;
                    throw new InvalidOperationException(L.T("O Windows não oferece controle de conexão para esta saída Bluetooth. Conecte nas configurações do Windows.", "Windows does not offer connection control for this Bluetooth output. Connect it in Windows settings."));
                }
                var topology = (IDeviceTopology)topologyObject; uint count;
                Marshal.ThrowExceptionForHR(topology.GetConnectorCount(out count));
                for (uint i = 0; i < count; i++)
                {
                    IConnector connector = null; IMMDevice filter = null; object controlObject = null;
                    try
                    {
                        Marshal.ThrowExceptionForHR(topology.GetConnector(i, out connector));
                        string filterId;
                        int connectorHr = connector.GetDeviceIdConnectedTo(out filterId);
                        if (diagnostic != null) diagnostic("Connector lookup: 0x" + connectorHr.ToString("X8") + "; Bluetooth filter=" + (!String.IsNullOrEmpty(filterId) && filterId.StartsWith(@"{2}.\\?\bth", StringComparison.OrdinalIgnoreCase)));
                        if (connectorHr < 0 || String.IsNullOrEmpty(filterId) ||
                            !filterId.StartsWith(@"{2}.\\?\bth", StringComparison.OrdinalIgnoreCase) || !filters.Add(filterId)) continue;
                        Marshal.ThrowExceptionForHR(enumerator.GetDevice(filterId, out filter));
                        iid = typeof(IKsControl).GUID;
                        int controlHr = filter.Activate(ref iid, 23, IntPtr.Zero, out controlObject);
                        if (diagnostic != null) diagnostic("IKsControl activation: 0x" + controlHr.ToString("X8"));
                        if (controlHr < 0) continue;
                        var control = (IKsControl)controlObject;
                        // BASICSUPPORT reads property capability; never issues the reconnect during a probe.
                        var property = new KsProperty { Set = PropertySet, Id = 0, Flags = 0x200 };
                        uint returned; var buffer = Marshal.AllocCoTaskMem(4);
                        try
                        {
                            Marshal.WriteInt32(buffer, 0);
                            int hr = control.KsProperty(ref property, 24, buffer, 4, out returned);
                            if (diagnostic != null) diagnostic("BASICSUPPORT: 0x" + hr.ToString("X8") + "; returned=" + returned + "; GET=" + (returned >= 4 && (Marshal.ReadInt32(buffer) & 1) != 0));
                            if (hr < 0 || returned < 4 || (Marshal.ReadInt32(buffer) & 1) == 0) continue;
                            supported = true;
                        }
                        finally { Marshal.FreeCoTaskMem(buffer); }
                        if (!probeOnly)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            property.Flags = 1; // GET, NULL data: prompt driver connection, not driver installation.
                            accepted |= control.KsProperty(ref property, 24, IntPtr.Zero, 0, out returned) >= 0;
                        }
                    }
                    catch (COMException) { /* Other connector/profile may support the property. */ }
                    finally { AudioService.Release(controlObject); AudioService.Release(filter); AudioService.Release(connector); }
                }
            }
            finally { AudioService.Release(topologyObject); AudioService.Release(endpoint); AudioService.Release(enumerator); }
            if (probeOnly) return supported;
            if (!supported) throw new InvalidOperationException(L.T("O driver desta saída não oferece conexão Bluetooth pelo aplicativo. Conecte nas configurações do Windows.", "The driver of this output does not allow Bluetooth connection from the app. Connect it in Windows settings."));
            if (!accepted) throw new InvalidOperationException(L.T("O Windows recusou a tentativa de conexão Bluetooth. Verifique o Bluetooth e o dispositivo.", "Windows refused the Bluetooth connection attempt. Check Bluetooth and the device."));
            return true;
        }
    }
    [StructLayout(LayoutKind.Sequential)] internal struct KsProperty { internal Guid Set; internal uint Id, Flags; }
    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KsProperty property, uint propertyLength, IntPtr data, uint dataLength, out uint returned);
        [PreserveSig] int KsMethod(IntPtr method, uint methodLength, IntPtr data, uint dataLength, out uint returned);
        [PreserveSig] int KsEvent(IntPtr evt, uint evtLength, IntPtr data, uint dataLength, out uint returned);
    }
    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
        [PreserveSig] int GetSubunitCount(out uint count);
        [PreserveSig] int GetSubunit(uint index, out IntPtr subunit);
        [PreserveSig] int GetPartById(uint id, out IntPtr part);
        [PreserveSig] int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSignalPath(IntPtr from, IntPtr to, int rejectMixed, out IntPtr parts);
    }
    [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IConnector
    {
        [PreserveSig] int GetType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IConnector other);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        [PreserveSig] int GetConnectedTo(out IConnector other);
        [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }
}
