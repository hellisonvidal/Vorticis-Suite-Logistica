using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Domain.Comum;

namespace Vorticis.Frota.Desktop;

/// <summary>Importação de planilha dos cadastros da Frota e do TMS: veículos, motoristas, frotas, rotas e tabelas de frete.</summary>
public static class ImportacoesFrota
{
    private static ServicoFrota F(ContextoImportacao c) => ServicosFrota.De(c.App);

    /// <summary>Código gravado (ex.: DIESEL_S10) a partir do código ou do rótulo da tela (ex.: "Diesel S10"), sem acento nem maiúsculas.</summary>
    internal static string? Codigo(string? texto, IEnumerable<string> codigos)
    {
        if (texto is null) return null;
        var t = TabelaImportada.Normalizar(texto);
        return codigos.FirstOrDefault(c => TabelaImportada.Normalizar(c) == t || TabelaImportada.Normalizar(RegrasFrota.Rotulo(c)) == t);
    }

    private static async Task<int> Codigo(ContextoImportacao c, LinhaImportacao l, string cadastro, Func<Task<int>> maior) =>
        l.Inteiro("codigo") is { } cod and > 0 ? cod : await c.ProximoCodigoAsync(cadastro, maior);

    private static readonly DefinicaoImportacao Veiculos = new("veiculos", "Veículos",
    [
        new("codigo", "Código", false, "1", "cod"),
        new("placa", "Placa", true, "ABC1D23", "placa"),
        new("renavam", "RENAVAM", false, "", "renavam"),
        new("tipo", "Tipo", false, "Cavalo", "tipo", "tipo_veiculo", "categoria"),
        new("marca", "Marca", false, "Volvo", "marca", "fabricante"),
        new("modelo", "Modelo", false, "FH 540", "modelo"),
        new("anofab", "Ano de fabricação", false, "2022", "ano_fabricacao", "ano_fab", "ano"),
        new("anomod", "Ano do modelo", false, "2023", "ano_modelo"),
        new("combustivel", "Combustível", false, "Diesel S10", "combustivel"),
        new("capacidade", "Capacidade (kg)", false, "30000", "capacidade", "capacidade_kg", "lotacao"),
        new("km", "Km atual", false, "150000", "km", "km_atual", "hodometro"),
        new("media", "Média esperada (km/l)", false, "2,5", "media", "media_esperada", "km_l"),
        new("frota", "Frota (código ou descrição)", false, "1", "frota"),
    ], async (c, l) =>
    {
        var frotas = await c.UmaVezAsync("frotas", () => F(c).FrotasAsync(c.Empresa, c.Usuario));
        long? frota = l.Texto("frota") is { } fr ? frotas.FirstOrDefault(f => f.Codigo.ToString() == fr || string.Equals(f.Descricao, fr, StringComparison.OrdinalIgnoreCase))?.Id : null;
        if (l.Texto("frota") is { } nome && frota is null) return Resultado.Falha($"Frota não encontrada: {nome}.");
        var tipo = Codigo(l.Texto("tipo"), RegrasFrota.TiposVeiculo) ?? (l.Texto("tipo") is null ? "OUTRO" : null);
        var comb = Codigo(l.Texto("combustivel"), RegrasFrota.CombustiveisVeiculo) ?? (l.Texto("combustivel") is null ? "DIESEL_S10" : null);
        if (tipo is null) return Resultado.Falha($"Tipo de veículo desconhecido: {l.Texto("tipo")} (use {string.Join(", ", RegrasFrota.TiposVeiculo.Select(RegrasFrota.Rotulo))}).");
        if (comb is null) return Resultado.Falha($"Combustível desconhecido: {l.Texto("combustivel")}.");
        var codigo = await Codigo(c, l, "veiculos", async () => (await F(c).VeiculosAsync(c.Empresa, c.Usuario)).Select(v => v.Codigo).DefaultIfEmpty(0).Max());
        var v = new Veiculo(0, codigo, RegrasFrota.NormalizarPlaca(l.Texto("placa")), l.Texto("renavam"), tipo, l.Texto("marca"), l.Texto("modelo"), l.Inteiro("anofab"),
                            l.Inteiro("anomod"), comb, l.Valor("capacidade"), l.Valor("km") ?? 0, l.Valor("media"), FrotaId: frota);
        return await F(c).GravarVeiculoAsync(c.Empresa, v, c.Usuario);
    });

