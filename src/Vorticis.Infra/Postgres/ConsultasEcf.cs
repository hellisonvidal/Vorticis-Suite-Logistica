using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using Vorticis.Application.Sped;
using Vorticis.Domain.Sped;

namespace Vorticis.Infra.Postgres;

/// <summary>
/// Leitura dos dados da ECF: a parte contábil (plano, saldos, lançamentos, períodos abertos) vem de <see cref="IConsultasEcd"/>; aqui entram o
/// cadastro completo da empresa, o regime do ano, o plano referencial, as apurações trimestrais do Fiscal (fiscal.apuracao_lucro), o LALUR
/// (Parte A e Parte B), os sócios e a ECD gerada. As tabelas fiscais são lidas direto do banco (o Infra não referencia o módulo Fiscal).
/// </summary>
public sealed partial class ConsultasEcf(NpgsqlDataSource ds, IConsultasEcd contabeis) : IConsultasEcf
{
    public async Task<DadosContabeisEcf?> CarregarAsync(long empresaId, int ano, CancellationToken ct)
    {
        var c = await contabeis.CarregarAsync(empresaId, ano, ct);
        if (c is null) return null;
        await using var con = await ds.OpenConnectionAsync(ct);

        EmpresaEcf empresa; string? nire;
        await using (var cmd = new NpgsqlCommand("""
            SELECT cnpj, razao_social, uf, cod_municipio_ibge, natureza_juridica, cnae_principal, logradouro, numero, complemento, bairro, cep, telefone, email,
                   data_inicio_ativ, nire
              FROM core.empresa WHERE id = @e
            """, con).Com("e", empresaId))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            string? T(int i) => r.IsDBNull(i) ? null : r.GetString(i).Trim();
            empresa = new EmpresaEcf(r.GetString(0).Trim(), r.GetString(1), r.GetString(2).Trim(), r.GetString(3).Trim(), r.GetString(4).Trim(), r.GetString(5).Trim(),
                T(6), T(7), T(8), T(9), T(10), T(11), T(12), r.GetFieldValue<DateOnly>(13));
            nire = T(14);
        }

        string? regime = null; var caixa = false;
        await using (var cmd = new NpgsqlCommand("""
            SELECT regime::text, regime_caixa FROM core.empresa_regime
             WHERE empresa_id = @e AND vigencia_inicio <= @fim AND (vigencia_fim IS NULL OR vigencia_fim >= @ini)
             ORDER BY vigencia_inicio DESC LIMIT 1
            """, con).Com("e", empresaId).Com("ini", c.InicioExercicio).Com("fim", c.FimExercicio))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            if (await r.ReadAsync(ct)) (regime, caixa) = (r.GetString(0), r.GetBoolean(1));

