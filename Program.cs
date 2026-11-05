using COW_AI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Đăng ký dịch vụ AI
builder.Services.AddHttpClient<OpenRouterService>();
builder.Services.AddHttpClient<DuckDuckGoService>();
builder.Services.AddSingleton<PollinationsService>();
builder.Services.AddSingleton<IntentDispatcherService>();
builder.Services.AddTransient<PromptService>();

// Đăng ký dịch vụ Edge TTS
builder.Services.AddSingleton<EdgeTtsClient>();
builder.Services.AddScoped<EdgeTtsService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
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
