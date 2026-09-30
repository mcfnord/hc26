using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using HexC.Server;
using HexC.Server.Controllers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Enable CORS so your future HTML client can talk to this server
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder => builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// Google sign-in ends in our own session cookie (AuthController).
builder.Services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "hexc_session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;   // https behind nginx, see UseForwardedHeaders
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        // An API: answer 401/403 instead of redirecting to a login page.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    });

// Keep the cookie-signing keys on disk so a deploy or restart doesn't sign everyone out.
var dataDir = Environment.GetEnvironmentVariable("HEXC_DATA");
if (!string.IsNullOrEmpty(dataDir))
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

// Table countdowns and the turn clock run on the server. Tests drive GameStore.Tick directly.
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<LobbyClock>();

var app = builder.Build();

// Games live in memory and are written through to SQLite so a deploy or restart keeps them.
if (!string.IsNullOrEmpty(dataDir))
{
    Directory.CreateDirectory(dataDir);
    GameStore.Configure(new GameRepository(Path.Combine(dataDir, "hexc.db")));
}

// nginx terminates TLS and sends X-Forwarded-Proto: https, so secure cookies work behind it.
app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto });

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Used by deploy.sh smoke test and for confirming which release is live.
app.MapGet("/healthz", () => Results.Ok(new {
    ok = true,
    release = Environment.GetEnvironmentVariable("HEXC_RELEASE") ?? "dev",
    startedUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()
}));

app.Run();

// Required for WebApplicationFactory<Program> in integration tests.
// This makes the implicit Program class accessible to the test project.
public partial class Program { }
