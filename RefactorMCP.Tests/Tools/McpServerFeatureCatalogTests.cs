using System.Linq;
using Xunit;

namespace RefactorMCP.Tests.Tools;

public class McpServerFeatureCatalogTests
{
    [Fact]
    public void Parse_RecognizesAdvancedJsonMode()
    {
        var options = McpCommandLineOptions.Parse(["--advanced", "--json", "RenameSymbol", "{}"]);

        Assert.Equal(McpFeatureMode.Advanced, options.Mode);
        Assert.True(options.IsJsonMode);
        Assert.Equal("--json", options.RemainingArgs[0]);
    }

    [Fact]
    public void BasicMode_ExposesOnlyCoreToolsAndUtilities()
    {
        var toolNames = McpServerFeatureCatalog.GetToolNames(McpFeatureMode.Basic);
        var promptTypes = McpServerFeatureCatalog.GetPromptTypes(McpFeatureMode.Basic);

        Assert.Contains("rename-symbol", toolNames);
        Assert.Contains("find-usages", toolNames);
        Assert.Contains("move-to-separate-file", toolNames);
        Assert.Contains("begin-load-solution", toolNames);
        Assert.DoesNotContain("extract-method", toolNames);
        Assert.DoesNotContain("safe-delete-field", toolNames);
        Assert.Empty(promptTypes);
    }

    [Fact]
    public void AdvancedMode_ExposesAdditionalToolsPromptsAndResources()
    {
        var toolNames = McpServerFeatureCatalog.GetToolNames(McpFeatureMode.Advanced);
        var promptTypes = McpServerFeatureCatalog.GetPromptTypes(McpFeatureMode.Advanced);
        var resourceTypes = McpServerFeatureCatalog.GetResourceTypes(McpFeatureMode.Advanced);

        Assert.Contains("extract-method", toolNames);
        Assert.Contains(typeof(AnalyzeRefactoringOpportunitiesTool), promptTypes);
        Assert.Contains(typeof(MetricsResource), resourceTypes);
    }

    [Fact]
    public void ListToolsCommand_UsesCurrentMode()
    {
        var originalMode = McpServerFeatureCatalog.CurrentMode;
        try
        {
            McpServerFeatureCatalog.Configure(McpFeatureMode.Basic);
            var basicTools = ListTools.ListToolsCommand()
                .Split('\n', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
            Assert.DoesNotContain("extract-method", basicTools);

            McpServerFeatureCatalog.Configure(McpFeatureMode.Advanced);
            var advancedTools = ListTools.ListToolsCommand()
                .Split('\n', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
            Assert.Contains("extract-method", advancedTools);
        }
        finally
        {
            McpServerFeatureCatalog.Configure(originalMode);
        }
    }
}
