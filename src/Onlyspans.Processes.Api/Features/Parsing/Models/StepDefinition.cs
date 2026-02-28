using YamlDotNet.Serialization;

namespace Onlyspans.Processes.Api.Features.Parsing.Models;

public sealed class StepDefinition
{
    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "script")]
    public string? Script { get; set; }

    [YamlMember(Alias = "script-path")]
    public string? ScriptPath { get; set; }

    [YamlMember(Alias = "type")]
    public string? Type { get; set; }

    [YamlMember(Alias = "optional")]
    public bool Optional { get; set; }

    [YamlMember(Alias = "blocking")]
    public bool Blocking { get; set; } = true;

    [YamlMember(Alias = "on_failure")]
    public string? OnFailure { get; set; }

    [YamlMember(Alias = "timeout")]
    public string? Timeout { get; set; }

    [YamlMember(Alias = "approvers")]
    public List<ApproverDefinition>? Approvers { get; set; }
}

public sealed class ApproverDefinition
{
    [YamlMember(Alias = "role")]
    public string? Role { get; set; }

    [YamlMember(Alias = "user")]
    public string? User { get; set; }
}
