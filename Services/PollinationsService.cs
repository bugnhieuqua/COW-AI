namespace COW_AI.Services;

public class PollinationsService
{
    private readonly string _baseUrl;

    public PollinationsService(IConfiguration config)
    {
        var url = config["Pollinations:BaseUrl"] ?? "https://image.pollinations.ai/prompt/";
        if (!url.EndsWith("/"))
        {
            url += "/";
        }
        _baseUrl = url;
    }

    /// <summary>
    /// Tạo URL hình ảnh thông qua dịch vụ Pollinations AI.
    /// </summary>
    public string GetImageUrl(string prompt, int width = 1024, int height = 1024)
    {
        var cleanPrompt = prompt?.Trim() ?? string.Empty;
        var encodedPrompt = Uri.EscapeDataString(cleanPrompt);
        return $"{_baseUrl}{encodedPrompt}?width={width}&height={height}&nologo=true";
    }
}
