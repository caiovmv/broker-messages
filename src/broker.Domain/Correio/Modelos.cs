namespace broker.Correio;

public sealed class Aplicativo
{
    public string Id { get; set; } = "";

    public string Nome { get; set; } = "";

    public string UsuarioSmtp { get; set; } = "";

    public string SenhaProtegida { get; set; } = "";

    public string TokenBusca { get; set; } = "";

    public string TokenProtegido { get; set; } = "";

    public string CriadoEm { get; set; } = "";
}

public sealed class Destino
{
    public string Id { get; set; } = "";

    public string AplicativoId { get; set; } = "";

    public string Canal { get; set; } = "";

    public string Provedor { get; set; } = "";

    public int Ordem { get; set; }

    public string Nome { get; set; } = "";

    public string SegredoProtegido { get; set; } = "";
}

public sealed class DominioRemetente
{
    public string Id { get; set; } = "";

    public string AplicativoId { get; set; } = "";

    public string Nome { get; set; } = "";

    public string Validado { get; set; } = "não validado";

    public string Liberado { get; set; } = "não liberado";

    public string SpfNome { get; set; } = "";

    public string SpfValor { get; set; } = "";

    public string DkimNome { get; set; } = "";

    public string DkimValor { get; set; } = "";

    public string ChaveNome { get; set; } = "";

    public string ChaveValor { get; set; } = "";
}

public sealed class MensagemGravada
{
    public string Id { get; set; } = "";

    public string AplicativoId { get; set; } = "";

    public string Canal { get; set; } = "";

    public string MsgId { get; set; } = "";

    public string Remetente { get; set; } = "";

    public string NomeRemetente { get; set; } = "";

    public string Assunto { get; set; } = "";

    public string Corpo { get; set; } = "";

    public string Anexos { get; set; } = "[]";

    public string CriadoEm { get; set; } = "";
}

public sealed class EntregaGravada
{
    public string Id { get; set; } = "";

    public string MensagemId { get; set; } = "";

    public string Destinatario { get; set; } = "";

    public string Papel { get; set; } = "to";

    public string Codigo { get; set; } = "";

    public string Status { get; set; } = "";

    public string Provedor { get; set; } = "";

    public string IdProvedor { get; set; } = "";

    public string AtualizadoEm { get; set; } = "";

    public string AssuntoPersonalizado { get; set; } = "";

    public string CorpoPersonalizado { get; set; } = "";
}

public sealed class TentativaGravada
{
    public string EntregaId { get; set; } = "";

    public string DestinoId { get; set; } = "";

    public bool Aberta { get; set; }

    public bool Sucesso { get; set; }

    public string IdProvedor { get; set; } = "";
}

public sealed record EntregaConsulta(
    string MensagemId,
    string MsgId,
    string Remetente,
    string Destinatario,
    string Status,
    string Codigo,
    string Quando,
    string Canal);

public sealed record AnexoDeEnvio(string Nome, string Tipo, string ConteudoBase64);

public sealed record PedidoAoProvedor(
    string Segredo,
    string Remetente,
    string NomeRemetente,
    string Destinatario,
    string Assunto,
    string Corpo,
    string Tipo,
    string EntregaId,
    IReadOnlyList<AnexoDeEnvio> Anexos);

public sealed record ResultadoDeProvedor(bool Sucesso, string? IdProvedor);
