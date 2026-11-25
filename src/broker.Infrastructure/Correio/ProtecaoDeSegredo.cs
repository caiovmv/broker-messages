using Microsoft.AspNetCore.DataProtection;

namespace broker.Correio;

public sealed class ProtecaoDeSegredo : IProtecaoDeSegredo
{
    private readonly IDataProtector _protetor;

    public ProtecaoDeSegredo(IDataProtectionProvider provedor)
    {
        _protetor = provedor.CreateProtector("broker.credenciais");
    }

    public string Proteger(string texto)
    {
        return _protetor.Protect(texto);
    }

    public string Revelar(string protegido)
    {
        return _protetor.Unprotect(protegido);
    }
}
