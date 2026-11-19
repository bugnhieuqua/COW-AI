using COW_AI.Models;

namespace COW_AI.Services;

public interface IStudioService
{
    Task<Project> CreateProjectAsync(int? userId, string title, string? description, string? voice);
    Task<Project?> GetProjectAsync(int projectId);
    Task<int?> GetSceneProjectIdAsync(int sceneId);
    Task<List<Project>> GetUserProjectsAsync(int? userId);
    Task<(bool Success, string? Error, List<Scene> Scenes)> GenerateScriptAsync(int projectId, string topic);
    Task<(bool Success, string? Error, string? AudioUrl, double Duration)> GenerateSceneAudioAsync(int sceneId);
    Task<(bool Success, string? Error, string? ImageUrl)> GenerateSceneImageAsync(int sceneId);
    Task<(bool Success, string? Error)> AutoProduceProjectAsync(int projectId);
    Task<bool> DeleteProjectAsync(int projectId);
    Task<(bool Success, string? Error, Scene? Scene)> UpdateScenePropertiesAsync(
        int sceneId,
        string? title,
        string? content,
        string? imagePrompt,
        double? duration,
        string? animationEffect,
        string? filterPreset,
        string? subtitlePosition);
    Task<Scene> AddSceneAsync(int projectId);
    Task<bool> DeleteSceneAsync(int sceneId);
    Task<(bool Success, string? Error, string HtmlComposition)> GenerateCompositionAsync(int projectId);
    Task<(bool Success, string? Error, string PlanMarkdown)> GeneratePlanMarkdownAsync(
        int projectId, 
        string sourceContent, 
        string videoType, 
        string voice, 
        List<string> avatarUrls);
    Task<(bool Success, string? Error, List<Scene> Scenes)> ApplyPlanAndProduceAsync(
        int projectId, 
        string planMarkdown, 
        bool autoProduce = false);
    Task<(bool Success, string Message, Project? Project)> ProcessChatEditAsync(
        int projectId, 
        string instruction);
}
