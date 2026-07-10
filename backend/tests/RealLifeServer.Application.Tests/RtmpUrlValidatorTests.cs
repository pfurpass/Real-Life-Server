using RealLifeServer.Application.Common.Utils;
using Xunit;

namespace RealLifeServer.Application.Tests;

public class RtmpUrlValidatorTests
{
    [Theory]
    [InlineData("rtmp://live.twitch.tv/app")]
    [InlineData("rtmps://a.rtmp.youtube.com/live2")]
    [InlineData("rtmp://127.0.0.1:1935/live")]
    public void IsValid_AcceptsRtmpAndRtmpsUrls(string url)
    {
        Assert.True(RtmpUrlValidator.IsValid(url));
    }

    [Theory]
    [InlineData("http://live.twitch.tv/app")]
    [InlineData("https://live.twitch.tv/app")]
    [InlineData("not a url")]
    [InlineData("rtmp://")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("ftp://example.com")]
    public void IsValid_RejectsEverythingElse(string? url)
    {
        Assert.False(RtmpUrlValidator.IsValid(url));
    }
}
