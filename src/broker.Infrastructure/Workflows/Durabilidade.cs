using broker.Health;
using Microsoft.Extensions.Hosting;
using Temporalio.Client;
using Temporalio.Common;
using Temporalio.Exceptions;
using Temporalio.Workflows;
using Temporalio.Activities;
using Temporalio.Worker;

namespace broker.Workflows.Adapters;

public sealed class OpcoesDeWorkflow
{
    public OpcoesDeWorkflow(string endpoint, string namespaceDeTrabalho, string fila, bool tls)
    {
        Endpoint = endpoint;
        NamespaceDeTrabalho = namespaceDeTrabalho;
        Fila = fila;
        Tls = tls;
    }

    public string Endpoint { get; }

    public string NamespaceDeTrabalho { get; }

    public string Fila { get; }

    public bool Tls { get; }
}

public static class DestinoDeWorkflow
{
    public static OpcoesDeWorkflow Montar(string endpoint, string? namespaceDeTrabalho, string? fila)
    {
        var tls = false;
        var alvo = endpoint.Trim();
        if (alvo.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            tls = true;
            alvo = alvo["https://".Length..];
        }
        else if (alvo.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            alvo = alvo["http://".Length..];
        }

        alvo = alvo.TrimEnd('/');
        var espaco = string.IsNullOrWhiteSpace(namespaceDeTrabalho) ? "default" : namespaceDeTrabalho.Trim();
        var nomeDaFila = string.IsNullOrWhiteSpace(fila) ? "broker" : fila.Trim();
        return new OpcoesDeWorkflow(alvo, espaco, nomeDaFila, tls);
    }
}

public sealed class DurabilidadeEmMemoria : broker.Correio.IDurabilidade, ISondaDeInfraestrutura
{
    private readonly broker.Correio.IExecutorDeEnvio _executor;

    public DurabilidadeEmMemoria(broker.Correio.IExecutorDeEnvio executor)
    {
        _executor = executor;
    }

    public string Nome => NomesDeSonda.Workflows;

    public Task EnviarEEsperarAsync(string mensagemId, CancellationToken cancellationToken)
    {
        return _executor.EnviarAsync(mensagemId, cancellationToken);
    }

    public Task DispararVerificacaoAsync(string dominioId, CancellationToken cancellationToken)
    {
        _ = VerificarEmFundoAsync(dominioId);
        return Task.CompletedTask;
    }

    private async Task VerificarEmFundoAsync(string dominioId)
    {
        try
        {
            await _executor.VerificarDominioAsync(dominioId, CancellationToken.None);
        }
        catch (Exception)
        {
            // A resposta do cadastro já saiu. A checagem lê o estado gravado.
        }
    }

    public Task<EstadoDaSonda> VerificarAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new EstadoDaSonda(true, "memoria"));
    }
}

public sealed class DurabilidadeTemporal : broker.Correio.IDurabilidade, ISondaDeInfraestrutura, IDisposable
{
    private readonly OpcoesDeWorkflow _opcoes;
    private readonly SemaphoreSlim _porta = new(1, 1);
    private TemporalClient? _cliente;

    public DurabilidadeTemporal(OpcoesDeWorkflow opcoes)
    {
        _opcoes = opcoes;
    }

    public string Nome => NomesDeSonda.Workflows;

    public Task EnviarEEsperarAsync(string mensagemId, CancellationToken cancellationToken)
    {
        return EsperarAsync("envio-" + mensagemId, (FluxoDeEnvio fluxo) => fluxo.Executar(mensagemId), cancellationToken);
    }

    public Task DispararVerificacaoAsync(string dominioId, CancellationToken cancellationToken)
    {
        return IniciarAsync("dominio-" + dominioId, (FluxoDeDominio fluxo) => fluxo.Executar(dominioId), cancellationToken);
    }

