using System.Security.Cryptography;
using Fenpo.Core.Transfers;
using Google.Protobuf;
using TransferFile = Fenpo.Protocol.V1.FileInfo;

namespace Fenpo.Core.Tests.Transfers;

public sealed class TransferStreamsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"fenpo-transfer-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task FilesRoundTripInOfferOrder()
    {
        var first = Write("a.txt", "first file");
        var second = Write("b.bin", new string('x', 300_000));
        using var wire = new MemoryStream();
        ulong written = 0;

        await TransferStreams.SendAsync(wire, [(first, (ulong)new FileInfo(first).Length), (second, (ulong)new FileInfo(second).Length)], done => written = done, TestContext.Current.CancellationToken);
        wire.Position = 0;
        var incoming = Path.Combine(_folder, "in");
        ulong read = 0;
        var saved = await TransferStreams.ReceiveAsync(wire, [Offer("a.txt", first), Offer("b.bin", second)], incoming, done => read = done, TestContext.Current.CancellationToken);

        Assert.Equal(2, saved.Count);
        Assert.Equal("first file", File.ReadAllText(saved[0]));
        Assert.Equal(new string('x', 300_000), File.ReadAllText(saved[1]));
        Assert.Equal(written, read);
        Assert.Empty(Directory.GetFiles(incoming, "*.fenpo-part"));
    }

    [Fact]
    public async Task ShortStreamLeavesNothingBehind()
    {
        var file = Write("a.txt", "0123456789");
        using var wire = new MemoryStream("01234"u8.ToArray());
        var incoming = Path.Combine(_folder, "in");

        await Assert.ThrowsAsync<EndOfStreamException>(() => TransferStreams.ReceiveAsync(wire, [Offer("a.txt", file)], incoming, _ => { }, TestContext.Current.CancellationToken));

        Assert.Empty(Directory.GetFiles(incoming));
    }

    [Fact]
    public async Task WrongHashIsRefusedAndEarlierFilesAreRemoved()
    {
        var good = Write("good.txt", "good");
        var bad = Write("bad.txt", "bad!");
        var badOffer = Offer("bad.txt", bad);
        badOffer.Sha256 = ByteString.CopyFrom(new byte[32]);
        using var wire = new MemoryStream("goodbad!"u8.ToArray());
        var incoming = Path.Combine(_folder, "in");

        await Assert.ThrowsAsync<InvalidDataException>(() => TransferStreams.ReceiveAsync(wire, [Offer("good.txt", good), badOffer], incoming, _ => { }, TestContext.Current.CancellationToken));

        Assert.Empty(Directory.GetFiles(incoming));
    }

    [Fact]
    public async Task ExistingFileIsNotOverwritten()
    {
        var incoming = Path.Combine(_folder, "in");
        Directory.CreateDirectory(incoming);
        File.WriteAllText(Path.Combine(incoming, "photo.jpg"), "old");
        using var wire = new MemoryStream("newnew"u8.ToArray());

        var saved = await TransferStreams.ReceiveAsync(wire, [new TransferFile { Name = "photo.jpg", Size = 3 }, new TransferFile { Name = "photo.jpg", Size = 3 }], incoming, _ => { }, TestContext.Current.CancellationToken);

        Assert.Equal("old", File.ReadAllText(Path.Combine(incoming, "photo.jpg")));
        Assert.Equal(["photo (2).jpg", "photo (3).jpg"], saved.Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("../../evil.exe", "evil.exe")]
    [InlineData(@"C:\Windows\win.ini", "win.ini")]
    [InlineData("a<b>c?.txt", "a_b_c_.txt")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("...", "file")]
    [InlineData("", "file")]
    [InlineData("rapor. ", "rapor")]
    [InlineData("Sözleşme_v3.pdf", "Sözleşme_v3.pdf")]
    public void NamesAreMadeSafe(string name, string expected) => Assert.Equal(expected, IncomingFileNames.Safe(name));

    [Fact]
    public void LongNamesKeepTheirExtension()
    {
        var safe = IncomingFileNames.Safe(new string('a', 300) + ".pdf");

        Assert.Equal(200, safe.Length);
        Assert.EndsWith(".pdf", safe, StringComparison.Ordinal);
    }

    private static TransferFile Offer(string name, string path) => new()
    {
        Name = name,
        Size = (ulong)new FileInfo(path).Length,
        Sha256 = ByteString.CopyFrom(SHA256.HashData(File.ReadAllBytes(path))),
    };

    private string Write(string name, string content)
    {
        Directory.CreateDirectory(Path.Combine(_folder, "out"));
        var path = Path.Combine(_folder, "out", name);
        File.WriteAllText(path, content);
        return path;
    }
}
