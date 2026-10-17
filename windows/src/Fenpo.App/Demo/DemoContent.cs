using System.Globalization;
using Fenpo.Core.Calls;
using Fenpo.Core.Notifications;

namespace Fenpo.App.Demo;

/// <summary>
/// A made-up message in a demo conversation.
/// </summary>
/// <param name="Outgoing">True when sent from the phone.</param>
/// <param name="Text">Text.</param>
/// <param name="Ago">How long ago it was sent.</param>
public sealed record DemoMessage(bool Outgoing, string Text, TimeSpan Ago);

/// <summary>
/// A made-up conversation.
/// </summary>
/// <param name="Name">Contact name.</param>
/// <param name="Number">Phone number, from a range set aside for fiction.</param>
/// <param name="Messages">Messages, oldest first.</param>
public sealed record DemoConversation(string Name, string Number, IReadOnlyList<DemoMessage> Messages);

/// <summary>
/// A made-up notification.
/// </summary>
/// <param name="AppId">App id with no real app behind it, so no cached icon or logo shows.</param>
/// <param name="AppName">App name, a plain word.</param>
/// <param name="Category">Category (filters, calls).</param>
/// <param name="Title">Title.</param>
/// <param name="Message">Text.</param>
/// <param name="Ago">How long ago it arrived.</param>
public sealed record DemoNotification(string AppId, string AppName, NotificationCategory Category, string Title, string Message, TimeSpan Ago);

/// <summary>
/// A made-up file transfer.
/// </summary>
/// <param name="Name">File name.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Incoming">True when it came from the phone.</param>
/// <param name="Done">Bytes done; equal to <paramref name="Size"/> when finished.</param>
/// <param name="Ago">How long ago it started.</param>
public sealed record DemoTransfer(string Name, ulong Size, bool Incoming, ulong Done, TimeSpan Ago);

/// <summary>
/// A made-up clipboard entry.
/// </summary>
/// <param name="Text">Text.</param>
/// <param name="FromPhone">True when copied on the phone.</param>
/// <param name="Ago">How long ago it was copied.</param>
public sealed record DemoClip(string Text, bool FromPhone, TimeSpan Ago);

/// <summary>
/// Everything the demo shows, in Turkish or English. Names, numbers, files and songs are invented; numbers come from
/// ranges kept for fiction and app names are plain words without another company's logo.
/// </summary>
public sealed record DemoContent
{
    /// <summary>Gets the phone name.</summary>
    public required string PhoneName { get; init; }

    /// <summary>Gets the PC name the phone sees.</summary>
    public required string PcName { get; init; }

    /// <summary>Gets the folder shown for received files.</summary>
    public required string IncomingFolder { get; init; }

    /// <summary>Gets the notifications, newest first.</summary>
    public required IReadOnlyList<DemoNotification> Notifications { get; init; }

    /// <summary>Gets the conversations.</summary>
    public required IReadOnlyList<DemoConversation> Conversations { get; init; }

    /// <summary>Gets the recent calls.</summary>
    public required IReadOnlyList<(string Name, CallKind Kind, TimeSpan Ago)> Calls { get; init; }

    /// <summary>Gets the player name.</summary>
    public required string PlayerName { get; init; }

    /// <summary>Gets the song title.</summary>
    public required string SongTitle { get; init; }

    /// <summary>Gets the artist.</summary>
    public required string SongArtist { get; init; }

    /// <summary>Gets the album.</summary>
    public required string SongAlbum { get; init; }

    /// <summary>Gets the file transfers, newest first.</summary>
    public required IReadOnlyList<DemoTransfer> Transfers { get; init; }

    /// <summary>Gets the clipboard entries, newest first.</summary>
    public required IReadOnlyList<DemoClip> Clips { get; init; }

