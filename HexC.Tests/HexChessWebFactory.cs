using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HexC.Tests;

/// <summary>
/// Creates an in-process test server using the real HexC.Server pipeline.
/// No port binding, no external process — runs entirely in memory.
/// </summary>
public class HexChessWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}
