using Fenpo.Core.Files;
using Fenpo.Core.Settings;

namespace Fenpo.Core.Tests.Files;

public sealed class SharedPathsTests
{
    private static readonly string _base = Path.Combine(Path.GetTempPath(), "fenpo-shared", "Belgeler");
    private static readonly SharedFolder[] _shared = [new("Belgeler", _base, false), new("Fotoğraflar", Path.Combine(Path.GetTempPath(), "fenpo-shared", "Fotos"), true)];

    private static string? Resolve(string path, Func<string, bool>? isLink = null) => SharedPaths.Resolve(_shared, path, isLink ?? (_ => false), out _);

    [Theory]
    [InlineData("Belgeler", "")]
    [InlineData("Belgeler/Vergi", "Vergi")]
    [InlineData("belgeler/Vergi/2026.pdf", "Vergi\\2026.pdf")]
    [InlineData("/Belgeler//Vergi/", "Vergi")]
    public void InsideASharedFolderResolves(string path, string relative) => Assert.Equal(Path.Combine(_base, relative).TrimEnd('\\'), Resolve(path));

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("Belgeler/..")]
    [InlineData("Belgeler/../Fotos")]
    [InlineData("Belgeler/Vergi/../../..")]
    [InlineData("Belgeler/ .. ")]
    [InlineData("Belgeler/.")]
    [InlineData("Belgeler\\..\\..")]
    [InlineData("C:/Windows")]
    [InlineData("Belgeler/C:")]
    [InlineData("Başka/dosya.txt")]
    [InlineData("Belgeler/a\u0000b")]
    [InlineData("Belgeler/a|b")]
    public void AnythingElseIsRefused(string path) => Assert.Null(Resolve(path));

    [Fact]
    public void ALinkBelowTheSharedFolderIsRefused()
    {
        var link = Path.Combine(_base, "Kısayol");

        Assert.Null(Resolve("Belgeler/Kısayol/gizli.txt", p => p == link));
        Assert.NotNull(Resolve("Belgeler/Başka/gizli.txt", p => p == link));
    }

    [Fact]
    public void TheRootFolderComesBack()
    {
        SharedPaths.Resolve(_shared, "Fotoğraflar/IMG_1.jpg", _ => false, out var root);

        Assert.True(root?.ReadOnly);
    }
}
