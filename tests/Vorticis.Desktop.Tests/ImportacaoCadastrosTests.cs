using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Domain.Comum;
using Vorticis.Frota;
using Vorticis.Frota.Desktop;
using Vorticis.Infra.Tests;

namespace Vorticis.Desktop.Tests;

public class ImportacaoCadastrosTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static async Task<List<string>> Importar(ContextoImportacao ctx, DefinicaoImportacao def, string csv)
    {
        var t = TabelaImportada.DeTexto(csv);
        var erros = new List<string>();
        foreach (var l in ImportacaoPlanilha.Linhas(t, ImportacaoPlanilha.Sugerir(t, def.Campos)))
            if (await def.Gravar(ctx, l) is { Sucesso: false } r) erros.Add($"Linha {l.Numero}: {string.Join(" ", r.Erros)}");
        return erros;
    }

    [Fact]
    public async Task Planilhas_de_frota_viram_cadastros_com_as_regras_das_telas()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("19131243000197");
        var ctx = new ContextoImportacao(db.App, empresa, adm);
        var svc = ServicosFrota.De(db.App);
        var defs = ImportacoesFrota.Todas.ToDictionary(d => d.Chave);

        Assert.Empty(await Importar(ctx, defs["frotas"], "Código;Descrição;Tipo\n7;Agregados do Sul;Agregada\n;Própria;"));
        Assert.Equal([(7, "AGREGADA"), (8, "PROPRIA")], (await svc.FrotasAsync(empresa, adm, ct)).Select(f => (f.Codigo, f.Tipo)));

        var erros = await Importar(ctx, defs["veiculos"], """
            Placa;Tipo;Marca;Modelo;Ano;Combustível;Km atual;Frota
            ABC-1D23;Cavalo;Volvo;FH 540;2022;Diesel S10;150.000;Agregados do Sul
            BRA2E19;Semirreboque;Randon;;;Não abastece;;
            XX;Cavalo;;;;;;
            DEF4G56;Foguete;;;;;;
            """);
        Assert.Equal(2, erros.Count);
        Assert.StartsWith("Linha 4:", erros[0]);
        Assert.Contains("Tipo de veículo desconhecido: Foguete", erros[1]);
        var veiculos = await svc.VeiculosAsync(empresa, adm, ct);
        Assert.Equal([(1, "ABC1D23", "CAVALO", "DIESEL_S10", 150000m), (2, "BRA2E19", "SEMIRREBOQUE", "NENHUM", 0m)],
                     veiculos.Select(v => (v.Codigo, v.Placa, v.Tipo, v.Combustivel, v.KmAtual)));
        Assert.NotNull(veiculos[0].FrotaId);
        Assert.Single(await Importar(ctx, defs["veiculos"], "Placa\nABC1D23"));        // placa repetida não duplica

        Assert.Empty(await Importar(ctx, defs["motoristas"], "Nome;CPF;Categoria CNH;Validade CNH\nPedro Alves;529.982.247-25;e;31/12/2027"));
        Assert.Equal(("52998224725", "E", new DateOnly(2027, 12, 31)), (await svc.MotoristasAsync(empresa, adm, ct)).Select(m => (m.Cpf, m.CnhCategoria, m.CnhValidade!.Value)).Single());

        Assert.Empty(await Importar(ctx, defs["rotas"], "Descrição;UF de origem;Origem;UF de destino;Destino\nCampinas x Rio;SP;campinas;RJ;Rio de Janeiro"));
        var rota = (await svc.RotasAsync(empresa, adm, ct)).Single();
        Assert.Equal(("ESTIMADA", "Campinas/SP", "Rio de Janeiro/RJ"), (rota.DistanciaOrigem, rota.Origem, rota.Destino));
        Assert.True(rota.DistanciaKm > 300);
        Assert.Contains("Município não encontrado", (await Importar(ctx, defs["rotas"], "Descrição;UF de origem;Origem;UF de destino;Destino\nX;SP;Atlantida;RJ;Rio")).Single());

        Assert.Empty(await Importar(ctx, defs["tabelasfrete"], "Descrição;UF de origem;UF de destino;Valor por kg;Frete mínimo;ICMS\nSP x RJ;SP;RJ;0,50;150;12"));
        Assert.Equal((0.50m, 150m, 12m), (await svc.TabelasFreteAsync(empresa, adm, ct)).Select(t => (t.ValorKg, t.FreteMinimo, t.AliquotaIcms)).Single());

        // abastecimentos do relatório de transações do cartão (ex.: Rede Frota): nomes de coluna e de combustível do relatório
        const string relatorio = """
            Data Transação;Placa;Produto;Quantidade;Valor Transação;Hodômetro;Estabelecimento;CPF Motorista
            10/03/2026;ABC-1D23;DIESEL S-10;300,5;1.803,00;150.800;Posto Alfa;529.982.247-25
            12/03/2026;ABC-1D23;ARLA 32;20;80,00;151.400;Posto Alfa;
            12/03/2026;ZZZ9Z99;DIESEL S-10;100;600,00;1000;Posto Beta;
            """;
        var errosAbast = await Importar(ctx, defs["abastecimentos"], relatorio);
        Assert.Contains("Veículo não cadastrado: ZZZ9Z99", Assert.Single(errosAbast));
        var abast = await svc.AbastecimentosAsync(empresa, new(2026, 3, 1), new(2026, 3, 31), null, adm, ct);
        Assert.Equal([("DIESEL_S10", 300.5m, 1803m, 150800m, true), ("ARLA32", 20m, 80m, 151400m, false)],
                     abast.OrderBy(a => a.Data).Select(a => (a.Combustivel, a.Litros, a.ValorTotal, a.Km, a.MotoristaId is not null)));
        Assert.Equal(3, (await Importar(ctx, defs["abastecimentos"], relatorio)).Count);    // a mesma planilha de novo não duplica
        Assert.Equal("DIESEL", ImportacoesFrota.Combustivel("Óleo Diesel S500"));
    }

    [Fact]
    public async Task Clientes_produtos_e_regras_fiscais_pela_planilha()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("27183946000359");
        var ctx = new ContextoImportacao(db.App, empresa, adm);
        var clientes = ImportacoesComuns.Todas.Single(d => d.Chave == "clientes");
        var erros = await Importar(ctx, clientes, "Razão social;CNPJ;UF;Cidade\nIndústria Alfa;11.222.333/0001-81;SP;Campinas\nBeta;11.222.333/0001-00;SP;");
        Assert.Contains("CPF ou CNPJ inválido", Assert.Single(erros));
        var alfa = (await db.App.Participantes.ListarAsync(empresa, adm, ct)).Single();
        Assert.Equal(("Indústria Alfa", "11222333000181", true, false), (alfa.Nome, alfa.CpfCnpj, alfa.Cliente, alfa.Fornecedor));

        var fiscais = Vorticis.Fiscal.Desktop.ImportacoesFiscais.Todas.ToDictionary(d => d.Chave);
        Assert.Empty(await Importar(ctx, fiscais["produtos"], "Descrição;Unidade;NCM;Tipo do item\nÓleo diesel S10;l;27101921;7"));
        var prod = (await Vorticis.Fiscal.VorticisAppFiscalExtensions.Fiscal(db.App).Produtos.ListarAsync(empresa, false, null, adm, ct)).Valor.Single();
        Assert.Equal((1, "L", "07"), (prod.Codigo, prod.Unidade, prod.TipoItem));
        Assert.Empty(await Importar(ctx, fiscais["regrasfiscais"], "Descrição;CFOP de saída;CFOP de entrada\nCompra;5102;1102"));
        Assert.Contains("(entrada)", (await Importar(ctx, fiscais["regrasfiscais"], "Descrição;CFOP de saída;CFOP de entrada\nErrada;6102;6102")).Single());
        // regra de saída, com o CST do ICMS
        Assert.Empty(await Importar(ctx, fiscais["regrasfiscais"], "Descrição;Operação;CFOP do XML;CFOP da escrituração;CST do ICMS\nVenda ST;Saída;5102;5405;060"));
        var venda = (await Vorticis.Fiscal.VorticisAppFiscalExtensions.Fiscal(db.App).RegrasFiscais.ListarAsync(empresa, adm, ct)).Single(r => r.Descricao == "Venda ST");
        Assert.Equal((Vorticis.Fiscal.Documentos.TipoOperacao.Saida, "5405", "060"), (venda.Operacao, venda.CfopDestino, venda.CstIcms));

        // o de-para fica gravado por empresa e cadastro (e é substituído ao gravar de novo)
        var layouts = new Vorticis.Infra.Importacao.ServicoLayoutsPlanilha(db.App.DataSource);
        Assert.Null(await layouts.LerAsync(empresa, "clientes", ct));
        await layouts.GravarAsync(empresa, "clientes", new Dictionary<string, string> { ["nome"] = "Razão social", ["cpfcnpj"] = "CNPJ", ["uf"] = "" }, ct);
        await layouts.GravarAsync(empresa, "clientes", new Dictionary<string, string> { ["nome"] = "Cliente" }, ct);
        Assert.Equal(new Dictionary<string, string> { ["nome"] = "Cliente" }, await layouts.LerAsync(empresa, "clientes", ct));
    }
}