    public async Task<EstadoDaSonda> VerificarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ClienteAsync(cancellationToken);
            return new EstadoDaSonda(true, "temporal");
        }
        catch (Exception excecao)
        {
            return new EstadoDaSonda(false, excecao.Message);
        }
    }

    public void Dispose()
    {
        _porta.Dispose();
    }

    private async Task EsperarAsync<T>(string id, System.Linq.Expressions.Expression<Func<T, Task<string>>> execucao, CancellationToken cancellationToken)
    {
        var cliente = await ClienteAsync(cancellationToken);
        WorkflowHandle<T> handle;
        try
        {
            handle = await cliente.StartWorkflowAsync(execucao, new WorkflowOptions(id: id, taskQueue: _opcoes.Fila));
        }
        catch (WorkflowAlreadyStartedException)
        {
            handle = cliente.GetWorkflowHandle<T>(id);
        }

        await handle.GetResultAsync<string>();
    }

    private async Task IniciarAsync<T>(string id, System.Linq.Expressions.Expression<Func<T, Task<string>>> execucao, CancellationToken cancellationToken)
    {
        var cliente = await ClienteAsync(cancellationToken);
        try
        {
            await cliente.StartWorkflowAsync(execucao, new WorkflowOptions(id: id, taskQueue: _opcoes.Fila));
        }
        catch (WorkflowAlreadyStartedException)
        {
        }
    }

    private async Task<TemporalClient> ClienteAsync(CancellationToken cancellationToken)
    {
        if (_cliente is not null)
        {
            return _cliente;
        }

        await _porta.WaitAsync(cancellationToken);
        try
        {
            if (_cliente is not null)
            {
                return _cliente;
            }

            var conexao = new TemporalClientConnectOptions
            {
                TargetHost = _opcoes.Endpoint,
                Namespace = _opcoes.NamespaceDeTrabalho,
            };
            if (_opcoes.Tls)
            {
                conexao.Tls = new TlsOptions();
            }

            _cliente = await TemporalClient.ConnectAsync(conexao);
            return _cliente;
        }
        finally
        {
            _porta.Release();
        }
    }
}

[Workflow]
public class FluxoDeEnvio
{
    [WorkflowRun]
    public Task<string> Executar(string mensagemId)
    {
        return Workflow.ExecuteActivityAsync(
            (AtividadesDeBroker atividade) => atividade.Enviar(mensagemId),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(2), RetryPolicy = new RetryPolicy { MaximumAttempts = 3 } });
    }
}

[Workflow]
public class FluxoDeDominio
{
    [WorkflowRun]
    public Task<string> Executar(string dominioId)
    {
        return Workflow.ExecuteActivityAsync(
            (AtividadesDeBroker atividade) => atividade.VerificarDominio(dominioId),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(2), RetryPolicy = new RetryPolicy { MaximumAttempts = 3 } });
    }
}

public sealed class AtividadesDeBroker
{
    private readonly broker.Correio.IExecutorDeEnvio _executor;

    public AtividadesDeBroker(broker.Correio.IExecutorDeEnvio executor)
    {
        _executor = executor;
    }

    [Activity]
    public async Task<string> Enviar(string mensagemId)
    {
        await _executor.EnviarAsync(mensagemId, ActivityExecutionContext.Current.CancellationToken);
        return mensagemId;
    }

    [Activity]
    public async Task<string> VerificarDominio(string dominioId)
    {
        await _executor.VerificarDominioAsync(dominioId, ActivityExecutionContext.Current.CancellationToken);
        return dominioId;
    }
}

public sealed class TrabalhadorTemporal : BackgroundService
{
    private readonly OpcoesDeWorkflow _opcoes;
    private readonly AtividadesDeBroker _atividades;

    public TrabalhadorTemporal(OpcoesDeWorkflow opcoes, AtividadesDeBroker atividades)
    {
        _opcoes = opcoes;
        _atividades = atividades;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var conexao = new TemporalClientConnectOptions
        {
            TargetHost = _opcoes.Endpoint,
            Namespace = _opcoes.NamespaceDeTrabalho,
        };
        if (_opcoes.Tls)
        {
            conexao.Tls = new TlsOptions();
        }

        var cliente = await TemporalClient.ConnectAsync(conexao);
        var opcoes = new TemporalWorkerOptions(_opcoes.Fila);
        opcoes.AddWorkflow<FluxoDeEnvio>();
        opcoes.AddWorkflow<FluxoDeDominio>();
        opcoes.AddAllActivities(_atividades);
        using var trabalhador = new TemporalWorker(cliente, opcoes);
        await trabalhador.ExecuteAsync(stoppingToken);
    }
}
