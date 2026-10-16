using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Fenpo.Transport;

/// <summary>
/// Registers <c>_fenpo._tcp</c> with the Windows mDNS responder through DnsServiceRegister (dnsapi.dll). Unlike WinRT
/// DnssdServiceInstance it needs no WinRT socket listener, so the TLS server stays on a .NET TcpListener (S5). The
/// iPhone cannot browse in the background, so the address also goes over the BLE channel.
/// </summary>
public sealed partial class DnsSdRegistration : IDisposable
{
    private const uint _requestVersion1 = 1;
    private const uint _requestPending = 9506;

    private readonly ILogger _logger;
    private readonly RegisterComplete _callback;
    private nint _request;
    private nint _instance;
    private nint _ip4;

    private DnsSdRegistration(string instanceName, string hostName, IPAddress address, ushort port, IReadOnlyDictionary<string, string> text, ILogger logger)
    {
        _logger = logger;
        _callback = OnRegisterComplete;
        _ip4 = Marshal.AllocHGlobal(4);
        Marshal.Copy(address.GetAddressBytes(), 0, _ip4, 4);

        var keys = text.Keys.ToArray();
        var values = keys.Select(k => text[k]).ToArray();
        _instance = DnsServiceConstructInstance(instanceName, hostName, _ip4, 0, port, 0, 0, (uint)keys.Length, keys, values);

        // dnsapi may hold on to the request until the registration is cancelled, so it lives in unmanaged memory.
        var request = new RegisterRequest
        {
            Version = _requestVersion1,
            ServiceInstance = _instance,
            RegisterCompletionCallback = Marshal.GetFunctionPointerForDelegate(_callback),
        };
        _request = Marshal.AllocHGlobal(Marshal.SizeOf<RegisterRequest>());
        Marshal.StructureToPtr(request, _request, false);
    }

    private delegate void RegisterComplete(uint status, nint context, nint instance);

    /// <summary>
    /// Registers the service.
    /// </summary>
    /// <param name="instanceName">Instance, e.g. "BUGRAKOSEN._fenpo._tcp.local".</param>
    /// <param name="hostName">Host, e.g. "BUGRAKOSEN.local".</param>
    /// <param name="address">IPv4 address.</param>
    /// <param name="port">TLS port.</param>
    /// <param name="text">TXT entries (id, v).</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The registration; dispose to withdraw it.</returns>
    public static DnsSdRegistration Register(string instanceName, string hostName, IPAddress address, ushort port, IReadOnlyDictionary<string, string> text, ILogger logger)
    {
        var registration = new DnsSdRegistration(instanceName, hostName, address, port, text, logger);
        var status = DnsServiceRegister(registration._request, 0);

        if (status != _requestPending)
        {
            logger.LogWarning("DNS-SD registration of {Instance} failed: {Status}", instanceName, status);
        }

        return registration;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_instance == 0)
        {
            return;
        }

        _ = DnsServiceDeRegister(_request, 0);
        DnsServiceFreeInstance(_instance);
        Marshal.FreeHGlobal(_ip4);
        Marshal.FreeHGlobal(_request);
        _instance = 0;
        _ip4 = 0;
        _request = 0;
    }

    private void OnRegisterComplete(uint status, nint context, nint instance) => _logger.LogInformation("DNS-SD registration completed: {Status}", status);

    [StructLayout(LayoutKind.Sequential)]
    private struct RegisterRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public nint ServiceInstance;
        public nint RegisterCompletionCallback;
        public nint QueryContext;
        public nint Credentials;
        public int UnicastEnabled;
    }

#pragma warning disable SYSLIB1054 // string[] marshalling needs the classic DllImport.
    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode)]
    private static extern nint DnsServiceConstructInstance(string serviceName, string hostName, nint ip4, nint ip6, ushort port, ushort priority, ushort weight, uint propertiesCount, string[] keys, string[] values);
#pragma warning restore SYSLIB1054

    [LibraryImport("dnsapi.dll")]
    private static partial uint DnsServiceRegister(nint request, nint cancel);

    [LibraryImport("dnsapi.dll")]
    private static partial uint DnsServiceDeRegister(nint request, nint cancel);

    [LibraryImport("dnsapi.dll")]
    private static partial void DnsServiceFreeInstance(nint instance);
}
