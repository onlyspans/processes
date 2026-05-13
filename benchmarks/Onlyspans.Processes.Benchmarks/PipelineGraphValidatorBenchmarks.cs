using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Onlyspans.Processes.Api.Features.Parsing.Models;
using Onlyspans.Processes.Api.Features.Validation;

namespace Onlyspans.Processes.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90, warmupCount: 5, iterationCount: 20)]
public class PipelineGraphValidatorBenchmarks
{
    [Params(1, 3, 10, 30)]
    public int StepCount { get; set; }

    private ProcessDefinition _definition = null!;
    private PipelineGraphValidator _validator = null!;

    [GlobalSetup]
    public void Setup()
    {
        _validator = new PipelineGraphValidator();

        var steps = new Dictionary<string, StepDefinition>();
        for (var i = 0; i < StepCount; i++)
        {
            steps[$"step-{i}"] = new StepDefinition
            {
                Script = $"echo step-{i}",
                OnFailure = i % 3 == 0 ? "rollback" : null,
                Optional = i % 5 == 0,
            };
        }

        _definition = new ProcessDefinition { Steps = steps };
    }

    [Benchmark]
    public async Task<int> ValidateGraph()
    {
        var result = await _validator.ValidateAsync(_definition);
        return result.Errors.Count;
    }
}
