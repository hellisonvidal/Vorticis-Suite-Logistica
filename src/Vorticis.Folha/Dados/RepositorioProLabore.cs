using Npgsql;
using Vorticis.Folha.Cadastros;

namespace Vorticis.Folha.Dados;

/// <summary>Pró-labore dos sócios (folha.pro_labore) e cálculos por competência (folha.pro_labore_calculo).</summary>
public sealed class RepositorioProLabore(NpgsqlDataSource ds)
{
    /// <summary>Todos os sócios pessoa física da empresa, com a configuração de pró-labore (se houver).</summary>
    public async Task<IReadOnlyList<ProLaboreSocio>> ListarAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT s.id, s.nome, s.cpf_cnpj, s.qualificacao, p.id, COALESCE(p.retira, FALSE), COALESCE(p.valor, 0), p.inicio, p.fim,
                   COALESCE(p.dependentes_irrf, 0), COALESCE(p.inss_outras_fontes, 0), p.observacao
              FROM core.socio s LEFT JOIN folha.pro_labore p ON p.socio_id = s.id AND p.empresa_id = s.empresa_id
             WHERE s.empresa_id = @e AND s.tipo_pessoa = 'F'
             ORDER BY s.nome
            """).Com("e", empresaId);
        var res = new List<ProLaboreSocio>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new ProLaboreSocio(r.GetInt64(0), r.GetString(1), r.Texto(2), r.GetString(3), r.Id(4), r.GetBoolean(5), r.GetDecimal(6),
                                       r.Data(7), r.Data(8), r.GetInt32(9), r.GetDecimal(10), r.Texto(11)));
        return res;
    }

    public Task GravarAsync(long empresaId, ProLaboreSocio p, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = ds.CreateCommand("""
            INSERT INTO folha.pro_labore (empresa_id, socio_id, retira, valor, inicio, fim, dependentes_irrf, inss_outras_fontes, observacao)
            SELECT @e, s.id, @r, @v, @i, @f, @d, @o, @obs FROM core.socio s WHERE s.id = @s AND s.empresa_id = @e
            ON CONFLICT (empresa_id, socio_id) DO UPDATE
               SET retira = EXCLUDED.retira, valor = EXCLUDED.valor, inicio = EXCLUDED.inicio, fim = EXCLUDED.fim,
                   dependentes_irrf = EXCLUDED.dependentes_irrf, inss_outras_fontes = EXCLUDED.inss_outras_fontes, observacao = EXCLUDED.observacao
            """).Com("e", empresaId).Com("s", p.SocioId).Com("r", p.Retira).Com("v", p.Valor).ComData("i", p.Inicio).ComData("f", p.Fim)
                .Com("d", (short)p.DependentesIrrf).Com("o", p.InssOutrasFontes).ComTexto("obs", p.Observacao);
        return await cmd.ExecuteNonQueryAsync(ct);
    });

    public async Task<IReadOnlyList<ProLaboreCalculo>> ListarCalculosAsync(long empresaId, DateOnly competencia, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT c.id, c.socio_id, s.nome, c.competencia, c.bruto, c.inss, c.irrf, c.liquido, c.inss_patronal, c.memoria
              FROM folha.pro_labore_calculo c JOIN core.socio s ON s.id = c.socio_id
             WHERE c.empresa_id = @e AND c.competencia = @c ORDER BY s.nome
            """).Com("e", empresaId).ComData("c", competencia);
        var res = new List<ProLaboreCalculo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new ProLaboreCalculo(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetFieldValue<DateOnly>(3), r.GetDecimal(4), r.GetDecimal(5),
                                         r.GetDecimal(6), r.GetDecimal(7), r.GetDecimal(8), r.GetString(9)));
        return res;
    }

    /// <summary>Substitui os cálculos da competência (o gatilho do banco recusa se a competência estiver fechada).</summary>
    public Task GravarCalculosAsync(long empresaId, DateOnly competencia, IReadOnlyList<ProLaboreCalculo> calculos, long usuarioId, CancellationToken ct)
        => Sql.TraduzirAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await using (var del = new NpgsqlCommand("DELETE FROM folha.pro_labore_calculo WHERE empresa_id = @e AND competencia = @c", con, tx))
                await del.Com("e", empresaId).ComData("c", competencia).ExecuteNonQueryAsync(ct);
            foreach (var c in calculos)
            {
                await using var ins = new NpgsqlCommand("""
                    INSERT INTO folha.pro_labore_calculo (empresa_id, socio_id, competencia, bruto, inss, irrf, liquido, inss_patronal, memoria, calculado_por)
                    VALUES (@e, @s, @c, @b, @i, @ir, @l, @p, @m, @u)
                    """, con, tx);
                await ins.Com("e", empresaId).Com("s", c.SocioId).ComData("c", competencia).Com("b", c.Bruto).Com("i", c.Inss).Com("ir", c.Irrf)
                         .Com("l", c.Liquido).Com("p", c.InssPatronal).Com("m", c.Memoria).Com("u", usuarioId).ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            return calculos.Count;
        });

    public Task ExcluirCalculosAsync(long empresaId, DateOnly competencia, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = ds.CreateCommand("DELETE FROM folha.pro_labore_calculo WHERE empresa_id = @e AND competencia = @c")
                                .Com("e", empresaId).ComData("c", competencia);
        return await cmd.ExecuteNonQueryAsync(ct);
    });
}
