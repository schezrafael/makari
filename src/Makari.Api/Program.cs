using Makari.Api.Features.Health;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MakariDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Makari")));

var app = builder.Build();
app.MapHealthEndpoint();
app.Run();

// Allows the integration tests to start the application in memory.
public partial class Program;
