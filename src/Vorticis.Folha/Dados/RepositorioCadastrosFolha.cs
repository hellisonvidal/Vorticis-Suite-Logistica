using Npgsql;
using Vorticis.Folha.Cadastros;
using Vorticis.Folha.Regras;

namespace Vorticis.Folha.Dados;

public sealed class RepositorioCadastrosFolha(NpgsqlDataSource ds) : IRepositorioCadastrosFolha
{
    // ------------------------------------------------------------------ departamentos e cargos

    public async Task<IReadOnlyList<Departamento>> ListarDepartamentosAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT d.id, d.codigo, d.nome, d.ativo, EXISTS (SELECT 1 FROM folha.funcionario f WHERE f.departamento_id = d.id)
              FROM folha.departamento d WHERE d.empresa_id = @e ORDER BY d.codigo
            """).Com("e", empresaId);
        var res = new List<Departamento>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(new Departamento(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetBoolean(4)));
        return res;
    }

    public Task<long> GravarDepartamentoAsync(long empresaId, Departamento d, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = d.Id == 0
            ? ds.CreateCommand("INSERT INTO folha.departamento (empresa_id, codigo, nome, ativo) VALUES (@e, @c, @n, @a) RETURNING id")
            : ds.CreateCommand("UPDATE folha.departamento SET codigo = @c, nome = @n, ativo = @a WHERE id = @id AND empresa_id = @e RETURNING id");
        cmd.Com("e", empresaId).Com("c", d.Codigo).Com("n", d.Nome).Com("a", d.Ativo);
        if (d.Id != 0) cmd.Com("id", d.Id);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    });

    public Task ExcluirDepartamentoAsync(long empresaId, long id, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = ds.CreateCommand("DELETE FROM folha.departamento WHERE id = @id AND empresa_id = @e").Com("id", id).Com("e", empresaId);
        return await cmd.ExecuteNonQueryAsync(ct);
    });

    public async Task<IReadOnlyList<Cargo>> ListarCargosAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT c.id, c.codigo, c.nome, c.cbo, c.ativo, EXISTS (SELECT 1 FROM folha.funcionario f WHERE f.cargo_id = c.id)
              FROM folha.cargo c WHERE c.empresa_id = @e ORDER BY c.codigo
            """).Com("e", empresaId);
        var res = new List<Cargo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(new Cargo(r.GetInt64(0), r.GetString(1), r.GetString(2), r.Texto(3), r.GetBoolean(4), r.GetBoolean(5)));
        return res;
    }

    public Task<long> GravarCargoAsync(long empresaId, Cargo c, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = c.Id == 0
            ? ds.CreateCommand("INSERT INTO folha.cargo (empresa_id, codigo, nome, cbo, ativo) VALUES (@e, @c, @n, @cbo, @a) RETURNING id")
            : ds.CreateCommand("UPDATE folha.cargo SET codigo = @c, nome = @n, cbo = @cbo, ativo = @a WHERE id = @id AND empresa_id = @e RETURNING id");
        cmd.Com("e", empresaId).Com("c", c.Codigo).Com("n", c.Nome).ComTexto("cbo", c.Cbo).Com("a", c.Ativo);
        if (c.Id != 0) cmd.Com("id", c.Id);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    });

    public Task ExcluirCargoAsync(long empresaId, long id, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = ds.CreateCommand("DELETE FROM folha.cargo WHERE id = @id AND empresa_id = @e").Com("id", id).Com("e", empresaId);
        return await cmd.ExecuteNonQueryAsync(ct);
    });

    // ------------------------------------------------------------------ funcionários

    private const string SelectFuncionario = """
        SELECT f.id, f.matricula, f.nome, f.cpf, f.pis_nis, f.data_nascimento, f.sexo, f.raca_cor, f.estado_civil, f.grau_instrucao, f.email, f.telefone,
               f.cep, f.logradouro, f.numero, f.complemento, f.bairro, f.cod_municipio_ibge, f.uf,
               f.data_admissao, f.categoria_esocial, f.tipo_contrato, f.data_termino_contrato, f.cargo_id, f.departamento_id, f.cbo,
               f.tipo_salario, f.salario, f.jornada_semanal_horas, f.jornada_mensal_horas, f.centro_custo_id, f.insalubridade_percentual, f.periculosidade,
               f.banco, f.agencia, f.conta, f.tipo_conta, f.chave_pix, f.situacao, f.data_desligamento, f.motivo_desligamento,
               c.nome, d.nome, cc.codigo, f.tipo_logradouro, f.cnpj_sindicato, f.tipo_jornada, f.descricao_jornada
          FROM folha.funcionario f
          LEFT JOIN folha.cargo c ON c.id = f.cargo_id
          LEFT JOIN folha.departamento d ON d.id = f.departamento_id
          LEFT JOIN contabil.centro_custo cc ON cc.id = f.centro_custo_id
        """;

    private static Funcionario Ler(NpgsqlDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.Texto(4), r.GetFieldValue<DateOnly>(5), r.GetString(6), r.GetInt16(7), r.GetInt16(8),
        r.GetString(9), r.Texto(10), r.Texto(11), r.Texto(12), r.Texto(13), r.Texto(14), r.Texto(15), r.Texto(16), r.Texto(17), r.Texto(18),
        r.GetFieldValue<DateOnly>(19), r.GetString(20), r.GetString(21), r.Data(22), r.Id(23), r.Id(24), r.Texto(25),
        r.GetString(26), r.GetDecimal(27), r.GetDecimal(28), r.GetDecimal(29), r.Id(30), r.GetDecimal(31), r.GetBoolean(32),
        r.Texto(33), r.Texto(34), r.Texto(35), r.Texto(36), r.Texto(37), r.GetString(38), r.Data(39), r.Texto(40), [],
        r.Texto(41), r.Texto(42), r.Texto(43), r.Texto(44), r.Texto(45), r.GetInt16(46), r.Texto(47));

    private async Task<Dictionary<long, List<DependenteFolha>>> DependentesAsync(long empresaId, long? funcionarioId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT x.funcionario_id, x.id, x.nome, x.cpf, x.data_nascimento, x.tipo_dependente, x.incapaz, x.dep_irrf, x.dep_salario_familia
              FROM folha.dependente x JOIN folha.funcionario f ON f.id = x.funcionario_id
             WHERE f.empresa_id = @e AND (@f::bigint IS NULL OR f.id = @f) ORDER BY x.id
            """).Com("e", empresaId).ComId("f", funcionarioId);
        var res = new Dictionary<long, List<DependenteFolha>>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (!res.TryGetValue(r.GetInt64(0), out var lista)) res[r.GetInt64(0)] = lista = [];
            lista.Add(new DependenteFolha(r.GetInt64(1), r.GetString(2), r.Texto(3), r.GetFieldValue<DateOnly>(4), r.GetString(5), r.GetBoolean(6),
                                          r.GetBoolean(7), r.GetBoolean(8)));
        }
        return res;
    }

    public async Task<IReadOnlyList<Funcionario>> ListarFuncionariosAsync(long empresaId, CancellationToken ct)
    {
        var lista = new List<Funcionario>();
        await using (var cmd = ds.CreateCommand(SelectFuncionario + " WHERE f.empresa_id = @e ORDER BY f.nome, f.matricula").Com("e", empresaId))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) lista.Add(Ler(r));
        var deps = await DependentesAsync(empresaId, null, ct);
        return lista.Select(f => deps.TryGetValue(f.Id, out var d) ? f with { Dependentes = d } : f).ToList();
    }

    public async Task<Funcionario?> ObterFuncionarioAsync(long empresaId, long id, CancellationToken ct)
    {
        Funcionario? f = null;
        await using (var cmd = ds.CreateCommand(SelectFuncionario + " WHERE f.empresa_id = @e AND f.id = @id").Com("e", empresaId).Com("id", id))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            if (await r.ReadAsync(ct)) f = Ler(r);
        if (f is null) return null;
        var deps = await DependentesAsync(empresaId, id, ct);
        return deps.TryGetValue(id, out var d) ? f with { Dependentes = d } : f;
    }

    public Task<long> GravarFuncionarioAsync(long empresaId, Funcionario f, DateOnly dataAlteracao, long usuarioId, CancellationToken ct)
        => Sql.TraduzirAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);

            // Situação anterior (para registrar alteração contratual)
            decimal? salarioAnterior = null; string? cargoAnterior = null;
            if (f.Id != 0)
            {
                await using var ant = new NpgsqlCommand("""
                    SELECT f.salario, c.nome FROM folha.funcionario f LEFT JOIN folha.cargo c ON c.id = f.cargo_id WHERE f.id = @id AND f.empresa_id = @e
                    """, con, tx).Com("id", f.Id).Com("e", empresaId);
                await using var ra = await ant.ExecuteReaderAsync(ct);
                if (!await ra.ReadAsync(ct)) throw new Vorticis.Domain.Comum.ViolacaoRegraException("Funcionário não encontrado.");
                salarioAnterior = ra.GetDecimal(0);
                cargoAnterior = ra.Texto(1);
            }

            const string colunas = """
                matricula = @matricula, nome = @nome, cpf = @cpf, pis_nis = @pis, data_nascimento = @nasc, sexo = @sexo, raca_cor = @raca, estado_civil = @civil,
                grau_instrucao = @grau, email = @email, telefone = @tel, cep = @cep, logradouro = @logr, numero = @num, complemento = @compl, bairro = @bairro,
                cod_municipio_ibge = @mun, uf = @uf, data_admissao = @adm, categoria_esocial = @cat, tipo_contrato = @contrato, data_termino_contrato = @termino,
                cargo_id = @cargo, departamento_id = @depto, cbo = @cbo, tipo_salario = @tsal, salario = @salario, jornada_semanal_horas = @jsem,
                jornada_mensal_horas = @jmes, centro_custo_id = @cc, insalubridade_percentual = @insal, periculosidade = @peric, banco = @banco,
                agencia = @ag, conta = @conta, tipo_conta = @tconta, chave_pix = @pix, situacao = @sit, data_desligamento = @deslig, motivo_desligamento = @motivo,
                tipo_logradouro = @tplogr, cnpj_sindicato = @sind, tipo_jornada = @tpjor, descricao_jornada = @dscjor
                """;
            long id;
            NpgsqlCommand Cmd(string sql)
            {
                var c = new NpgsqlCommand(sql, con, tx);
                c.Com("e", empresaId).Com("matricula", f.Matricula).Com("nome", f.Nome).Com("cpf", f.Cpf).ComTexto("pis", f.PisNis).ComData("nasc", f.Nascimento)
                 .Com("sexo", f.Sexo).Com("raca", (short)f.RacaCor).Com("civil", (short)f.EstadoCivil).Com("grau", f.GrauInstrucao).ComTexto("email", f.Email)
                 .ComTexto("tel", f.Telefone).ComTexto("cep", f.Cep).ComTexto("logr", f.Logradouro).ComTexto("num", f.Numero).ComTexto("compl", f.Complemento)
                 .ComTexto("bairro", f.Bairro).ComTexto("mun", f.CodMunicipioIbge).ComTexto("uf", f.Uf).ComData("adm", f.Admissao).Com("cat", f.Categoria)
                 .Com("contrato", f.TipoContrato).ComData("termino", f.TerminoContrato).ComId("cargo", f.CargoId).ComId("depto", f.DepartamentoId)
                 .ComTexto("cbo", f.Cbo).Com("tsal", f.TipoSalario).Com("salario", f.Salario).Com("jsem", f.JornadaSemanal).Com("jmes", f.JornadaMensal)
                 .ComId("cc", f.CentroCustoId).Com("insal", f.Insalubridade).Com("peric", f.Periculosidade).ComTexto("banco", f.Banco).ComTexto("ag", f.Agencia)
                 .ComTexto("conta", f.Conta).ComTexto("tconta", f.TipoConta).ComTexto("pix", f.ChavePix).Com("sit", f.Situacao).ComData("deslig", f.Desligamento)
                 .ComTexto("motivo", f.MotivoDesligamento).ComTexto("tplogr", f.TipoLogradouro).ComTexto("sind", f.CnpjSindicato)
                 .Com("tpjor", (short)f.TipoJornada).ComTexto("dscjor", f.DescricaoJornada);
                return c;
            }
            if (f.Id == 0)
            {
                await using var ins = Cmd($"""
                    INSERT INTO folha.funcionario (empresa_id, matricula, nome, cpf, pis_nis, data_nascimento, sexo, raca_cor, estado_civil, grau_instrucao, email, telefone,
                        cep, logradouro, numero, complemento, bairro, cod_municipio_ibge, uf, data_admissao, categoria_esocial, tipo_contrato, data_termino_contrato,
                        cargo_id, departamento_id, cbo, tipo_salario, salario, jornada_semanal_horas, jornada_mensal_horas, centro_custo_id, insalubridade_percentual,
                        periculosidade, banco, agencia, conta, tipo_conta, chave_pix, situacao, data_desligamento, motivo_desligamento,
                        tipo_logradouro, cnpj_sindicato, tipo_jornada, descricao_jornada)
                    VALUES (@e, @matricula, @nome, @cpf, @pis, @nasc, @sexo, @raca, @civil, @grau, @email, @tel, @cep, @logr, @num, @compl, @bairro, @mun, @uf, @adm, @cat,
                        @contrato, @termino, @cargo, @depto, @cbo, @tsal, @salario, @jsem, @jmes, @cc, @insal, @peric, @banco, @ag, @conta, @tconta, @pix, @sit, @deslig, @motivo,
                        @tplogr, @sind, @tpjor, @dscjor)
                    RETURNING id
                    """);
                id = (long)(await ins.ExecuteScalarAsync(ct))!;
            }
            else
            {
                id = f.Id;
                await using var upd = Cmd($"UPDATE folha.funcionario SET {colunas} WHERE id = @id AND empresa_id = @e").Com("id", id);
                await upd.ExecuteNonQueryAsync(ct);
                // Alterações contratuais (S-2206): salário ou cargo
                var cargoNovo = await NomeCargoAsync(con, tx, f.CargoId, ct);
                if (salarioAnterior != f.Salario || cargoAnterior != cargoNovo)
                {
                    var partes = new List<string>();
                    if (salarioAnterior != f.Salario) partes.Add("alteração salarial");
                    if (cargoAnterior != cargoNovo) partes.Add("alteração de cargo");
                    await using var alt = new NpgsqlCommand("""
                        INSERT INTO folha.alteracao_contratual (empresa_id, funcionario_id, data_alteracao, descricao, salario_anterior, salario_novo, cargo_anterior, cargo_novo)
                        VALUES (@e, @f, @d, @desc, @sa, @sn, @ca, @cn)
                        """, con, tx).Com("e", empresaId).Com("f", id).ComData("d", dataAlteracao).Com("desc", string.Join(" e ", partes))
                        .Com("sa", NpgsqlTypes.NpgsqlDbType.Numeric, salarioAnterior).Com("sn", f.Salario).ComTexto("ca", cargoAnterior).ComTexto("cn", cargoNovo);
                    await alt.ExecuteNonQueryAsync(ct);
                }
                await using var del = new NpgsqlCommand("DELETE FROM folha.dependente WHERE funcionario_id = @f", con, tx).Com("f", id);
                await del.ExecuteNonQueryAsync(ct);
            }
            foreach (var d in f.Dependentes)
            {
                await using var dep = new NpgsqlCommand("""
                    INSERT INTO folha.dependente (funcionario_id, nome, cpf, data_nascimento, tipo_dependente, incapaz, dep_irrf, dep_salario_familia)
                    VALUES (@f, @n, @cpf, @nasc, @t, @inc, @ir, @sf)
                    """, con, tx).Com("f", id).Com("n", d.Nome).ComTexto("cpf", d.Cpf).ComData("nasc", d.Nascimento).Com("t", d.Tipo).Com("inc", d.Incapaz)
                    .Com("ir", d.DepIrrf).Com("sf", d.DepSalarioFamilia);
                await dep.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            return id;
        });

    private static async Task<string?> NomeCargoAsync(NpgsqlConnection con, NpgsqlTransaction tx, long? cargoId, CancellationToken ct)
    {
        if (cargoId is null) return null;
        await using var cmd = new NpgsqlCommand("SELECT nome FROM folha.cargo WHERE id = @id", con, tx).Com("id", cargoId);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public Task ExcluirFuncionarioAsync(long empresaId, long id, long usuarioId, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
        foreach (var sql in new[]
        {
            "DELETE FROM folha.alteracao_contratual WHERE funcionario_id = @id AND empresa_id = @e",
            "DELETE FROM folha.afastamento WHERE funcionario_id = @id AND empresa_id = @e",
            "DELETE FROM folha.dependente WHERE funcionario_id = @id AND EXISTS (SELECT 1 FROM folha.funcionario x WHERE x.id = @id AND x.empresa_id = @e)",
            "DELETE FROM folha.funcionario WHERE id = @id AND empresa_id = @e",
        })
        {
            await using var cmd = new NpgsqlCommand(sql, con, tx).Com("id", id).Com("e", empresaId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return 0;
    });

    public async Task<bool> FuncionarioTemMovimentoAsync(long empresaId, long id, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT EXISTS (SELECT 1 FROM folha.calculo WHERE funcionario_id = @id AND empresa_id = @e)
                OR EXISTS (SELECT 1 FROM folha.evento_variavel WHERE funcionario_id = @id AND empresa_id = @e)
                OR EXISTS (SELECT 1 FROM folha.ferias WHERE funcionario_id = @id AND empresa_id = @e)
                OR EXISTS (SELECT 1 FROM folha.rescisao WHERE funcionario_id = @id AND empresa_id = @e)
                OR EXISTS (SELECT 1 FROM folha.provisao WHERE funcionario_id = @id AND empresa_id = @e)
            """).Com("id", id).Com("e", empresaId);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public Task DefinirDesligamentoAsync(long empresaId, long funcionarioId, DateOnly? data, string? motivo, long usuarioId, CancellationToken ct)
        => Sql.TraduzirAsync(async () =>
        {
            await using var con = await ds.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
            await using var cmd = new NpgsqlCommand("""
                UPDATE folha.funcionario SET situacao = CASE WHEN @d::date IS NULL THEN 'ATIVO' ELSE 'DESLIGADO' END,
                       data_desligamento = @d::date, motivo_desligamento = @m
                 WHERE id = @id AND empresa_id = @e
                """, con, tx).Com("id", funcionarioId).Com("e", empresaId).ComData("d", data).ComTexto("m", motivo);
            await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return 0;
        });

    // ------------------------------------------------------------------ rubricas

    public async Task<IReadOnlyList<Rubrica>> ListarRubricasAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT id, codigo, descricao, tipo, natureza_esocial, inc_inss, inc_irrf, inc_fgts, variavel, sistema, ativa
              FROM folha.rubrica WHERE empresa_id = @e ORDER BY codigo
            """).Com("e", empresaId);
        var res = new List<Rubrica>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new Rubrica(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)[0], r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7),
                                r.GetBoolean(8), r.GetBoolean(9), r.GetBoolean(10)));
        return res;
    }

    public Task<long> GravarRubricaAsync(long empresaId, Rubrica x, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = x.Id == 0
            ? ds.CreateCommand("""
                INSERT INTO folha.rubrica (empresa_id, codigo, descricao, tipo, natureza_esocial, inc_inss, inc_irrf, inc_fgts, variavel, sistema, ativa)
                VALUES (@e, @c, @d, @t, @n, @ci, @cr, @cf, @v, FALSE, @a) RETURNING id
                """)
            : ds.CreateCommand("""
                UPDATE folha.rubrica SET descricao = @d, tipo = CASE WHEN sistema THEN tipo ELSE @t END, natureza_esocial = @n, inc_inss = @ci, inc_irrf = @cr,
                       inc_fgts = @cf, variavel = @v, ativa = @a WHERE id = @id AND empresa_id = @e RETURNING id
                """);
        cmd.Com("e", empresaId).Com("c", x.Codigo).Com("d", x.Descricao).Com("t", x.Tipo.ToString()).Com("n", x.Natureza).Com("ci", x.IncInss).Com("cr", x.IncIrrf)
           .Com("cf", x.IncFgts).Com("v", x.Variavel).Com("a", x.Ativa);
        if (x.Id != 0) cmd.Com("id", x.Id);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    });

    public Task ExcluirRubricaAsync(long empresaId, long id, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = ds.CreateCommand("DELETE FROM folha.rubrica WHERE id = @id AND empresa_id = @e AND NOT sistema").Com("id", id).Com("e", empresaId);
        return await cmd.ExecuteNonQueryAsync(ct);
    });

    public async Task GarantirRubricasPadraoAsync(long empresaId, CancellationToken ct)
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        foreach (var r in RubricasPadrao.Todas)
        {
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO folha.rubrica (empresa_id, codigo, descricao, tipo, natureza_esocial, inc_inss, inc_irrf, inc_fgts, variavel, sistema)
                VALUES (@e, @c, @d, @t, @n, @ci, @cr, @cf, @v, TRUE) ON CONFLICT (empresa_id, codigo) DO NOTHING
                """, con, tx).Com("e", empresaId).Com("c", r.Codigo).Com("d", r.Descricao).Com("t", r.Tipo.ToString()).Com("n", r.NaturezaEsocial)
                .Com("ci", r.IncInss).Com("cr", r.IncIrrf).Com("cf", r.IncFgts).Com("v", r.Variavel);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------------ parâmetros

    public async Task<ParametroEmpresa> ObterParametrosAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT rat_aliquota, fap, terceiros_aliquota, fpas, cod_terceiros, simples_anexo, class_trib, responsavel_nome, responsavel_cpf,
                   responsavel_email, responsavel_telefone
              FROM folha.parametro_empresa WHERE empresa_id = @e
            """).Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return ParametroEmpresa.Padrao;
        return new ParametroEmpresa(r.GetDecimal(0), r.GetDecimal(1), r.GetDecimal(2), r.GetString(3), r.GetString(4), r.Texto(5), r.GetString(6), r.Texto(7),
                                    r.Texto(8), r.Texto(9), r.Texto(10));
    }

    public async Task<ConfiguracaoFolha> ObterConfiguracaoAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("SELECT dia_pagamento, esocial_ambiente, esocial_transmissor FROM folha.configuracao_empresa WHERE empresa_id = @e")
            .Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new ConfiguracaoFolha(r.GetInt16(0), r.GetInt16(1), r.GetString(2)) : ConfiguracaoFolha.Padrao;
    }

    public Task GravarConfiguracaoAsync(long empresaId, ConfiguracaoFolha c, long usuarioId, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO folha.configuracao_empresa (empresa_id, dia_pagamento, esocial_ambiente, esocial_transmissor)
            VALUES (@e, @d, @a, @t)
            ON CONFLICT (empresa_id) DO UPDATE SET dia_pagamento = @d, esocial_ambiente = @a, esocial_transmissor = @t, atualizado_em = now()
            """, con, tx).Com("e", empresaId).Com("d", (short)c.DiaPagamento).Com("a", (short)c.AmbienteEsocial).Com("t", c.TransmissorEsocial);
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return 0;
    });

    public Task GravarParametrosAsync(long empresaId, ParametroEmpresa p, long usuarioId, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var con = await ds.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        await Sql.DefinirUsuarioAsync(con, tx, usuarioId, ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO folha.parametro_empresa (empresa_id, rat_aliquota, fap, terceiros_aliquota, fpas, cod_terceiros, simples_anexo, class_trib,
                   responsavel_nome, responsavel_cpf, responsavel_email, responsavel_telefone)
            VALUES (@e, @rat, @fap, @terc, @fpas, @cterc, @anexo, @ct, @rn, @rc, @re, @rt)
            ON CONFLICT (empresa_id) DO UPDATE SET rat_aliquota = @rat, fap = @fap, terceiros_aliquota = @terc, fpas = @fpas, cod_terceiros = @cterc,
                   simples_anexo = @anexo, class_trib = @ct, responsavel_nome = @rn, responsavel_cpf = @rc, responsavel_email = @re, responsavel_telefone = @rt
            """, con, tx).Com("e", empresaId).Com("rat", p.RatAliquota).Com("fap", p.Fap).Com("terc", p.TerceirosAliquota).Com("fpas", p.Fpas)
            .Com("cterc", p.CodTerceiros).ComTexto("anexo", p.SimplesAnexo).Com("ct", p.ClassTrib).ComTexto("rn", p.ResponsavelNome).ComTexto("rc", p.ResponsavelCpf)
            .ComTexto("re", p.ResponsavelEmail).ComTexto("rt", p.ResponsavelTelefone);
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return 0;
    });

    public async Task<string?> ObterRegimeTributarioAsync(long empresaId, DateOnly data, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT regime::text FROM core.empresa_regime
             WHERE empresa_id = @e AND vigencia_inicio <= @d AND (vigencia_fim IS NULL OR vigencia_fim >= @d)
             ORDER BY vigencia_inicio DESC LIMIT 1
            """).Com("e", empresaId).ComData("d", data);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public async Task<IReadOnlyList<ParametroLegal>> ListarParametrosLegaisAsync(CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT chave, vigencia_inicio, vigencia_fim, valor::text, fundamento_legal FROM core.parametro_legal WHERE chave LIKE 'FOLHA\_%' ORDER BY chave, vigencia_inicio
            """);
        var res = new List<ParametroLegal>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) res.Add(new ParametroLegal(r.GetString(0), r.GetFieldValue<DateOnly>(1), r.Data(2), r.GetString(3), r.GetString(4)));
        return res;
    }

    // ------------------------------------------------------------------ afastamentos, alterações, empresa

    public async Task<IReadOnlyList<Afastamento>> ListarAfastamentosAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT a.id, a.funcionario_id, f.nome, a.data_inicio, a.data_fim, a.codigo_motivo, a.observacao
              FROM folha.afastamento a JOIN folha.funcionario f ON f.id = a.funcionario_id WHERE a.empresa_id = @e ORDER BY a.data_inicio DESC
            """).Com("e", empresaId);
        var res = new List<Afastamento>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new Afastamento(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetFieldValue<DateOnly>(3), r.Data(4), r.GetString(5), r.Texto(6)));
        return res;
    }

    public Task<long> GravarAfastamentoAsync(long empresaId, Afastamento a, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = a.Id == 0
            ? ds.CreateCommand("INSERT INTO folha.afastamento (empresa_id, funcionario_id, data_inicio, data_fim, codigo_motivo, observacao) VALUES (@e, @f, @i, @fim, @c, @o) RETURNING id")
            : ds.CreateCommand("UPDATE folha.afastamento SET funcionario_id = @f, data_inicio = @i, data_fim = @fim, codigo_motivo = @c, observacao = @o WHERE id = @id AND empresa_id = @e RETURNING id");
        cmd.Com("e", empresaId).Com("f", a.FuncionarioId).ComData("i", a.Inicio).ComData("fim", a.Fim).Com("c", a.CodigoMotivo).ComTexto("o", a.Observacao);
        if (a.Id != 0) cmd.Com("id", a.Id);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    });

    public Task ExcluirAfastamentoAsync(long empresaId, long id, CancellationToken ct) => Sql.TraduzirAsync(async () =>
    {
        await using var cmd = ds.CreateCommand("DELETE FROM folha.afastamento WHERE id = @id AND empresa_id = @e").Com("id", id).Com("e", empresaId);
        return await cmd.ExecuteNonQueryAsync(ct);
    });

    public async Task<IReadOnlyList<AlteracaoContratual>> ListarAlteracoesAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT id, funcionario_id, data_alteracao, descricao, salario_anterior, salario_novo, cargo_anterior, cargo_novo
              FROM folha.alteracao_contratual WHERE empresa_id = @e ORDER BY data_alteracao, id
            """).Com("e", empresaId);
        var res = new List<AlteracaoContratual>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            res.Add(new AlteracaoContratual(r.GetInt64(0), r.GetInt64(1), r.GetFieldValue<DateOnly>(2), r.GetString(3), r.Valor(4), r.Valor(5), r.Texto(6), r.Texto(7)));
        return res;
    }

    public async Task<EmpresaFolha?> ObterEmpresaAsync(long empresaId, CancellationToken ct)
    {
        await using var cmd = ds.CreateCommand("""
            SELECT e.id, e.cnpj, e.razao_social, e.cnae_principal, e.uf, e.cod_municipio_ibge, p.responsavel_nome, p.responsavel_cpf, p.responsavel_email,
                   p.responsavel_telefone
              FROM core.empresa e LEFT JOIN folha.parametro_empresa p ON p.empresa_id = e.id WHERE e.id = @e
            """).Com("e", empresaId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new EmpresaFolha(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.Texto(6), r.Texto(7), r.Texto(8), r.Texto(9))
            : null;
    }
}