    private static readonly DefinicaoImportacao Motoristas = new("motoristas", "Motoristas",
    [
        new("codigo", "Código", false, "1", "cod"),
        new("nome", "Nome", true, "João da Silva", "nome", "motorista"),
        new("cpf", "CPF", true, "529.982.247-25", "cpf"),
        new("cnh", "CNH", false, "01234567890", "cnh", "registro_cnh", "numero_cnh"),
        new("categoria", "Categoria da CNH", false, "E", "categoria", "categoria_cnh", "cat_cnh"),
        new("validade", "Validade da CNH", false, "31/12/2027", "validade", "validade_cnh", "vencimento_cnh"),
        new("telefone", "Telefone", false, "(11) 99999-0000", "telefone", "celular", "fone"),
    ], async (c, l) =>
    {
        var codigo = await Codigo(c, l, "motoristas", async () => (await F(c).MotoristasAsync(c.Empresa, c.Usuario)).Select(m => m.Codigo).DefaultIfEmpty(0).Max());
        var m = new Motorista(0, codigo, l.Texto("nome") ?? "", new string([.. (l.Texto("cpf") ?? "").Where(char.IsAsciiDigit)]), l.Texto("cnh"),
                              l.Texto("categoria")?.ToUpperInvariant(), l.Data("validade"), l.Texto("telefone"));
        return await F(c).GravarMotoristaAsync(c.Empresa, m, c.Usuario);
    });

    private static readonly DefinicaoImportacao Frotas = new("frotas", "Frotas",
    [
        new("codigo", "Código", false, "1", "cod"),
        new("descricao", "Descrição", true, "Frota própria", "descricao", "frota", "nome"),
        new("tipo", "Tipo (própria, agregada ou terceiros)", false, "Própria", "tipo"),
        new("responsavel", "Responsável", false, "Carlos", "responsavel"),
    ], async (c, l) =>
    {
        var tipo = Codigo(l.Texto("tipo"), RegrasLogistica.TiposFrota) ?? (l.Texto("tipo") is null ? "PROPRIA" : null);
        if (tipo is null) return Resultado.Falha($"Tipo de frota desconhecido: {l.Texto("tipo")} (use própria, agregada ou terceiros).");
        var codigo = await Codigo(c, l, "frotas", async () => (await F(c).FrotasAsync(c.Empresa, c.Usuario)).Select(f => f.Codigo).DefaultIfEmpty(0).Max());
        return await F(c).GravarFrotaAsync(c.Empresa, new FrotaCadastro(0, codigo, l.Texto("descricao") ?? "", tipo, l.Texto("responsavel"), null), c.Usuario);
    });

    /// <summary>Município pelo nome e UF (sem acento nem maiúsculas) ou pelo código IBGE.</summary>
    internal static ParadaRota? Municipio(string? uf, string? nome)
    {
        if (nome is null) return null;
        var m = BaseMunicipios.Obter(nome) ?? BaseMunicipios.DaUf(uf).FirstOrDefault(x => TabelaImportada.Normalizar(x.Nome) == TabelaImportada.Normalizar(nome));
        return m is null ? null : new ParadaRota(m.Uf, m.Ibge, m.Nome);
    }

