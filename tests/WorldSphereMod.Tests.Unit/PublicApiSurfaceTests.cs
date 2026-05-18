using Xunit;
using FluentAssertions;

public class PublicApiSurfaceTests
{
    [Fact]
    public void Connect_returns_false_when_no_host_present()
    {
        var result = WorldSphereAPI.Connect(out var api);
        result.Should().BeFalse();
        api.Should().BeNull();
    }
}
