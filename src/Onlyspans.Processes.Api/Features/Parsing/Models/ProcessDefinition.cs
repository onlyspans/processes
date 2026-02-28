using YamlDotNet.Serialization;

namespace Onlyspans.Processes.Api.Features.Parsing.Models;

public sealed class ProcessDefinition
{
    [YamlMember(Alias = "variables")]
    public List<VariableDefinition>? Variables { get; set; }

    [YamlMember(Alias = "steps")]
    public Dictionary<string, StepDefinition>? Steps { get; set; }

    public List<(string Name, StepDefinition Definition, int Order)> GetOrderedSteps()
    {
        if (Steps is null or { Count: 0 })
            return [];

        return Steps
            .Select((kvp, idx) => (kvp.Key, kvp.Value, idx))
            .ToList();
    }
}
