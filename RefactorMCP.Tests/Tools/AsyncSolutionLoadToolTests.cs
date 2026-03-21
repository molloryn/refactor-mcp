using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using Xunit;

namespace RefactorMCP.Tests.Tools;

public class AsyncSolutionLoadToolTests : RefactorMCP.Tests.TestBase
{
    [Fact]
    public async Task BeginLoadSolution_PollUntilCompleted_ReturnsCompletedStatus()
    {
        UnloadSolutionTool.ClearSolutionCache();

        var initialStatus = AsyncSolutionLoadTool.BeginLoadSolution(SolutionPath, CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(initialStatus.OperationId));
        Assert.True(initialStatus.State is "Queued" or "Loading" or "Completed");

        var completedStatus = await WaitForTerminalStatusAsync(initialStatus.OperationId);

        Assert.Equal("Completed", completedStatus.State);
        Assert.True(completedStatus.IsTerminal);
        Assert.Equal(2, completedStatus.TotalProjects);
        Assert.Equal(2, completedStatus.CompletedProjects);
        Assert.Equal(2, completedStatus.TotalProjectFiles);
        Assert.Equal(100, completedStatus.ProgressPercent);
        Assert.Contains("Successfully loaded solution", completedStatus.Message);
    }

    [Fact]
    public async Task BeginLoadSolution_WhenSolutionAlreadyLoaded_ReturnsCompletedSnapshot()
    {
        UnloadSolutionTool.ClearSolutionCache();
        await LoadSolutionTool.LoadSolution(SolutionPath, null, CancellationToken.None);

        var status = AsyncSolutionLoadTool.BeginLoadSolution(SolutionPath, CancellationToken.None);

        Assert.Equal("Completed", status.State);
        Assert.True(status.IsTerminal);
        Assert.Equal(2, status.TotalProjects);
        Assert.Equal(2, status.CompletedProjects);
        Assert.Contains("Successfully loaded solution", status.Message);
    }

    [Fact]
    public void GetLoadSolutionStatus_UnknownOperation_ThrowsMcpException()
    {
        Assert.Throws<McpException>(() =>
            AsyncSolutionLoadTool.GetLoadSolutionStatus("missing-operation", CancellationToken.None));
    }

    [Fact]
    public void CancelLoadSolution_UnknownOperation_ThrowsMcpException()
    {
        Assert.Throws<McpException>(() =>
            AsyncSolutionLoadTool.CancelLoadSolution("missing-operation", CancellationToken.None));
    }

    private static async Task<SolutionLoadStatus> WaitForTerminalStatusAsync(string operationId)
    {
        var timeout = TimeSpan.FromSeconds(30);
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var status = AsyncSolutionLoadTool.GetLoadSolutionStatus(operationId, CancellationToken.None);
            if (status.IsTerminal)
            {
                return status;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting for solution load operation '{operationId}'.");
    }
}
