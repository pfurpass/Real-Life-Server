using System.Text.Json.Serialization;

namespace RealLifeServer.Domain.Enums;

/// <summary>
/// Serialized as its member name ("Twitch", "YouTube", "Custom"), not the numeric default -
/// without this, System.Text.Json only accepts an integer for this property, so the frontend's
/// {"platform":"YouTube"} payload fails JSON model binding before the controller (and any
/// application-layer validation) ever runs, surfacing as a bare "One or more validation errors
/// occurred." with nothing in the API logs. Integer values (0/1/2) still deserialize fine -
/// JsonStringEnumConverter accepts both by default.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StreamPlatform
{
    Twitch = 0,
    YouTube = 1,
    Custom = 2
}
