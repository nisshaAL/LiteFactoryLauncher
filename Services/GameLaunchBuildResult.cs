using LiteFactoryLauncher.Models;

namespace LiteFactoryLauncher.Services;

public sealed class GameLaunchBuildResult
{
    private GameLaunchBuildResult(GameLaunchPlan? plan, GameLaunchValidationState state, string? errorMessage)
    {
        Plan = plan;
        State = state;
        ErrorMessage = errorMessage;
    }

    public GameLaunchPlan? Plan { get; }

    public GameLaunchValidationState State { get; }

    public string? ErrorMessage { get; }

    public static GameLaunchBuildResult FromPlan(GameLaunchPlan plan)
    {
        return new GameLaunchBuildResult(plan, plan.ValidationState, null);
    }

    public static GameLaunchBuildResult Failed(string message, GameLaunchPlan? plan = null)
    {
        return new GameLaunchBuildResult(plan, GameLaunchValidationState.RuntimeInvalid, message);
    }
}
