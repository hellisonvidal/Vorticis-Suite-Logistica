using Vorticis.Desktop.Comum;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Desktop.ViewModels;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Relatorios;
using Vorticis.Infra;
using Vorticis.Infra.Relatorios;
using Vorticis.Application.Relatorios;

namespace Vorticis.Fiscal.Tests;

/// <summary>Contexto mínimo para exercitar os ViewModels do módulo sem a janela principal.</summary>
public sealed class ContextoDeTeste(VorticisApp app, long usuarioId, long empresaId) : IContexto
{
    public VorticisApp? App => app;
    public SessaoUsuario? Sessao { get; } = new(usuarioId, "teste", "Usuário de Teste", true, false);
    public long? EmpresaId => empresaId;
    public bool Pode(Permissao permissao) => true;
    public Task RecarregarEmpresasAsync(long? selecionar = null) => Task.CompletedTask;
    public void AoConcluirConfiguracaoInicial() { }
    public Task<bool> ConfirmarAsync(string titulo, string mensagem, string textoSim = "Sim", string textoNao = "Não") => Task.FromResult(true);
    public Task ReiniciarParaLoginAsync(string mensagem) => Task.CompletedTask;
    public Task ConcluirImplantacaoAsync(string loginAdministrador) => Task.CompletedTask;
    public Task<string?> SolicitarSenhaAsync(string titulo, string mensagem, string textoSim = "Confirmar", string textoNao = "Cancelar") => Task.FromResult<string?>(null);
    public Application.Cadastros.ModulosEmpresa ModuloAtual => Application.Cadastros.ModulosEmpresa.Contabil;
    public Task<Application.Cadastros.ModulosEmpresa?> EscolherModulosAsync(Application.Cadastros.ModulosEmpresa inicial) => Task.FromResult<Application.Cadastros.ModulosEmpresa?>(inicial);
    public string DescricaoEmpresa => "Empresa de Teste LTDA - 33.000.167/0001-01";
    public void AbrirTela(string tela) { }
}

public class TabelasFiscaisTests
{
    [Fact]
    public void Tabelas_dos_relatorios_exportam_para_pdf_e_excel_com_as_mesmas_linhas()
    {
        var itens = new[]
        {
            new LinhaItemFiscal(1, ModeloDocumento.NFe55, TipoOperacao.Saida, "100", "1", new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 10), SituacaoDocumento.Autorizada,
                "Cliente X", "11222333000181", "RJ", "k", false,
                new ItemFiscal(1, null, null, null, null, "5102", null, 1, 100, 100, 0, 0, 0, 0, 100, "00", "0", 100, 18, 18, 0, 0, null, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, 0, 0, 0)),
        };
        var livro = TabelasFiscais.Livro(TipoOperacao.Saida, LivrosFiscais.Montar(itens, TipoOperacao.Saida));
        Assert.Equal(2, livro.Linhas.Count);            // documento + total
        Assert.All(livro.Linhas, l => Assert.Equal(livro.Colunas.Count, l.Valores.Count));
        var resumo = TabelasFiscais.ResumoCfop(LivrosFiscais.ResumirPorCfop(itens));
        Assert.All(resumo.Linhas, l => Assert.Equal(resumo.Colunas.Count, l.Valores.Count));

