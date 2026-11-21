using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using COW_AI.Data;
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
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// Cấu hình 100% PostgreSQL (xóa bỏ hoàn toàn SQLite)
var pgConnStr = builder.Configuration.GetConnectionString("PostgreSql");
var csBuilder = new NpgsqlConnectionStringBuilder(
    string.IsNullOrWhiteSpace(pgConnStr)
        ? "Host=localhost;Port=5432;Database=cowai_db;Username=postgres;Password=postgres"
        : pgConnStr);

var envPgPass = builder.Configuration["POSTGRES_PASSWORD"]
    ?? builder.Configuration["pass postgres"]
    ?? Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
if (!string.IsNullOrWhiteSpace(envPgPass))
{
    csBuilder.Password = envPgPass;
}

var envPgPort = builder.Configuration["POSTGRES_PORT"]
    ?? builder.Configuration["post"]
    ?? Environment.GetEnvironmentVariable("POSTGRES_PORT");
if (int.TryParse(envPgPort, out var parsedPgPort) && parsedPgPort > 0 && parsedPgPort <= 65535)
{
    csBuilder.Port = parsedPgPort;
}

var envPgDb = builder.Configuration["POSTGRES_DB"] ?? Environment.GetEnvironmentVariable("POSTGRES_DB");
if (!string.IsNullOrWhiteSpace(envPgDb))
{
    csBuilder.Database = envPgDb.Trim();
}

var envPgUser = builder.Configuration["POSTGRES_USER"] ?? Environment.GetEnvironmentVariable("POSTGRES_USER");
if (!string.IsNullOrWhiteSpace(envPgUser))
{
    csBuilder.Username = envPgUser.Trim();
}

var finalPgConnStr = csBuilder.ConnectionString;
var targetDbName = string.IsNullOrWhiteSpace(csBuilder.Database) ? "cowai_db" : csBuilder.Database;

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(finalPgConnStr);
});

// Đăng ký dịch vụ xác thực tài khoản, VieNeu TTS, Auto-Video, Studio & Video Rendering Engine
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IVieNeuTtsService, VieNeuTtsService>();
builder.Services.AddSingleton<VideoDurationEstimator>();
builder.Services.AddScoped<IAutoVideoService, AutoVideoService>();
builder.Services.AddScoped<IStudioService, StudioService>();
builder.Services.AddScoped<IVideoRenderService, VideoRenderService>();
builder.Services.AddSingleton<IImageCompressionService, ImageCompressionService>();

// Cấu hình Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "COWAI_Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
});

// Chống brute-force đăng nhập / đăng ký (20 lượt / phút / IP)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AuthPolicy", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "local",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Đăng ký dịch vụ AI
builder.Services.AddHttpClient<OpenRouterService>();
builder.Services.AddHttpClient<OllamaService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(120);
});
builder.Services.AddHttpClient("LocalImage", client =>
{
    client.Timeout = TimeSpan.FromSeconds(180);
});
builder.Services.AddHttpClient("LocalImageHealth", client =>
{
    client.Timeout = TimeSpan.FromSeconds(3);
});
builder.Services.AddHttpClient<DuckDuckGoService>();
builder.Services.AddSingleton<PollinationsService>();
builder.Services.AddSingleton<LocalImageService>();
builder.Services.AddSingleton<ImageQuotaService>();
builder.Services.AddSingleton<IntentDispatcherService>();
builder.Services.AddSingleton<DirectoryAgentService>();
builder.Services.AddSingleton<AgentIntentClassifier>();
builder.Services.AddSingleton<WorkspaceRagService>();
builder.Services.AddSingleton<KodeWorkspaceAgentService>();
builder.Services.AddTransient<PromptService>();

// HttpClient cho tầng dịch prompt dự phòng (Pollinations Text API, không cần API key)
builder.Services.AddHttpClient("PollinationsText", client =>
{
    client.Timeout = TimeSpan.FromSeconds(25);
});

// Đăng ký dịch vụ Edge TTS (Facade Pattern)
builder.Services.AddSingleton<EdgeTtsClient>();
builder.Services.AddScoped<ITtsService, TtsService>();

var app = builder.Build();

