using Stateless;
using Onlyspans.Processes.Api.Features.Parsing.Models;

namespace Onlyspans.Processes.Api.Features.Validation;

/// <summary>
/// Builds a finite state machine from the process definition using Stateless
/// and validates structural properties: reachability, deadlock-freedom, completability.
/// </summary>
public sealed class PipelineGraphValidator
{
    private const string StateNotStarted = "__not_started__";
    private const string StateCompleted  = "__completed__";
    private const string StateFailed     = "__failed__";

    private const string TriggerStart        = "Start";
    private const string TriggerStepSuccess  = "StepSucceeded";
    private const string TriggerStepFailed   = "StepFailed";
    private const string TriggerStepSkipped  = "StepSkipped";

    public async Task<PipelineValidationResult> ValidateAsync(ProcessDefinition definition)
    {
        var errors = new List<string>();
        var orderedSteps = definition.GetOrderedSteps();

        if (orderedSteps.Count == 0)
        {
            errors.Add("No steps defined — pipeline has no reachable states");
            return new PipelineValidationResult(errors);
        }

        var graph = BuildTransitionGraph(orderedSteps);

        ValidateReachability(graph, orderedSteps, errors);
        ValidateCompletability(graph, orderedSteps, errors);
        ValidateNoDeadEnds(graph, orderedSteps, errors);

        await BuildAndVerifyStateMachineAsync(orderedSteps, errors);

        return new PipelineValidationResult(errors);
    }

    /// <summary>
    /// Builds a directed graph: node -> set of reachable nodes.
    /// Models transitions based on on_failure and optional flags.
    /// </summary>
    private static Dictionary<string, HashSet<string>> BuildTransitionGraph(
        List<(string Name, StepDefinition Definition, int Order)> steps)
    {
        var graph = new Dictionary<string, HashSet<string>>();

        graph[StateNotStarted] = [steps[0].Name];
        graph[StateCompleted]  = [];
        graph[StateFailed]     = [];

        for (var i = 0; i < steps.Count; i++)
        {
            var (name, def, _) = steps[i];
            var next = i < steps.Count - 1 ? steps[i + 1].Name : StateCompleted;

            var edges = new HashSet<string> { next };

            var onFailure = (def.OnFailure?.ToLowerInvariant()) switch
            {
                "continue" => next,
                "rollback" => StateFailed,
                _          => StateFailed,
            };
            edges.Add(onFailure);

            if (def.Optional)
                edges.Add(next);

            graph[name] = edges;
        }

        return graph;
    }

    private static void ValidateReachability(
        Dictionary<string, HashSet<string>> graph,
        List<(string Name, StepDefinition Definition, int Order)> steps,
        List<string> errors)
    {
        var reachable = Bfs(graph, StateNotStarted);

        foreach (var (name, _, _) in steps)
        {
            if (!reachable.Contains(name))
                errors.Add($"Step '{name}' is not reachable from the pipeline start");
        }
    }

    private static void ValidateCompletability(
        Dictionary<string, HashSet<string>> graph,
        List<(string Name, StepDefinition Definition, int Order)> steps,
        List<string> errors)
    {
        foreach (var (name, _, _) in steps)
        {
            var reachableFromStep = Bfs(graph, name);
            if (!reachableFromStep.Contains(StateCompleted) &&
                !reachableFromStep.Contains(StateFailed))
            {
                errors.Add($"Step '{name}' has no path to a terminal state (completed or failed)");
            }
        }
    }

    private static void ValidateNoDeadEnds(
        Dictionary<string, HashSet<string>> graph,
        List<(string Name, StepDefinition Definition, int Order)> steps,
        List<string> errors)
    {
        foreach (var (name, _, _) in steps)
        {
            if (!graph.TryGetValue(name, out var edges) || edges.Count == 0)
                errors.Add($"Step '{name}' is a dead-end with no outgoing transitions");
        }
    }

    /// <summary>
    /// Builds an actual Stateless state machine to verify that all transitions
    /// are consistent and no configuration exceptions occur.
    /// </summary>
    private static async Task BuildAndVerifyStateMachineAsync(
        List<(string Name, StepDefinition Definition, int Order)> steps,
        List<string> errors)
    {
        try
        {
            var currentState = StateNotStarted;
            var machine = new StateMachine<string, string>(() => currentState, s => currentState = s);

            machine.Configure(StateNotStarted)
                .Permit(TriggerStart, steps[0].Name);

            for (var i = 0; i < steps.Count; i++)
            {
                var (name, def, _) = steps[i];
                var next = i < steps.Count - 1 ? steps[i + 1].Name : StateCompleted;

                var config = machine.Configure(name);
                config.Permit(TriggerStepSuccess, next);

                var failTarget = def.OnFailure?.ToLowerInvariant() == "continue" ? next : StateFailed;

                if (failTarget != next)
                    config.Permit(TriggerStepFailed, failTarget);
                else
                    config.PermitReentry(TriggerStepFailed);

                if (def.Optional)
                {
                    if (next != failTarget)
                        config.Permit(TriggerStepSkipped, next);
                }
            }

            machine.Configure(StateCompleted);
            machine.Configure(StateFailed);

            machine.Fire(TriggerStart);
            var permittedAfterStart = (await machine.GetPermittedTriggersAsync()).ToList();

            if (permittedAfterStart.Count == 0)
                errors.Add("After starting, the pipeline has no permitted transitions");
        }
        catch (Exception ex)
        {
            errors.Add($"FSM configuration error: {ex.Message}");
        }
    }

    private static HashSet<string> Bfs(Dictionary<string, HashSet<string>> graph, string start)
    {
        var visited = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!graph.TryGetValue(node, out var neighbors)) continue;

            foreach (var neighbor in neighbors.Where(n => visited.Add(n)))
                queue.Enqueue(neighbor);
        }

        return visited;
    }
}

public sealed record PipelineValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
