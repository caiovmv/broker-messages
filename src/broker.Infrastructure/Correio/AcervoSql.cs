using System.Data.Common;
using Dapper;
using broker.Persistence.Adapters;

namespace broker.Correio;

public sealed class AcervoSql : IAcervo
{
    private readonly SessaoSql _sessao;

    public AcervoSql(SessaoSql sessao)
    {
        _sessao = sessao;
    }

    public async Task<Aplicativo?> AplicativoPorIdAsync(string id, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        return await conexao.QuerySingleOrDefaultAsync<Aplicativo>(new CommandDefinition(
            "SELECT id AS Id, nome AS Nome, usuario_smtp AS UsuarioSmtp, senha_protegida AS SenhaProtegida, token_busca AS TokenBusca, token_protegido AS TokenProtegido, criado_em AS CriadoEm FROM aplicativo WHERE id = @id",
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<Aplicativo?> AplicativoPorUsuarioAsync(string usuario, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        return await conexao.QuerySingleOrDefaultAsync<Aplicativo>(new CommandDefinition(
            "SELECT id AS Id, nome AS Nome, usuario_smtp AS UsuarioSmtp, senha_protegida AS SenhaProtegida, token_busca AS TokenBusca, token_protegido AS TokenProtegido, criado_em AS CriadoEm FROM aplicativo WHERE usuario_smtp = @usuario",
            new { usuario },
            cancellationToken: cancellationToken));
    }

    public async Task<Aplicativo?> AplicativoPorTokenAsync(string tokenBusca, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        return await conexao.QuerySingleOrDefaultAsync<Aplicativo>(new CommandDefinition(
            "SELECT id AS Id, nome AS Nome, usuario_smtp AS UsuarioSmtp, senha_protegida AS SenhaProtegida, token_busca AS TokenBusca, token_protegido AS TokenProtegido, criado_em AS CriadoEm FROM aplicativo WHERE token_busca = @tokenBusca",
            new { tokenBusca },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Aplicativo>> AplicativosAsync(CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        var linhas = await conexao.QueryAsync<Aplicativo>(new CommandDefinition(
            "SELECT id AS Id, nome AS Nome, usuario_smtp AS UsuarioSmtp, senha_protegida AS SenhaProtegida, token_busca AS TokenBusca, token_protegido AS TokenProtegido, criado_em AS CriadoEm FROM aplicativo ORDER BY criado_em",
            cancellationToken: cancellationToken));
        return linhas.ToArray();
    }

    public async Task InserirAplicativoAsync(Aplicativo aplicativo, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        await conexao.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO aplicativo (id, nome, usuario_smtp, senha_protegida, token_busca, token_protegido, criado_em)
            VALUES (@Id, @Nome, @UsuarioSmtp, @SenhaProtegida, @TokenBusca, @TokenProtegido, @CriadoEm)
            """,
            aplicativo,
            cancellationToken: cancellationToken));
    }

    public async Task AtualizarSegredosAsync(string id, string senhaProtegida, string tokenBusca, string tokenProtegido, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        await conexao.ExecuteAsync(new CommandDefinition(
            "UPDATE aplicativo SET senha_protegida = @senhaProtegida, token_busca = @tokenBusca, token_protegido = @tokenProtegido WHERE id = @id",
            new { id, senhaProtegida, tokenBusca, tokenProtegido },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Destino>> DestinosAsync(string aplicativoId, string? canal, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        var sql = canal is null
            ? "SELECT id AS Id, aplicativo_id AS AplicativoId, canal AS Canal, provedor AS Provedor, ordem AS Ordem, nome AS Nome, segredo_protegido AS SegredoProtegido FROM destino WHERE aplicativo_id = @aplicativoId ORDER BY ordem"
            : "SELECT id AS Id, aplicativo_id AS AplicativoId, canal AS Canal, provedor AS Provedor, ordem AS Ordem, nome AS Nome, segredo_protegido AS SegredoProtegido FROM destino WHERE aplicativo_id = @aplicativoId AND canal = @canal ORDER BY ordem";
        var linhas = await conexao.QueryAsync<Destino>(new CommandDefinition(sql, new { aplicativoId, canal }, cancellationToken: cancellationToken));
        return linhas.ToArray();
    }

    public async Task InserirDestinoAsync(Destino destino, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        await conexao.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO destino (id, aplicativo_id, canal, provedor, ordem, nome, segredo_protegido)
            VALUES (@Id, @AplicativoId, @Canal, @Provedor, @Ordem, @Nome, @SegredoProtegido)
            """,
            destino,
            cancellationToken: cancellationToken));
    }

    public async Task ReordenarDestinosAsync(string aplicativoId, string canal, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        for (var i = 0; i < ids.Count; i++)
        {
            await conexao.ExecuteAsync(new CommandDefinition(
                "UPDATE destino SET ordem = @ordem WHERE id = @id AND aplicativo_id = @aplicativoId AND canal = @canal",
                new { ordem = i + 1, id = ids[i], aplicativoId, canal },
                cancellationToken: cancellationToken));
        }
    }

    public async Task RemoverDestinoAsync(string id, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        await conexao.ExecuteAsync(new CommandDefinition("DELETE FROM destino WHERE id = @id", new { id }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<DominioRemetente>> DominiosAsync(string aplicativoId, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        var linhas = await conexao.QueryAsync<DominioRemetente>(new CommandDefinition(SqlDominio + " WHERE aplicativo_id = @aplicativoId", new { aplicativoId }, cancellationToken: cancellationToken));
        return linhas.ToArray();
    }

    public async Task<DominioRemetente?> DominioAsync(string aplicativoId, string nome, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        return await conexao.QuerySingleOrDefaultAsync<DominioRemetente>(new CommandDefinition(
            SqlDominio + " WHERE aplicativo_id = @aplicativoId AND nome = @nome",
            new { aplicativoId, nome },
            cancellationToken: cancellationToken));
    }

    public async Task<DominioRemetente?> DominioPorIdAsync(string id, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        return await conexao.QuerySingleOrDefaultAsync<DominioRemetente>(new CommandDefinition(SqlDominio + " WHERE id = @id", new { id }, cancellationToken: cancellationToken));
    }

    public async Task InserirDominioAsync(DominioRemetente dominio, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        await conexao.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dominio (id, aplicativo_id, nome, validado, liberado, spf_nome, spf_valor, dkim_nome, dkim_valor, chave_nome, chave_valor)
            VALUES (@Id, @AplicativoId, @Nome, @Validado, @Liberado, @SpfNome, @SpfValor, @DkimNome, @DkimValor, @ChaveNome, @ChaveValor)
            """,
            dominio,
            cancellationToken: cancellationToken));
    }

    public async Task AtualizarDominioAsync(DominioRemetente dominio, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        await conexao.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dominio SET validado = @Validado, liberado = @Liberado, spf_nome = @SpfNome, spf_valor = @SpfValor,
            dkim_nome = @DkimNome, dkim_valor = @DkimValor, chave_nome = @ChaveNome, chave_valor = @ChaveValor
            WHERE id = @Id
            """,
            dominio,
            cancellationToken: cancellationToken));
    }

    public async Task RemoverDominioAsync(string id, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        await conexao.ExecuteAsync(new CommandDefinition("DELETE FROM dominio WHERE id = @id", new { id }, cancellationToken: cancellationToken));
    }

    public async Task InserirMensagemAsync(MensagemGravada mensagem, IReadOnlyList<EntregaGravada> entregas, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        await using var transacao = await conexao.BeginTransactionAsync(cancellationToken);
        await conexao.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO mensagem (id, aplicativo_id, canal, msgid, remetente, nome_remetente, assunto, corpo, anexos, criado_em)
            VALUES (@Id, @AplicativoId, @Canal, @MsgId, @Remetente, @NomeRemetente, @Assunto, @Corpo, @Anexos, @CriadoEm)
            """,
            mensagem,
            transacao,
            cancellationToken: cancellationToken));
        foreach (var entrega in entregas)
        {
            await conexao.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO entrega (id, mensagem_id, destinatario, papel, codigo, status, provedor, id_provedor, atualizado_em, assunto_personalizado, corpo_personalizado)
                VALUES (@Id, @MensagemId, @Destinatario, @Papel, @Codigo, @Status, @Provedor, @IdProvedor, @AtualizadoEm, @AssuntoPersonalizado, @CorpoPersonalizado)
                """,
                entrega,
                transacao,
                cancellationToken: cancellationToken));
        }

        await transacao.CommitAsync(cancellationToken);
    }

    public async Task<MensagemGravada?> MensagemAsync(string id, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        return await conexao.QuerySingleOrDefaultAsync<MensagemGravada>(new CommandDefinition(
            """
            SELECT id AS Id, aplicativo_id AS AplicativoId, canal AS Canal, msgid AS MsgId, remetente AS Remetente,
            nome_remetente AS NomeRemetente, assunto AS Assunto, corpo AS Corpo, anexos AS Anexos, criado_em AS CriadoEm
            FROM mensagem WHERE id = @id
            """,
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<EntregaGravada>> EntregasDaMensagemAsync(string mensagemId, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        var linhas = await conexao.QueryAsync<EntregaGravada>(new CommandDefinition(SqlEntrega + " WHERE mensagem_id = @mensagemId", new { mensagemId }, cancellationToken: cancellationToken));
        return linhas.ToArray();
    }

    public async Task<IReadOnlyList<EntregaConsulta>> ConsultarAsync(string aplicativoId, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        var linhas = await conexao.QueryAsync<EntregaConsulta>(new CommandDefinition(
            """
            SELECT m.id AS MensagemId, m.msgid AS MsgId, m.remetente AS Remetente, e.destinatario AS Destinatario,
            e.status AS Status, e.codigo AS Codigo, e.atualizado_em AS Quando, m.canal AS Canal
            FROM entrega e INNER JOIN mensagem m ON m.id = e.mensagem_id
            WHERE m.aplicativo_id = @aplicativoId
            """,
            new { aplicativoId },
            cancellationToken: cancellationToken));
        return linhas.ToArray();
    }

    public async Task<EntregaGravada?> EntregaPorIdAsync(string id, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        return await conexao.QuerySingleOrDefaultAsync<EntregaGravada>(new CommandDefinition(SqlEntrega + " WHERE id = @id", new { id }, cancellationToken: cancellationToken));
    }

    public async Task<EntregaGravada?> EntregaPorProvedorAsync(string idProvedor, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        var linhas = await conexao.QueryAsync<EntregaGravada>(new CommandDefinition(
            SqlEntrega + " WHERE id_provedor <> ''",
            cancellationToken: cancellationToken));
        return linhas.FirstOrDefault(item => item.IdProvedor == idProvedor || idProvedor.StartsWith(item.IdProvedor, StringComparison.Ordinal));
    }

    public async Task AtualizarEntregaAsync(string id, string codigo, string status, string? provedor, string? idProvedor, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        await conexao.ExecuteAsync(new CommandDefinition(
            """
            UPDATE entrega SET codigo = @codigo, status = @status,
            provedor = COALESCE(@provedor, provedor), id_provedor = COALESCE(@idProvedor, id_provedor),
            atualizado_em = @quando WHERE id = @id
            """,
            new { id, codigo, status, provedor, idProvedor, quando = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TentativaGravada>> TentativasAsync(string entregaId, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        var linhas = await conexao.QueryAsync<TentativaLinha>(new CommandDefinition(
            "SELECT entrega_id AS EntregaId, destino_id AS DestinoId, aberta AS Aberta, sucesso AS Sucesso, id_provedor AS IdProvedor FROM tentativa WHERE entrega_id = @entregaId",
            new { entregaId },
            cancellationToken: cancellationToken));
        return linhas.Select(item => new TentativaGravada
        {
            EntregaId = item.EntregaId,
            DestinoId = item.DestinoId,
            Aberta = item.Aberta != 0,
            Sucesso = item.Sucesso != 0,
            IdProvedor = item.IdProvedor,
        }).ToArray();
    }

    public async Task<bool> AbrirTentativaAsync(string entregaId, string destinoId, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        var existe = await conexao.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM tentativa WHERE entrega_id = @entregaId AND destino_id = @destinoId",
            new { entregaId, destinoId },
            cancellationToken: cancellationToken));
        if (existe > 0)
        {
            return false;
        }

        await conexao.ExecuteAsync(new CommandDefinition(
            "INSERT INTO tentativa (entrega_id, destino_id, aberta, sucesso, id_provedor) VALUES (@entregaId, @destinoId, 1, 0, '')",
            new { entregaId, destinoId },
            cancellationToken: cancellationToken));
        return true;
    }

    public async Task FecharTentativaAsync(string entregaId, string destinoId, bool sucesso, string? idProvedor, CancellationToken cancellationToken)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        await conexao.ExecuteAsync(new CommandDefinition(
            "UPDATE tentativa SET aberta = 0, sucesso = @sucesso, id_provedor = @idProvedor WHERE entrega_id = @entregaId AND destino_id = @destinoId",
            new { entregaId, destinoId, sucesso = sucesso ? 1 : 0, idProvedor = idProvedor ?? "" },
            cancellationToken: cancellationToken));
    }

    private Task<DbConnection> AbrirAsync(CancellationToken cancellationToken)
    {
        return _sessao.AbrirAsync(cancellationToken);
    }

    private const string SqlDominio =
        """
        SELECT id AS Id, aplicativo_id AS AplicativoId, nome AS Nome, validado AS Validado, liberado AS Liberado,
        spf_nome AS SpfNome, spf_valor AS SpfValor, dkim_nome AS DkimNome, dkim_valor AS DkimValor,
        chave_nome AS ChaveNome, chave_valor AS ChaveValor FROM dominio
        """;

    private const string SqlEntrega =
        """
        SELECT id AS Id, mensagem_id AS MensagemId, destinatario AS Destinatario, papel AS Papel, codigo AS Codigo,
        status AS Status, provedor AS Provedor, id_provedor AS IdProvedor, atualizado_em AS AtualizadoEm,
        assunto_personalizado AS AssuntoPersonalizado, corpo_personalizado AS CorpoPersonalizado FROM entrega
        """;

    private sealed class TentativaLinha
    {
        public string EntregaId { get; set; } = "";

        public string DestinoId { get; set; } = "";

        public int Aberta { get; set; }

        public int Sucesso { get; set; }

        public string IdProvedor { get; set; } = "";
    }
}