        var referencial = new List<ReferencialEcf>();
        if (c.CodPlanRef is not null)
            await using (var cmd = new NpgsqlCommand("""
                SELECT codigo, descricao, tipo = 'A', nivel, cod_nat, codigo_pai FROM contabil.plano_referencial
                 WHERE cod_plan_ref = @p AND ano_vigencia = @a ORDER BY codigo
                """, con).Com("p", NpgsqlTypes.NpgsqlDbType.Varchar, c.CodPlanRef).Com("a", (short)ano))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct))
                    referencial.Add(new ReferencialEcf(r.GetString(0), r.GetString(1), r.GetBoolean(2), r.GetInt16(3), r.GetString(4).Trim(), r.IsDBNull(5) ? null : r.GetString(5)));

        var apuracoes = new List<ApuracaoEcf>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT regime, trimestre, periodo_inicio, periodo_fim, receita_bruta, lucro_contabil, adicoes_irpj, exclusoes_irpj, adicoes_csll, exclusoes_csll,
                   compensacao_irpj, compensacao_csll, prejuizo_irpj, prejuizo_csll, base_irpj, base_csll, irpj_normal, irpj_adicional, irpj_deducoes, irpj_a_pagar,
                   csll_devida, csll_deducoes, csll_a_pagar, resultado::text
              FROM fiscal.apuracao_lucro WHERE empresa_id = @e AND ano = @a ORDER BY trimestre
            """, con).Com("e", empresaId).Com("a", (short)ano))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                var forma = r.GetString(0) == "REAL" ? FormaTributacaoEcf.Real : FormaTributacaoEcf.Presumido;
                var (receitas, outras) = forma == FormaTributacaoEcf.Presumido ? LerReceitas(r.GetString(23)) : ([], 0m);
                apuracoes.Add(new ApuracaoEcf(forma, r.GetInt16(1), r.GetFieldValue<DateOnly>(2), r.GetFieldValue<DateOnly>(3), r.GetDecimal(4),
                    r.IsDBNull(5) ? null : r.GetDecimal(5), r.GetDecimal(6), r.GetDecimal(7), r.GetDecimal(8), r.GetDecimal(9), r.GetDecimal(10), r.GetDecimal(11),
                    r.GetDecimal(12), r.GetDecimal(13), r.GetDecimal(14), r.GetDecimal(15), r.GetDecimal(16), r.GetDecimal(17), r.GetDecimal(18), r.GetDecimal(19),
                    r.GetDecimal(20), r.GetDecimal(21), r.GetDecimal(22), receitas, outras));
            }

        var lalur = new List<LancamentoLalurEcf>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT trimestre, tributo, tipo = 'ADICAO', descricao, valor, codigo_ecf, conta_id FROM fiscal.lalur_lancamento
             WHERE empresa_id = @e AND ano = @a ORDER BY trimestre, id
            """, con).Com("e", empresaId).Com("a", (short)ano))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                lalur.Add(new LancamentoLalurEcf(r.GetInt16(0), r.GetString(1), r.GetBoolean(2), r.GetString(3), r.GetDecimal(4),
                    r.IsDBNull(5) ? null : r.GetString(5).Trim(), r.IsDBNull(6) ? null : r.GetInt64(6)));

        var parteB = new List<MovimentoParteBEcf>();
        await using (var cmd = new NpgsqlCommand("SELECT tributo, tipo, ano, trimestre, valor FROM fiscal.lalur_parte_b WHERE empresa_id = @e ORDER BY id", con).Com("e", empresaId))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                parteB.Add(new MovimentoParteBEcf(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt16(2), r.IsDBNull(3) ? null : r.GetInt16(3), r.GetDecimal(4)));

        var socios = new List<SocioEcf>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT nome, tipo_pessoa, cpf_cnpj, participacao, data_entrada, data_saida, qualificacao FROM core.socio WHERE empresa_id = @e ORDER BY id
            """, con).Com("e", empresaId))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                socios.Add(new SocioEcf(r.GetString(0), r.GetString(1)[0], r.IsDBNull(2) ? null : r.GetString(2).Trim(), r.IsDBNull(3) ? null : r.GetDecimal(3),
                    r.IsDBNull(4) ? null : r.GetFieldValue<DateOnly>(4), r.IsDBNull(5) ? null : r.GetFieldValue<DateOnly>(5), r.GetString(6)));

        EcdDaEcf? ecd = null;
        await using (var cmd = new NpgsqlCommand("""
            SELECT hash_sha1, dt_ini, dt_fin, num_ordem, nat_livro FROM contabil.ecd_gerada WHERE empresa_id = @e AND ano = @a
             ORDER BY gerado_em DESC, id DESC LIMIT 1
            """, con).Com("e", empresaId).Com("a", (short)ano))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            if (await r.ReadAsync(ct))
                ecd = new EcdDaEcf(r.GetString(0).Trim(), r.GetFieldValue<DateOnly>(1), r.GetFieldValue<DateOnly>(2), r.GetInt32(3), r.GetString(4), nire);

        return new DadosContabeisEcf(empresa, regime, caixa, c.ExercicioId, c.InicioExercicio, c.FimExercicio, c.CodPlanRef, c.Plano, c.SaldosAbertura, c.Lancamentos,
            referencial, apuracoes, lalur, parteB, socios, ecd, c.PeriodosAbertos);
    }

    // ---------------------------------------------------------------- memória do Lucro Presumido
    [GeneratedRegex(@"receita (?<v>[\d.]+,\d{2}) × (?<pi>[\d.,]+)% = base IRPJ [\d.]+,\d{2} · × (?<pc>[\d.,]+)% = base CSLL")]
    private static partial Regex RegexReceita();
    [GeneratedRegex(@"Outras receitas .*: (?<v>[\d.]+,\d{2})\s*$")]
    private static partial Regex RegexOutras();

    private static decimal Moeda(string s) => decimal.Parse(s.Replace(".", "").Replace(',', '.'), CultureInfo.InvariantCulture);
    private static decimal Perc(string s) => decimal.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);

    /// <summary>Receita por percentual de presunção (e outras receitas), extraída da memória de cálculo gravada na apuração.</summary>
    internal static (IReadOnlyList<ReceitaPresumidaEcf>, decimal) LerReceitas(string json)
    {
        var receitas = new List<ReceitaPresumidaEcf>();
        var outras = 0m;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("memoria", out var memoria)) return (receitas, outras);
            foreach (var linha in memoria.EnumerateArray().Select(x => x.GetString() ?? ""))
            {
                if (RegexReceita().Match(linha) is { Success: true } m)
                    receitas.Add(new ReceitaPresumidaEcf(Perc(m.Groups["pi"].Value), Perc(m.Groups["pc"].Value), Moeda(m.Groups["v"].Value)));
                else if (RegexOutras().Match(linha) is { Success: true } o) outras += Moeda(o.Groups["v"].Value);
            }
        }
        catch (JsonException) { }
        return (receitas, outras);
    }

    public async Task<long> RegistrarAsync(long empresaId, int ano, DateOnly inicio, DateOnly fim, string formaTributacao, string hashSha1, int linhas, int assinaturas,
                                           long? usuarioId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            INSERT INTO contabil.ecf_gerada (empresa_id, ano, dt_ini, dt_fin, forma_trib, hash_sha1, linhas, assinaturas, gerado_por)
            VALUES (@e, @a, @ini, @fim, @f, @h, @l, @s, (SELECT id FROM core.usuario WHERE id = @u))
            RETURNING id
            """).Com("e", empresaId).Com("a", (short)ano).Com("ini", inicio).Com("fim", fim).Com("f", formaTributacao).Com("h", hashSha1).Com("l", linhas)
                .Com("s", (short)assinaturas).Com("u", NpgsqlTypes.NpgsqlDbType.Bigint, usuarioId);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<IReadOnlyList<EcfRegistrada>> ListarAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT id, ano, dt_ini, dt_fin, forma_trib, hash_sha1, linhas, assinaturas, gerado_em
              FROM contabil.ecf_gerada WHERE empresa_id = @e ORDER BY gerado_em DESC, id DESC
            """).Com("e", empresaId);
        var res = new List<EcfRegistrada>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new EcfRegistrada(r.GetInt64(0), r.GetInt16(1), r.GetFieldValue<DateOnly>(2), r.GetFieldValue<DateOnly>(3), r.GetString(4), r.GetString(5),
                                      r.GetInt32(6), r.GetInt16(7), r.GetFieldValue<DateTimeOffset>(8)));
        return res;
    }
}
