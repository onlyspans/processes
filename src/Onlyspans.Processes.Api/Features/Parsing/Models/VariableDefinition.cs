using YamlDotNet.Serialization;

namespace Onlyspans.Processes.Api.Features.Parsing.Models;

public sealed class VariableDefinition
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = null!;

    [YamlMember(Alias = "value")]
    public string? Value { get; set; }

    /// <summary>
    /// "secrets" — resolve via Variables service;
    /// null / empty — inline value expected.
    /// </summary>
    [YamlMember(Alias = "source")]
    public string? Source { get; set; }
}
