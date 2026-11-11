using COW_AI.Services;

var builder = WebApplication.CreateBuilder(args);

// Nạp các biến môi trường từ Key.env và .env nếu tồn tại
foreach (var envFileName in new[] { "Key.env", ".env" })
{
    var envFilePath = Path.Combine(builder.Environment.ContentRootPath, envFileName);
    if (File.Exists(envFilePath))
    {
        foreach (var rawLine in File.ReadAllLines(envFilePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var parts = line.Split('=', 2);
            if (parts.Length == 2)
            {
                var k = parts[0].Trim();
                var v = parts[1].Trim().Trim('"', '\'');
                Environment.SetEnvironmentVariable(k, v);
                builder.Configuration[k] = v;
                if (k.Equals("OLLAMA", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("OLLAMA_API_KEY", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Configuration["Ollama:ApiKey"] = v;
                    Environment.SetEnvironmentVariable("Ollama__ApiKey", v);
                }
            }
        }
    }
}

// Add services to the container.
builder.Services.AddControllersWithViews();

// Đăng ký dịch vụ AI
builder.Services.AddHttpClient<OpenRouterService>();
builder.Services.AddHttpClient<OllamaService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(120);
});
builder.Services.AddHttpClient<DuckDuckGoService>();
builder.Services.AddSingleton<PollinationsService>();
builder.Services.AddSingleton<IntentDispatcherService>();
builder.Services.AddTransient<PromptService>();

// HttpClient cho tầng dịch prompt dự phòng (Pollinations Text API, không cần API key)
builder.Services.AddHttpClient("PollinationsText", client =>
{
    client.Timeout = TimeSpan.FromSeconds(25);
});

// Đăng ký dịch vụ Edge TTS (Facade Pattern)
builder.Services.AddSingleton<EdgeTtsClient>();
builder.Services.AddScoped<EdgeTtsService>();
builder.Services.AddScoped<ITtsService, TtsService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

// Attribute-routed controllers (ví dụ /api/tts/speak)
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
