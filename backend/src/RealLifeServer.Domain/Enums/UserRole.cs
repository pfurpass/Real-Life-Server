using System.Text.Json.Serialization;

namespace RealLifeServer.Domain.Enums;

/// <summary>Serialized as its member name - the frontend's UserRole type is already a string union.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserRole
{
    Admin = 0,
    Moderator = 1,
    Streamer = 2
}
