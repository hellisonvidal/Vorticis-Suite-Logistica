using Vorticis.Desktop.Comum.Ajuda;
using Vorticis.Desktop.Comum.Modulos;
using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Tests;

/// <summary>
/// Manual do usuário (docs/manual): embutido no casco, com índice e mapa tela → tópico (F1). Regra do projeto: toda função nova
/// ou alterada atualiza o manual no mesmo commit; estes testes falham se surgir tela sem tópico.
/// </summary>
public class ManualTests(SessaoHeadless headless)
{
    private static IEnumerable<ItemMenu> Folhas(IEnumerable<ItemMenu> itens) =>
        itens.SelectMany(i => i.Filhos is null ? [i] : Folhas(i.Filhos));

    private static MainViewModelBase[] Modulos() =>
        [new Vorticis.Desktop.ViewModels.MainViewModel(), new Vorticis.Fiscal.Desktop.MainViewModel(), new Vorticis.Folha.Desktop.MainViewModel(), new Vorticis.Erp.Desktop.MainViewModel(), new Vorticis.Frota.Desktop.MainViewModel(), new Vorticis.Logistica.Desktop.MainViewModel()];

    [Fact]
    public void Indice_carrega_todos_os_arquivos_embutidos_e_os_links_internos_existem()
    {
        var arquivos = ManualUsuario.Arquivos;
        Assert.Equal("index.md", arquivos[0].Nome);
        string[] esperados = ["index.md", "01-primeiros-passos.md", "02-cadastros-comuns.md", "03-contabil.md", "04-auditoria.md",
                              "05-fiscal.md", "06-folha.md", "07-obrigacoes.md", "08-configuracoes.md", "10-gerencial.md", "12-frota.md", "13-logistica.md"];
        Assert.Equal(esperados.Order(), arquivos.Select(a => a.Nome).Order());
        Assert.All(arquivos, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Titulo));
            Assert.True(a.Topicos.Count(t => t.Nivel == 2) >= 1 || a.Nome == "index.md", a.Nome);
        });
        // links "arquivo.md#secao" e "#secao" de todos os arquivos resolvem
        foreach (var a in arquivos)
        {
            var links = a.Blocos.SelectMany(b => b switch
            {
                BlocoParagrafo p => p.Trechos,
                BlocoLista l => l.Itens.SelectMany(i => i),
                BlocoTabela t => t.Cabecalho.Concat(t.Linhas.SelectMany(x => x)).SelectMany(c => c),
                _ => [],
            }).Where(t => t.Link is not null && !t.Link.Contains("://"));
            Assert.All(links, t => Assert.True(ManualUsuario.Resolver(t.Link!, a.Nome) is not null, $"{a.Nome}: link quebrado {t.Link}"));
        }
        Assert.All(arquivos.SelectMany(a => a.Blocos).OfType<BlocoParagrafo>(), p => Assert.DoesNotContain("**", MarkdownSimples.TextoPlano(p)));
    }

    [Fact]
    public void Toda_tela_registrada_tem_topico_no_manual()
    {
        foreach (var vm in Modulos())
        {
            var chaves = vm.ChavesDeTelas.Concat(Folhas(vm.Menus).Where(i => i.Chave is not null && i.Chave != TelasComuns.Sair).Select(i => i.Chave!)).Distinct();
            Assert.All(chaves, c => Assert.True(ManualUsuario.TopicoDaTela(c) is not null,
                $"{vm.Modulo.Nome}: a tela '{c}' não tem tópico em docs/manual/mapa.txt (ou o título da seção mudou)."));
        }
    }

    [Fact]
    public void Mapa_so_tem_telas_que_existem()
    {
        var existentes = Modulos().SelectMany(m => m.ChavesDeTelas).ToHashSet();
        Assert.All(ManualUsuario.Mapa.Keys, c => Assert.True(existentes.Contains(c), $"mapa.txt: tela inexistente '{c}'."));
    }

    [Fact]
    public void Menu_Ajuda_tem_o_manual_e_o_F1_abre_o_topico_da_tela_atual()
    {
        foreach (var vm in Modulos())
        {
            var ajuda = vm.Menus[^1];
            Assert.Contains(ajuda.Filhos!, i => i.Chave == TelasComuns.Manual && i.Atalho == "F1");
            Assert.Contains(vm.Atalhos, a => a.Gesto == "F1" && a.Chave == TelasComuns.ManualContextual);
        }
        var m = new ManualViewModel(Modulos()[0]);
        m.IrParaTela("balancete");
        Assert.Equal("03-contabil.md#balancete-de-verificacao", ManualUsuario.Mapa["balancete"]);
        Assert.Equal("relatorios-contabeis", m.Topico!.Ancora);        // "###" abre a seção "##" que o contém
        m.IrParaTela("tela-que-nao-existe");                            // sem tópico: mantém o atual
        Assert.Equal("relatorios-contabeis", m.Topico!.Ancora);
    }

    [Fact]
    public void Indice_filtra_pelo_modulo_busca_por_palavra_e_gera_html()
    {
        var m = new ManualViewModel(new Vorticis.Fiscal.Desktop.MainViewModel());
        m.Preparar();
        var arquivos = m.Itens.Where(i => i.EhArquivo).Select(i => i.Topico.Arquivo).ToList();
        Assert.Contains("05-fiscal.md", arquivos);
        Assert.DoesNotContain("03-contabil.md", arquivos);
        Assert.DoesNotContain("06-folha.md", arquivos);
        m.TodosModulos = true;
        Assert.Contains(m.Itens, i => i.Topico.Arquivo == "06-folha.md");

        m.Busca = "ferias abono";
        Assert.Contains(m.Itens, i => i.Topico.Ancora == "ferias");
        Assert.All(m.Itens, i => Assert.Equal(2, i.Topico.Nivel));
        m.Busca = "";

        string? aberto = null;
        m.AbrirArquivo = c => aberto = c;
        m.IrParaTela("efdicmsipi");
        m.AbrirNoNavegadorCommand.Execute(null);
        Assert.NotNull(m.UltimoHtml);
        var html = File.ReadAllText(m.UltimoHtml!);
        Assert.Contains("<h2 id=\"05-fiscal--sped-efd-icms-ipi\">", html);
        Assert.EndsWith("#05-fiscal--sped-efd-icms-ipi", aberto);
        Assert.Contains("<table>", html);
    }

    [Fact]
    public void Renderizador_do_markdown_cobre_titulos_listas_tabelas_e_negrito()
    {
        var blocos = MarkdownSimples.Analisar("# T\n\n**Negrito** e *itálico* com `cod` e [link](a.md#x)\n\n- um\n- dois\n\n1. a\n2. b\n\n| A | B |\n|---|---|\n| 1 | 2 |\n");
        Assert.Collection(blocos, b => Assert.IsType<BlocoTitulo>(b), b => Assert.IsType<BlocoParagrafo>(b), b => Assert.False(((BlocoLista)b).Ordenada),
                          b => Assert.True(((BlocoLista)b).Ordenada), b => Assert.Single(((BlocoTabela)b).Linhas));
        var p = (BlocoParagrafo)blocos[1];
        Assert.Contains(p.Trechos, t => t.Negrito && t.Texto == "Negrito");
        Assert.Contains(p.Trechos, t => t.Italico && t.Texto == "itálico");
        Assert.Contains(p.Trechos, t => t.Codigo && t.Texto == "cod");
        Assert.Contains(p.Trechos, t => t.Link == "a.md#x");
        Assert.Equal("13o-salario", MarkdownSimples.Ancora("13º salário"));
    }

    [Fact]
    public async Task Tela_do_manual_monta_o_conteudo_do_topico()
    {
        await headless.Sessao.Dispatch(() =>
        {
            var vm = new ManualViewModel(Modulos()[0]);
            vm.IrParaTela("lancamento");
            var tela = new Vorticis.Desktop.Comum.Views.ManualView { DataContext = vm };
            var janela = new Avalonia.Controls.Window { Content = tela, Width = 1000, Height = 700 };
            janela.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var conteudo = Assert.IsType<Avalonia.Controls.StackPanel>(tela.Conteudo.Content);
            Assert.True(conteudo.Children.Count > 5);
            vm.Navegar("08-configuracoes.md#backup-e-restauracao");     // link interno troca o conteúdo
            Assert.Equal("backup-e-restauracao", vm.Topico!.Ancora);
            janela.Close();
        }, CancellationToken.None);
    }
}
