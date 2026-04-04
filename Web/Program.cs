using DevOps.GitHub.Sync.Web.Controllers;
using DevOps.GitHub.Sync.Web.Options;
using DevOps.GitHub.Sync.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ── GitHub App options ────────────────────────────────────────────────────────
builder.Services.Configure<GitHubAppOptions>(
    builder.Configuration.GetSection(GitHubAppOptions.SectionName));

// ── In-memory cache (used to cache GitHub installation access tokens) ─────────
builder.Services.AddMemoryCache();

// ── HTTP client for GitHub API ────────────────────────────────────────────────
builder.Services.AddHttpClient("GitHub", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "DevOps-GitHub-Sync");
    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
});

// ── Application services ──────────────────────────────────────────────────────
builder.Services.AddSingleton<GitHubAppService>();

// ── OpenTelemetry – custom activity source for sync request tracing ───────────
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(SyncController.ActivitySourceName));

// ── MVC + API controllers ─────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();

var app = builder.Build();

app.MapDefaultEndpoints();

// ── HTTP pipeline ─────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

