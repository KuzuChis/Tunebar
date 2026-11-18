using Tunebar.Bridge;

namespace Tunebar.Tests;

public class OriginValidatorTests
{
    private const string Id = "abcdefghijklmnopabcdefghijklmnop";
    private readonly OriginValidator _validator = new(Id);

    [Fact]
    public void Exact_extension_origin_is_allowed() =>
        Assert.True(_validator.IsAllowed("chrome-extension://" + Id));

    [Theory]
    [InlineData(null)]
    [InlineData("https://www.youtube.com")]
    [InlineData("chrome-extension://ponmlkjihgfedcbaponmlkjihgfedcba")]
    [InlineData("chrome-extension://abcdefghijklmnopabcdefghijklmnop.evil")]
    public void Other_origins_are_rejected(string? origin) =>
        Assert.False(_validator.IsAllowed(origin));
}
