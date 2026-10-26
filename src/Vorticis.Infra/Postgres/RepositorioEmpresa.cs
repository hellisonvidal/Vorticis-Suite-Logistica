using Npgsql;
using Vorticis.Application.Cadastros;

namespace Vorticis.Infra.Postgres;

public sealed class RepositorioEmpresa(NpgsqlDataSource ds) : IRepositorioEmpresa
{
    public async Task<bool> CnpjExisteAsync(string cnpj, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT EXISTS (SELECT 1 FROM core.empresa WHERE cnpj = @c)").Com("c", cnpj);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public Task<long> CriarAsync(NovaEmpresa e, IReadOnlyList<ContaModelo> plano, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, null, ct);

            long id;
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO core.empresa (cnpj, razao_social, nome_fantasia, natureza_juridica, cnae_principal, uf,
                                          cod_municipio_ibge, data_inicio_ativ, cep, logradouro, numero, complemento, bairro,
                                          municipio, email, telefone, situacao_cadastral, fonte_dados, dados_consultados_em, inscricao_estadual,
                                          nire, plano_modelo, mascara_plano, inicio_escrituracao, usa_centro_custo, grande_porte,
                                          conta_apuracao, conta_lucros, conta_prejuizos, mascara_centro_custo, exige_documento, data_bloqueio, usa_estorno,
                                          modulo_contabil, modulo_fiscal, modulo_folha, modulo_escritorio, modulo_erp, modulo_condominio, modulo_frota)
                VALUES (@cnpj, @rs, @nf, @nj, @cnae, @uf, @mun, @ini, @cep, @log, @num, @compl, @bairro,
                        @municipio, @email, @tel, @sit, @fonte, @consultado, @ie,
                        @nire, @modelo, @mascara, @inicioesc, @cc, @porte, @capur, @cluc, @cprej, @mcc, @exdoc, @bloq, @estorno, @mc, @mf, @mp, @me, @mr, @md, @mt) RETURNING id
                """, con, tx)
                .Com("cnpj", e.Cnpj).Com("rs", e.RazaoSocial).Com("nf", e.NomeFantasia).Com("nj", e.NaturezaJuridica)
                .Com("cnae", e.CnaePrincipal).Com("uf", e.Uf).Com("mun", e.CodMunicipioIbge).Com("ini", e.DataInicioAtividades)
                .ComTexto("cep", e.Complemento?.Cep).ComTexto("log", e.Complemento?.Logradouro).ComTexto("num", e.Complemento?.Numero)
                .ComTexto("compl", e.Complemento?.Complemento).ComTexto("bairro", e.Complemento?.Bairro)
                .ComTexto("municipio", e.Complemento?.Municipio).ComTexto("email", e.Complemento?.Email)
                .ComTexto("tel", e.Complemento?.Telefone).ComTexto("sit", e.Complemento?.SituacaoCadastral)
                .ComTexto("fonte", e.Complemento?.FonteDados)
                .Com("consultado", NpgsqlTypes.NpgsqlDbType.TimestampTz, e.Complemento?.DadosConsultadosEm)
                .ComTexto("ie", e.InscricaoEstadual).ComTexto("nire", e.Nire)
                .ComTexto("modelo", e.Contabil?.ModeloPlano).ComTexto("mascara", e.Contabil?.MascaraPlano)
                .Com("inicioesc", NpgsqlTypes.NpgsqlDbType.Date, e.Contabil?.InicioEscrituracao)
                .Com("cc", e.Contabil?.UsaCentroCusto ?? false).Com("porte", e.Contabil?.GrandePorte ?? false)
                .ComTexto("capur", e.Contabil?.ContaApuracao).ComTexto("cluc", e.Contabil?.ContaLucros).ComTexto("cprej", e.Contabil?.ContaPrejuizos)
                .ComTexto("mcc", e.Contabil?.MascaraCentroCusto).Com("exdoc", e.Contabil?.ExigeDocumento ?? false)
                .Com("bloq", NpgsqlTypes.NpgsqlDbType.Date, e.Contabil?.DataBloqueio)
                .Com("estorno", e.Contabil?.UsaEstorno ?? true)
                .Com("mc", e.Modulos.HasFlag(ModulosEmpresa.Contabil)).Com("mf", e.Modulos.HasFlag(ModulosEmpresa.Fiscal))
                .Com("mp", e.Modulos.HasFlag(ModulosEmpresa.Folha)).Com("me", e.Modulos.HasFlag(ModulosEmpresa.Escritorio))
                .Com("mr", e.Modulos.HasFlag(ModulosEmpresa.Erp)).Com("md", e.Modulos.HasFlag(ModulosEmpresa.Condominio))
                .Com("mt", e.Modulos.HasFlag(ModulosEmpresa.Frota)))
                id = (long)(await cmd.ExecuteScalarAsync(ct))!;

            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO core.estabelecimento (empresa_id, cnpj, matriz, uf, cod_municipio_ibge, inscricao_estadual)
                VALUES (@id, @cnpj, TRUE, @uf, @mun, @ie);
                INSERT INTO core.empresa_regime (empresa_id, regime, vigencia_inicio, apuracao_irpj, regime_caixa)
                VALUES (@id, @reg::core.regime_tributario, GREATEST(make_date(@ano, 1, 1), @ini), @apirpj, @caixa);
                """, con, tx)
                .Com("id", id).Com("cnpj", e.Cnpj).Com("uf", e.Uf).Com("mun", e.CodMunicipioIbge).ComTexto("ie", e.InscricaoEstadual)
                .Com("reg", e.Regime.Sql()).Com("ano", e.AnoPrimeiroExercicio).Com("ini", e.DataInicioAtividades)
                .ComTexto("apirpj", e.Contabil?.ApuracaoIrpj).Com("caixa", e.Contabil?.RegimeCaixa ?? false))
                await cmd.ExecuteNonQueryAsync(ct);

            var inicioExercicio = await CriarExercicioAsync(con, tx, id, e.AnoPrimeiroExercicio, ct);
            await InserirPlanoAsync(con, tx, id, plano, inicioExercicio, ct);

            await tx.CommitAsync(ct);
            return id;
        });

    public Task CriarExercicioAsync(long empresaId, int ano, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await CriarExercicioAsync(con, tx, empresaId, ano, ct);
            await tx.CommitAsync(ct);
            return 0;
        });

    /// <summary>Exercício = ano civil, começando no início das atividades se este cair no próprio ano.</summary>
    private static async Task<DateOnly> CriarExercicioAsync(NpgsqlConnection con, NpgsqlTransaction tx, long empresaId, int ano, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            WITH ex AS (
                INSERT INTO contabil.exercicio (empresa_id, ano, data_inicio, data_fim)
                SELECT @e, @ano, GREATEST(make_date(@ano, 1, 1), e.data_inicio_ativ), make_date(@ano, 12, 31)
                  FROM core.empresa e WHERE e.id = @e
                RETURNING id, data_inicio),
            per AS (
                INSERT INTO contabil.periodo (exercicio_id, competencia)
                SELECT ex.id, gs::date FROM ex,
                       generate_series(date_trunc('month', ex.data_inicio), make_date(@ano, 12, 1), INTERVAL '1 month') gs)
            SELECT data_inicio FROM ex
            """, con, tx).Com("e", empresaId).Com("ano", (short)ano);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return r.GetFieldValue<DateOnly>(0);
    }

    internal static async Task InserirPlanoAsync(NpgsqlConnection con, NpgsqlTransaction tx, long empresaId,
                                                IReadOnlyList<ContaModelo> plano, DateOnly dataInclusao, CancellationToken ct)
    {
        if (plano.Count == 0) return;
        var codigos = plano.Select(c => c.Codigo).ToHashSet();
        var ids = new Dictionary<string, long>();

        // Ordem por nível garante que o pai já exista ao inserir o filho.
        foreach (var c in plano.OrderBy(c => c.Codigo.Count(ch => ch == '.')).ThenBy(c => c.Codigo, StringComparer.Ordinal))
        {
            var ultimoPonto = c.Codigo.LastIndexOf('.');
            var codigoPai = ultimoPonto < 0 ? null : c.Codigo[..ultimoPonto];
            if (codigoPai is not null && !ids.ContainsKey(codigoPai))
                throw new Domain.Comum.ViolacaoRegraException($"Plano inválido: conta {c.Codigo} sem a conta superior {codigoPai}.");
            var analitica = codigoPai is not null && (c.Analitica ?? !codigos.Any(o => o.StartsWith(c.Codigo + ".", StringComparison.Ordinal)));

            await using var cmd = new NpgsqlCommand("""
                INSERT INTO contabil.plano_contas (empresa_id, codigo, descricao, tipo, nivel, conta_pai_id, cod_nat, natureza_saldo, data_inclusao)
                VALUES (@e, @c, @d, @t::contabil.tipo_conta, @n, @pai, @nat, @sal::contabil.indicador_dc, @inc) RETURNING id
                """, con, tx)
                .Com("e", empresaId).Com("c", c.Codigo).Com("d", c.Descricao).Com("t", analitica ? "A" : "S")
                .Com("n", (short)(c.Codigo.Count(ch => ch == '.') + 1)).Com("pai", codigoPai is null ? null : ids[codigoPai])
                .Com("nat", c.CodNat).Com("sal", c.NaturezaSaldo.ToString()).Com("inc", dataInclusao);
            ids[c.Codigo] = (long)(await cmd.ExecuteScalarAsync(ct))!;
        }
    }

    public async Task<EmpresaDetalhe?> ObterAsync(long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT id, cnpj, razao_social, nire, uf, cod_municipio_ibge, municipio, cep, logradouro, numero, complemento, bairro,
                   nome_fantasia, natureza_juridica, cnae_principal, data_inicio_ativ, inscricao_estadual, email, telefone,
                   (SELECT regime::text FROM core.empresa_regime r WHERE r.empresa_id = e.id ORDER BY vigencia_inicio DESC LIMIT 1),
                   situacao_cadastral, fonte_dados, modulo_contabil, modulo_fiscal, modulo_folha,
                   modulo_escritorio, modulo_erp, modulo_condominio, modulo_frota
              FROM core.empresa e WHERE id = @id AND ativa
            """).Com("id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        string? T(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        return new EmpresaDetalhe(r.GetInt64(0), r.GetString(1), r.GetString(2), T(3), r.GetString(4), r.GetString(5), T(6), T(7), T(8), T(9), T(10), T(11),
                                  T(12), r.GetString(13), r.GetString(14), r.GetFieldValue<DateOnly>(15), T(16), T(17), T(18),
                                  T(19) is { } reg ? Mapeamentos.RegimeDeSql(reg) : null, T(20), T(21),
                                  (r.GetBoolean(22) ? ModulosEmpresa.Contabil : 0) | (r.GetBoolean(23) ? ModulosEmpresa.Fiscal : 0)
                                  | (r.GetBoolean(24) ? ModulosEmpresa.Folha : 0) | (r.GetBoolean(25) ? ModulosEmpresa.Escritorio : 0)
                                  | (r.GetBoolean(26) ? ModulosEmpresa.Erp : 0) | (r.GetBoolean(27) ? ModulosEmpresa.Condominio : 0)
                                  | (r.GetBoolean(28) ? ModulosEmpresa.Frota : 0));
    }

    public Task AtualizarAsync(AlteracaoEmpresa e, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, null, ct);
            await using (var cmd = new NpgsqlCommand("""
                UPDATE core.empresa
                   SET razao_social = @rs, nome_fantasia = @nf, natureza_juridica = @nj, cnae_principal = @cnae, uf = @uf,
                       cod_municipio_ibge = @mun, data_inicio_ativ = @ini, inscricao_estadual = @ie, cep = @cep, logradouro = @log,
                       numero = @num, complemento = @compl, bairro = @bairro, municipio = @municipio, email = @email, telefone = @tel,
                       nire = @nire,
                       modulo_contabil = COALESCE(@mc, modulo_contabil), modulo_fiscal = COALESCE(@mf, modulo_fiscal),
                       modulo_folha = COALESCE(@mp, modulo_folha), modulo_escritorio = COALESCE(@me, modulo_escritorio),
                       modulo_erp = COALESCE(@mr, modulo_erp), modulo_condominio = COALESCE(@md, modulo_condominio),
                       modulo_frota = COALESCE(@mt, modulo_frota)
                 WHERE id = @id AND ativa;
                UPDATE core.estabelecimento SET uf = @uf, cod_municipio_ibge = @mun, inscricao_estadual = @ie WHERE empresa_id = @id AND matriz;
                """, con, tx)
                .Com("id", e.Id).Com("rs", e.RazaoSocial).ComTexto("nf", e.NomeFantasia).Com("nj", e.NaturezaJuridica)
                .Com("cnae", e.CnaePrincipal).Com("uf", e.Uf).Com("mun", e.CodMunicipioIbge).Com("ini", e.DataInicioAtividades)
                .ComTexto("ie", e.InscricaoEstadual).ComTexto("cep", e.Complemento?.Cep).ComTexto("log", e.Complemento?.Logradouro)
                .ComTexto("num", e.Complemento?.Numero).ComTexto("compl", e.Complemento?.Complemento).ComTexto("bairro", e.Complemento?.Bairro)
                .ComTexto("municipio", e.Complemento?.Municipio).ComTexto("email", e.Complemento?.Email).ComTexto("tel", e.Complemento?.Telefone)
                .ComTexto("nire", e.Nire)
                .Com("mc", NpgsqlTypes.NpgsqlDbType.Boolean, e.Modulos?.HasFlag(ModulosEmpresa.Contabil))
                .Com("mf", NpgsqlTypes.NpgsqlDbType.Boolean, e.Modulos?.HasFlag(ModulosEmpresa.Fiscal))
                .Com("mp", NpgsqlTypes.NpgsqlDbType.Boolean, e.Modulos?.HasFlag(ModulosEmpresa.Folha))
                .Com("me", NpgsqlTypes.NpgsqlDbType.Boolean, e.Modulos?.HasFlag(ModulosEmpresa.Escritorio))
                .Com("mr", NpgsqlTypes.NpgsqlDbType.Boolean, e.Modulos?.HasFlag(ModulosEmpresa.Erp))
                .Com("md", NpgsqlTypes.NpgsqlDbType.Boolean, e.Modulos?.HasFlag(ModulosEmpresa.Condominio))
                .Com("mt", NpgsqlTypes.NpgsqlDbType.Boolean, e.Modulos?.HasFlag(ModulosEmpresa.Frota)))
                await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });

    public Task AtivarModulosAsync(long id, ModulosEmpresa modulos, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("""
                UPDATE core.empresa
                   SET modulo_contabil = modulo_contabil OR @mc, modulo_fiscal = modulo_fiscal OR @mf, modulo_folha = modulo_folha OR @mp,
                       modulo_escritorio = modulo_escritorio OR @me, modulo_erp = modulo_erp OR @mr, modulo_condominio = modulo_condominio OR @md,
                       modulo_frota = modulo_frota OR @mt
                 WHERE id = @id
                """).Com("id", id).Com("mc", modulos.HasFlag(ModulosEmpresa.Contabil)).Com("mf", modulos.HasFlag(ModulosEmpresa.Fiscal))
                .Com("mp", modulos.HasFlag(ModulosEmpresa.Folha)).Com("me", modulos.HasFlag(ModulosEmpresa.Escritorio))
                .Com("mr", modulos.HasFlag(ModulosEmpresa.Erp)).Com("md", modulos.HasFlag(ModulosEmpresa.Condominio))
                .Com("mt", modulos.HasFlag(ModulosEmpresa.Frota));
            return await cmd.ExecuteNonQueryAsync(ct);
        });

    public Task AtualizarInscricaoEstadualAsync(long id, string? inscricao, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var cmd = ds.CreateCommand("""
                UPDATE core.empresa SET inscricao_estadual = @ie WHERE id = @id;
                UPDATE core.estabelecimento SET inscricao_estadual = @ie WHERE empresa_id = @id AND matriz;
                """).Com("id", id).ComTexto("ie", inscricao);
            return await cmd.ExecuteNonQueryAsync(ct);
        });

    public Task<int> ExcluirAsync(long id, CancellationToken ct)
        => Mapeamentos.TraduzirErrosAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Mapeamentos.DefinirUsuarioAsync(con, tx, null, ct);
            await using var cmd = new NpgsqlCommand("SELECT core.fn_excluir_empresa(@id)", con, tx).Com("id", id);
            var lancamentos = (int)(await cmd.ExecuteScalarAsync(ct))!;
            await tx.CommitAsync(ct);
            return lancamentos;
        });

    public async Task<IReadOnlyList<EmpresaResumo>> ListarAsync(CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT id, cnpj, razao_social FROM core.empresa WHERE ativa ORDER BY razao_social");
        var res = new List<EmpresaResumo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(new EmpresaResumo(r.GetInt64(0), r.GetString(1), r.GetString(2)));
        return res;
    }
}