        var pdf = new GeradorPdf().Gerar(ExportacaoRelatorio.ParaPdf("Livro", ["Empresa"], [livro, resumo], paisagem: true));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Conteudo, 0, 4));
        var linhas = ExportadorExcel.Ler(ExportadorExcel.Gerar([livro]));
        Assert.Equal(2, linhas.Count);
        Assert.Equal("CFOP", livro.Colunas[8].Titulo);
        Assert.Equal("5102", linhas[0]["CFOP"]?.ToString());
    }

    [Fact]
    public void Tabelas_de_apuracao_tem_linhas_compativeis_com_as_colunas()
    {
        var tabela = Simples.TabelaSimples.Carregar(Comum.ParametrosEmbutidos.Json(Comum.ChavesParametro.SimplesNacional));
        var r = Simples.CalculadoraSimples.Calcular(tabela, new Simples.EntradaSimples(new DateOnly(2026, 3, 1),
            [new Simples.ReceitaSimples(Simples.AtividadeSimples.ComercioRevenda, 50_000m)], 300_000m, 0));
        foreach (var t in TabelasFiscais.Simples(r)) Assert.All(t.Linhas, l => Assert.Equal(t.Colunas.Count, l.Valores.Count));
        var p = Lucro.ParametrosLucro.Carregar(Comum.ParametrosEmbutidos.Json(Comum.ChavesParametro.LucroPresumidoReal));
        foreach (var t in TabelasFiscais.Lucro(Lucro.CalculadoraLucro.CalcularPresumido(p, new Lucro.EntradaPresumido(2026, 1, [new Lucro.ReceitaPresumida("SERVICOS_GERAIS", 1000m)], 0, 0, 0))))
            Assert.All(t.Linhas, l => Assert.Equal(t.Colunas.Count, l.Valores.Count));
        foreach (var t in TabelasFiscais.PisCofins(Lucro.CalculadoraLucro.CalcularPisCofins(p, new Lucro.EntradaPisCofins(new DateOnly(2026, 3, 1), Lucro.RegimePisCofins.NaoCumulativo, 1000m, 100m, 0, 0))))
            Assert.All(t.Linhas, l => Assert.Equal(t.Colunas.Count, l.Valores.Count));
    }
}

