using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Makari.Api.Tests.Features.Hosting;

public sealed class WebHostingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "makari-hosting-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;

    public WebHostingTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "assets"));
        Directory.CreateDirectory(Path.Combine(_root, "api"));
        File.WriteAllText(Path.Combine(_root, "index.html"), "<!doctype html><html><body>Makari SPA test</body></html>");
        File.WriteAllText(Path.Combine(_root, "assets", "test.js"), "console.log('Makari');");
        File.WriteAllText(Path.Combine(_root, "api", "hidden.json"), "{\"mustNotServe\":true}");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseWebRoot(_root);
        });
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/catalogo")]
    [InlineData("/ventas/hoy")]
    [InlineData("/apiary")]
    public async Task ClientRoutesServeSpa(string path)
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Makari SPA test", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api")]
    [InlineData("/api/unknown")]
    [InlineData("/API/unknown")]
    [InlineData("/api/hidden.json")]
    [InlineData("/health/unknown")]
    [InlineData("/assets/missing.js")]
    public async Task ServerRoutesAndMissingAssetsNeverBecomeHtml(string path)
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task HealthAssetsHeadAndApiMethodsKeepTheirContractsWithoutDatabase()
    {
        using var client = _factory.CreateClient();
        Assert.Equal("ok", (await client.GetFromJsonAsync<Dictionary<string, string>>("/health"))!["status"]);
        Assert.Contains("Makari", await client.GetStringAsync("/assets/test.js"));
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/catalogo"));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        using var wrongMethod = await client.DeleteAsync("/api/sales");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
        using var invalidSale = await client.PostAsJsonAsync("/api/sales", new { });
        Assert.Equal(HttpStatusCode.BadRequest, invalidSale.StatusCode);
        Assert.NotEqual("text/html", invalidSale.Content.Headers.ContentType?.MediaType);
        using var postSpa = await client.PostAsJsonAsync("/catalogo", new { });
        Assert.NotEqual(HttpStatusCode.OK, postSpa.StatusCode);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
