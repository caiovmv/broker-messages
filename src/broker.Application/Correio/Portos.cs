using System.Security.Cryptography;
using System.Text;

namespace broker.Correio;

public interface IAcervo
{
    Task<Aplicativo?> AplicativoPorIdAsync(string id, CancellationToken cancellationToken);

    Task<Aplicativo?> AplicativoPorUsuarioAsync(string usuario, CancellationToken cancellationToken);

    Task<Aplicativo?> AplicativoPorTokenAsync(string tokenBusca, CancellationToken cancellationToken);

    Task<IReadOnlyList<Aplicativo>> AplicativosAsync(CancellationToken cancellationToken);

    Task InserirAplicativoAsync(Aplicativo aplicativo, CancellationToken cancellationToken);

    Task AtualizarSegredosAsync(string id, string senhaProtegida, string tokenBusca, string tokenProtegido, CancellationToken cancellationToken);

    Task<IReadOnlyList<Destino>> DestinosAsync(string aplicativoId, string? canal, CancellationToken cancellationToken);

    Task InserirDestinoAsync(Destino destino, CancellationToken cancellationToken);

    Task ReordenarDestinosAsync(string aplicativoId, string canal, IReadOnlyList<string> ids, CancellationToken cancellationToken);

    Task RemoverDestinoAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<DominioRemetente>> DominiosAsync(string aplicativoId, CancellationToken cancellationToken);

    Task<DominioRemetente?> DominioAsync(string aplicativoId, string nome, CancellationToken cancellationToken);

    Task<DominioRemetente?> DominioPorIdAsync(string id, CancellationToken cancellationToken);

    Task InserirDominioAsync(DominioRemetente dominio, CancellationToken cancellationToken);

    Task AtualizarDominioAsync(DominioRemetente dominio, CancellationToken cancellationToken);

    Task RemoverDominioAsync(string id, CancellationToken cancellationToken);

    Task InserirMensagemAsync(MensagemGravada mensagem, IReadOnlyList<EntregaGravada> entregas, CancellationToken cancellationToken);

    Task<MensagemGravada?> MensagemAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<EntregaGravada>> EntregasDaMensagemAsync(string mensagemId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EntregaConsulta>> ConsultarAsync(string aplicativoId, CancellationToken cancellationToken);

    Task<EntregaGravada?> EntregaPorIdAsync(string id, CancellationToken cancellationToken);

    Task<EntregaGravada?> EntregaPorProvedorAsync(string idProvedor, CancellationToken cancellationToken);

    Task AtualizarEntregaAsync(string id, string codigo, string status, string? provedor, string? idProvedor, CancellationToken cancellationToken);

    Task<IReadOnlyList<TentativaGravada>> TentativasAsync(string entregaId, CancellationToken cancellationToken);

    Task<bool> AbrirTentativaAsync(string entregaId, string destinoId, CancellationToken cancellationToken);

    Task FecharTentativaAsync(string entregaId, string destinoId, bool sucesso, string? idProvedor, CancellationToken cancellationToken);
}

public interface IProtecaoDeSegredo
{
    string Proteger(string texto);

    string Revelar(string protegido);
}

public interface IProvedorDeCanal
{
    string Nome { get; }

    string Canal { get; }

    Task<ResultadoDeProvedor> EnviarAsync(PedidoAoProvedor pedido, CancellationToken cancellationToken);
}

public interface IConsultaDeDns
{
    Task<bool> ContemTxtAsync(string nome, string trecho, CancellationToken cancellationToken);
}

public interface IRegistroDeDominio
{
    Task RegistrarAsync(string segredo, DominioRemetente dominio, CancellationToken cancellationToken);
}

public interface IExecutorDeEnvio
{
    Task EnviarAsync(string mensagemId, CancellationToken cancellationToken);

    Task VerificarDominioAsync(string dominioId, CancellationToken cancellationToken);
}

public interface IDurabilidade
{
    Task EnviarEEsperarAsync(string mensagemId, CancellationToken cancellationToken);

    Task DispararVerificacaoAsync(string dominioId, CancellationToken cancellationToken);
}

public static class ResumoDeSegredo
{
    public static string De(string valor)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor))).ToLowerInvariant();
    }

    public static bool Iguais(string esquerda, string direita)
    {
        var a = Encoding.UTF8.GetBytes(esquerda);
        var b = Encoding.UTF8.GetBytes(direita);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
