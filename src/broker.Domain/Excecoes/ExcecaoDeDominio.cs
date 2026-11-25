using broker.Common;

namespace broker.Common;

/// <summary>
/// Invariante quebrada. Sobe ate a borda HTTP, que responde com o codigo do erro e nao com a pilha.
/// </summary>
public sealed class ExcecaoDeDominio : Exception
{
    public ExcecaoDeDominio(Erro erro)
        : base(erro.Mensagem)
    {
        Erro = erro;
    }

    public Erro Erro { get; }
}
