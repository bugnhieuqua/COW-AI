using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
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

// Cấu hình Dual-Mode Cơ sở dữ liệu: PostgreSQL (Production/Docker) & SQLite Fallback (Local/Test)
var pgConnStr = builder.Configuration.GetConnectionString("PostgreSql");
var usePostgres = !string.IsNullOrWhiteSpace(pgConnStr) && builder.Configuration.GetValue<bool>("Database:UsePostgres", false);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (usePostgres)
    {
        options.UseNpgsql(pgConnStr);
    }
    else
    {
        var dataDir = Path.Combine(builder.Environment.ContentRootPath, "Data");
        if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "cowai.db");
        options.UseSqlite($"Data Source={dbPath}");
    }
});

// Đăng ký dịch vụ xác thực tài khoản, VieNeu TTS, Auto-Video, Studio & Video Rendering Engine
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IVieNeuTtsService, VieNeuTtsService>();
builder.Services.AddScoped<IAutoVideoService, AutoVideoService>();
builder.Services.AddScoped<IStudioService, StudioService>();
builder.Services.AddScoped<IVideoRenderService, VideoRenderService>();

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
builder.Services.AddSingleton<IntentDispatcherService>();
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

// Khởi tạo Database và Admin mặc định nếu chưa có
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // Chỉ chạy vá lược đồ tương thích ngược khi dùng SQLite (PostgreSQL dùng EnsureCreated/Npgsql chuẩn)
    if (!usePostgres)
    {
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Projects"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Projects"" PRIMARY KEY AUTOINCREMENT,
                ""UserId"" INTEGER NULL CONSTRAINT ""FK_Projects_Users_UserId"" REFERENCES ""Users"" (""Id"") ON DELETE CASCADE,
                ""Title"" TEXT NOT NULL,
                ""Description"" TEXT NULL,
                ""Status"" TEXT NOT NULL,
                ""ScriptJson"" TEXT NULL,
                ""Voice"" TEXT NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""UpdatedAt"" TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ""Scenes"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Scenes"" PRIMARY KEY AUTOINCREMENT,
                ""ProjectId"" INTEGER NOT NULL CONSTRAINT ""FK_Scenes_Projects_ProjectId"" REFERENCES ""Projects"" (""Id"") ON DELETE CASCADE,
                ""OrderIndex"" INTEGER NOT NULL,
                ""Title"" TEXT NOT NULL,
                ""Content"" TEXT NOT NULL,
                ""ImagePrompt"" TEXT NULL,
                ""ImageUrl"" TEXT NULL,
                ""AudioUrl"" TEXT NULL,
                ""DurationSeconds"" REAL NOT NULL,
                ""AnimationEffect"" TEXT NULL DEFAULT 'ken-burns',
                ""FilterPreset"" TEXT NULL DEFAULT 'none',
                ""SubtitlePosition"" TEXT NULL DEFAULT 'bottom'
            );
            CREATE INDEX IF NOT EXISTS ""IX_Projects_UserId"" ON ""Projects"" (""UserId"");
            CREATE INDEX IF NOT EXISTS ""IX_Scenes_ProjectId"" ON ""Scenes"" (""ProjectId"");
        ");

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        async Task EnsureSqliteColumnAsync(string table, string column, string definition)
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = '{column}';";
            var exists = await checkCmd.ExecuteScalarAsync();
            if (exists == null)
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};";
                await alterCmd.ExecuteNonQueryAsync();
            }
        }

        await EnsureSqliteColumnAsync("Scenes", "AnimationEffect", "TEXT DEFAULT 'ken-burns'");
        await EnsureSqliteColumnAsync("Scenes", "FilterPreset", "TEXT DEFAULT 'none'");
        await EnsureSqliteColumnAsync("Scenes", "SubtitlePosition", "TEXT DEFAULT 'bottom'");
        await EnsureSqliteColumnAsync("Scenes", "SceneType", "TEXT DEFAULT 'standard'");
        await EnsureSqliteColumnAsync("Scenes", "AvatarPosition", "TEXT DEFAULT 'center'");
        await EnsureSqliteColumnAsync("Scenes", "Speaker", "TEXT DEFAULT 'Host'");
        await EnsureSqliteColumnAsync("Projects", "VideoType", "TEXT DEFAULT 'comparison'");
        await EnsureSqliteColumnAsync("Projects", "AvatarsJson", "TEXT NULL");
        await EnsureSqliteColumnAsync("Projects", "PlanMarkdown", "TEXT NULL");
        await EnsureSqliteColumnAsync("Projects", "SourceFileName", "TEXT NULL");
        await EnsureSqliteColumnAsync("Projects", "SourceContent", "TEXT NULL");
    }

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

app.MapStaticAssets();

// Attribute-routed controllers (ví dụ /api/tts/speak, /api/account/status)
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
