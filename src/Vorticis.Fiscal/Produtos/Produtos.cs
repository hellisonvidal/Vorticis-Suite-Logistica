using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Produtos;

/// <summary>Produto/mercadoria do cadastro fiscal, com os padrões usados na digitação de notas.</summary>
public sealed record Produto(
    long Id, int Codigo, string Descricao, string Unidade, string? Ncm, string? Cest, string? Gtin, string Origem, string TipoItem,
    string? CfopEntrada, string? CfopSaida, string? CstIcmsEntrada, string? CstIcmsSaida,
    decimal AliqIcms, decimal AliqIpi, decimal AliqPis, decimal AliqCofins, bool Ativo = true);

/// <summary>Tipo do item (SPED, registro 0200, campo TIPO_ITEM).</summary>
public static class TiposItem
{
    public static IReadOnlyList<(string Codigo, string Descricao)> Todos { get; } =
    [
        ("00", "Mercadoria para revenda"), ("01", "Matéria-prima"), ("02", "Embalagem"), ("03", "Produto em processo"), ("04", "Produto acabado"),
        ("05", "Subproduto"), ("06", "Produto intermediário"), ("07", "Material de uso e consumo"), ("08", "Ativo imobilizado"),
        ("09", "Serviços"), ("10", "Outros insumos"), ("99", "Outras"),
    ];

    public static string Rotulo(string codigo) => Todos.FirstOrDefault(t => t.Codigo == codigo).Descricao ?? codigo;
}

/// <summary>Origem da mercadoria (campo orig do ICMS).</summary>
public static class OrigensMercadoria
{
    public static IReadOnlyList<(string Codigo, string Descricao)> Todas { get; } =
    [
        ("0", "Nacional"), ("1", "Estrangeira - importação direta"), ("2", "Estrangeira - adquirida no mercado interno"),
        ("3", "Nacional - importação superior a 40% e até 70%"), ("4", "Nacional - processos produtivos básicos"),
        ("5", "Nacional - importação até 40%"), ("6", "Estrangeira - importação direta, sem similar nacional"),
        ("7", "Estrangeira - mercado interno, sem similar nacional"), ("8", "Nacional - importação superior a 70%"),
    ];
}

/// <summary>GTIN (código de barras EAN/UPC): 8, 12, 13 ou 14 dígitos com dígito verificador módulo 10.</summary>
public static class ValidadorGtin
{
    public static bool Valido(string? gtin)
    {
        if (gtin is not { Length: 8 or 12 or 13 or 14 } || !gtin.All(char.IsAsciiDigit)) return false;
        var soma = 0;
        for (var i = gtin.Length - 2; i >= 0; i--)
            soma += (gtin[i] - '0') * ((gtin.Length - 2 - i) % 2 == 0 ? 3 : 1);
        return (10 - soma % 10) % 10 == gtin[^1] - '0';
    }

    /// <summary>Texto do XML ("SEM GTIN", vazio, inválido) vira null; GTIN válido volta só com os dígitos.</summary>
    public static string? Normalizar(string? texto)
    {
        var d = new string((texto ?? "").Where(char.IsAsciiDigit).ToArray());
        return Valido(d) ? d : null;
    }
}

public sealed record FiltroItensNotas(DateOnly Inicio, DateOnly Fim, TipoOperacao? Operacao = null, long? ParticipanteId = null, long? ProdutoId = null,
                                      string? Cfop = null, string? Ncm = null, int Limite = 20000);

/// <summary>Item de nota fiscal para o relatório "Itens das notas fiscais".</summary>
public sealed record LinhaItemNota(
    DateOnly DataLivro, TipoOperacao Operacao, ModeloDocumento Modelo, string Serie, string Numero, string? Participante, string? Documento,
    int NItem, int? CodigoProduto, string? Descricao, string? Ncm, string Cfop, string? Unidade, decimal Quantidade, decimal ValorUnitario,
    decimal ValorProduto, decimal ValorContabil, decimal Icms, decimal IcmsSt, decimal Ipi, decimal Pis, decimal Cofins, decimal Iss);

public interface IRepositorioProdutos
{
    Task<IReadOnlyList<Produto>> ListarAsync(long empresaId, bool apenasAtivos, string? texto, CancellationToken ct);

    /// <summary>Grava (Id = 0 inclui com o próximo código da empresa) e devolve o id. Código de barras repetido: <see cref="ViolacaoRegraException"/>.</summary>
    Task<long> GravarAsync(long empresaId, Produto produto, long usuarioId, CancellationToken ct);

    /// <summary>Quantidade de itens de notas que usam o produto.</summary>
    Task<int> ContarUsoAsync(long empresaId, long produtoId, CancellationToken ct);
    Task<bool> ExcluirAsync(long empresaId, long produtoId, CancellationToken ct);
    Task<IReadOnlyList<LinhaItemNota>> ListarItensNotasAsync(long empresaId, FiltroItensNotas filtro, CancellationToken ct);
}

