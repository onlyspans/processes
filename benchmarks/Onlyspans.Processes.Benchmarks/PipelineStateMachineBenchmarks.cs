using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Onlyspans.Processes.Api.Features.Parsing.Models;
using Onlyspans.Processes.Api.Features.Pipeline;

namespace Onlyspans.Processes.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90, warmupCount: 5, iterationCount: 20)]
public class PipelineStateMachineBenchmarks
{
    [Params(1, 3, 10, 30)]
    public int StepCount { get; set; }

    private List<(string Name, StepDefinition Definition, int Order)> _steps = null!;

    [GlobalSetup]
    public void Setup()
    {
        _steps = Enumerable.Range(0, StepCount)
            .Select(i => (
                Name: $"step-{i}",
                Definition: (StepDefinition)new()
                {
                    Script = $"echo step-{i}",
                    OnFailure = null,
                    Optional = false,
                },
                Order: i
            ))
            .ToList();
    }

    [Benchmark]
    public string FullPipelineRun()
    {
        var fsm = new PipelineStateMachine(_steps);
        fsm.Start();

        for (var i = 0; i < StepCount; i++)
            fsm.StepSucceeded();

        return fsm.CurrentState;
    }

    [Benchmark]
    public string RollbackPath()
    {
        var rollbackSteps = _steps
            .Select(s => (
                s.Name,
                (StepDefinition)new()
                {
                    Script = s.Definition.Script,
                    OnFailure = "rollback",
                    Optional = false,
                },
                s.Order))
            .ToList();

        var fsm = new PipelineStateMachine(rollbackSteps);
        fsm.Start();
        fsm.StepFailed();
        fsm.RollbackCompleted();

        return fsm.CurrentState;
    }
}
