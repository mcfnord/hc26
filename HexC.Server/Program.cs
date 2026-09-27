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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("AllowAll");
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
