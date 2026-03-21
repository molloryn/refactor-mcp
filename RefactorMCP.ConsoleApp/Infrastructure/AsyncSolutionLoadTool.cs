using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Threading;

[McpServerToolType]
public static class AsyncSolutionLoadTool
{
    [McpServerTool, Description("Begin loading a solution in the background and return an operation id plus progress snapshot")]
    public static SolutionLoadStatus BeginLoadSolution(
        [Description(RefactoringHelpers.SolutionPathDescription)] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        return SolutionLoadManager.BeginLoadSolution(solutionPath, cancellationToken);
    }

    [McpServerTool, Description("Get the current status and progress for a background solution load operation")]
    public static SolutionLoadStatus GetLoadSolutionStatus(
        [Description("Operation id returned by BeginLoadSolution")] string operationId,
        CancellationToken cancellationToken = default)
    {
        return SolutionLoadManager.GetLoadSolutionStatus(operationId);
    }

    [McpServerTool, Description("Cancel a background solution load operation")]
    public static SolutionLoadStatus CancelLoadSolution(
        [Description("Operation id returned by BeginLoadSolution")] string operationId,
        CancellationToken cancellationToken = default)
    {
        return SolutionLoadManager.CancelLoadSolution(operationId);
    }
}
