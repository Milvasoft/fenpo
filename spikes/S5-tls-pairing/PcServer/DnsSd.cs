using System.Net;
using System.Runtime.InteropServices;

namespace Fenpo.Spikes.S5;

/// <summary>
/// Registers a DNS-SD service with the Windows mDNS responder through DnsServiceRegister (dnsapi.dll,
/// Windows 10 1809+). Unlike WinRT DnssdServiceInstance it does not need a WinRT socket listener, so the
/// TLS server can stay on a plain .NET TcpListener.
/// </summary>
internal sealed class DnsSd : IDisposable
{
    private const uint _dnsQueryRequestVersion1 = 1;
    private const uint _dnsRequestPending = 9506;

    private readonly RegisterComplete _callback;
    private IntPtr _request;
    private IntPtr _instance;
    private IntPtr _ip4;

    private DnsSd(string instanceName, string hostName, IPAddress address, ushort port, IReadOnlyDictionary<string, string> text)
    {
        _callback = OnRegisterComplete;
        _ip4 = Marshal.AllocHGlobal(4);
        Marshal.Copy(address.GetAddressBytes(), 0, _ip4, 4);

        var keys = text.Keys.ToArray();
        var values = keys.Select(k => text[k]).ToArray();
        _instance = DnsServiceConstructInstance(instanceName, hostName, _ip4, IntPtr.Zero, port, 0, 0, (uint)keys.Length, keys, values);

        // The request lives in unmanaged memory: dnsapi may hold on to it until the registration is cancelled.
        var request = new DnsServiceRegisterRequest
        {
            Version = _dnsQueryRequestVersion1,
            InterfaceIndex = 0,
            ServiceInstance = _instance,
            RegisterCompletionCallback = Marshal.GetFunctionPointerForDelegate(_callback),
        };
        _request = Marshal.AllocHGlobal(Marshal.SizeOf<DnsServiceRegisterRequest>());
        Marshal.StructureToPtr(request, _request, false);
    }

    private delegate void RegisterComplete(uint status, IntPtr context, IntPtr instance);

    public static DnsSd Register(string instanceName, string hostName, IPAddress address, ushort port, IReadOnlyDictionary<string, string> text)
    {
        var registration = new DnsSd(instanceName, hostName, address, port, text);
        var status = DnsServiceRegister(registration._request, IntPtr.Zero);
        Log.Info("dnssd", $"DnsServiceRegister {instanceName} {hostName} {address}:{port}: {(status == _dnsRequestPending ? "pending" : $"error {status}")}");
        return registration;
    }

    public void Dispose()
    {
        if (_instance == IntPtr.Zero)
        {
            return;
        }

        var status = DnsServiceDeRegister(_request, IntPtr.Zero);
        Log.Info("dnssd", $"DnsServiceDeRegister: {(status == _dnsRequestPending ? "pending" : $"error {status}")}");
        DnsServiceFreeInstance(_instance);
        Marshal.FreeHGlobal(_ip4);
        Marshal.FreeHGlobal(_request);
        _instance = IntPtr.Zero;
        _ip4 = IntPtr.Zero;
    }

    private void OnRegisterComplete(uint status, IntPtr context, IntPtr instance) => Log.Info("dnssd", $"Registration completed with status {status}");

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceRegisterRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public IntPtr ServiceInstance;
        public IntPtr RegisterCompletionCallback;
        public IntPtr QueryContext;
        public IntPtr Credentials;
        public int UnicastEnabled;
    }

    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DnsServiceConstructInstance(string serviceName, string hostName, IntPtr ip4, IntPtr ip6, ushort port, ushort priority, ushort weight, uint propertiesCount, string[] keys, string[] values);

    [DllImport("dnsapi.dll")]
    private static extern uint DnsServiceRegister(IntPtr request, IntPtr cancel);

    [DllImport("dnsapi.dll")]
    private static extern uint DnsServiceDeRegister(IntPtr request, IntPtr cancel);

    [DllImport("dnsapi.dll")]
    private static extern void DnsServiceFreeInstance(IntPtr instance);
}
