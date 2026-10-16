using Fenpo.Core.Notifications;

namespace Fenpo.Core.Tests.Notifications;

public sealed class TextCleanupTests
{
    [Theory]
    [InlineData("‎WhatsApp", "WhatsApp")]
    [InlineData("Bu bir örnek bülten önizlemesidir. ­ ­ ­ ­ ", "Bu bir örnek bülten önizlemesidir.")]
    [InlineData("  Akşam   görüşelim mi?  ", "Akşam görüşelim mi?")]
    [InlineData(null, "")]
    [InlineData("Tamam 👍", "Tamam 👍")]
    public void CleansSingleLine(string? input, string expected) => Assert.Equal(expected, TextCleanup.Clean(input, singleLine: true));

    [Fact]
    public void KeepsParagraphsWhenAsked()
    {
        Assert.Equal("Birinci satır\nİkinci satır", TextCleanup.Clean("Birinci satır  \r\nİkinci satır", singleLine: false));
        Assert.Equal("Birinci satır İkinci satır", TextCleanup.Clean("Birinci satır\nİkinci satır", singleLine: true));
    }
}
