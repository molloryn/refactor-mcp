using ModelContextProtocol;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RefactorMCP.Tests.Tools;

public class FindUsagesToolTests : RefactorMCP.Tests.TestBase
{
    [Fact]
    public async Task FindUsages_FieldAcrossFiles_ReturnsDeclarationsAndReferences()
    {
        const string symbolFileSource = """
public class Counter
{
    public int Value;

    public int Read() => Value;
}
""";

        const string consumerFileSource = """
public class Consumer
{
    public int Use(Counter counter) => counter.Value;
}
""";

        await LoadSolutionTool.LoadSolution(SolutionPath, null, CancellationToken.None);

        var symbolFilePath = Path.Combine(TestOutputPath, "Counter.cs");
        var consumerFilePath = Path.Combine(TestOutputPath, "Consumer.cs");
        await TestUtilities.CreateTestFile(symbolFilePath, symbolFileSource);
        await TestUtilities.CreateTestFile(consumerFilePath, consumerFileSource);

        var solution = await RefactoringHelpers.GetOrLoadSolution(SolutionPath);
        var project = solution.Projects.First();
        RefactoringHelpers.AddDocumentToProject(project, symbolFilePath);
        RefactoringHelpers.AddDocumentToProject(project, consumerFilePath);

        var result = await FindUsagesTool.FindUsages(
            SolutionPath,
            symbolFilePath,
            "Value",
            maxResults: 10,
            cancellationToken: CancellationToken.None);

        Assert.Equal("Value", result.SymbolName);
        Assert.Equal("Field", result.SymbolKind);
        Assert.Single(result.Declarations);
        Assert.Equal(2, result.TotalReferenceCount);
        Assert.Equal(2, result.ReturnedReferenceCount);
        Assert.False(result.IsTruncated);
        Assert.Contains(result.References, location => RefactoringHelpers.PathEquals(location.FilePath, symbolFilePath));
        Assert.Contains(result.References, location => RefactoringHelpers.PathEquals(location.FilePath, consumerFilePath));
        Assert.All(result.References, location => Assert.False(string.IsNullOrWhiteSpace(location.LineText)));
    }

    [Fact]
    public async Task FindUsages_MaxResults_TruncatesReferences()
    {
        const string source = """
public class Counter
{
    public int Value;

    public int Read() => Value + Value + Value;
}
""";

        await LoadSolutionTool.LoadSolution(SolutionPath, null, CancellationToken.None);

        var symbolFilePath = Path.Combine(TestOutputPath, "Counter.cs");
        await TestUtilities.CreateTestFile(symbolFilePath, source);

        var solution = await RefactoringHelpers.GetOrLoadSolution(SolutionPath);
        var project = solution.Projects.First();
        RefactoringHelpers.AddDocumentToProject(project, symbolFilePath);

        var result = await FindUsagesTool.FindUsages(
            SolutionPath,
            symbolFilePath,
            "Value",
            maxResults: 1,
            cancellationToken: CancellationToken.None);

        Assert.Equal(3, result.TotalReferenceCount);
        Assert.Equal(1, result.ReturnedReferenceCount);
        Assert.True(result.IsTruncated);
        Assert.Single(result.References);
    }

    [Fact]
    public async Task FindUsages_UnknownSymbol_ThrowsMcpException()
    {
        const string source = """
public class Counter
{
    public int Value;
}
""";

        await LoadSolutionTool.LoadSolution(SolutionPath, null, CancellationToken.None);

        var symbolFilePath = Path.Combine(TestOutputPath, "Counter.cs");
        await TestUtilities.CreateTestFile(symbolFilePath, source);

        var solution = await RefactoringHelpers.GetOrLoadSolution(SolutionPath);
        var project = solution.Projects.First();
        RefactoringHelpers.AddDocumentToProject(project, symbolFilePath);

        await Assert.ThrowsAsync<McpException>(() => FindUsagesTool.FindUsages(
            SolutionPath,
            symbolFilePath,
            "MissingValue",
            cancellationToken: CancellationToken.None));
    }
}