/// <summary>Cadastro de produtos do Fiscal e relatórios de produtos e de itens das notas.</summary>
public sealed class ServicoProdutos(IRepositorioProdutos repositorio, Autorizador autorizador)
{
    public async Task<Resultado<IReadOnlyList<Produto>>> ListarAsync(long empresaId, bool apenasAtivos, string? texto, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<Produto>>(usuarioId, empresaId, Permissao.FiscalDocumentosConsultar, ct) is { } negado) return negado;
        return Resultado<IReadOnlyList<Produto>>.Ok(await repositorio.ListarAsync(empresaId, apenasAtivos, texto, ct));
    }

    /// <summary>Validação do cadastro; devolve o produto normalizado (somente dígitos em NCM/CEST/GTIN, texto aparado) ou os erros.</summary>
    public static Resultado<Produto> Validar(Produto p)
    {
        var erros = new List<string>();
        static string? Dig(string? t) => string.IsNullOrWhiteSpace(t) ? null : new string(t.Where(char.IsAsciiDigit).ToArray());
        var descricao = (p.Descricao ?? "").Trim();
        var unidade = (p.Unidade ?? "").Trim().ToUpperInvariant();
        if (descricao.Length == 0) erros.Add("Informe a descrição do produto.");
        else if (descricao.Length > 200) erros.Add("A descrição pode ter até 200 caracteres.");
        if (unidade.Length == 0 || unidade.Length > 10) erros.Add("Informe a unidade (até 10 caracteres).");
        var ncm = Dig(p.Ncm);
        if (ncm is not null && ncm.Length != 8) erros.Add("NCM deve ter 8 dígitos.");
        var cest = Dig(p.Cest);
        if (cest is not null && cest.Length != 7) erros.Add("CEST deve ter 7 dígitos.");
        var gtin = Dig(p.Gtin);
        if (gtin is not null && !ValidadorGtin.Valido(gtin)) erros.Add("Código de barras (GTIN) inválido: use 8, 12, 13 ou 14 dígitos com dígito verificador correto.");
        if (p.Origem is not { Length: 1 } || p.Origem[0] is < '0' or > '8') erros.Add("Origem da mercadoria inválida (0 a 8).");
        if (TiposItem.Todos.All(t => t.Codigo != p.TipoItem)) erros.Add("Tipo do item inválido.");
        var cfopE = Dig(p.CfopEntrada);
        var cfopS = Dig(p.CfopSaida);
        if (cfopE is not null && Cfop.Operacao(cfopE) != TipoOperacao.Entrada) erros.Add("CFOP de entrada padrão: informe 4 dígitos iniciados por 1, 2 ou 3.");
        if (cfopS is not null && Cfop.Operacao(cfopS) != TipoOperacao.Saida) erros.Add("CFOP de saída padrão: informe 4 dígitos iniciados por 5, 6 ou 7.");
        string? Cst(string? t, string nome)
        {
            var d = Dig(t);
            if (d is not null && d.Length is not (2 or 3)) erros.Add($"{nome}: informe 2 dígitos (CST) ou 3 (CSOSN).");
            return d;
        }
        var cstE = Cst(p.CstIcmsEntrada, "CST de entrada padrão");
        var cstS = Cst(p.CstIcmsSaida, "CST de saída padrão");
        foreach (var (nome, v) in new[] { ("ICMS", p.AliqIcms), ("IPI", p.AliqIpi), ("PIS", p.AliqPis), ("COFINS", p.AliqCofins) })
            if (v is < 0 or > 100) erros.Add($"Alíquota de {nome} deve estar entre 0 e 100.");
        if (erros.Count > 0) return Resultado<Produto>.Falha(erros);
        return Resultado<Produto>.Ok(p with
        {
            Descricao = descricao, Unidade = unidade, Ncm = ncm, Cest = cest, Gtin = gtin, CfopEntrada = cfopE, CfopSaida = cfopS, CstIcmsEntrada = cstE, CstIcmsSaida = cstS,
        });
    }

    public async Task<Resultado<long>> GravarAsync(long empresaId, Produto produto, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FiscalDocumentosImportar, ct) is { } negado) return negado;
        var v = Validar(produto);
        if (!v.Sucesso) return Resultado<long>.Falha(v.Erros);
        try { return Resultado<long>.Ok(await repositorio.GravarAsync(empresaId, v.Valor, usuarioId, ct)); }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    /// <summary>Exclui o produto sem uso em notas; produto já utilizado deve ser inativado.</summary>
    public async Task<Resultado> ExcluirAsync(long empresaId, long produtoId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalDocumentosImportar, ct) is { } negado) return Resultado.Falha(negado);
        var usos = await repositorio.ContarUsoAsync(empresaId, produtoId, ct);
        if (usos > 0) return Resultado.Falha($"O produto é usado em {usos} item(ns) de notas fiscais: inative-o em vez de excluir.");
        return await repositorio.ExcluirAsync(empresaId, produtoId, ct) ? Resultado.Ok() : Resultado.Falha("Produto não encontrado.");
    }

    // ------------------------------------------------------------------ relatórios

    public async Task<Resultado<IReadOnlyList<Produto>>> RelatorioProdutosAsync(long empresaId, bool apenasAtivos, string? texto, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<Produto>>(usuarioId, empresaId, Permissao.FiscalRelatorios, ct) is { } negado) return negado;
        return Resultado<IReadOnlyList<Produto>>.Ok(await repositorio.ListarAsync(empresaId, apenasAtivos, texto, ct));
    }

    /// <summary>Itens das notas autorizadas no período (data do livro), com os filtros do relatório.</summary>
    public async Task<Resultado<IReadOnlyList<LinhaItemNota>>> RelatorioItensAsync(long empresaId, FiltroItensNotas filtro, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<LinhaItemNota>>(usuarioId, empresaId, Permissao.FiscalRelatorios, ct) is { } negado) return negado;
        if (filtro.Fim < filtro.Inicio) return Resultado<IReadOnlyList<LinhaItemNota>>.Falha("Data final anterior à inicial.");
        return Resultado<IReadOnlyList<LinhaItemNota>>.Ok(await repositorio.ListarItensNotasAsync(empresaId, filtro, ct));
    }
}