public class ViewModelsFiscaisTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Fluxo_pelas_telas_importar_consultar_livro_exportar_apurar_e_guias()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        const string cnpj = "33000167000101";
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        var ctx = new ContextoDeTeste(db.App, adm, empresa);
        var pasta = Directory.CreateTempSubdirectory("telas_fiscais").FullName;
        ArquivosGerados.AbrirAposGerar = false;
        ArquivosGerados.Pasta = Path.Combine(pasta, "saida");
        try
        {
            var mar = new DateOnly(2026, 3, 10);
            File.WriteAllBytes(Path.Combine(pasta, "venda.xml"), XmlsDeTeste.Bytes(XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 10, mar, [new ItemNfe("5102", 30_000m, 5_400m)])));
            File.WriteAllBytes(Path.Combine(pasta, "servico.xml"), XmlsDeTeste.Bytes(XmlsDeTeste.Nfse(cnpj, XmlsDeTeste.CnpjCliente, 3, mar, 20_000m, iss: 1_000m)));
            File.WriteAllBytes(Path.Combine(pasta, "compra.xml"), XmlsDeTeste.Bytes(XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, cnpj, 1, 77, mar, [new ItemNfe("1102", 8_000m, 1_440m)])));

            // Importação pela tela
            var importacao = new ImportacaoXmlViewModel(ctx) { Pasta = pasta, IncluirSubpastas = false, CompletarPeloCnpj = false };
            await importacao.ImportarPastaCommand.ExecuteAsync(null);
            Assert.Empty(importacao.Erros);
            Assert.Equal(3, importacao.Resultados.Count(r => r.Situacao == "Importado"));
            Assert.Contains("3 importado(s)", importacao.Resumo);
            await importacao.ImportarArquivosAsync([Path.Combine(pasta, "venda.xml")]);
            Assert.Equal("Já importado", importacao.Resultados.Single().Situacao);

            // Consulta
            var docs = new DocumentosViewModel(ctx) { Inicio = new DateTime(2026, 3, 1), Fim = new DateTime(2026, 3, 31) };
            await docs.ConsultarCommand.ExecuteAsync(null);
            Assert.Equal(3, docs.Documentos.Count);
            docs.Selecionado = docs.Documentos.First(d => d.Modelo == "NF-e" && d.Operacao == "Saída");
            for (var i = 0; i < 50 && docs.Itens.Count == 0; i++) await Task.Delay(20, ct);
            Assert.Equal("5102", docs.Itens.Single().Cfop);

            // Livro de saídas + exportação
            var livro = new LivroSaidasViewModel(ctx) { Inicio = new DateTime(2026, 3, 1), Fim = new DateTime(2026, 3, 31) };
            await livro.GerarCommand.ExecuteAsync(null);
            Assert.Empty(livro.Erros);
            Assert.True(livro.TemDados);
            Assert.Equal(2, livro.Exibidas.Count);   // livro + resumo por CFOP
            await livro.ExportarPdfCommand.ExecuteAsync(null);
            Assert.EndsWith(".pdf", livro.UltimoArquivo);
            Assert.True(File.Exists(livro.UltimoArquivo));
            livro.ExportarExcelCommand.Execute(null);
            Assert.EndsWith(".xlsx", livro.UltimoArquivo);
            Assert.Contains("Cliente Teste Ltda", ExportadorExcel.Ler(File.ReadAllBytes(livro.UltimoArquivo!)).Select(l => l["Cliente"]?.ToString()).ToList());
            var resumo = new ResumoCfopViewModel(ctx) { Inicio = new DateTime(2026, 3, 1), Fim = new DateTime(2026, 3, 31) };
            await resumo.GerarCommand.ExecuteAsync(null);
            Assert.Equal(["1102", "5102", "NFSE"], resumo.Exibidas.Single().Linhas.Where(l => l[0] != "TOTAL").Select(l => l[0]));

            // Simples Nacional pela tela
            var simples = new ApuracaoSimplesViewModel(ctx) { CompetenciaData = new DateTime(2026, 3, 1), Rbt12Texto = "300.000,00", Folha12Texto = "0,00" };
            await simples.SugerirCommand.ExecuteAsync(null);
            Assert.Empty(simples.Erros);
            Assert.Equal(2, simples.Receitas.Count);
            simples.Rbt12Texto = "300.000,00";
            await simples.CalcularCommand.ExecuteAsync(null);
            Assert.Empty(simples.Erros);
            Assert.Equal(3, simples.Exibidas.Count);
            await simples.SalvarCommand.ExecuteAsync(null);
            Assert.Contains("DAS", simples.Mensagem);
            Assert.True(simples.PodeContabilizar);

            // Guias a partir da apuração salva
            var guias = new GuiasViewModel(ctx) { CompetenciaInicio = new DateTime(2026, 1, 1), CompetenciaFim = new DateTime(2026, 12, 1) };
            await guias.GerarCommand.ExecuteAsync(null);
            Assert.Empty(guias.Erros);
            var das = Assert.Single(guias.Linhas);
            Assert.Equal("DAS", das.Documento);
            Assert.Equal("20/04/2026", das.Vencimento);
            await guias.ExportarPdfCommand.ExecuteAsync(null);
            Assert.True(File.Exists(guias.UltimoArquivo));

            // Regras de contabilização e contabilização pelas telas
            var regras = new RegrasContabilizacaoViewModel(ctx);
            regras.NovaCommand.Execute(null);
            (regras.Cfop, regras.Debito, regras.Credito) = ("5102", "1.1.02.001", "3.1.01.001");
            await regras.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(regras.Erros);
            Assert.Single(regras.Regras);
            var notas = new ContabilizarNotasViewModel(ctx) { Inicio = new DateTime(2026, 3, 1), Fim = new DateTime(2026, 3, 31) };
            await notas.PreVisualizarCommand.ExecuteAsync(null);
            Assert.Equal(3, notas.Pendentes.Count);
            Assert.Equal(1, notas.Pendentes.Count(p => p.Marcada));   // só a venda 5102 tem regra
            await notas.ContabilizarCommand.ExecuteAsync(null);
            Assert.Single(notas.Contabilizadas);
            notas.Contabilizadas[0].Marcada = true;
            await notas.DesfazerCommand.ExecuteAsync(null);
            Assert.Empty(notas.Contabilizadas);

            // Histórico de receita e folha
            var historico = new BaseMensalViewModel(ctx) { Ano = 2025 };
            await historico.CarregarCommand.ExecuteAsync(null);
            Assert.Equal(12, historico.Linhas.Count);
            historico.Linhas[0].ReceitaTexto = "10.000,00";
            await historico.SalvarCommand.ExecuteAsync(null);
            Assert.Empty(historico.Erros);
            Assert.Equal("10.000,00", historico.Linhas[0].ReceitaOriginal);
        }
        finally
        {
            Directory.Delete(pasta, true);
        }
    }
}
