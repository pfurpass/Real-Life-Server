using RealLifeServer.Application.Common.Interfaces;

namespace RealLifeServer.Infrastructure;

public class DateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
