namespace broker.Persistence.Adapters;

internal static class EsquemaDoBroker
{
    public const int Versao = 10;

    public const string Ansi =
        """
        CREATE TABLE IF NOT EXISTS aplicativo (
            id varchar(36) NOT NULL PRIMARY KEY,
            nome varchar(200) NOT NULL,
            usuario_smtp varchar(200) NOT NULL UNIQUE,
            senha_protegida varchar(4000) NOT NULL,
            token_busca varchar(64) NOT NULL UNIQUE,
            token_protegido varchar(4000) NOT NULL,
            criado_em varchar(40) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS destino (
            id varchar(36) NOT NULL PRIMARY KEY,
            aplicativo_id varchar(36) NOT NULL,
            canal varchar(16) NOT NULL,
            provedor varchar(32) NOT NULL,
            ordem integer NOT NULL,
            nome varchar(200) NOT NULL,
            segredo_protegido varchar(4000) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS dominio (
            id varchar(36) NOT NULL PRIMARY KEY,
            aplicativo_id varchar(36) NOT NULL,
            nome varchar(253) NOT NULL,
            validado varchar(64) NOT NULL,
            liberado varchar(64) NOT NULL,
            spf_nome varchar(300) NOT NULL,
            spf_valor varchar(500) NOT NULL,
            dkim_nome varchar(300) NOT NULL,
            dkim_valor varchar(4000) NOT NULL,
            chave_nome varchar(300) NOT NULL,
            chave_valor varchar(200) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS mensagem (
            id varchar(36) NOT NULL PRIMARY KEY,
            aplicativo_id varchar(36) NOT NULL,
            canal varchar(16) NOT NULL,
            msgid varchar(64) NOT NULL,
            remetente varchar(320) NOT NULL,
            nome_remetente varchar(200) NOT NULL,
            assunto varchar(998) NOT NULL,
            corpo text NOT NULL,
            anexos text NOT NULL,
            criado_em varchar(40) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS entrega (
            id varchar(36) NOT NULL PRIMARY KEY,
            mensagem_id varchar(36) NOT NULL,
            destinatario varchar(320) NOT NULL,
            papel varchar(8) NOT NULL,
            codigo varchar(8) NOT NULL,
            status varchar(120) NOT NULL,
            provedor varchar(32) NOT NULL,
            id_provedor varchar(200) NOT NULL,
            atualizado_em varchar(40) NOT NULL,
            assunto_personalizado text NOT NULL,
            corpo_personalizado text NOT NULL
        );
        CREATE TABLE IF NOT EXISTS tentativa (
            entrega_id varchar(36) NOT NULL,
            destino_id varchar(36) NOT NULL,
            aberta integer NOT NULL,
            sucesso integer NOT NULL,
            id_provedor varchar(200) NOT NULL,
            PRIMARY KEY (entrega_id, destino_id)
        );
        """;

    public const string SqlServer =
        """
        IF OBJECT_ID(N'aplicativo', N'U') IS NULL
        CREATE TABLE aplicativo (
            id nvarchar(36) NOT NULL PRIMARY KEY,
            nome nvarchar(200) NOT NULL,
            usuario_smtp nvarchar(200) NOT NULL UNIQUE,
            senha_protegida nvarchar(4000) NOT NULL,
            token_busca nvarchar(64) NOT NULL UNIQUE,
            token_protegido nvarchar(4000) NOT NULL,
            criado_em nvarchar(40) NOT NULL
        );
        IF OBJECT_ID(N'destino', N'U') IS NULL
        CREATE TABLE destino (
            id nvarchar(36) NOT NULL PRIMARY KEY,
            aplicativo_id nvarchar(36) NOT NULL,
            canal nvarchar(16) NOT NULL,
            provedor nvarchar(32) NOT NULL,
            ordem int NOT NULL,
            nome nvarchar(200) NOT NULL,
            segredo_protegido nvarchar(4000) NOT NULL
        );
        IF OBJECT_ID(N'dominio', N'U') IS NULL
        CREATE TABLE dominio (
            id nvarchar(36) NOT NULL PRIMARY KEY,
            aplicativo_id nvarchar(36) NOT NULL,
            nome nvarchar(253) NOT NULL,
            validado nvarchar(64) NOT NULL,
            liberado nvarchar(64) NOT NULL,
            spf_nome nvarchar(300) NOT NULL,
            spf_valor nvarchar(500) NOT NULL,
            dkim_nome nvarchar(300) NOT NULL,
            dkim_valor nvarchar(4000) NOT NULL,
            chave_nome nvarchar(300) NOT NULL,
            chave_valor nvarchar(200) NOT NULL
        );
        IF OBJECT_ID(N'mensagem', N'U') IS NULL
        CREATE TABLE mensagem (
            id nvarchar(36) NOT NULL PRIMARY KEY,
            aplicativo_id nvarchar(36) NOT NULL,
            canal nvarchar(16) NOT NULL,
            msgid nvarchar(64) NOT NULL,
            remetente nvarchar(320) NOT NULL,
            nome_remetente nvarchar(200) NOT NULL,
            assunto nvarchar(998) NOT NULL,
            corpo nvarchar(max) NOT NULL,
            anexos nvarchar(max) NOT NULL,
            criado_em nvarchar(40) NOT NULL
        );
        IF OBJECT_ID(N'entrega', N'U') IS NULL
        CREATE TABLE entrega (
            id nvarchar(36) NOT NULL PRIMARY KEY,
            mensagem_id nvarchar(36) NOT NULL,
            destinatario nvarchar(320) NOT NULL,
            papel nvarchar(8) NOT NULL,
            codigo nvarchar(8) NOT NULL,
            status nvarchar(120) NOT NULL,
            provedor nvarchar(32) NOT NULL,
            id_provedor nvarchar(200) NOT NULL,
            atualizado_em nvarchar(40) NOT NULL,
            assunto_personalizado nvarchar(max) NOT NULL,
            corpo_personalizado nvarchar(max) NOT NULL
        );
        IF OBJECT_ID(N'tentativa', N'U') IS NULL
        CREATE TABLE tentativa (
            entrega_id nvarchar(36) NOT NULL,
            destino_id nvarchar(36) NOT NULL,
            aberta int NOT NULL,
            sucesso int NOT NULL,
            id_provedor nvarchar(200) NOT NULL,
            CONSTRAINT PK_tentativa PRIMARY KEY (entrega_id, destino_id)
        );
        """;
}
