using COW_AI.Models;

namespace COW_AI.Services;

public interface IAutoVideoService
{
    Task<string> GenerateMarkdownPlanAsync(
        string sourceContent, 
        string videoType, 
        string voice, 
        List<string> avatarUrls, 
        CancellationToken ct = default);

    Task<List<Scene>> ParsePlanToScenesAsync(
        int projectId, 
        string markdownPlan, 
        CancellationToken ct = default);

    Task<string> BuildHyperFramesCompositionAsync(
        Project project, 
        List<Scene> scenes, 
        CancellationToken ct = default);

    Task<(bool Success, string Message, Project? UpdatedProject)> ProcessChatEditAsync(
        int projectId, 
        string userInstruction, 
        CancellationToken ct = default);
}
