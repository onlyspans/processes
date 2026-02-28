using Onlyspans.Processes.Api.Features.Parsing.Models;

namespace Onlyspans.Processes.Api.Features.Parsing;

public interface IProcessDefinitionParser
{
    ProcessDefinition Parse(string yaml);
}
