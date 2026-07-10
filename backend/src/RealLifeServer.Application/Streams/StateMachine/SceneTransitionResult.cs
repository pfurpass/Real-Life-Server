using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Streams.StateMachine;

public sealed record SceneTransitionResult(
    SceneState PreviousState,
    SceneState NewState,
    bool Changed,
    SceneTrigger Trigger)
{
    public static SceneTransitionResult Unchanged(SceneState current, SceneTrigger trigger) =>
        new(current, current, false, trigger);
}
