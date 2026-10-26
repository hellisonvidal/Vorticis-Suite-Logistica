using System.Text.RegularExpressions;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra.Postgres;

namespace Vorticis.Infra.Financeiro;

/// <summary>Conta bancária da empresa: banco (código de compensação), agência, conta e a conta contábil que a representa.</summary>
public sealed record ContaBancaria(long Id, int Codigo, string Descricao, string Banco, string Agencia, string Conta, string? ContaContabil, bool Ativa = true)
{
    public string Resumo => $"{Codigo} – {Descricao} (banco {Banco}, ag. {Agencia}, c/c {Conta})";
}

public enum MeioPagamento { Dinheiro, Boleto, Pix, CartaoCredito, CartaoDebito, Cheque, Transferencia, Outros }

/// <summary>Parcela gerada por uma forma de pagamento (número "1/3", vencimento e valor).</summary>
public sealed record ParcelaGerada(string Numero, DateOnly Vencimento, decimal Valor);

/// <summary>
/// Forma de pagamento: meio e prazos em dias de cada parcela, contados da data-base (emissão da nota): "0" = à vista,
/// "30/60/90" = três parcelas. As duplicatas da nota saem daqui.
/// </summary>
public sealed partial record FormaPagamento(long Id, int Codigo, string Descricao, MeioPagamento Meio, string Prazos, long? ContaBancariaId, bool Ativa = true)
{
    [GeneratedRegex(@"^\d{1,3}(/\d{1,3}){0,119}$")]
    private static partial Regex FormatoPrazos();

    public static IReadOnlyList<(MeioPagamento Meio, string Texto)> Meios { get; } =
    [
        (MeioPagamento.Dinheiro, "Dinheiro"), (MeioPagamento.Boleto, "Boleto"), (MeioPagamento.Pix, "PIX"), (MeioPagamento.CartaoCredito, "Cartão de crédito"),
        (MeioPagamento.CartaoDebito, "Cartão de débito"), (MeioPagamento.Cheque, "Cheque"), (MeioPagamento.Transferencia, "Transferência bancária"), (MeioPagamento.Outros, "Outros"),
    ];

    public string MeioTexto => Meios.First(m => m.Meio == Meio).Texto;

    public static string? NormalizarPrazos(string? prazos)
    {
        var p = string.Concat((prazos ?? "").Where(c => !char.IsWhiteSpace(c))).Replace(',', '/').Replace(';', '/');
        return FormatoPrazos().IsMatch(p) ? p : null;
    }

    public IReadOnlyList<int> Dias => (NormalizarPrazos(Prazos) ?? "0").Split('/').Select(int.Parse).ToList();

    public bool AVista => Dias is [0];

    /// <summary>Prazos de N parcelas: a primeira vence em <paramref name="primeiro"/> dias e as demais a cada <paramref name="intervalo"/> dias.</summary>
    public static string MontarPrazos(int parcelas, int primeiro, int intervalo) =>
        string.Join("/", Enumerable.Range(0, Math.Clamp(parcelas, 1, 120)).Select(i => Math.Max(0, primeiro) + i * Math.Max(0, intervalo)));

    /// <summary>Intervalo em dias entre as parcelas (null se só há uma parcela ou os intervalos não são iguais).</summary>
    public int? Intervalo => Dias.Count < 2 ? null : Dias.Zip(Dias.Skip(1), (a, b) => b - a).Distinct().ToList() is [var unico] ? unico : null;

    public string CondicaoTexto => AVista ? "À vista" : $"{Dias.Count}x ({string.Join("/", Dias)} dias)";

    /// <summary>Divide o total em parcelas iguais (a última absorve a diferença do arredondamento), vencendo nos prazos a partir da data-base.</summary>
    public IReadOnlyList<ParcelaGerada> GerarParcelas(decimal total, DateOnly dataBase)
    {
        var dias = Dias;
        var n = dias.Count;
        var parte = Math.Round(total / n, 2, MidpointRounding.AwayFromZero);
        return dias.Select((d, i) => new ParcelaGerada(n == 1 ? "1" : $"{i + 1}/{n}", dataBase.AddDays(d), i == n - 1 ? total - parte * (n - 1) : parte)).ToList();
    }

    public IReadOnlyList<string> Validar()
    {
        var erros = new List<string>();
        if (Codigo <= 0) erros.Add("Informe o código (número maior que zero).");
        if (string.IsNullOrWhiteSpace(Descricao)) erros.Add("Informe a descrição da forma de pagamento.");
        if (NormalizarPrazos(Prazos) is null)
            erros.Add("Prazos inválidos: informe os dias de cada parcela separados por barra (0 = à vista; 30/60/90 = três parcelas; até 120 parcelas).");
        else if (Dias.Zip(Dias.Skip(1)).Any(p => p.Second < p.First)) erros.Add("Os prazos das parcelas devem estar em ordem crescente (ex.: 30/60/90).");
        return erros;
    }
}

