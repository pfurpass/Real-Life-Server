using System.Text.Json;
using RealLifeServer.Api.Controllers;
using RealLifeServer.Domain.Enums;
using Xunit;

namespace RealLifeServer.Api.Tests;

/// <summary>
/// Regression coverage for the reported bug: adding a Twitch or YouTube destination failed with
/// a bare "One or more validation errors occurred." and nothing in the API logs. Root cause:
/// StreamPlatform had no JsonStringEnumConverter, so System.Text.Json only accepted an integer
/// for it - the frontend's {"platform":"YouTube"} payload failed JSON model binding before the
/// controller (and any application-layer validation) ever ran, which is also why nothing was
/// logged. These tests deserialize with JsonSerializerDefaults.Web - the same options preset
/// ASP.NET Core's MVC pipeline uses for request bodies - so they exercise the exact same
/// model-binding step that was actually broken, rather than the command handler (which was
/// never reached and was never the buggy layer).
/// </summary>
public class AddDestinationRequestJsonTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("Twitch", StreamPlatform.Twitch)]
    [InlineData("YouTube", StreamPlatform.YouTube)]
    [InlineData("Custom", StreamPlatform.Custom)]
    public void Deserialize_PlatformAsExactMemberNameString_Succeeds(string platformJson, StreamPlatform expected)
    {
        var json = $"{{\"platform\":\"{platformJson}\",\"rtmpUrl\":\"rtmp://a.rtmp.youtube.com/live2\",\"streamKey\":\"key\"}}";

        var request = JsonSerializer.Deserialize<AddDestinationRequest>(json, Options);

        Assert.NotNull(request);
        Assert.Equal(expected, request!.Platform);
        Assert.Equal("rtmp://a.rtmp.youtube.com/live2", request.RtmpUrl);
        Assert.Equal("key", request.StreamKey);
    }

    [Theory]
    [InlineData(0, StreamPlatform.Twitch)]
    [InlineData(1, StreamPlatform.YouTube)]
    [InlineData(2, StreamPlatform.Custom)]
    public void Deserialize_PlatformAsNumericValue_StillSucceeds(int platformJson, StreamPlatform expected)
    {
        // JsonStringEnumConverter accepts integers by default too - callers that still send the
        // numeric form (if any exist) keep working.
        var json = $"{{\"platform\":{platformJson},\"rtmpUrl\":\"rtmp://live.twitch.tv/app\",\"streamKey\":\"key\"}}";

        var request = JsonSerializer.Deserialize<AddDestinationRequest>(json, Options);

        Assert.NotNull(request);
        Assert.Equal(expected, request!.Platform);
    }

    [Fact]
    public void Deserialize_UnknownPlatformString_ThrowsAJsonExceptionNamingTheOffendingValue()
    {
        const string json = "{\"platform\":\"Kick\",\"rtmpUrl\":\"rtmp://kick.com/live\",\"streamKey\":\"key\"}";

        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AddDestinationRequest>(json, Options));

        // This is what ASP.NET Core turns into the ValidationProblemDetails "errors" entry the
        // frontend now surfaces (see extractErrorMessage) - it must at least name which field
        // failed so a reader can tell what went wrong, unlike the previous bare "validation
        // errors occurred." (STJ's exact wording for a failed constructor-parameter conversion
        // names the enclosing record type rather than StreamPlatform itself, so the JSON path is
        // the one part of the message that reliably identifies the offending field.)
        Assert.Contains("platform", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_Platform_RoundTripsAsTheMemberNameString()
    {
        var request = new AddDestinationRequest(StreamPlatform.YouTube, "rtmp://a.rtmp.youtube.com/live2", "key");

        var json = JsonSerializer.Serialize(request, Options);

        Assert.Contains("\"platform\":\"YouTube\"", json);
    }
}
