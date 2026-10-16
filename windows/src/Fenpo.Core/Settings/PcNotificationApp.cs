namespace Fenpo.Core.Settings;

/// <summary>
/// An app that showed notifications on the PC.
/// </summary>
/// <param name="Id">Windows app user model id.</param>
/// <param name="Name">Name shown in Fenpo.</param>
/// <param name="Send">Whether its notifications go to the phone.</param>
public sealed record PcNotificationApp(string Id, string Name, bool Send);
