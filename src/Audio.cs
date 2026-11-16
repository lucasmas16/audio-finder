using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace AudioFinder
{
    internal sealed class Endpoint
    {
        internal string Id, Name;
        internal string InstanceId = "", ContainerId = "";
        internal uint State;
        internal bool Bluetooth;
        internal bool Active { get { return State == 1; } }
        public override string ToString() { return Name + (Active ? "" : L.T(" · indisponível", " · unavailable")); }
    }
    internal sealed class AudioService : IDisposable
    {
        private IMMDeviceEnumerator enumerator;
        private AudioNotifications notifications;
        internal event Action Changed;
        internal AudioService(bool subscribe)
        {
            enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
            if (subscribe) { notifications = new AudioNotifications(() => { var callback = Changed; if (callback != null) callback(); }); Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(notifications)); }
        }
        internal List<Endpoint> Enumerate()
        {
            var result = new List<Endpoint>(); IMMDeviceCollection collection = null;
            try
            {
                Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 15, out collection));
                uint count; Marshal.ThrowExceptionForHR(collection.GetCount(out count));
                for (uint i = 0; i < count; i++)
                {
                    IMMDevice device = null; IPropertyStore properties = null;
                    try
                    {
                        Marshal.ThrowExceptionForHR(collection.Item(i, out device));
                        string id; uint state; Marshal.ThrowExceptionForHR(device.GetId(out id)); Marshal.ThrowExceptionForHR(device.GetState(out state));
                        Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out properties));
                        string name = ReadString(properties, new PropertyKey("a45c254e-df1c-4efd-8020-67d146a850e0", 14));
                        string instance = ReadString(properties, new PropertyKey("78c34fc8-104a-4aca-9ea4-524d52996e57", 256));
                        if (String.IsNullOrEmpty(instance)) instance = ReadString(properties, new PropertyKey("b3f8fa53-0004-438e-9003-51a46e139bfc", 2));
                        string bus = ReadString(properties, new PropertyKey("a45c254e-df1c-4efd-8020-67d146a850e0", 24));
                        string container = ReadGuid(properties, new PropertyKey("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c", 2));
                        result.Add(new Endpoint { Id = id, Name = String.IsNullOrEmpty(name) ? L.T("Saída de áudio", "Audio output") : name, State = state, InstanceId = instance, ContainerId = container, Bluetooth = bus.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) || IsBluetooth(instance) });
                    }
                    catch (COMException) { /* A device may disappear while the collection is being read. */ }
                    finally { Release(properties); Release(device); }
                }
            }
            finally { Release(collection); }
            return result.OrderByDescending(d => d.Active).ThenBy(d => d.Name).ToList();
        }
        internal string Default(int role)
        {
            IMMDevice device = null;
            try { if (enumerator.GetDefaultAudioEndpoint(0, role, out device) < 0) return ""; string id; Marshal.ThrowExceptionForHR(device.GetId(out id)); return id; }
            finally { Release(device); }
        }
        internal void Select(string id)
        {
            if (!Enumerate().Any(d => d.Id == id && d.Active)) throw new InvalidOperationException(L.T("O dispositivo está desconectado ou desabilitado.", "The device is disconnected or disabled."));
            var previous = Enumerable.Range(0, 3).Select(Default).ToArray();
            IPolicyConfig policy = null;
            try
            {
                policy = (IPolicyConfig)new PolicyConfig();
                try
                {
                    for (int role = 0; role < 3; role++) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role));
                    for (int role = 0; role < 3; role++) if (Default(role) != id) throw new InvalidOperationException(L.T("O Windows não confirmou a troca de saída. Tente novamente.", "Windows did not confirm the output change. Try again."));
                }
                catch { for (int role = 0; role < 3; role++) if (!String.IsNullOrEmpty(previous[role])) policy.SetDefaultEndpoint(previous[role], role); throw; }
            }
            finally { Release(policy); }
        }
        internal void RestoreDefaults(string[] ids)
        {
            IPolicyConfig policy = null;
            try { policy = (IPolicyConfig)new PolicyConfig(); for (int role = 0; role < 3; role++) if (!String.IsNullOrEmpty(ids[role])) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(ids[role], role)); }
            finally { Release(policy); }
        }
        private static string ReadGuid(IPropertyStore store, PropertyKey key)
        {
            PropVariant value; if (store.GetValue(ref key, out value) < 0) return "";
            try { if (value.Type != 72 || value.Pointer == IntPtr.Zero) return ""; var guid = (Guid)Marshal.PtrToStructure(value.Pointer, typeof(Guid)); return guid == Guid.Empty ? "" : guid.ToString("D"); }
            finally { PropVariantClear(ref value); }
        }
        private static string ReadString(IPropertyStore store, PropertyKey key)
        {
            PropVariant value; if (store.GetValue(ref key, out value) < 0) return "";
            try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "" : ""; }
            finally { PropVariantClear(ref value); }
        }
        private static bool IsBluetooth(string instance)
        {
            if (String.IsNullOrEmpty(instance)) return false;
            uint node;
            if (CM_Locate_DevNode(out node, instance, 0) != 0) return instance.StartsWith("BTH", StringComparison.OrdinalIgnoreCase);
            for (int i = 0; i < 12; i++)
            {
                var buffer = new StringBuilder(1024);
                if (CM_Get_Device_ID(node, buffer, buffer.Capacity, 0) == 0 && buffer.ToString().StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) return true;
                uint parent; if (CM_Get_Parent(out parent, node, 0) != 0) break; node = parent;
            }
            return false;
        }
        internal static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        public void Dispose()
        {
            if (enumerator == null) return;
            if (notifications != null) { enumerator.UnregisterEndpointNotificationCallback(notifications); notifications = null; }
            Release(enumerator); enumerator = null;
        }
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Locate_DevNode(out uint node, string id, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_ID(uint node, StringBuilder buffer, int size, uint flags);
        [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    }
    [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey
    {
        internal Guid Format; internal uint Id;
        internal PropertyKey(string format, uint id) { Format = new Guid(format); Id = id; }
    }
    [StructLayout(LayoutKind.Explicit, Size = 24)] internal struct PropVariant { [FieldOffset(0)] internal ushort Type; [FieldOffset(8)] internal IntPtr Pointer; }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] internal class DeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint states, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IAudioNotifications callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IAudioNotifications callback);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IMMDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object value);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count); [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value); [PreserveSig] int Commit();
    }
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComVisible(true)]
    public interface IAudioNotifications
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, NotificationPropertyKey key);
    }
    [StructLayout(LayoutKind.Sequential)] public struct NotificationPropertyKey { public Guid Format; public uint Id; }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class AudioNotifications : IAudioNotifications
    {
        private readonly Action callback;
        internal AudioNotifications(Action action) { callback = action; }
        private int Signal() { try { callback(); } catch (InvalidOperationException) { } return 0; }
        public int OnDeviceStateChanged(string id, uint state) { return Signal(); }
        public int OnDeviceAdded(string id) { return Signal(); }
        public int OnDeviceRemoved(string id) { return Signal(); }
        public int OnDefaultDeviceChanged(int flow, int role, string id) { return flow == 0 ? Signal() : 0; }
        public int OnPropertyValueChanged(string id, NotificationPropertyKey key) { return Signal(); }
    }
    // IPolicyConfig is a Windows internal COM interface. Keep isolated and verify after every change.
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] internal class PolicyConfig { }
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr id, IntPtr format);
        [PreserveSig] int GetDeviceFormat(IntPtr id, int def, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(IntPtr id);
        [PreserveSig] int SetDeviceFormat(IntPtr id, IntPtr format, IntPtr mix);
        [PreserveSig] int GetProcessingPeriod(IntPtr id, int def, IntPtr period, IntPtr minimum);
        [PreserveSig] int SetProcessingPeriod(IntPtr id, IntPtr period);
        [PreserveSig] int GetShareMode(IntPtr id, IntPtr mode);
        [PreserveSig] int SetShareMode(IntPtr id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(IntPtr id, int fx, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(IntPtr id, int fx, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility(IntPtr id, int visible);
    }
}
