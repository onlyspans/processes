using Stateless;
using Onlyspans.Processes.Api.Features.Parsing.Models;

namespace Onlyspans.Processes.Api.Features.Pipeline;

/// <summary>
/// Runtime state machine for pipeline execution.
/// Each step is a state; transitions are driven by step outcomes.
/// </summary>
public sealed class PipelineStateMachine
{
    public const string StateNotStarted = "__not_started__";
    public const string StateCompleted  = "__completed__";
    public const string StateFailed     = "__failed__";

    private readonly StateMachine<string, string> _machine;
    private readonly List<(string Name, StepDefinition Definition, int Order)> _steps;
    private string _currentState;

    public string CurrentState => _currentState;
    public bool IsTerminal => _currentState is StateCompleted or StateFailed;

    public PipelineStateMachine(List<(string Name, StepDefinition Definition, int Order)> steps)
    {
        _steps        = steps;
        _currentState = StateNotStarted;
        _machine      = new StateMachine<string, string>(() => _currentState, s => _currentState = s);

        Configure();
    }

    public void Start() => _machine.Fire(Trigger.Start);
    public void StepSucceeded() => _machine.Fire(Trigger.StepSucceeded);
    public void StepFailed() => _machine.Fire(Trigger.StepFailed);
    public void StepSkipped() => _machine.Fire(Trigger.StepSkipped);

    public async Task<IEnumerable<string>> GetPermittedTriggersAsync() =>
        await _machine.GetPermittedTriggersAsync();

    public (string Name, StepDefinition Definition)? GetCurrentStep()
    {
        var step = _steps.FirstOrDefault(s => s.Name == _currentState);
        return step.Name is null ? null : (step.Name, step.Definition);
    }

    private void Configure()
    {
        _machine.Configure(StateNotStarted)
            .Permit(Trigger.Start, _steps[0].Name);

        for (var i = 0; i < _steps.Count; i++)
        {
            var (name, def, _) = _steps[i];
            var next = i < _steps.Count - 1 ? _steps[i + 1].Name : StateCompleted;

            var config = _machine.Configure(name);
            config.Permit(Trigger.StepSucceeded, next);

            var failTarget = def.OnFailure?.ToLowerInvariant() == "continue" ? next : StateFailed;
            config.Permit(Trigger.StepFailed, failTarget);

            if (def.Optional)
                config.Permit(Trigger.StepSkipped, next);
        }

        _machine.Configure(StateCompleted);
        _machine.Configure(StateFailed);
    }

    public static class Trigger
    {
        public const string Start        = "Start";
        public const string StepSucceeded = "StepSucceeded";
        public const string StepFailed   = "StepFailed";
        public const string StepSkipped  = "StepSkipped";
    }
}