    /// <summary>
    /// Gets the content in the language the app runs in.
    /// </summary>
    public static DemoContent Current { get; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? Turkish() : English();

    private static TimeSpan Minutes(double value) => TimeSpan.FromMinutes(value);

    private static DemoContent Turkish() => new()
    {
        PhoneName = "Ayşe’nin iPhone’u",
        PcName = "Ofis PC",
        IncomingFolder = @"D:\Fenpo\Gelenler",
        Notifications =
        [
            new("demo.messages", "Mesajlar", NotificationCategory.Social, "Deniz Yılmaz", "Akşam 7’de sahilde buluşuyoruz, unutma!", Minutes(2)),
            new("demo.events", "Takvim", NotificationCategory.Schedule, "Ekip toplantısı", "15:30 · Toplantı odası 2", Minutes(9)),
            new("demo.messages", "Mesajlar", NotificationCategory.Social, "Can Demir", "Sunumun son halini gönderebilir misin?", Minutes(21)),
            new("demo.calls", "Telefon", NotificationCategory.MissedCall, "Selin Aydın", "Cevapsız arama", Minutes(38)),
            new("demo.mail", "Posta", NotificationCategory.Email, "Elif Kaya", "Haftalık rapor · Bu haftanın özetini ekledim.", Minutes(52)),
            new("demo.notes", "Anımsatıcılar", NotificationCategory.Other, "Market alışverişi", "Süt, ekmek, zeytin, domates", Minutes(85)),
        ],
        Conversations =
        [
            new("Deniz Yılmaz", "+90 500 010 20 30",
            [
                new(false, "Bu akşam müsait misin?", Minutes(30)),
                new(true, "Evet, 7 gibi çıkarım.", Minutes(26)),
                new(false, "Süper! Ben de Mert’i çağırdım.", Minutes(5)),
                new(false, "Akşam 7’de sahilde buluşuyoruz, unutma!", Minutes(2)),
            ]),
            new("Can Demir", "+90 500 010 20 31",
            [
                new(false, "Toplantı 15:30’a alındı.", Minutes(64)),
                new(true, "Tamam, sunumu yanımda getiririm.", Minutes(60)),
                new(false, "Sunumun son halini gönderebilir misin?", Minutes(21)),
            ]),
            new("Selin Aydın", "+90 500 010 20 32",
            [
                new(false, "Hafta sonu kahvaltıya geliyor musunuz?", TimeSpan.FromHours(26)),
                new(true, "Geliyoruz, ben de börek getiririm.", TimeSpan.FromHours(25.5)),
                new(false, "Harika, görüşürüz!", TimeSpan.FromHours(25)),
            ]),
        ],
        Calls =
        [
            ("Selin Aydın", CallKind.Missed, Minutes(38)),
            ("Can Demir", CallKind.Incoming, TimeSpan.FromHours(3)),
            ("Deniz Yılmaz", CallKind.Incoming, TimeSpan.FromHours(22)),
            ("Elif Kaya", CallKind.Missed, TimeSpan.FromHours(27)),
        ],
        PlayerName = "Müzik",
        SongTitle = "Kıyıda Bir Akşam",
        SongArtist = "Deniz Işıkları",
        SongAlbum = "Sahil Yolu",
        Transfers =
        [
            new("Teklif_2026.pdf", 3_250_585, Incoming: false, Done: 2_016_000, Minutes(0)),
            new("Sahilde_Gün_Batımı.jpg", 4_404_019, Incoming: true, Done: 4_404_019, Minutes(4)),
            new("Ekim_Sunumu.pptx", 5_872_025, Incoming: false, Done: 5_872_025, Minutes(17)),
            new("Tatil_Videosu.mov", 192_937_984, Incoming: true, Done: 192_937_984, TimeSpan.FromHours(2)),
        ],
        Clips =
        [
            new("Lale Sokak No: 12, Kat 3", FromPhone: true, Minutes(1)),
            new("Toplantı notları: bütçe onayı cuma günü", FromPhone: false, Minutes(6)),
            new("482 913", FromPhone: true, Minutes(14)),
        ],
    };

    private static DemoContent English() => new()
    {
        PhoneName = "Office phone",
        PcName = "Office PC",
        IncomingFolder = @"D:\Fenpo\Incoming",
        Notifications =
        [
            new("demo.messages", "Messages", NotificationCategory.Social, "Maya Collins", "Dinner by the pier at 7, don’t forget!", Minutes(2)),
            new("demo.events", "Calendar", NotificationCategory.Schedule, "Team meeting", "3:30 PM · Room 2", Minutes(9)),
            new("demo.messages", "Messages", NotificationCategory.Social, "Daniel Reed", "Can you send me the final slides?", Minutes(21)),
            new("demo.calls", "Phone", NotificationCategory.MissedCall, "Priya Shah", "Missed call", Minutes(38)),
            new("demo.mail", "Mail", NotificationCategory.Email, "Laura Bennett", "Weekly report · I attached this week’s summary.", Minutes(52)),
            new("demo.notes", "Reminders", NotificationCategory.Other, "Groceries", "Milk, bread, olives, tomatoes", Minutes(85)),
        ],
        Conversations =
        [
            new("Maya Collins", "+1 415 555 0134",
            [
                new(false, "Are you free tonight?", Minutes(30)),
                new(true, "Yes, I can leave around 7.", Minutes(26)),
                new(false, "Great! I asked Tom to come too.", Minutes(5)),
                new(false, "Dinner by the pier at 7, don’t forget!", Minutes(2)),
            ]),
            new("Daniel Reed", "+1 415 555 0162",
            [
                new(false, "The meeting moved to 3:30.", Minutes(64)),
                new(true, "OK, I’ll bring the slides.", Minutes(60)),
                new(false, "Can you send me the final slides?", Minutes(21)),
            ]),
            new("Priya Shah", "+1 415 555 0178",
            [
                new(false, "Brunch this weekend?", TimeSpan.FromHours(26)),
                new(true, "We’re in! I’ll bring pastries.", TimeSpan.FromHours(25.5)),
                new(false, "Perfect, see you then!", TimeSpan.FromHours(25)),
            ]),
        ],
        Calls =
        [
            ("Priya Shah", CallKind.Missed, Minutes(38)),
            ("Daniel Reed", CallKind.Incoming, TimeSpan.FromHours(3)),
            ("Maya Collins", CallKind.Incoming, TimeSpan.FromHours(22)),
            ("Laura Bennett", CallKind.Missed, TimeSpan.FromHours(27)),
        ],
        PlayerName = "Music",
        SongTitle = "Evening Tide",
        SongArtist = "Harbor Lights",
        SongAlbum = "Coast Road",
        Transfers =
        [
            new("Proposal_2026.pdf", 3_250_585, Incoming: false, Done: 2_016_000, Minutes(0)),
            new("Beach_Sunset.jpg", 4_404_019, Incoming: true, Done: 4_404_019, Minutes(4)),
            new("October_Review.pptx", 5_872_025, Incoming: false, Done: 5_872_025, Minutes(17)),
            new("Holiday_Video.mov", 192_937_984, Incoming: true, Done: 192_937_984, TimeSpan.FromHours(2)),
        ],
        Clips =
        [
            new("12 Harbor Street, 3rd floor", FromPhone: true, Minutes(1)),
            new("Meeting notes: budget sign-off on Friday", FromPhone: false, Minutes(6)),
            new("482 913", FromPhone: true, Minutes(14)),
        ],
    };
}
