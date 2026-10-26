using Npgsql;

namespace Vorticis.Infra.Tests;

/// <summary>Migração 052: os procuradores (core.procurador) passam a ser contadores com procuração no vínculo da empresa.</summary>
public class MigracaoProcuracaoTests
{
    private static string PastaDb()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "db"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Pasta db não encontrada."), "db");
    }

    private static async Task ExecutarAsync(NpgsqlConnection con, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, con);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<T> EscalarAsync<T>(NpgsqlConnection con, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, con);
        return await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken) is { } o and not DBNull ? (T)o : default!;
    }

    [Fact]
    public async Task Migra_procuradores_para_contadores_casando_por_cpf_ou_criando_o_contador()
    {
        BancoDeTeste.ExigirBanco();
        var nome = "vorticis_mig_" + Guid.NewGuid().ToString("N");
        await using (var srv = new NpgsqlConnection(BancoDeTeste.ConexaoServidor))
        {
            await srv.OpenAsync(TestContext.Current.CancellationToken);
            await ExecutarAsync(srv, $"CREATE DATABASE {nome}");
        }
        var cs = new NpgsqlConnectionStringBuilder(BancoDeTeste.ConexaoServidor) { Database = nome, Pooling = false }.ConnectionString;
        try
        {
            await using var con = new NpgsqlConnection(cs);
            await con.OpenAsync(TestContext.Current.CancellationToken);
            var scripts = Directory.GetFiles(PastaDb(), "0*.sql").Order().ToList();
            foreach (var f in scripts.Where(f => string.CompareOrdinal(Path.GetFileName(f), "052") < 0))
                await ExecutarAsync(con, await File.ReadAllTextAsync(f, TestContext.Current.CancellationToken));

            await ExecutarAsync(con, """
                INSERT INTO core.empresa (id, cnpj, razao_social, natureza_juridica, cnae_principal, uf, cod_municipio_ibge, data_inicio_ativ)
                VALUES (9001, '11444777000161', 'Empresa A', '2062', '4751201', 'SP', '3550308', '2020-01-01'),
                       (9002, '34238864000168', 'Empresa B', '2062', '4751201', 'SP', '3550308', '2020-01-01');
                -- contador já cadastrado (com CRC e vínculo vigente na empresa A) e outro cadastrado sem vínculo
                INSERT INTO core.contador (id, nome, cpf, crc, uf_crc) VALUES (9001, 'Ana Contadora', '11144477735', '1SP123456/O-7', 'SP'),
                                                                               (9002, 'Carlos Contador', '39053344705', '1SP654321/O-1', 'SP');
                INSERT INTO core.empresa_contabilidade (empresa_id, contador_id, inicio, responsavel_tecnico) VALUES (9001, 9001, '2022-01-01', TRUE);
                INSERT INTO core.procurador (empresa_id, nome, cpf, email, telefone, vigencia_inicio, vigencia_fim, observacao) VALUES
                    (9001, 'Ana (procuradora)', '11144477735', 'ana@exemplo.com.br', '11999990000', '2025-03-01', NULL, 'Procuração 1'),
                    (9001, 'Beto Procurador', '52998224725', 'beto@exemplo.com.br', '11988880000', '2023-01-01', '2023-12-31', 'antiga'),
                    (9001, 'Beto Procurador', '52998224725', 'beto@exemplo.com.br', '11988880000', '2024-01-01', '2024-12-31', 'recente'),
                    (9002, 'Carlos (procurador)', '39053344705', NULL, NULL, '2025-01-01', '2026-12-31', NULL);
                """);

            await ExecutarAsync(con, await File.ReadAllTextAsync(scripts.Single(f => Path.GetFileName(f).StartsWith("052_")), TestContext.Current.CancellationToken));

            // com contador correspondente: reaproveita o cadastro (CRC preservado) e marca o vínculo vigente existente
            Assert.Equal(1L, await EscalarAsync<long>(con, "SELECT count(*) FROM core.contador WHERE cpf = '11144477735'"));
            Assert.Equal("1SP123456/O-7", await EscalarAsync<string>(con, "SELECT crc FROM core.contador WHERE cpf = '11144477735'"));
            Assert.Equal("true|2025-03-01||ECD,ECF,ESOCIAL|Procuração 1|true", await EscalarAsync<string>(con, """
                SELECT lower(procurador::text) || '|' || procuracao_inicio || '|' || COALESCE(procuracao_fim::text, '') || '|' ||
                       array_to_string(procuracao_abrangencia, ',') || '|' || procuracao_observacao || '|' || lower(responsavel_tecnico::text)
                  FROM core.empresa_contabilidade WHERE empresa_id = 9001 AND contador_id = 9001
                """));
            Assert.Equal(1L, await EscalarAsync<long>(con, "SELECT count(*) FROM core.empresa_contabilidade WHERE empresa_id = 9001 AND contador_id = 9001"));

            // sem contador correspondente: cria o contador (sem CRC) com os dados do procurador e o vínculo pela procuração mais recente
            Assert.Equal("Beto Procurador|beto@exemplo.com.br|11988880000|", await EscalarAsync<string>(con,
                "SELECT nome || '|' || email || '|' || telefone || '|' || COALESCE(crc, '') FROM core.contador WHERE cpf = '52998224725'"));
            Assert.Equal("2024-01-01|2024-12-31|recente", await EscalarAsync<string>(con, """
                SELECT v.procuracao_inicio || '|' || v.procuracao_fim || '|' || v.procuracao_observacao
                  FROM core.empresa_contabilidade v JOIN core.contador c ON c.id = v.contador_id WHERE c.cpf = '52998224725' AND v.procurador
                """));

            // contador existente sem vínculo na empresa: cria o vínculo (encerrado com o fim da procuração)
            Assert.Equal("9002|2025-01-01|2026-12-31|2026-12-31", await EscalarAsync<string>(con, """
                SELECT contador_id || '|' || inicio || '|' || fim || '|' || procuracao_fim FROM core.empresa_contabilidade WHERE empresa_id = 9002 AND procurador
                """));
            Assert.Equal(3L, await EscalarAsync<long>(con, "SELECT count(*) FROM core.empresa_contabilidade WHERE procurador"));
            Assert.Null(await EscalarAsync<string?>(con, "SELECT to_regclass('core.procurador')::text"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var srv = new NpgsqlConnection(BancoDeTeste.ConexaoServidor);
            await srv.OpenAsync(TestContext.Current.CancellationToken);
            await ExecutarAsync(srv, $"DROP DATABASE IF EXISTS {nome} WITH (FORCE)");
        }
    }
}
