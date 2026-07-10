using RealLifeServer.Domain.Common;

namespace RealLifeServer.Domain.Entities;

public class AuditLogEntry : BaseEntity
{
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? DetailsJson { get; set; }
}
