using DevOps.GitHub.Sync.Data;
using DevOps.GitHub.Sync.Web.Options;
using DevOps.GitHub.Sync.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ── GitHub App options ────────────────────────────────────────────────────────
builder.Services.Configure<GitHubAppOptions>(
    builder.Configuration.GetSection(GitHubAppOptions.SectionName));

// ── Database (EF Core 10.0.0 / Azure SQL) ────────────────────────────────────
// EF Core 10 ships alongside .NET 10 (November 2025).
var connectionString = builder.Configuration.GetConnectionString("DevOpsGitHubSync")
    ?? throw new InvalidOperationException(
        "Connection string 'DevOpsGitHubSync' not found. " +
        "Ensure the Aspire AppHost wires up the Azure SQL resource.");

builder.Services.AddDataServices(connectionString);

// ── HTTP client for GitHub API ────────────────────────────────────────────────
builder.Services.AddHttpClient("GitHub", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "DevOps-GitHub-Sync");
    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
});

// ── Application services ──────────────────────────────────────────────────────
builder.Services.AddScoped<GitHubAppService>();

// ── MVC + API controllers ─────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();

var app = builder.Build();

app.MapDefaultEndpoints();

// ── Auto-apply EF migrations on startup ───────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

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

