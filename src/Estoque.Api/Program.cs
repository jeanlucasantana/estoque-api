using Estoque.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("Estoque"))
    .UseSnakeCaseNamingConvention());

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