/// <summary>
/// Cadastros › Contas bancárias e Formas de pagamento (Contábil, Fiscal e ERP; o Escritório não usa). Ver exige acesso à empresa;
/// manter exige uma das permissões de manutenção do plano de contas, das duplicatas ou dos pedidos do ERP.
/// </summary>
public sealed class ServicoCadastrosFinanceiros(VorticisApp app)
{
    private readonly BancoModulo _db = new(app.DataSource);
    private readonly Autorizador _aut = app.Autorizador;

    private static readonly Permissao[] Manter =
        [Permissao.PlanoContasManter, Permissao.FiscalDuplicatasManter, Permissao.FiscalDocumentosImportar, Permissao.ErpVendas, Permissao.ErpCompras];

    private async Task<string?> NegarAsync(long usuario, long empresa, CancellationToken ct)
    {
        var p = await _aut.ObterAsync(usuario, empresa, ct);
        if (!p.TemAcesso) return "Você não tem acesso a esta empresa.";
        return p.Administrador || Manter.Any(p.Pode) ? null : $"Seu perfil ({p.NomePerfil}) não permite manter os cadastros financeiros.";
    }

    private async Task<bool> PodeVerAsync(long usuario, long empresa, CancellationToken ct) => (await _aut.ObterAsync(usuario, empresa, ct)).TemAcesso;

    private static async Task<Resultado<T>> Tentar<T>(Func<Task<T>> acao)
    {
        try { return Resultado<T>.Ok(await acao()); }
        catch (ViolacaoRegraException ex) { return Resultado<T>.Falha(ex.Message); }
    }

    // ------------------------------------------------------------------ contas bancárias

