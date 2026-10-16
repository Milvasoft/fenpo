using Fenpo.Core.Devices;

namespace Fenpo.Core.Tests.Devices;

public sealed class ModelNamesTests
{
    [Theory]
    [InlineData("iPhone14,5", "iPhone 13")]
    [InlineData("iPhone18,4", "iPhone Air")]
    [InlineData("iPhone99,1", "iPhone")]
    [InlineData("iPad16,3", "iPad")]
    [InlineData("SM-G975F", "SM-G975F")]
    public void Resolves(string identifier, string expected) => Assert.Equal(expected, ModelNames.Resolve(identifier));

    [Fact]
    public void StatusExposesModelName() => Assert.Equal("iPhone 13", new PhoneStatus { ModelIdentifier = "iPhone14,5" }.ModelName);
}
