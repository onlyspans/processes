namespace Onlyspans.Processes.Api.Tests.Fixtures;

public static class YamlFixture
{
    private static readonly string BasePath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "Yaml");

    public static string Load(string fileName)
    {
        var path = Path.Combine(BasePath, fileName);

        if (!File.Exists(path))
            throw new FileNotFoundException($"YAML fixture not found: {path}");

        return File.ReadAllText(path);
    }

    public static string ValidSimple            => Load("valid-simple.yaml");
    public static string ValidFull              => Load("valid-full.yaml");
    public static string ValidWithVariables     => Load("valid-with-variables.yaml");
    public static string ValidWithApproval      => Load("valid-with-approval.yaml");
    public static string ValidWithScriptPath    => Load("valid-with-script-path.yaml");
    public static string ValidOptionalSteps     => Load("valid-optional-steps.yaml");
    public static string ValidContinueThenSucceed => Load("valid-continue-then-succeed.yaml");
    public static string ValidRollback            => Load("valid-rollback.yaml");

    public static string InvalidNoSteps                  => Load("invalid-no-steps.yaml");
    public static string InvalidEmptySteps               => Load("invalid-empty-steps.yaml");
    public static string InvalidBothScriptAndPath        => Load("invalid-both-script-and-path.yaml");
    public static string InvalidNoScriptNoPath           => Load("invalid-no-script-no-path.yaml");
    public static string InvalidDuplicateVariables       => Load("invalid-duplicate-variables.yaml");
    public static string InvalidApprovalNoApprovers      => Load("invalid-approval-no-approvers.yaml");
    public static string InvalidUnknownType              => Load("invalid-unknown-type.yaml");
    public static string InvalidVariableNoValueNoSource  => Load("invalid-variable-no-value-no-source.yaml");
    public static string InvalidNotYaml                  => Load("invalid-not-yaml.yaml");
}