// Tự động tạo mới CSDL PostgreSQL (cowai_db), khởi tạo Schema & tài khoản Admin mặc định
var adminConnBuilder = new NpgsqlConnectionStringBuilder(finalPgConnStr)
{
    Database = "postgres"
};
await using (var adminConn = new NpgsqlConnection(adminConnBuilder.ConnectionString))
{
    await adminConn.OpenAsync();
    await using var checkDbCmd = adminConn.CreateCommand();
    checkDbCmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = @dbname;";
    checkDbCmd.Parameters.AddWithValue("dbname", targetDbName);
    var dbExists = await checkDbCmd.ExecuteScalarAsync();
    if (dbExists == null)
    {
        var safeDbIdent = "\"" + targetDbName.Replace("\"", "\"\"") + "\"";
        await using var createDbCmd = adminConn.CreateCommand();
        createDbCmd.CommandText = $"CREATE DATABASE {safeDbIdent} WITH ENCODING 'UTF8';";
        await createDbCmd.ExecuteNonQueryAsync();
    }
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // Đảm bảo đầy đủ cột mở rộng trên PostgreSQL (idempotent)
    await db.Database.ExecuteSqlRawAsync(@"
        ALTER TABLE ""Scenes"" ADD COLUMN IF NOT EXISTS ""AnimationEffect"" TEXT DEFAULT 'ken-burns';
        ALTER TABLE ""Scenes"" ADD COLUMN IF NOT EXISTS ""FilterPreset"" TEXT DEFAULT 'none';
        ALTER TABLE ""Scenes"" ADD COLUMN IF NOT EXISTS ""SubtitlePosition"" TEXT DEFAULT 'bottom';
        ALTER TABLE ""Scenes"" ADD COLUMN IF NOT EXISTS ""SceneType"" TEXT DEFAULT 'standard';
        ALTER TABLE ""Scenes"" ADD COLUMN IF NOT EXISTS ""AvatarPosition"" TEXT DEFAULT 'center';
        ALTER TABLE ""Scenes"" ADD COLUMN IF NOT EXISTS ""Speaker"" TEXT DEFAULT 'Host';
        ALTER TABLE ""Scenes"" ADD COLUMN IF NOT EXISTS ""TtsText"" TEXT NULL;
        ALTER TABLE ""Scenes"" ADD COLUMN IF NOT EXISTS ""VisualKeyword"" TEXT NULL;
        ALTER TABLE ""Scenes"" ADD COLUMN IF NOT EXISTS ""BeatRole"" TEXT DEFAULT 'step';
        ALTER TABLE ""Scenes"" ADD COLUMN IF NOT EXISTS ""SectionBadge"" TEXT DEFAULT '';
        ALTER TABLE ""Scenes"" ADD COLUMN IF NOT EXISTS ""SfxCue"" TEXT DEFAULT 'whoosh';
        ALTER TABLE ""Projects"" ADD COLUMN IF NOT EXISTS ""VideoType"" TEXT DEFAULT 'comparison';
        ALTER TABLE ""Projects"" ADD COLUMN IF NOT EXISTS ""AvatarsJson"" TEXT NULL;
        ALTER TABLE ""Projects"" ADD COLUMN IF NOT EXISTS ""PlanMarkdown"" TEXT NULL;
        ALTER TABLE ""Projects"" ADD COLUMN IF NOT EXISTS ""SourceFileName"" TEXT NULL;
        ALTER TABLE ""Projects"" ADD COLUMN IF NOT EXISTS ""SourceContent"" TEXT NULL;
        ALTER TABLE ""Projects"" ADD COLUMN IF NOT EXISTS ""SourceMarkdown"" TEXT NULL;
        ALTER TABLE ""Projects"" ADD COLUMN IF NOT EXISTS ""SequentialSubMode"" TEXT DEFAULT 'auto';
        ALTER TABLE ""Projects"" ADD COLUMN IF NOT EXISTS ""DurationPreset"" TEXT DEFAULT 'auto';
        ALTER TABLE ""Projects"" ADD COLUMN IF NOT EXISTS ""EstimatedDurationSeconds"" DOUBLE PRECISION DEFAULT 36.0;
        ALTER TABLE ""Projects"" ADD COLUMN IF NOT EXISTS ""SkillKey"" TEXT DEFAULT 'compare-3zone';
        ALTER TABLE ""Projects"" ADD COLUMN IF NOT EXISTS ""AspectRatio"" TEXT DEFAULT '16:9';
        ALTER TABLE ""Projects"" ADD COLUMN IF NOT EXISTS ""IsAspectLocked"" BOOLEAN NOT NULL DEFAULT FALSE;
        CREATE TABLE IF NOT EXISTS ""WorkspaceSessions"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""SessionId"" VARCHAR(128) NOT NULL UNIQUE,
            ""FolderName"" VARCHAR(255) NOT NULL,
            ""RootPath"" VARCHAR(1024) NOT NULL,
            ""SourceType"" VARCHAR(32) NOT NULL DEFAULT 'server_path',
            ""FileCount"" INTEGER NOT NULL DEFAULT 0,
            ""AgentId"" VARCHAR(160) NOT NULL DEFAULT '',
            ""AutoApprove"" BOOLEAN NOT NULL DEFAULT FALSE,
            ""LastIndexedAt"" TIMESTAMPTZ NULL,
            ""TrashManifestJson"" TEXT NOT NULL DEFAULT '[]',
            ""CreatedAt"" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            ""UpdatedAt"" TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        ALTER TABLE ""WorkspaceSessions"" ADD COLUMN IF NOT EXISTS ""UserId"" INTEGER NULL;

        DELETE FROM ""Scenes"" WHERE ""ProjectId"" IN (SELECT ""Id"" FROM ""Projects"" WHERE ""Title"" ILIKE '%OWASP%' OR ""Title"" ILIKE '%Dev vs%' OR ""Title"" ILIKE '%Dự án mới%' OR ""Title"" ILIKE '%Bài giảng mới%');
        DELETE FROM ""Projects"" WHERE ""Title"" ILIKE '%OWASP%' OR ""Title"" ILIKE '%Dev vs%' OR ""Title"" ILIKE '%Dự án mới%' OR ""Title"" ILIKE '%Bài giảng mới%' OR (""SourceFileName"" IS NULL AND (SELECT COUNT(*) FROM ""Scenes"" WHERE ""Scenes"".""ProjectId"" = ""Projects"".""Id"") = 0);
    ");

    var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
    await auth.SeedDefaultAdminAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();

// Attribute-routed controllers (ví dụ /api/tts/speak, /api/account/status)
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
