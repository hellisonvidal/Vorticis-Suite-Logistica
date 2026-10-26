using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Infra;
using Vorticis.Infra.Postgres;
using P = Vorticis.Domain.Seguranca.Permissao;

namespace Vorticis.Frota;

/// <summary>
/// Módulo Frota (a empresa ativa é a transportadora). Cadastros com FROTA_CADASTROS; abastecimentos, ordens de serviço e
/// viagens com FROTA_OPERACAO; painel e relatórios com FROTA_RELATORIOS (qualquer uma das três dá acesso à consulta).
/// </summary>
public sealed partial class ServicoFrota(VorticisApp app)
{
    private readonly BancoModulo _db = new(app.DataSource);
    private readonly Autorizador _aut = app.Autorizador;

    private Task<string?> Negar(long u, long e, P p, CancellationToken ct) => _aut.VerificarAsync(u, e, p, ct);

    private async Task<bool> PodeVerAsync(long u, long e, CancellationToken ct)
    {
        var p = await _aut.ObterAsync(u, e, ct);
        return p.Administrador || new[] { P.FrotaCadastros, P.FrotaOperacao, P.FrotaRelatorios, P.FrotaManutencao, P.FrotaAprovar }.Any(p.Pode);
    }

    private static async Task<Resultado<T>> Tentar<T>(Func<Task<T>> acao)
    {
        try { return Resultado<T>.Ok(await acao()); }
        catch (ViolacaoRegraException ex) { return Resultado<T>.Falha(Traduzir(ex.Message)); }
    }

    private static async Task<Resultado> Tentar(Func<Task> acao)
    {
        try { await acao(); return Resultado.Ok(); }
        catch (ViolacaoRegraException ex) { return Resultado.Falha(Traduzir(ex.Message)); }
    }

    private static string Traduzir(string msg) => msg.StartsWith("Registro repetido", StringComparison.Ordinal)
        ? "Já existe um cadastro com este código, placa ou CPF."
        : msg.StartsWith("O registro está em uso", StringComparison.Ordinal)
            ? "O cadastro tem abastecimentos, manutenções ou viagens e não pode ser excluído: desmarque Ativo."
            : msg;

    // ------------------------------------------------------------------ configuração

    public async Task<ConfiguracaoFrota> ConfiguracaoAsync(long empresa, CancellationToken ct = default)
        => (await _db.ListarAsync("SELECT dias_aviso, tolerancia_consumo, limite_autorizacao, fator_rodoviario, velocidade_media, conta_diaria_despesa, conta_diaria_credito FROM frota.configuracao WHERE empresa_id = @e",
                                  r => new ConfiguracaoFrota(r.GetInt16(0), r.GetDecimal(1), r.GetDecimal(2), r.GetDecimal(3), r.GetDecimal(4), r.Texto(5), r.Texto(6)), ct, ("e", empresa))).FirstOrDefault() ?? new ConfiguracaoFrota();