    public async Task<IReadOnlyList<ContaBancaria>> ContasAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct)
            ? await _db.ListarAsync("""
                SELECT id, codigo, descricao, banco, agencia, conta, conta_contabil, ativa FROM core.conta_bancaria WHERE empresa_id = @e ORDER BY codigo
                """, r => new ContaBancaria(r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                                            r.IsDBNull(6) ? null : r.GetString(6), r.GetBoolean(7)), ct, ("e", empresa))
            : [];

    public static IReadOnlyList<string> Validar(ContaBancaria c)
    {
        var erros = new List<string>();
        if (c.Codigo <= 0) erros.Add("Informe o código (número maior que zero).");
        if (string.IsNullOrWhiteSpace(c.Descricao)) erros.Add("Informe a descrição da conta (ex.: Banco do Brasil – movimento).");
        if (!Regex.IsMatch(c.Banco ?? "", @"^\d{3}$")) erros.Add("Banco: informe o código de compensação com 3 dígitos (ex.: 001, 237, 341).");
        if (!Regex.IsMatch(c.Agencia ?? "", @"^\d{1,6}(-[0-9Xx])?$")) erros.Add("Agência inválida: só números, com dígito opcional (ex.: 1234-5).");
        if (!Regex.IsMatch(c.Conta ?? "", @"^\d{1,15}(-[0-9Xx]{1,2})?$")) erros.Add("Conta inválida: só números, com dígito opcional (ex.: 12345-6).");
        return erros;
    }

    public async Task<Resultado<long>> GravarContaAsync(long empresa, ContaBancaria c, long usuario, CancellationToken ct = default)
    {
        if (await NegarAsync(usuario, empresa, ct) is { } n) return Resultado<long>.Falha(n);
        c = c with { Descricao = c.Descricao?.Trim() ?? "", Banco = c.Banco?.Trim() ?? "", Agencia = c.Agencia?.Trim() ?? "", Conta = c.Conta?.Trim() ?? "",
                     ContaContabil = string.IsNullOrWhiteSpace(c.ContaContabil) ? null : c.ContaContabil.Trim() };
        if (Validar(c) is { Count: > 0 } erros) return Resultado<long>.Falha(erros);
        if (c.ContaContabil is { } cc && await _db.EscalarAsync<long?>("SELECT id FROM contabil.plano_contas WHERE empresa_id = @e AND codigo = @c", ct, ("e", empresa), ("c", cc)) is null)
            return Resultado<long>.Falha($"A conta contábil {cc} não existe no plano de contas da empresa.");
        return await Tentar(async () => c.Id == 0
            ? (await _db.EscalarAsync<long>("""
                INSERT INTO core.conta_bancaria (empresa_id, codigo, descricao, banco, agencia, conta, conta_contabil, ativa)
                VALUES (@e, @cod, @d, @b, @a, @c, @cc, @at) RETURNING id
                """, ct, ("e", empresa), ("cod", c.Codigo), ("d", c.Descricao), ("b", c.Banco), ("a", c.Agencia), ("c", c.Conta), ("cc", c.ContaContabil), ("at", c.Ativa)))
            : await _db.ExecutarAsync("""
                UPDATE core.conta_bancaria SET descricao = @d, banco = @b, agencia = @a, conta = @c, conta_contabil = @cc, ativa = @at
                 WHERE id = @id AND empresa_id = @e
                """, ct, ("id", c.Id), ("e", empresa), ("d", c.Descricao), ("b", c.Banco), ("a", c.Agencia), ("c", c.Conta), ("cc", c.ContaContabil), ("at", c.Ativa)) == 1
                ? c.Id : throw new ViolacaoRegraException("Conta bancária não encontrada."));
    }

    public async Task<Resultado> ExcluirContaAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await NegarAsync(usuario, empresa, ct) is { } n) return Resultado.Falha(n);
        var r = await Tentar(() => _db.ExecutarAsync("DELETE FROM core.conta_bancaria WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
        return r.Sucesso ? Resultado.Ok() : Resultado.Falha(r.Erros);
    }

    // ------------------------------------------------------------------ formas de pagamento

    private static string Sql(MeioPagamento m) => m switch
    {
        MeioPagamento.CartaoCredito => "CARTAO_CREDITO",
        MeioPagamento.CartaoDebito => "CARTAO_DEBITO",
        _ => m.ToString().ToUpperInvariant(),
    };

    private static MeioPagamento Meio(string s) => s switch
    {
        "CARTAO_CREDITO" => MeioPagamento.CartaoCredito,
        "CARTAO_DEBITO" => MeioPagamento.CartaoDebito,
        _ => Enum.Parse<MeioPagamento>(s, ignoreCase: true),
    };

    public async Task<IReadOnlyList<FormaPagamento>> FormasAsync(long empresa, long usuario, CancellationToken ct = default)
        => await PodeVerAsync(usuario, empresa, ct)
            ? await _db.ListarAsync("""
                SELECT id, codigo, descricao, meio, prazos, conta_bancaria_id, ativa FROM core.forma_pagamento WHERE empresa_id = @e ORDER BY codigo
                """, r => new FormaPagamento(r.GetInt64(0), r.GetInt32(1), r.GetString(2), Meio(r.GetString(3)), r.GetString(4),
                                             r.IsDBNull(5) ? null : r.GetInt64(5), r.GetBoolean(6)), ct, ("e", empresa))
            : [];

    public async Task<Resultado<long>> GravarFormaAsync(long empresa, FormaPagamento f, long usuario, CancellationToken ct = default)
    {
        if (await NegarAsync(usuario, empresa, ct) is { } n) return Resultado<long>.Falha(n);
        f = f with { Descricao = f.Descricao?.Trim() ?? "", Prazos = FormaPagamento.NormalizarPrazos(f.Prazos) ?? f.Prazos ?? "" };
        if (f.Validar() is { Count: > 0 } erros) return Resultado<long>.Falha(erros);
        if (f.ContaBancariaId is { } cb && await _db.EscalarAsync<long?>("SELECT id FROM core.conta_bancaria WHERE id = @id AND empresa_id = @e", ct, ("id", cb), ("e", empresa)) is null)
            return Resultado<long>.Falha("A conta bancária escolhida não é desta empresa.");
        return await Tentar(async () => f.Id == 0
            ? (await _db.EscalarAsync<long>("""
                INSERT INTO core.forma_pagamento (empresa_id, codigo, descricao, meio, prazos, conta_bancaria_id, ativa)
                VALUES (@e, @cod, @d, @m, @p, @cb, @at) RETURNING id
                """, ct, ("e", empresa), ("cod", f.Codigo), ("d", f.Descricao), ("m", Sql(f.Meio)), ("p", f.Prazos), ("cb", f.ContaBancariaId), ("at", f.Ativa)))
            : await _db.ExecutarAsync("""
                UPDATE core.forma_pagamento SET descricao = @d, meio = @m, prazos = @p, conta_bancaria_id = @cb, ativa = @at
                 WHERE id = @id AND empresa_id = @e
                """, ct, ("id", f.Id), ("e", empresa), ("d", f.Descricao), ("m", Sql(f.Meio)), ("p", f.Prazos), ("cb", f.ContaBancariaId), ("at", f.Ativa)) == 1
                ? f.Id : throw new ViolacaoRegraException("Forma de pagamento não encontrada."));
    }

    public async Task<Resultado> ExcluirFormaAsync(long empresa, long id, long usuario, CancellationToken ct = default)
    {
        if (await NegarAsync(usuario, empresa, ct) is { } n) return Resultado.Falha(n);
        var r = await Tentar(() => _db.ExecutarAsync("DELETE FROM core.forma_pagamento WHERE id = @id AND empresa_id = @e", ct, ("id", id), ("e", empresa)));
        return r.Sucesso ? Resultado.Ok() : Resultado.Falha(r.Erros);
    }

    public static int ProximoCodigo(IEnumerable<int> codigos) => Sequencia.MenorLivre(codigos);
}

public static class CadastrosFinanceirosExtensions
{
    public static ServicoCadastrosFinanceiros CadastrosFinanceiros(this VorticisApp app) => new(app);
}
