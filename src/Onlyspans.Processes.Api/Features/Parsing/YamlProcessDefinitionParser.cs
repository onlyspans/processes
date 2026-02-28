using Onlyspans.Processes.Api.Features.Parsing.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Onlyspans.Processes.Api.Features.Parsing;

public sealed class YamlProcessDefinitionParser : IProcessDefinitionParser
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    public ProcessDefinition Parse(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        return _deserializer.Deserialize<ProcessDefinition>(yaml);
    }
}