    public async Task<Resultado> GravarConfiguracaoAsync(long empresa, ConfiguracaoFrota c, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaCadastros, ct) is { } n) return Resultado.Falha(n);
        if (c.DiasAviso is < 1 or > 365) return Resultado.Falha("Antecedência dos avisos: de 1 a 365 dias.");
        if (c.ToleranciaConsumo is < 0 or > 100) return Resultado.Falha("Tolerância do consumo: de 0 a 100%.");
        if (c.LimiteAutorizacao < 0) return Resultado.Falha("O limite de autorização automática não pode ser negativo.");
        if (c.FatorRodoviario is < 1 or > 3) return Resultado.Falha("Fator rodoviário: de 1,00 a 3,00.");
        if (c.VelocidadeMedia is < 10 or > 130) return Resultado.Falha("Velocidade média: de 10 a 130 km/h.");
        c = c with { ContaDiariaDespesa = N(c.ContaDiariaDespesa), ContaDiariaCredito = N(c.ContaDiariaCredito) };
        if ((c.ContaDiariaDespesa is null) != (c.ContaDiariaCredito is null)) return Resultado.Falha("Contabilização das diárias: informe as duas contas (despesa e crédito) ou nenhuma.");
        if (c.ContaDiariaDespesa is not null)
        {
            if (c.ContaDiariaDespesa == c.ContaDiariaCredito) return Resultado.Falha("As contas de despesa e de crédito das diárias devem ser diferentes.");
            foreach (var conta in new[] { c.ContaDiariaDespesa, c.ContaDiariaCredito! })
                if (await _db.EscalarAsync<int>("SELECT count(*)::int FROM contabil.plano_contas WHERE empresa_id = @e AND codigo = @c AND tipo = 'A' AND ativa", ct, ("e", empresa), ("c", conta)) == 0)
                    return Resultado.Falha($"A conta {conta} não existe no plano de contas, não é analítica ou está inativa.");
        }
        return await Tentar(() => _db.ExecutarAsync("""
            INSERT INTO frota.configuracao (empresa_id, dias_aviso, tolerancia_consumo, limite_autorizacao, fator_rodoviario, velocidade_media, conta_diaria_despesa, conta_diaria_credito)
            VALUES (@e, @d, @t, @l, @f, @vm, @cd, @cc)
            ON CONFLICT (empresa_id) DO UPDATE SET dias_aviso = EXCLUDED.dias_aviso, tolerancia_consumo = EXCLUDED.tolerancia_consumo,
                                                   limite_autorizacao = EXCLUDED.limite_autorizacao, fator_rodoviario = EXCLUDED.fator_rodoviario,
                                                   velocidade_media = EXCLUDED.velocidade_media, conta_diaria_despesa = EXCLUDED.conta_diaria_despesa,
                                                   conta_diaria_credito = EXCLUDED.conta_diaria_credito
            """, ct, ("e", empresa), ("d", (short)c.DiasAviso), ("t", c.ToleranciaConsumo), ("l", c.LimiteAutorizacao), ("f", c.FatorRodoviario), ("vm", c.VelocidadeMedia),
            ("cd", c.ContaDiariaDespesa), ("cc", c.ContaDiariaCredito)));
    }

    // ------------------------------------------------------------------ veículos

    public async Task<List<Veiculo>> VeiculosAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarVeiculosAsync(empresa, ct) : [];

    private Task<List<Veiculo>> ListarVeiculosAsync(long empresa, CancellationToken ct) => _db.ListarAsync("""
        SELECT id, codigo, placa, renavam, tipo, marca, modelo, ano_fabricacao, ano_modelo, combustivel, capacidade_kg, km_atual, media_esperada, ativo, observacao, frota_id
          FROM frota.veiculo WHERE empresa_id = @e ORDER BY codigo
        """, r => new Veiculo(r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.Texto(3), r.GetString(4), r.Texto(5), r.Texto(6),
                              r.IsDBNull(7) ? null : r.GetInt16(7), r.IsDBNull(8) ? null : r.GetInt16(8), r.GetString(9), r.Valor(10), r.GetDecimal(11), r.Valor(12),
                              r.GetBoolean(13), r.Texto(14), r.Id(15)), ct, ("e", empresa));

    public async Task<Resultado<long>> GravarVeiculoAsync(long empresa, Veiculo v, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaCadastros, ct) is { } n) return Resultado<long>.Falha(n);
        static string? N(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        v = v with { Placa = RegrasFrota.NormalizarPlaca(v.Placa), Renavam = N(new string([.. (v.Renavam ?? "").Where(char.IsAsciiDigit)])), Marca = N(v.Marca),
                     Modelo = N(v.Modelo), Observacao = N(v.Observacao) };
        if (RegrasFrota.Validar(v) is { Count: > 0 } erros) return Resultado<long>.Falha(erros);
        (string, object?)[] p =
        [
            ("e", empresa), ("id", v.Id), ("cod", v.Codigo), ("placa", v.Placa), ("ren", v.Renavam), ("tipo", v.Tipo), ("marca", v.Marca), ("modelo", v.Modelo),
            ("af", (short?)v.AnoFabricacao), ("am", (short?)v.AnoModelo), ("comb", v.Combustivel), ("cap", v.CapacidadeKg), ("km", v.KmAtual),
            ("media", v.MediaEsperada), ("ativo", v.Ativo), ("obs", v.Observacao), ("fro", v.FrotaId),
        ];
        return await Tentar(async () => v.Id == 0
            ? await _db.EscalarAsync<long>("""
                INSERT INTO frota.veiculo (empresa_id, codigo, placa, renavam, tipo, marca, modelo, ano_fabricacao, ano_modelo, combustivel, capacidade_kg, km_atual,
                                           media_esperada, ativo, observacao, frota_id)
                VALUES (@e, @cod, @placa, @ren, @tipo, @marca, @modelo, @af, @am, @comb, @cap, @km, @media, @ativo, @obs, @fro) RETURNING id
                """, ct, p)
            : await _db.EscalarAsync<long?>("""
                UPDATE frota.veiculo SET placa = @placa, renavam = @ren, tipo = @tipo, marca = @marca, modelo = @modelo, ano_fabricacao = @af, ano_modelo = @am,
                       combustivel = @comb, capacidade_kg = @cap, km_atual = @km, media_esperada = @media, ativo = @ativo, observacao = @obs, frota_id = @fro
                 WHERE id = @id AND empresa_id = @e RETURNING id
                """, ct, p) ?? throw new ViolacaoRegraException("Veículo não encontrado."));
    }

    public async Task<Resultado> ExcluirVeiculoAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaCadastros, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(() => _db.ExecutarAsync("""
            DELETE FROM frota.plano_manutencao WHERE veiculo_id = @id AND empresa_id = @e
               AND NOT EXISTS (SELECT 1 FROM frota.ordem_servico WHERE plano_id = plano_manutencao.id);
            DELETE FROM frota.veiculo WHERE id = @id AND empresa_id = @e
            """, ct, ("id", id), ("e", empresa)));
    }

    // ------------------------------------------------------------------ motoristas

    public async Task<List<Motorista>> MotoristasAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarMotoristasAsync(empresa, ct) : [];

    private Task<List<Motorista>> ListarMotoristasAsync(long empresa, CancellationToken ct) => _db.ListarAsync("""
        SELECT id, codigo, nome, cpf, cnh, cnh_categoria, cnh_validade, telefone, ativo FROM frota.motorista WHERE empresa_id = @e ORDER BY nome
        """, r => new Motorista(r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.Texto(4), r.Texto(5), r.Data(6), r.Texto(7), r.GetBoolean(8)),
        ct, ("e", empresa));

    public async Task<Resultado<long>> GravarMotoristaAsync(long empresa, Motorista m, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaCadastros, ct) is { } n) return Resultado<long>.Falha(n);
        static string? D(string? s) => new string([.. (s ?? "").Where(char.IsAsciiDigit)]) is { Length: > 0 } d ? d : null;
        m = m with { Nome = m.Nome?.Trim() ?? "", Cpf = D(m.Cpf) ?? "", Cnh = D(m.Cnh), CnhCategoria = string.IsNullOrWhiteSpace(m.CnhCategoria) ? null : m.CnhCategoria.Trim().ToUpperInvariant(),
                     Telefone = string.IsNullOrWhiteSpace(m.Telefone) ? null : m.Telefone.Trim() };
        if (RegrasFrota.Validar(m) is { Count: > 0 } erros) return Resultado<long>.Falha(erros);
        (string, object?)[] p =
        [
            ("e", empresa), ("id", m.Id), ("cod", m.Codigo), ("nome", m.Nome), ("cpf", m.Cpf), ("cnh", m.Cnh), ("cat", m.CnhCategoria), ("val", m.CnhValidade),
            ("tel", m.Telefone), ("ativo", m.Ativo),
        ];
        return await Tentar(async () => m.Id == 0
            ? await _db.EscalarAsync<long>("""
                INSERT INTO frota.motorista (empresa_id, codigo, nome, cpf, cnh, cnh_categoria, cnh_validade, telefone, ativo)
                VALUES (@e, @cod, @nome, @cpf, @cnh, @cat, @val, @tel, @ativo) RETURNING id
                """, ct, p)
            : await _db.EscalarAsync<long?>("""
                UPDATE frota.motorista SET nome = @nome, cpf = @cpf, cnh = @cnh, cnh_categoria = @cat, cnh_validade = @val, telefone = @tel, ativo = @ativo
                 WHERE id = @id AND empresa_id = @e RETURNING id
                """, ct, p) ?? throw new ViolacaoRegraException("Motorista não encontrado."));
    }

    public async Task<Resultado> ExcluirMotoristaAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaCadastros, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(() => _db.ExecutarAsync("DELETE FROM frota.motorista WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
    }

    // ------------------------------------------------------------------ abastecimentos

    public async Task<List<Abastecimento>> AbastecimentosAsync(long empresa, DateOnly inicio, DateOnly fim, long? veiculo, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarAbastecimentosAsync(empresa, inicio, fim, veiculo, ct) : [];

    private Task<List<Abastecimento>> ListarAbastecimentosAsync(long empresa, DateOnly inicio, DateOnly fim, long? veiculo, CancellationToken ct) => _db.ListarAsync("""
        SELECT a.id, a.veiculo_id, a.motorista_id, a.data, a.km, a.litros, a.valor_total, a.combustivel, a.posto, a.tanque_cheio, a.observacao, v.placa, m.nome
          FROM frota.abastecimento a JOIN frota.veiculo v ON v.id = a.veiculo_id LEFT JOIN frota.motorista m ON m.id = a.motorista_id
         WHERE a.empresa_id = @e AND a.data BETWEEN @i AND @f AND (@v::bigint IS NULL OR a.veiculo_id = @v)
         ORDER BY a.data, v.codigo, a.km
        """, r => new Abastecimento(r.GetInt64(0), r.GetInt64(1), r.Id(2), r.GetFieldValue<DateOnly>(3), r.GetDecimal(4), r.GetDecimal(5), r.GetDecimal(6),
                                    r.GetString(7), r.Texto(8), r.GetBoolean(9), r.Texto(10), r.GetString(11), r.Texto(12)),
        ct, ("e", empresa), ("i", inicio), ("f", fim), ("v", veiculo));

    public async Task<Resultado<long>> GravarAbastecimentoAsync(long empresa, Abastecimento a, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado<long>.Falha(n);
        var veiculo = (await ListarVeiculosAsync(empresa, ct)).FirstOrDefault(v => v.Id == a.VeiculoId);
        if (veiculo is null) return Resultado<long>.Falha("Escolha o veículo.");
        var anterior = await _db.EscalarAsync<decimal?>("""
            SELECT max(km) FROM frota.abastecimento WHERE veiculo_id = @v AND id <> @id AND combustivel <> 'ARLA32' AND (data < @d OR (data = @d AND id < @id))
            """, ct, ("v", a.VeiculoId), ("id", a.Id), ("d", a.Data));
        if (RegrasFrota.Validar(a, veiculo, a.Combustivel == "ARLA32" ? null : anterior) is { Count: > 0 } erros) return Resultado<long>.Falha(erros);
        (string, object?)[] p =
        [
            ("e", empresa), ("id", a.Id), ("v", a.VeiculoId), ("m", a.MotoristaId), ("d", a.Data), ("km", a.Km), ("l", a.Litros), ("valor", a.ValorTotal),
            ("comb", a.Combustivel), ("posto", string.IsNullOrWhiteSpace(a.Posto) ? null : a.Posto.Trim()), ("cheio", a.TanqueCheio),
            ("obs", string.IsNullOrWhiteSpace(a.Observacao) ? null : a.Observacao.Trim()),
        ];
        return await Tentar(async () =>
        {
            var id = a.Id == 0
                ? await _db.EscalarAsync<long>("""
                    INSERT INTO frota.abastecimento (empresa_id, veiculo_id, motorista_id, data, km, litros, valor_total, combustivel, posto, tanque_cheio, observacao)
                    VALUES (@e, @v, @m, @d, @km, @l, @valor, @comb, @posto, @cheio, @obs) RETURNING id
                    """, ct, p)
                : await _db.EscalarAsync<long?>("""
                    UPDATE frota.abastecimento SET veiculo_id = @v, motorista_id = @m, data = @d, km = @km, litros = @l, valor_total = @valor, combustivel = @comb,
                           posto = @posto, tanque_cheio = @cheio, observacao = @obs WHERE id = @id AND empresa_id = @e RETURNING id
                    """, ct, p) ?? throw new ViolacaoRegraException("Abastecimento não encontrado.");
            await AtualizarKmAsync(a.VeiculoId, a.Km, ct);
            return id;
        });
    }

    public async Task<Resultado> ExcluirAbastecimentoAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(() => _db.ExecutarAsync("DELETE FROM frota.abastecimento WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
    }

    /// <summary>O hodômetro do veículo acompanha o maior km informado (abastecimento, manutenção, viagem).</summary>
    private Task AtualizarKmAsync(long veiculo, decimal km, CancellationToken ct)
        => _db.ExecutarAsync("UPDATE frota.veiculo SET km_atual = GREATEST(km_atual, @km) WHERE id = @v", ct, ("v", veiculo), ("km", km));

    // ------------------------------------------------------------------ manutenção

    public async Task<List<PlanoManutencao>> PlanosAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarPlanosAsync(empresa, ct) : [];

    private Task<List<PlanoManutencao>> ListarPlanosAsync(long empresa, CancellationToken ct) => _db.ListarAsync("""
        SELECT p.id, p.veiculo_id, p.descricao, p.intervalo_km, p.intervalo_dias, p.ultima_km, p.ultima_data, p.ativo, v.placa, v.km_atual
          FROM frota.plano_manutencao p JOIN frota.veiculo v ON v.id = p.veiculo_id WHERE p.empresa_id = @e ORDER BY v.codigo, p.descricao
        """, r => new PlanoManutencao(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.Valor(3), r.IsDBNull(4) ? null : r.GetInt32(4), r.Valor(5), r.Data(6),
                                      r.GetBoolean(7), r.GetString(8), r.GetDecimal(9)), ct, ("e", empresa));

    public async Task<Resultado<long>> GravarPlanoAsync(long empresa, PlanoManutencao p, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaManutencao, ct) is { } n) return Resultado<long>.Falha(n);
        if (string.IsNullOrWhiteSpace(p.Descricao)) return Resultado<long>.Falha("Informe o serviço (ex.: troca de óleo).");
        if (p.IntervaloKm is null && p.IntervaloDias is null) return Resultado<long>.Falha("Informe o intervalo em km, em dias ou os dois.");
        if (p.IntervaloKm is <= 0 || p.IntervaloDias is <= 0) return Resultado<long>.Falha("Os intervalos devem ser maiores que zero.");
        if (p.IntervaloDias is not null && p.UltimaData is null) return Resultado<long>.Falha("Com intervalo em dias, informe a data da última vez.");
        (string, object?)[] q =
        [
            ("e", empresa), ("id", p.Id), ("v", p.VeiculoId), ("d", p.Descricao.Trim()), ("ik", p.IntervaloKm), ("id2", p.IntervaloDias), ("uk", p.UltimaKm),
            ("ud", p.UltimaData), ("a", p.Ativo),
        ];
        return await Tentar(async () => p.Id == 0
            ? await _db.EscalarAsync<long>("""
                INSERT INTO frota.plano_manutencao (empresa_id, veiculo_id, descricao, intervalo_km, intervalo_dias, ultima_km, ultima_data, ativo)
                VALUES (@e, @v, @d, @ik, @id2, @uk, @ud, @a) RETURNING id
                """, ct, q)
            : await _db.EscalarAsync<long?>("""
                UPDATE frota.plano_manutencao SET veiculo_id = @v, descricao = @d, intervalo_km = @ik, intervalo_dias = @id2, ultima_km = @uk, ultima_data = @ud, ativo = @a
                 WHERE id = @id AND empresa_id = @e RETURNING id
                """, ct, q) ?? throw new ViolacaoRegraException("Plano não encontrado."));
    }

    public async Task<Resultado> ExcluirPlanoAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaManutencao, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(() => _db.ExecutarAsync("DELETE FROM frota.plano_manutencao WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
    }

    // ------------------------------------------------------------------ viagens

    public async Task<List<Viagem>> ViagensAsync(long empresa, DateOnly inicio, DateOnly fim, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarViagensAsync(empresa, inicio, fim, ct) : [];

    private Task<List<Viagem>> ListarViagensAsync(long empresa, DateOnly inicio, DateOnly fim, CancellationToken ct) => _db.ListarAsync("""
        SELECT x.id, x.numero, x.veiculo_id, x.motorista_id, x.cliente, x.origem, x.destino, x.saida, x.chegada, x.km_saida, x.km_chegada, x.valor_frete,
               x.pedagio, x.diarias, x.outras_despesas, x.situacao, v.placa, m.nome
          FROM frota.viagem x JOIN frota.veiculo v ON v.id = x.veiculo_id LEFT JOIN frota.motorista m ON m.id = x.motorista_id
         WHERE x.empresa_id = @e AND (x.saida BETWEEN @i AND @f OR x.situacao = 'EM_ANDAMENTO') ORDER BY x.saida DESC, x.numero DESC
        """, r => new Viagem(r.GetInt64(0), r.GetInt32(1), r.GetInt64(2), r.Id(3), r.Texto(4), r.GetString(5), r.GetString(6), r.GetFieldValue<DateOnly>(7), r.Data(8),
                             r.GetDecimal(9), r.Valor(10), r.GetDecimal(11), r.GetDecimal(12), r.GetDecimal(13), r.GetDecimal(14), r.GetString(15), r.GetString(16), r.Texto(17)),
        ct, ("e", empresa), ("i", inicio), ("f", fim));

    public async Task<Resultado<int>> GravarViagemAsync(long empresa, Viagem v, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado<int>.Falha(n);
        if (RegrasFrota.Validar(v) is { Count: > 0 } erros) return Resultado<int>.Falha(erros);
        return await Tentar(async () =>
        {
            var numero = v.Id == 0 ? await _db.EscalarAsync<int>("SELECT COALESCE(max(numero), 0) + 1 FROM frota.viagem WHERE empresa_id = @e", ct, ("e", empresa)) : v.Numero;
            (string, object?)[] p =
            [
                ("e", empresa), ("id", v.Id), ("n", numero), ("v", v.VeiculoId), ("m", v.MotoristaId), ("cli", string.IsNullOrWhiteSpace(v.Cliente) ? null : v.Cliente.Trim()),
                ("o", v.Origem.Trim()), ("d", v.Destino.Trim()), ("s", v.Saida), ("c", v.Chegada), ("ks", v.KmSaida), ("kc", v.KmChegada), ("f", v.ValorFrete),
                ("pd", v.Pedagio), ("di", v.Diarias), ("ou", v.OutrasDespesas), ("sit", v.Situacao),
            ];
            if (v.Id == 0)
                await _db.ExecutarAsync("""
                    INSERT INTO frota.viagem (empresa_id, numero, veiculo_id, motorista_id, cliente, origem, destino, saida, chegada, km_saida, km_chegada, valor_frete,
                                              pedagio, diarias, outras_despesas, situacao)
                    VALUES (@e, @n, @v, @m, @cli, @o, @d, @s, @c, @ks, @kc, @f, @pd, @di, @ou, @sit)
                    """, ct, p);
            else if (await _db.ExecutarAsync("""
                    UPDATE frota.viagem SET veiculo_id = @v, motorista_id = @m, cliente = @cli, origem = @o, destino = @d, saida = @s, chegada = @c, km_saida = @ks,
                           km_chegada = @kc, valor_frete = @f, pedagio = @pd, diarias = @di, outras_despesas = @ou, situacao = @sit WHERE id = @id AND empresa_id = @e
                    """, ct, p) == 0) throw new ViolacaoRegraException("Viagem não encontrada.");
            await AtualizarKmAsync(v.VeiculoId, v.KmChegada ?? v.KmSaida, ct);
            return numero;
        });
    }

    public async Task<Resultado> ExcluirViagemAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaOperacao, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(() => _db.ExecutarAsync("DELETE FROM frota.viagem WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
    }

    // ------------------------------------------------------------------ documentos e vencimentos

    public async Task<List<DocumentoFrota>> DocumentosAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct) ? await ListarDocumentosAsync(empresa, ct) : [];

    private Task<List<DocumentoFrota>> ListarDocumentosAsync(long empresa, CancellationToken ct) => _db.ListarAsync("""
        SELECT d.id, d.veiculo_id, d.motorista_id, d.tipo, d.numero, d.vencimento, d.valor, d.observacao, COALESCE(v.placa, m.nome, 'Empresa')
          FROM frota.documento d LEFT JOIN frota.veiculo v ON v.id = d.veiculo_id LEFT JOIN frota.motorista m ON m.id = d.motorista_id
         WHERE d.empresa_id = @e ORDER BY d.vencimento
        """, r => new DocumentoFrota(r.GetInt64(0), r.Id(1), r.Id(2), r.GetString(3), r.Texto(4), r.GetFieldValue<DateOnly>(5), r.Valor(6), r.Texto(7),
                                     r.GetString(8) is { Length: 7 } placa && r.Id(1) is not null ? RegrasFrota.FormatarPlaca(placa) : r.GetString(8)), ct, ("e", empresa));

    public async Task<Resultado<long>> GravarDocumentoAsync(long empresa, DocumentoFrota d, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaCadastros, ct) is { } n) return Resultado<long>.Falha(n);
        if (!RegrasFrota.TiposDocumento.Contains(d.Tipo)) return Resultado<long>.Falha("Escolha o tipo do documento.");
        if (d.VeiculoId is not null && d.MotoristaId is not null) return Resultado<long>.Falha("O documento é de um veículo OU de um motorista (ou da empresa).");
        (string, object?)[] p =
        [
            ("e", empresa), ("id", d.Id), ("v", d.VeiculoId), ("m", d.MotoristaId), ("t", d.Tipo), ("n", string.IsNullOrWhiteSpace(d.Numero) ? null : d.Numero.Trim()),
            ("venc", d.Vencimento), ("valor", d.Valor), ("obs", string.IsNullOrWhiteSpace(d.Observacao) ? null : d.Observacao.Trim()),
        ];
        return await Tentar(async () => d.Id == 0
            ? await _db.EscalarAsync<long>("""
                INSERT INTO frota.documento (empresa_id, veiculo_id, motorista_id, tipo, numero, vencimento, valor, observacao)
                VALUES (@e, @v, @m, @t, @n, @venc, @valor, @obs) RETURNING id
                """, ct, p)
            : await _db.EscalarAsync<long?>("""
                UPDATE frota.documento SET veiculo_id = @v, motorista_id = @m, tipo = @t, numero = @n, vencimento = @venc, valor = @valor, observacao = @obs
                 WHERE id = @id AND empresa_id = @e RETURNING id
                """, ct, p) ?? throw new ViolacaoRegraException("Documento não encontrado."));
    }

    public async Task<Resultado> ExcluirDocumentoAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await Negar(usuario, empresa, P.FrotaCadastros, ct) is { } n) return Resultado.Falha(n);
        return await Tentar(() => _db.ExecutarAsync("DELETE FROM frota.documento WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
    }

    /// <summary>
    /// Painel da frota: documentos e CNHs vencidos ou a vencer (antecedência da configuração), preventivas vencidas ou a vencer e
    /// a última média de consumo abaixo do esperado (tolerância da configuração). Vencidos primeiro.
    /// </summary>
    public async Task<List<AvisoFrota>> AvisosAsync(long empresa, DateOnly hoje, long usuario, CancellationToken ct = default)
    {
        if (!await PodeVerAsync(usuario, empresa, ct)) return [];
        return await AvisosInternosAsync(empresa, hoje, await ConfiguracaoAsync(empresa, ct), ct);
    }

    private async Task<List<AvisoFrota>> AvisosInternosAsync(long empresa, DateOnly hoje, ConfiguracaoFrota cfg, CancellationToken ct)
    {
        var avisos = new List<AvisoFrota>();
        foreach (var d in await ListarDocumentosAsync(empresa, ct))
            if (RegrasFrota.SituacaoVencimento(d.Vencimento, hoje, cfg.DiasAviso) is { } g)
                avisos.Add(new AvisoFrota(g, $"{RegrasFrota.Rotulo(d.Tipo)} – {d.Dono}", RegrasFrota.TextoVencimento(d.Vencimento, hoje), d.Vencimento));
        foreach (var m in (await ListarMotoristasAsync(empresa, ct)).Where(m => m.Ativo && m.CnhValidade is not null))
            if (RegrasFrota.SituacaoVencimento(m.CnhValidade!.Value, hoje, cfg.DiasAviso) is { } g)
                avisos.Add(new AvisoFrota(g, $"CNH – {m.Nome}", RegrasFrota.TextoVencimento(m.CnhValidade.Value, hoje), m.CnhValidade));
        var veiculos = (await ListarVeiculosAsync(empresa, ct)).Where(v => v.Ativo).ToDictionary(v => v.Id);
        foreach (var p in (await ListarPlanosAsync(empresa, ct)).Where(p => p.Ativo && veiculos.ContainsKey(p.VeiculoId)))
        {
            var s = RegrasFrota.Situacao(p, veiculos[p.VeiculoId].KmAtual, hoje, cfg.DiasAviso);
            if (s.Gravidade is not { } g) continue;
            var partes = new List<string>();
            if (s.ProximaKm is { } k) partes.Add(string.Create(Formato.PtBr, $"aos {k:N0} km (veículo com {veiculos[p.VeiculoId].KmAtual:N0} km)"));
            if (s.ProximaData is { } d) partes.Add($"em {d:dd/MM/yyyy}");
            avisos.Add(new AvisoFrota(g, $"Manutenção – {RegrasFrota.FormatarPlaca(p.Placa ?? "")}", $"{p.Descricao}: {string.Join(" ou ", partes)}", s.ProximaData));
        }
        var abastecimentos = await ListarAbastecimentosAsync(empresa, hoje.AddMonths(-6), hoje, null, ct);
        foreach (var ultima in RegrasFrota.Medias(abastecimentos).GroupBy(m => m.VeiculoId).Select(g => g.MaxBy(m => m.Data)!))
            if (veiculos.TryGetValue(ultima.VeiculoId, out var v) && RegrasFrota.ConsumoForaDoEsperado(ultima.Media, v.MediaEsperada, cfg.ToleranciaConsumo))
                avisos.Add(new AvisoFrota(GravidadeAviso.Atencao, $"Consumo – {RegrasFrota.FormatarPlaca(v.Placa)}",
                                          string.Create(Formato.PtBr, $"média de {ultima.Media:N2} km/l, abaixo do esperado ({v.MediaEsperada:N2} km/l)"), ultima.Data));
        foreach (var o in (await ListarOrdensAsync(empresa, hoje, hoje, ct)).Where(o => o.Situacao == "AGUARDANDO"))
            avisos.Add(new AvisoFrota(GravidadeAviso.Atencao, $"Autorização – OS nº {o.Numero} ({RegrasFrota.FormatarPlaca(o.Placa ?? "")})",
                                      $"{o.Descricao}: R$ {o.Total.ToString("N2", Formato.PtBr)} aguardando autorização", o.Data));
        return [.. avisos.OrderBy(a => a.Gravidade).ThenBy(a => a.Data ?? DateOnly.MaxValue)];
    }

    // ------------------------------------------------------------------ relatórios

    public async Task<Resultado<Vorticis.Application.Relatorios.TabelaRelatorio>> RelatorioAsync(long empresa, string titulo, DateOnly inicio, DateOnly fim, long usuario,
                                                                                                CancellationToken ct = default, FiltroRelatorioFrota? filtro = null)
    {
        if (await Negar(usuario, empresa, P.FrotaRelatorios, ct) is { } n) return Resultado<Vorticis.Application.Relatorios.TabelaRelatorio>.Falha(n);
        if (TabelasFrota.UsaFiltros(titulo) || titulo == TabelasFrota.TituloResumo) return await RelatorioNovoAsync(empresa, titulo, inicio, fim, filtro ?? new(), ct);
        var veiculos = await ListarVeiculosAsync(empresa, ct);
        var abastecimentos = await ListarAbastecimentosAsync(empresa, inicio, fim, null, ct);
        var tabela = titulo switch
        {
            TabelasFrota.TituloCustos => TabelasFrota.Custos(veiculos, abastecimentos, (await ListarOrdensAsync(empresa, inicio, fim, ct))
                                                                 .Where(o => o.Data >= inicio && o.Data <= fim && o.Situacao is not ("CANCELADA" or "REPROVADA")).ToList(),
                                                             (await ListarViagensAsync(empresa, inicio, fim, ct)).Where(v => v.Saida >= inicio && v.Saida <= fim).ToList()),
            TabelasFrota.TituloConsumo => TabelasFrota.Consumo(veiculos, abastecimentos, (await ConfiguracaoAsync(empresa, ct)).ToleranciaConsumo),
            TabelasFrota.TituloViagens => TabelasFrota.Viagens((await ListarViagensAsync(empresa, inicio, fim, ct)).Where(v => v.Saida >= inicio && v.Saida <= fim).ToList(),
                                                               await ListarAbastecimentosAsync(empresa, inicio, fim.AddDays(60), null, ct)),
            TabelasFrota.TituloAcompanhamento => TabelasFrota.Acompanhamento(
                (await ListarOrdensAsync(empresa, inicio, fim, ct)).Where(o => o.Data >= inicio && o.Data <= fim).ToList(),
                await ListarAutorizacoesAsync(empresa, inicio, DateOnly.MaxValue, ct), await ListarNotasAsync(empresa, inicio, DateOnly.MaxValue, ct),
                DateOnly.FromDateTime(DateTime.Today)),
            TabelasFrota.TituloAprovacoes => TabelasFrota.Aprovacoes(await ListarAutorizacoesAsync(empresa, inicio, fim, ct)),
            TabelasFrota.TituloNotas => TabelasFrota.Notas(await ListarNotasAsync(empresa, inicio, fim, ct)),
            TabelasFrota.TituloCustosFrota => TabelasFrota.CustosFrota(await ListarFrotasAsync(empresa, ct), veiculos, abastecimentos,
                (await ListarOrdensAsync(empresa, inicio, fim, ct)).Where(o => o.Data >= inicio && o.Data <= fim && o.Situacao is not ("CANCELADA" or "REPROVADA")).ToList(),
                (await ListarViagensAsync(empresa, inicio, fim, ct)).Where(v => v.Saida >= inicio && v.Saida <= fim).ToList(), await ListarDiariasAsync(empresa, inicio, fim, null, null, ct)),
            TabelasFrota.TituloDiariasColaborador => TabelasFrota.Diarias(await ListarDiariasAsync(empresa, inicio, fim, null, null, ct), false),
            TabelasFrota.TituloDiariasFrota => TabelasFrota.Diarias(await ListarDiariasAsync(empresa, inicio, fim, null, null, ct), true),
            _ => TabelasFrota.Vencimentos(await AvisosAsync(empresa, DateOnly.FromDateTime(DateTime.Today), usuario, ct)),
        };
        return Resultado<Vorticis.Application.Relatorios.TabelaRelatorio>.Ok(tabela);
    }
}

public static class ServicosFrota
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VorticisApp, ServicoFrota> Instancias = new();
    public static ServicoFrota De(VorticisApp app) => Instancias.GetValue(app, a =>
    {
        var s = new ServicoFrota(a);
        a.Pendencias.RegistrarProcessador(s.ProcessadorPendencias);
        return s;
    });
}
