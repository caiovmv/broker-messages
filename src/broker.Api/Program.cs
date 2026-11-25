using broker.Api;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(opcoes =>
{
    opcoes.AddServerHeader = false;
    opcoes.ConfigureEndpointDefaults(extremidade =>
    {
        extremidade.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
    });
});

builder.Services.AddEsqueleto(builder.Configuration);
builder.Services.AddCompressao();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseCorrelacao();
app.UseCabecalhosDeSeguranca();
app.UseCompressao();
app.UseCors();
app.UseAutenticacaoDoOperador();
app.UseTratamentoDeExcecao();
app.UseCacheDeLeitura();
app.UseSwaggerUI(opcoes =>
{
    opcoes.RoutePrefix = "swagger";
    opcoes.SwaggerEndpoint("/openapi/v1.json", "broker");
});
app.UseReDoc(opcoes =>
{
    opcoes.RoutePrefix = "redoc";
    opcoes.SpecUrl("/openapi/v1.json");
});
app.MapOpenApi();
app.MapearRotas();
app.MapControllers();

app.Run();

public partial class Program;
