using Vorticis.Application.Cadastros;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Contabil;
using Vorticis.Infra.Relatorios;

namespace Vorticis.Infra.Tests;

/// <summary>Montagem do Livro Contábil Completo, dos termos isolados e do bloco de assinaturas (opção comum dos PDFs).</summary>
public class LivroContabilTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    /// <summary>Gerador que só guarda o documento montado, para conferir a estrutura sem depender do texto do PDF.</summary>
    private sealed class GeradorEspiao : IGeradorPdf
    {
        public DocumentoRelatorio? Ultimo { get; private set; }
        public ArquivoGerado Gerar(DocumentoRelatorio documento) { Ultimo = documento; return new ArquivoGerado([], 1); }
    }

    private static readonly DadosTermos Termos = new(7, "Fulana de Tal", "390.533.447-05", "Maria Contadora", "SP-123456/O-7");

    private (ServicoLivros Livros, GeradorEspiao Espiao) NovoServico()
    {
        var espiao = new GeradorEspiao();
        var app = db.App;
        return (new ServicoLivros(app.Relatorios, app.Demonstracoes, app.Consultas, app.RepositorioEmpresas, app.Autorizador, espiao, app.Assinaturas), espiao);
    }

    private async Task<long> EmpresaComLancamentosAsync(string cnpj)
    {
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        Assert.True((await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, new(2026, 3, 10), OrigemLancamento.Manual,
            [new("1.1.01.001", IndicadorDC.D, 500m, "Venda à vista"), new("3.1.01.001", IndicadorDC.C, 500m, "Venda à vista")]), adm, ct)).Sucesso);
        return empresa;
    }

    [Fact]
    public async Task Livro_contem_so_o_que_foi_marcado_e_na_ordem_do_livro()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaComLancamentosAsync("11222333000181");
        var adm = await db.AdminAsync();
        var (livros, espiao) = NovoServico();

        // tudo marcado: termos + Diário + Razão + Balancete + Balanço + DRE + DMPL + DLPA
        var completo = await livros.LivroAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), Termos, new OpcoesLivro(), adm, ct);
        Assert.True(completo.Sucesso, string.Join("; ", completo.Erros));
        var d = espiao.Ultimo!;
        Assert.Equal("LIVRO CONTÁBIL Nº 7", d.Titulo);
        Assert.NotNull(d.TermoAbertura);
        Assert.NotNull(d.TermoEncerramento);
        Assert.Contains("LIVRO CONTÁBIL Nº 7 (Diário, Razão, Balancete de Verificação, Balanço Patrimonial, DRE, DMPL e DLPA)", d.TermoAbertura!.Texto);
        string[] titulos = [.. d.Blocos.Where(b => b.Titulo is not null).Select(b => b.Titulo!)];
        int Pos(string inicio) => Array.FindIndex(titulos, t => t.StartsWith(inicio, StringComparison.Ordinal));
        Assert.True(Pos("LIVRO DIÁRIO") < Pos("LIVRO RAZÃO"));
        Assert.True(Pos("LIVRO RAZÃO") < Pos("BALANCETE"));
        Assert.True(Pos("BALANCETE") < Pos("BALANÇO PATRIMONIAL"));
        Assert.True(Pos("BALANÇO PATRIMONIAL") < Pos("DEMONSTRAÇÃO DO RESULTADO"));
        Assert.True(Pos("DEMONSTRAÇÃO DO RESULTADO") < Pos("DMPL"));
        Assert.True(Pos("DMPL") < Pos("DLPA"));
        Assert.False(d.Blocos[0].NovaPagina);                                              // a primeira parte já abre a seção
        Assert.All(new[] { "LIVRO RAZÃO", "BALANCETE", "BALANÇO PATRIMONIAL", "DEMONSTRAÇÃO DO RESULTADO", "DMPL", "DLPA" },
                   t => Assert.True(d.Blocos.First(b => b.Titulo?.StartsWith(t, StringComparison.Ordinal) == true).NovaPagina, t));   // cada parte em página nova
        Assert.Null(d.Assinaturas);                                                        // sem assinaturas marcadas

        // parcial: só DMPL e DLPA, sem termo de encerramento e com as duas assinaturas
        await CadastrarResponsaveisAsync(empresa, adm);
        var parcial = new OpcoesLivro(TermoAbertura: true, Diario: false, Razao: false, Balancete: false, Balanco: false, Dre: false,
                                      TermoEncerramento: false, Assinaturas: new OpcoesAssinatura(true, true));
        var r = await livros.LivroAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), Termos, parcial, adm, ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        d = espiao.Ultimo!;
        Assert.Equal(["DMPL — Demonstração das Mutações do Patrimônio Líquido", "DLPA — Demonstração de Lucros ou Prejuízos Acumulados"],
                     d.Blocos.Where(b => b.Titulo is not null).Select(b => b.Titulo));
        Assert.NotNull(d.TermoAbertura);
        Assert.Null(d.TermoEncerramento);
        Assert.Contains("(DMPL e DLPA)", d.TermoAbertura!.Texto);
        Assert.Equal(2, d.Assinaturas!.Count);
        Assert.Contains("Ana Sócia", d.Assinaturas[0]);
        Assert.Contains("CPF 529.982.247-25", d.Assinaturas[0]);
        Assert.Contains("CRC SP-654321/O-1", d.Assinaturas[1]);
    }

    [Fact]
    public async Task Livro_valida_numero_selecao_e_assinaturas_sem_cadastro()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaComLancamentosAsync("33000167000101");
        var adm = await db.AdminAsync();
        var (livros, _) = NovoServico();
        var periodo = (new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var nada = await livros.LivroAsync(empresa, periodo.Item1, periodo.Item2, Termos,
            new OpcoesLivro(false, false, false, false, false, false, false, false, false), adm, ct);
        Assert.Contains(nada.Erros, e => e.Contains("ao menos um item"));
        var semNumero = await livros.LivroAsync(empresa, periodo.Item1, periodo.Item2, Termos with { NumeroLivro = 0 }, new OpcoesLivro(), adm, ct);
        Assert.Contains(semNumero.Erros, e => e.Contains("número do livro"));
        // termos exigem representante e contador; sem termos, não
        var semResp = Termos with { RepresentanteNome = "", ContadorCrc = "" };
        Assert.False((await livros.LivroAsync(empresa, periodo.Item1, periodo.Item2, semResp, new OpcoesLivro(), adm, ct)).Sucesso);
        Assert.True((await livros.LivroAsync(empresa, periodo.Item1, periodo.Item2, semResp,
            new OpcoesLivro(TermoAbertura: false, TermoEncerramento: false, Diario: false, Razao: false, Balancete: false), adm, ct)).Sucesso);
        // assinatura marcada, mas a empresa não tem sócio nem contador cadastrados
        var semCadastro = await livros.LivroAsync(empresa, periodo.Item1, periodo.Item2, Termos,
            new OpcoesLivro(Assinaturas: new OpcoesAssinatura(true, true)), adm, ct);
        Assert.Contains(semCadastro.Erros, e => e.Contains("sócio/responsável"));
        Assert.Contains(semCadastro.Erros, e => e.Contains("contador"));
    }

    [Fact]
    public async Task Termos_sao_gerados_isoladamente_com_o_texto_do_diario()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaComLancamentosAsync("11444777000161");
        var adm = await db.AdminAsync();
        var (livros, espiao) = NovoServico();

        var abertura = await livros.TermoIsoladoAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), Termos with { Folhas = 120 }, TipoTermo.Abertura, false, adm, ct);
        Assert.True(abertura.Sucesso, string.Join("; ", abertura.Erros));
        var d = espiao.Ultimo!;
        Assert.Empty(d.Blocos);
        Assert.Null(d.TermoEncerramento);
        Assert.Equal("TERMO DE ABERTURA", d.TermoAbertura!.Titulo);
        Assert.Contains("Contém este livro 120 folhas, numeradas tipograficamente de 1 a 120, e servirá de LIVRO DIÁRIO Nº 7", d.TermoAbertura.Texto);
        Assert.Equal(2, d.TermoAbertura.Assinaturas.Count);

        // sem total de folhas, fica a linha para preencher; o livro contábil muda o nome do livro
        Assert.True((await livros.TermoIsoladoAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), Termos, TipoTermo.Encerramento, true, adm, ct)).Sucesso);
        d = espiao.Ultimo!;
        Assert.Null(d.TermoAbertura);
        Assert.Equal("TERMO DE ENCERRAMENTO", d.TermoEncerramento!.Titulo);
        Assert.Contains("Contém este livro ______ folhas", d.TermoEncerramento.Texto);
        Assert.Contains("serviu de LIVRO CONTÁBIL Nº 7", d.TermoEncerramento.Texto);
        Assert.Contains("31/12/2026", d.TermoEncerramento.Texto);

        Assert.Contains((await livros.TermoIsoladoAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), Termos with { Folhas = 0 }, TipoTermo.Abertura, false, adm, ct)).Erros,
                        e => e.Contains("folhas"));
        Assert.False((await livros.TermoIsoladoAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), Termos with { ContadorNome = "" }, TipoTermo.Abertura, false, adm, ct)).Sucesso);
    }

    [Fact]
    public async Task Pdf_real_do_livro_tem_termos_isolados_e_bloco_de_assinaturas()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await EmpresaComLancamentosAsync("45723174000110");
        var adm = await db.AdminAsync();
        await CadastrarResponsaveisAsync(empresa, adm);

        // termo isolado: PDF de uma página só com o termo
        var termo = await db.App.Livros.TermoIsoladoAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), Termos with { Folhas = 12 }, TipoTermo.Abertura, false, adm, ct);
        Assert.True(termo.Sucesso, string.Join("; ", termo.Erros));
        Assert.Equal(1, termo.Valor.Paginas);

        // livro completo com assinaturas: mais páginas que o livro só com os termos
        var so_termos = await db.App.Livros.LivroAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), Termos,
            new OpcoesLivro(Diario: false, Razao: false, Balancete: false, Balanco: false, Dre: false, Dmpl: false, Dlpa: false), adm, ct);
        var completo = await db.App.Livros.LivroAsync(empresa, new(2026, 1, 1), new(2026, 12, 31), Termos,
            new OpcoesLivro(Assinaturas: new OpcoesAssinatura(true, true)), adm, ct);
        Assert.True(so_termos.Sucesso && completo.Sucesso, string.Join("; ", so_termos.Erros.Concat(completo.Erros)));
        Assert.Equal(2, so_termos.Valor.Paginas);                                           // abertura + encerramento
        Assert.True(completo.Valor.Paginas > so_termos.Valor.Paginas + 4);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(completo.Valor.Conteudo, 0, 4));

        // o mesmo bloco de assinaturas vale para qualquer relatório (opção comum do gerador)
        var gerador = new GeradorPdf();
        var simples = new DocumentoRelatorio("RELATÓRIO", ["Empresa"], [new Bloco(Texto: "Conteúdo")]);
        var comAssinatura = gerador.Gerar(simples with { Assinaturas = ["Ana Sócia\nSócia — CPF 529.982.247-25"] });
        Assert.True(comAssinatura.Conteudo.Length > gerador.Gerar(simples).Conteudo.Length);
        Assert.Equal(1, comAssinatura.Paginas);
    }

    [Fact]
    public async Task Servico_de_assinaturas_usa_os_cadastros_da_empresa()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("27865757000102");
        var adm = await db.AdminAsync();

        Assert.Empty((await db.App.Assinaturas.MontarAsync(empresa, OpcoesAssinatura.Nenhuma, ct)).Valor);
        Assert.False((await db.App.Assinaturas.MontarAsync(empresa, new OpcoesAssinatura(Responsavel: true), ct)).Sucesso);
        Assert.False((await db.App.Assinaturas.MontarAsync(empresa, new OpcoesAssinatura(Contador: true), ct)).Sucesso);

        await CadastrarResponsaveisAsync(empresa, adm);
        var contador = await db.App.Assinaturas.MontarAsync(empresa, new OpcoesAssinatura(Contador: true), ct);
        Assert.Single(contador.Valor);
        Assert.StartsWith("Beltrana Contadora", contador.Valor[0]);
        var ambos = await db.App.Assinaturas.MontarAsync(empresa, new OpcoesAssinatura(true, true), ct);
        Assert.Equal(2, ambos.Valor.Count);
        Assert.StartsWith("Ana Sócia", ambos.Valor[0]);
    }

    private async Task CadastrarResponsaveisAsync(long empresa, long adm)
    {
        var ct = TestContext.Current.CancellationToken;
        var socio = await db.App.Responsaveis.SalvarSocioAsync(empresa,
            new Socio(0, "Ana Sócia", 'F', "529.982.247-25", null, "Sócia-Administradora", null, 100m, new(2020, 1, 1), null, true, null, null), adm, ct);
        Assert.True(socio.Sucesso, string.Join("; ", socio.Erros));
        var contador = await db.App.Responsaveis.VincularContadorAsync(empresa,
            new Contador(0, "Beltrana Contadora", "390.533.447-05", "654321/O-1", "SP", null, null, null), new(2026, 1, 1), true, adm, ct);
        Assert.True(contador.Sucesso, string.Join("; ", contador.Erros));
    }
}
