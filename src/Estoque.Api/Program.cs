using System.Text.Json.Serialization;
using Estoque.Api.Common;
using Estoque.Api.Data;
using Estoque.Api.Features.Pedidos;
using Estoque.Api.Features.Produtos;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Configuração obrigatória: a aplicação não sobe se faltar algo ou se algum valor for inválido.
builder.Services.AddOptions<ApiOptions>()
    .BindConfiguration(ApiOptions.Secao)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<BancoDeDadosOptions>()
    .Configure<IConfiguration>((opcoes, configuracao) =>
        opcoes.ConnectionString = configuracao.GetConnectionString("Estoque") ?? string.Empty)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<FreteOptions>()
    .BindConfiguration(FreteOptions.Secao)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// A connection string é lida quando o DbContext é criado, e não na montagem do builder (assim os testes conseguem
// sobrescrevê-la). Ela vem da configuração, e não das Options validadas, para que as ferramentas do EF Core
// (migrations add e bundle) consigam montar o DbContext sem a configuração de produção. A obrigatoriedade continua
// garantida pelo ValidateOnStart de BancoDeDadosOptions quando a API sobe.
builder.Services.AddDbContext<AppDbContext>((servicos, options) => options
    .UseNpgsql(servicos.GetRequiredService<IConfiguration>().GetConnectionString("Estoque"))
    .UseSnakeCaseNamingConvention());

builder.Services.AddHttpClient<ServicoFrete>((servicos, http) =>
{
    var opcoes = servicos.GetRequiredService<IOptions<FreteOptions>>().Value;
    http.BaseAddress = new Uri(opcoes.UrlBase.TrimEnd('/') + "/");
    http.Timeout = ServicoFrete.TempoMaximoDeEspera;
});

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOptions<ConfirmacoesOptions>()
    .BindConfiguration(ConfirmacoesOptions.Secao)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<ProcessadorDeConfirmacoes>();

builder.Services.AddLimiteDeRequisicoes();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TratadorDeExcecoesInesperadas>();
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(opcoes =>
    opcoes.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(tags: ["banco"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Antes da chave de API: um volume abusivo é barrado mesmo sem uma chave válida.
app.UseRateLimiter();
app.UseMiddleware<ChaveApiMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// live: só confirma que o processo responde. ready: confirma também que o banco está acessível.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("banco") });

var api = app.MapGroup("/api");
api.MapProdutos();
api.MapPedidos();

app.Run();
