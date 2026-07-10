namespace RealLifeServer.Application.Streams.StateMachine;

/// <summary>
/// The counters the state machine needs to evaluate guard conditions (timeouts, recovery
/// hysteresis). Owned and incremented by <c>SceneMonitorHostedService</c> - the state machine
/// itself is stateless and reads these as plain input.
/// </summary>
public sealed record SceneContext(
    TimeSpan TimeInCurrentState,
    int ConsecutiveGoodSamples);