    private static readonly DefinicaoImportacao Rotas = new("rotas", "Rotas",
    [
        new("codigo", "Código", false, "1", "cod"),
        new("descricao", "Descrição", true, "Campinas x Rio", "descricao", "rota", "nome"),
        new("ufo", "UF de origem", true, "SP", "uf_origem", "uf_orig"),
        new("origem", "Município de origem", true, "Campinas", "origem", "municipio_origem", "cidade_origem"),
        new("ufd", "UF de destino", true, "RJ", "uf_destino", "uf_dest"),
        new("destino", "Município de destino", true, "Rio de Janeiro", "destino", "municipio_destino", "cidade_destino"),
        new("km", "Distância (km)", false, "", "km", "distancia", "distancia_km"),
        new("pedagios", "Pedágios (R$)", false, "", "pedagio", "pedagios"),
    ], async (c, l) =>
    {
        var origem = Municipio(l.Texto("ufo"), l.Texto("origem"));
        var destino = Municipio(l.Texto("ufd"), l.Texto("destino"));
        if (origem is null || destino is null)
            return Resultado.Falha($"Município não encontrado: {(origem is null ? $"{l.Texto("origem")}/{l.Texto("ufo")}" : $"{l.Texto("destino")}/{l.Texto("ufd")}")}.");
        var cfg = await c.UmaVezAsync("cfg", () => F(c).ConfiguracaoAsync(c.Empresa));
        var km = l.Valor("km") ?? RegrasLogistica.EstimarKm([origem, destino], cfg.FatorRodoviario) ?? 0;
        var codigo = await Codigo(c, l, "rotas", async () => (await F(c).RotasAsync(c.Empresa, c.Usuario)).Select(r => r.Codigo).DefaultIfEmpty(0).Max());
        var rota = new Rota(0, codigo, l.Texto("descricao") ?? "", km, l.Valor("km") is null ? "ESTIMADA" : "MANUAL", RegrasLogistica.EstimarHoras(km, cfg.VelocidadeMedia),
                            l.Valor("pedagios") ?? 0, true, [origem, destino]);
        return await F(c).GravarRotaAsync(c.Empresa, rota, c.Usuario);
    });

    private static readonly DefinicaoImportacao TabelasFrete = new("tabelasfrete", "Tabelas de frete",
    [
        new("codigo", "Código", false, "1", "cod"),
        new("descricao", "Descrição", true, "SP x RJ fracionado", "descricao", "tabela", "nome"),
        new("ufo", "UF de origem", false, "SP", "uf_origem"),
        new("ufd", "UF de destino", false, "RJ", "uf_destino"),
        new("kg", "Valor por kg", false, "0,50", "valor_kg", "r_kg", "frete_kg"),
        new("minimo", "Frete mínimo", false, "150,00", "frete_minimo", "minimo"),
        new("cubagem", "Fator de cubagem (kg/m³)", false, "300", "cubagem", "fator_cubagem"),
        new("advalorem", "Ad valorem (%)", false, "0,3", "ad_valorem", "advalorem"),
        new("gris", "GRIS (%)", false, "0,2", "gris"),
        new("pedagio", "Pedágio por fração de 100 kg", false, "5,00", "pedagio", "pedagio_fracao"),
        new("coleta", "Taxa de coleta", false, "30,00", "taxa_coleta", "coleta"),
        new("entrega", "Taxa de entrega", false, "40,00", "taxa_entrega", "entrega"),
        new("outras", "Outras taxas", false, "", "outras_taxas", "outras"),
        new("icms", "Alíquota de ICMS (%)", false, "12", "icms", "aliquota_icms"),
        new("prazo", "Prazo de entrega (dias)", false, "3", "prazo", "prazo_dias"),
    ], async (c, l) =>
    {
        var codigo = await Codigo(c, l, "tabelasfrete", async () => (await F(c).TabelasFreteAsync(c.Empresa, c.Usuario)).Select(t => t.Codigo).DefaultIfEmpty(0).Max());
        var t = new TabelaFrete(0, codigo, l.Texto("descricao") ?? "", null, l.Texto("ufo"), l.Texto("ufd"), l.Valor("kg") ?? 0, l.Valor("minimo") ?? 0,
                                l.Valor("cubagem") ?? 300, l.Valor("advalorem") ?? 0, l.Valor("gris") ?? 0, l.Valor("pedagio") ?? 0, l.Valor("coleta") ?? 0,
                                l.Valor("entrega") ?? 0, l.Valor("outras") ?? 0, l.Valor("icms") ?? 0, l.Inteiro("prazo") ?? 0);
        return await F(c).GravarTabelaFreteAsync(c.Empresa, t, c.Usuario);
    });

    public static IReadOnlyList<DefinicaoImportacao> Todas { get; } =
    [
        Veiculos with { TelaCadastro = ViewModels.FrotaTelas.Veiculos }, Motoristas with { TelaCadastro = ViewModels.FrotaTelas.Motoristas },
        Frotas with { TelaCadastro = ViewModels.FrotaTelas.Frotas }, Rotas with { TelaCadastro = ViewModels.FrotaTelas.Rotas },
        TabelasFrete with { TelaCadastro = ViewModels.FrotaTelas.TabelasFrete },
    ];
}
