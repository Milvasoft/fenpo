using Fenpo.Bluetooth.Ams;
using Fenpo.Bluetooth.Ancs;
using Fenpo.Bluetooth.Link;
using Fenpo.Bluetooth.Map;
using Fenpo.Bluetooth.Pbap;
using Fenpo.Bluetooth.Proximity;
using Fenpo.Bluetooth.Status;
using Fenpo.Core.Contacts;
using Fenpo.Core.Devices;
using Fenpo.Core.Media;
using Fenpo.Core.Messages;
using Fenpo.Core.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Fenpo.Bluetooth;

/// <summary>
/// Registers the iPhone Bluetooth sources.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the link, the ANCS/AMS clients behind the source-independent interfaces, the status tracker, RSSI monitoring
    /// and the hosted service that runs them.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddFenpoBluetooth(this IServiceCollection services) => services
        .AddSingleton<PairedPhoneLocator>()
        .AddSingleton<IphoneLink>()
        .AddSingleton<AncsClient>()
        .AddSingleton<INotificationSource>(sp => sp.GetRequiredService<AncsClient>())
        .AddSingleton<AmsClient>()
        .AddSingleton<IMediaSource>(sp => sp.GetRequiredService<AmsClient>())
        .AddSingleton<MnsServer>()
        .AddSingleton<MapMessageSource>()
        .AddSingleton<IMessageSource>(sp => sp.GetRequiredService<MapMessageSource>())
        .AddSingleton<PbapClientRecord>()
        .AddSingleton<PhonebookSource>()
        .AddSingleton<IContactSource>(sp => sp.GetRequiredService<PhonebookSource>())
        .AddSingleton<PhoneStatusTracker>()
        .AddSingleton<IPhoneStatusSource>(sp => sp.GetRequiredService<PhoneStatusTracker>())
        .AddSingleton<AdvertisementRssiMonitor>()
        .AddSingleton<BluetoothHostedService>()
        .AddHostedService(sp => sp.GetRequiredService<BluetoothHostedService>());
}
