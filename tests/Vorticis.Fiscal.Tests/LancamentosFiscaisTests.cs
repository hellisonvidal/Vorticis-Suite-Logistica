using System.IO.Compression;
using Vorticis.Application.Cadastros;
using Vorticis.Domain.Comum;
using Vorticis.Fiscal.Contabilizacao;
using Vorticis.Fiscal.Desktop.ViewModels;
using Vorticis.Fiscal.Documentos;
using Vorticis.Infra;

namespace Vorticis.Fiscal.Tests;

/// <summary>CNPJs válidos distintos para as empresas de teste (um banco por classe, CNPJ único por empresa).</summary>
internal static class CnpjsDeTeste
{
    public static string Valido(int semente)
    {
        var b = $"{10_000_000 + semente:D8}0001".Select(c => c - '0').ToArray();
        int Dv(int[] d)
        {
            var pesos = d.Length == 12 ? new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 } : new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
            var r = d.Select((x, i) => x * pesos[i]).Sum() % 11;
            return r < 2 ? 0 : 11 - r;
        }
        var d1 = Dv(b);
        var d2 = Dv([.. b, d1]);
        return string.Concat(b) + d1 + d2;
    }
}

internal sealed class ConsultaCnpjFalsa : IConsultaCnpj
{
    public int Chamadas;
    public string? Falhar;
    public Task<Resultado<DadosCnpj>> ConsultarAsync(string cnpj, CancellationToken ct)
    {
        Interlocked.Increment(ref Chamadas);
        if (cnpj == Falhar) return Task.FromResult(Resultado<DadosCnpj>.Falha("Serviço indisponível (teste)."));
        return Task.FromResult(Resultado<DadosCnpj>.Ok(new DadosCnpj(cnpj, "Fornecedor Público SA", "Forn Pub", "2062", "4751201", "SP", "3550308", "São Paulo",
            new DateOnly(2010, 1, 1), "ATIVA", false, false, "01310100", "Av. Paulista", "1000", "Sala 1", "Bela Vista", "contato@fornecedor.test", "1130000000",
            "teste", DateTimeOffset.Now)));
    }
}

internal sealed class ConsultaIeFalsa : IConsultaInscricaoEstadual
{
    public Task<Resultado<IReadOnlyList<InscricaoEstadualEncontrada>>> ConsultarAsync(string cnpj, CancellationToken ct) =>
        Task.FromResult(Resultado<IReadOnlyList<InscricaoEstadualEncontrada>>.Ok([new InscricaoEstadualEncontrada("SP", "123456789012", true, "teste", DateTimeOffset.Now)]));
}

internal sealed class ProgressoSincrono(Action<ProgressoImportacao> acao) : IProgress<ProgressoImportacao>
{
    public void Report(ProgressoImportacao v) => acao(v);
}

/// <summary>Fornecedor novo vindo do XML (importação em arquivo ou pela chave) completado pela consulta pública do CNPJ.</summary>
public class CadastroPeloCnpjTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Importacao_de_xml_completa_o_fornecedor_novo_pela_consulta_do_cnpj_sem_trocar_os_dados_do_xml()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var emp = CnpjsDeTeste.Valido(31);
        var forn = CnpjsDeTeste.Valido(32);
        var empresa = await db.CriarEmpresaAsync(emp);
        var adm = await db.AdminAsync();
        var consulta = new ConsultaCnpjFalsa();
        await using var app = await VorticisApp.IniciarAsync(db.ConnectionString, consulta, ct, new ConsultaIeFalsa());
        var pasta = Directory.CreateTempSubdirectory("xml_forn").FullName;
        try
        {
            var arq = Path.Combine(pasta, "compra.xml");
            await File.WriteAllBytesAsync(arq, XmlsDeTeste.Bytes(XmlsDeTeste.Nfe(forn, emp, 1, 4321, new DateOnly(2026, 3, 10), [new ItemNfe("5102", 100m)],
                                                                                nomeEmit: "Nome do XML Ltda")), ct);
            var vm = new ImportacaoXmlViewModel(new ContextoDeTeste(app, adm, empresa));
            Assert.True(vm.CompletarPeloCnpj);
            await vm.ImportarArquivosAsync([arq]);
            Assert.Empty(vm.Erros);
            Assert.Equal(forn, Assert.Single(vm.Ultimo!.Arquivos).ParticipanteNovo);
            Assert.Contains("completado pela consulta do CNPJ", vm.Resumo);
            var p = Assert.Single(await app.Participantes.ListarAsync(empresa, adm, ct), x => x.CpfCnpj == forn);
            // o nome e a UF vêm do XML; o que faltava vem da consulta (fantasia, endereço, contato)
            Assert.Equal(("Nome do XML Ltda", "Forn Pub", "Av. Paulista", "Bela Vista", "contato@fornecedor.test"), (p.Nome, p.NomeFantasia, p.Logradouro, p.Bairro, p.Email));
            Assert.True(p.Fornecedor);
            Assert.Equal(1, consulta.Chamadas);
        }
        finally { Directory.Delete(pasta, true); }
    }
}

/// <summary>Importação em lote: pastas recursivas, .zip com subpastas, progresso, cancelamento, resumo exportável.</summary>
public class ImportacaoEmLoteTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static readonly DateOnly Mar = new(2026, 3, 10);

    private static byte[] Venda(string emp, int numero) =>
        XmlsDeTeste.Bytes(XmlsDeTeste.Nfe(emp, XmlsDeTeste.CnpjCliente, 1, numero, Mar, [new ItemNfe("5102", 10m + numero % 7)]));

    [Fact]
    public async Task Pasta_recursiva_e_zip_com_subpastas_com_progresso_e_resumo()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var emp = CnpjsDeTeste.Valido(1);
        var empresa = await db.CriarEmpresaAsync(emp);
        var adm = await db.AdminAsync();
        var svc = db.App.Fiscal().Documentos;
        var pasta = Directory.CreateTempSubdirectory("lote_xml").FullName;
        try
        {
            // 120 XMLs em subpastas fundas, 30 dentro de um .zip (com subpastas), 1 duplicado, 1 de terceiros e 1 ilegível
            for (var i = 1; i <= 120; i++)
            {
                var dir = Path.Combine(pasta, "a", $"b{i % 4}", "c");
                Directory.CreateDirectory(dir);
                await File.WriteAllBytesAsync(Path.Combine(dir, $"n{i}.xml"), Venda(emp, i), ct);
            }
            using (var zip = ZipFile.Open(Path.Combine(pasta, "lote.zip"), ZipArchiveMode.Create))
                for (var i = 1; i <= 30; i++)
                {
                    await using var s = zip.CreateEntry($"x/y{i % 3}/z{i}.xml").Open();
                    await s.WriteAsync(Venda(emp, 1000 + i), ct);
                }
            await File.WriteAllBytesAsync(Path.Combine(pasta, "dup.xml"), Venda(emp, 1), ct);
            await File.WriteAllBytesAsync(Path.Combine(pasta, "terceiros.xml"),
                XmlsDeTeste.Bytes(XmlsDeTeste.Nfe("99887766000155", "55443322000199", 1, 5000, Mar, [new ItemNfe("5102", 10m)])), ct);
            await File.WriteAllTextAsync(Path.Combine(pasta, "quebrado.xml"), "<x/>", ct);

            var todos = ServicoDocumentosFiscais.ListarArquivosDaPasta(pasta, true);
            Assert.Equal(124, todos.Count);   // 120 + dup + terceiros + quebrado + 1 zip
            Assert.Equal(153, ServicoDocumentosFiscais.ContarArquivos(todos));   // o zip conta pelos 30 XML de dentro
            Assert.Equal(4, ServicoDocumentosFiscais.ListarArquivosDaPasta(pasta, false).Count);   // sem subpastas: só a raiz

            var relatos = new List<ProgressoImportacao>();
            var r = await svc.ImportarPastaAsync(empresa, pasta, true, adm, new ProgressoSincrono(relatos.Add), ct);
            Assert.True(r.Sucesso, string.Join(";", r.Erros));
            var res = r.Valor;
            Assert.Equal((150, 1, 1, 1), (res.Importados, res.Duplicados, res.OutrasEmpresas, res.Erros));
            Assert.False(res.Interrompida);
            Assert.Equal(153, relatos[^1].Total);
            Assert.Equal(153, relatos[^1].Processados);
            Assert.Equal(relatos.Count, relatos.Select(x => x.Processados).Distinct().Count());   // andamento crescente
            Assert.Equal(150L, await db.EscalarAsync<long>("SELECT count(*) FROM fiscal.documento WHERE empresa_id = @e", ("e", empresa)));
            Assert.Contains(res.Arquivos, a => a.Arquivo == "lote.zip/x/y1/z1.xml" && a.Situacao == SituacaoImportacao.Importado);

            // Resumo: nome do arquivo e motivo de cada pendência, exportável em CSV
            var csv = res.ParaCsv();
            Assert.Contains("\"terceiros.xml\";\"Outra empresa\"", csv);
            Assert.Contains("não pertence à empresa", csv);
            Assert.Contains("\"quebrado.xml\";\"Erro\"", csv);
            Assert.Contains("\"dup.xml\";\"Já importado\"", csv);
            Assert.Contains("1 de outra empresa", res.Resumo);

            // Reimportar tudo: nada novo, nenhuma exceção
            var de_novo = (await svc.ImportarPastaAsync(empresa, pasta, true, adm, ct: ct)).Valor;
            Assert.Equal((0, 151), (de_novo.Importados, de_novo.Duplicados));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public async Task Cancelamento_mantem_o_que_foi_gravado_inclusive_entre_lotes()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var emp = CnpjsDeTeste.Valido(2);
        var empresa = await db.CriarEmpresaAsync(emp);
        var adm = await db.AdminAsync();
        var arquivos = Enumerable.Range(1, 260).Select(i => new ArquivoXml($"n{i}.xml", Venda(emp, 2000 + i))).ToList();
        using var cts = new CancellationTokenSource();
        var progresso = new ProgressoSincrono(p => { if (p.Processados == 230) cts.Cancel(); });   // depois do 1º lote de 200 já confirmado

        var r = await db.App.Fiscal().Documentos.ImportarAsync(empresa, arquivos, adm, progresso, cts.Token, total: arquivos.Count);
        Assert.True(r.Sucesso, string.Join(";", r.Erros));
        Assert.True(r.Valor.Interrompida);
        Assert.Equal((230, 30), (r.Valor.Importados, r.Valor.NaoProcessados));
        Assert.Contains("cancelada", r.Valor.Resumo);
        Assert.Equal(230L, await db.EscalarAsync<long>("SELECT count(*) FROM fiscal.documento WHERE empresa_id = @e", ("e", empresa)));
        Assert.Equal(230L, await db.EscalarAsync<long>("SELECT count(*) FROM fiscal.documento_item i JOIN fiscal.documento d ON d.id = i.documento_id WHERE d.empresa_id = @e", ("e", empresa)));
    }

    [Fact]
    public async Task Erro_de_um_arquivo_no_lote_nao_desfaz_os_demais_e_cancelamento_na_sequencia_e_aplicado()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var emp = CnpjsDeTeste.Valido(3);
        var empresa = await db.CriarEmpresaAsync(emp);
        var adm = await db.AdminAsync();
        var chave = XmlsDeTeste.Chave(3001);
        var r = await db.App.Fiscal().Documentos.ImportarAsync(empresa,
        [
            new ArquivoXml("a.xml", Venda(emp, 3001)),
            new ArquivoXml("lixo.xml", XmlsDeTeste.Bytes("<x/>")),
            new ArquivoXml("cancel.xml", XmlsDeTeste.Bytes(XmlsDeTeste.CancelamentoNfe(chave))),   // cancela a nota do mesmo lote
            new ArquivoXml("b.xml", Venda(emp, 3002)),
        ], adm, ct: ct);
        Assert.Equal((2, 1, 1), (r.Valor.Importados, r.Valor.Erros, r.Valor.Cancelamentos));
        Assert.Equal("CANCELADA", await db.EscalarAsync<string>("SELECT situacao FROM fiscal.documento WHERE empresa_id = @e AND chave_acesso = @c", ("e", empresa), ("c", chave)));
        Assert.Equal(2L, await db.EscalarAsync<long>("SELECT count(*) FROM fiscal.documento WHERE empresa_id = @e", ("e", empresa)));
    }

    [Fact]
    public async Task Tela_de_importacao_roda_em_segundo_plano_com_progresso_cancelar_e_resumo()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var emp = CnpjsDeTeste.Valido(4);
        var empresa = await db.CriarEmpresaAsync(emp);
        var adm = await db.AdminAsync();
        var ctx = new ContextoDeTeste(db.App, adm, empresa);
        var pasta = Directory.CreateTempSubdirectory("lote_tela").FullName;
        try
        {
            for (var i = 1; i <= 25; i++) await File.WriteAllBytesAsync(Path.Combine(pasta, $"n{i}.xml"), Venda(emp, 4000 + i), ct);
            var vm = new ImportacaoXmlViewModel(ctx) { Pasta = pasta, CompletarPeloCnpj = false };
            ResultadoImportacao? concluida = null;
            vm.Concluida += r => { concluida = r; return Task.CompletedTask; };
            var estados = new List<bool>();
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Importando)) estados.Add(vm.Importando); };
            Assert.False(vm.CancelarCommand.CanExecute(null));
            await vm.ImportarPastaCommand.ExecuteAsync(null);
            Assert.Equal([true, false], estados);   // ligou e desligou o "importando"
            Assert.False(vm.Importando);
            Assert.Equal(25, concluida!.Importados);
            Assert.Equal(100d, vm.Percentual);
            Assert.True(vm.TemResumo);
            Assert.Contains("25 importado(s)", vm.Resumo);
            Assert.Equal(25, vm.Resultados.Count);
            Assert.Contains("n1.xml", vm.ResumoCsv());

            // cancelar durante a importação: pede o cancelamento assim que há andamento
            for (var i = 1; i <= 25; i++) await File.WriteAllBytesAsync(Path.Combine(pasta, $"m{i}.xml"), Venda(emp, 4100 + i), ct);
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Importando) && vm.Importando) vm.CancelarCommand.Execute(null); };
            await vm.ImportarPastaCommand.ExecuteAsync(null);
            Assert.True(vm.Ultimo!.Interrompida);
            Assert.Contains("cancelada", vm.Resumo);
            Assert.Equal(vm.Ultimo.Importados, (int)await db.EscalarAsync<long>("SELECT count(*) FROM fiscal.documento WHERE empresa_id = @e AND numero LIKE '41%'", ("e", empresa)));
        }
        finally { Directory.Delete(pasta, true); }
    }
}

/// <summary>Lançamentos › Documentos fiscais: importar na tela, editar escrituração, excluir (com desfazer contabilização) e busca do participante.</summary>
public class LancamentosNotasTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static readonly DateOnly Mar = new(2026, 3, 10);

    private static async Task Aguardar(Func<bool> condicao, CancellationToken ct)
    {
        for (var i = 0; i < 200 && !condicao(); i++) await Task.Delay(25, ct);
        Assert.True(condicao(), "A condição esperada não ocorreu a tempo.");
    }

    [Fact]
    public async Task Importa_na_tela_edita_a_escrituracao_e_exclui_nota_importada()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var emp = CnpjsDeTeste.Valido(10);
        var empresa = await db.CriarEmpresaAsync(emp);
        var adm = await db.AdminAsync();
        var ctx = new ContextoDeTeste(db.App, adm, empresa);
        var pasta = Directory.CreateTempSubdirectory("lanc_xml").FullName;
        try
        {
            var entrada = Path.Combine(pasta, "compra.xml");
            var saida = Path.Combine(pasta, "venda.xml");
            await File.WriteAllBytesAsync(entrada, XmlsDeTeste.Bytes(XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, emp, 1, 801, Mar, [new ItemNfe("5102", 500m, 90m)], nomeEmit: "Fornecedor Alfa")), ct);
            await File.WriteAllBytesAsync(saida, Venda(emp, 802), ct);

            var vm = new NotaEntradaViewModel(ctx);
            await vm.CarregarAsync();
            vm.Importador.CompletarPeloCnpj = false;   // sem consulta pública na internet (coberta em CadastroPeloCnpjTests)
            await vm.Importador.ImportarArquivosAsync([entrada, saida]);   // pelo botão "Importar XML" da tela
            Assert.Empty(vm.Importador.Erros);
            var nota = Assert.Single(vm.Lista);   // a venda (saída) não aparece na tela de entrada
            Assert.Equal(("801", "XML"), (nota.Numero, nota.Origem));
            Assert.Contains("1 nota(s) de entrada importada(s)", vm.Mensagem);
            var vmSaida = new NotaSaidaViewModel(ctx);
            await vmSaida.CarregarAsync();
            await vmSaida.Importador.ImportarArquivosAsync([saida]);   // já importada: duplicada
            Assert.Equal(1, vmSaida.Importador.Ultimo!.Duplicados);
            vmSaida.FiltroInicio = new DateTime(2026, 3, 1);
            vmSaida.FiltroFim = new DateTime(2026, 3, 31);
            await vmSaida.ConsultarCommand.ExecuteAsync(null);
            Assert.Equal("802", Assert.Single(vmSaida.Lista).Numero);

            // selecionar abre a nota: dados do XML bloqueados, escrituração liberada
            await Aguardar(() => vm.Selecionada is not null && vm.Importada && vm.Itens.Count == 1, ct);
            Assert.False(vm.PodeEditar);
            Assert.True(vm.PodeEditarEscrituracao);
            Assert.Contains("não podem ser alterados", vm.AvisoImportada);
            Assert.Equal("5102", vm.Itens[0].Cfop);

            vm.SalvarCommand.Execute(null);
            await Aguardar(() => vm.Erros.Any(e => e.Contains("Gravar escrituração")), ct);

            // escrituração: CFOP de entrada, observação e contas
            vm.ItemSelecionado = vm.Itens[0];
            vm.Item.Cfop = "1102";
            await vm.AplicarEscrituracaoItemCommand.ExecuteAsync(null);
            Assert.Equal(("5102", "1102"), (vm.Itens[0].Cfop, vm.Itens[0].CfopEscrituracao));
            vm.Observacoes = "Conferida com o pedido 55";
            vm.ContaDebito = "1.1.04.001";
            vm.ContaCredito = "2.1.01.001";
            await vm.SalvarEscrituracaoCommand.ExecuteAsync(null);
            Assert.Empty(vm.Erros);
            Assert.Equal("Escrituração gravada.", vm.Mensagem);

            var dig = db.App.Fiscal().Digitacao;
            var gravada = (await dig.ObterAsync(empresa, nota.Id, adm, ct)).Valor;
            Assert.Equal(("Conferida com o pedido 55", "1.1.04.001", "2.1.01.001"), (gravada.Observacoes, gravada.ContaDebito, gravada.ContaCredito));
            Assert.Equal(("1102", "5102", 500m), (gravada.Itens[0].Cfop, gravada.Itens[0].CfopOriginal, gravada.Itens[0].ValorProduto));   // valores do XML intactos
            Assert.Equal(1_000_000m / 2_000m, gravada.Totais.Total);   // total do XML: 500 + 0 de IPI/ST

            // validações do serviço
            var esc = new EscrituracaoDocumento(nota.Id, null, null, null, null, [new EscrituracaoItem(1, "6102", null, null)]);
            Assert.Contains("não é de entrada", (await dig.GravarEscrituracaoAsync(empresa, esc, adm, ct)).Erros[0]);
            Assert.Contains("não encontrada", (await dig.GravarEscrituracaoAsync(empresa, esc with { ContaDebito = "9.9.9.9", Itens = [] }, adm, ct)).Erros[0]);
            Assert.Contains("não existe na nota", (await dig.GravarEscrituracaoAsync(empresa, esc with { Itens = [new EscrituracaoItem(7, "1102", null, null)] }, adm, ct)).Erros[0]);

            // o livro e a contabilização usam o CFOP de escrituração e as contas escolhidas
            var livro = (await db.App.Fiscal().Documentos.ResumoPorCfopAsync(empresa, TipoOperacao.Entrada, Mar, Mar, adm, ct)).Valor;
            Assert.Equal("1102", Assert.Single(livro).Cfop);
            var cont = db.App.Fiscal().Contabilizacao;
            var c = (await cont.ContabilizarAsync(empresa, Mar, Mar, TipoOperacao.Entrada, null, adm, ct)).Valor;
            Assert.Equal((1, 500m), (c.Contabilizados, c.Valor));   // sem nenhuma regra: valeram as contas da nota
            var lanc = (await db.App.Fiscal().Documentos.ConsultarAsync(empresa, new FiltroDocumentos(Texto: "801"), adm, ct)).Valor.Single();
            Assert.True(lanc.Contabilizado);
            Assert.Equal(2L, await db.EscalarAsync<long>("SELECT count(*) FROM contabil.partida WHERE lancamento_id = @l", ("l", lanc.LancamentoId!.Value)));

            // contabilizada: escrituração travada; excluir exige desfazer a contabilização (com confirmação)
            Assert.Contains("contabilizada", (await dig.GravarEscrituracaoAsync(empresa, esc with { Itens = [] }, adm, ct)).Erros[0]);
            var semDesfazer = await cont.ExcluirDocumentosAsync(empresa, [nota.Id], false, adm, ct);
            Assert.False(semDesfazer.Sucesso);
            Assert.Contains("Desfazer contabilização e excluir", semDesfazer.Erros[0]);

            await vm.ConsultarCommand.ExecuteAsync(null);
            vm.Selecionada = vm.Lista.Single();
            await Aguardar(() => vm.Contabilizado, ct);
            Assert.False(vm.PodeEditarEscrituracao);
            await vm.ExcluirCommand.ExecuteAsync(null);   // o contexto de teste confirma "Desfazer contabilização e excluir"
            Assert.Empty(vm.Erros);
            Assert.Empty(vm.Lista);
            Assert.Equal(0L, await db.EscalarAsync<long>("SELECT count(*) FROM fiscal.documento WHERE id = @d", ("d", nota.Id)));
            Assert.Equal("ESTORNADO", await db.EscalarAsync<string>("SELECT status::text FROM contabil.lancamento WHERE id = @l", ("l", lanc.LancamentoId.Value)));
        }
        finally { Directory.Delete(pasta, true); }
    }

    private static byte[] Venda(string emp, int numero) =>
        XmlsDeTeste.Bytes(XmlsDeTeste.Nfe(emp, XmlsDeTeste.CnpjCliente, 1, numero, Mar, [new ItemNfe("5102", 10m)]));

    [Fact]
    public async Task Exclui_nota_digitada_e_importada_nao_contabilizadas_pela_lista()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var emp = CnpjsDeTeste.Valido(11);
        var empresa = await db.CriarEmpresaAsync(emp);
        var adm = await db.AdminAsync();
        var ctx = new ContextoDeTeste(db.App, adm, empresa);
        await db.App.Fiscal().Documentos.ImportarAsync(empresa, [new ArquivoXml("v.xml", Venda(emp, 901))], adm, ct: ct);

        var vm = new NotaSaidaViewModel(ctx) { FiltroInicio = new DateTime(2026, 3, 1), FiltroFim = new DateTime(2026, 3, 31) };
        await vm.CarregarAsync();
        vm.FiltroInicio = new DateTime(2026, 3, 1);
        vm.FiltroFim = new DateTime(2026, 3, 31);
        await vm.ConsultarCommand.ExecuteAsync(null);
        Assert.Equal("901", Assert.Single(vm.Lista).Numero);
        Assert.False(vm.EditarCommand.CanExecute(null));   // nada selecionado
        vm.Selecionada = vm.Lista[0];
        Assert.True(vm.EditarCommand.CanExecute(null));
        await vm.EditarCommand.ExecuteAsync(null);
        Assert.True(vm.Importada);
        Assert.Contains("somente leitura", vm.Mensagem);
        await vm.ExcluirCommand.ExecuteAsync(null);
        Assert.Empty(vm.Lista);
        Assert.Equal("Nota excluída.", vm.Mensagem);
        Assert.Equal(0L, await db.EscalarAsync<long>("SELECT count(*) FROM fiscal.documento WHERE empresa_id = @e", ("e", empresa)));
    }

    [Fact]
    public async Task Busca_do_participante_cadastrados_base_publica_e_cpf()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var emp = CnpjsDeTeste.Valido(12);
        var empresa = await db.CriarEmpresaAsync(emp);
        var adm = await db.AdminAsync();
        var consulta = new ConsultaCnpjFalsa();
        await using var app = await VorticisApp.IniciarAsync(db.ConnectionString, consulta, ct, new ConsultaIeFalsa());
        var svc = app.Fiscal().ParticipantesNota;
        var pub = CnpjsDeTeste.Valido(20);

        Assert.Equal("52998224725", ServicoParticipanteNota.DocumentoDigitado("529.982.247-25"));
        Assert.Null(ServicoParticipanteNota.DocumentoDigitado("Fornecedor Alfa"));
        Assert.Null((await svc.BuscarAsync(empresa, "Fornecedor Alfa", TipoOperacao.Entrada, adm, ct)).Valor.Novo);   // texto comum: sem busca
        Assert.Contains("inválido", (await svc.BuscarAsync(empresa, "11.111.111/1111-11", TipoOperacao.Entrada, adm, ct)).Erros[0]);

        // não cadastrado: base pública preenche razão social, endereço, UF, município e IE
        var r = (await svc.BuscarAsync(empresa, Vorticis.Domain.Cadastros.Cnpj.Formatar(pub), TipoOperacao.Entrada, adm, ct)).Valor;
        Assert.Null(r.Cadastrado);
        var novo = r.Novo!;
        Assert.Equal(("Fornecedor Público SA", "SP", "São Paulo", "123456789012"), (novo.Nome, novo.Uf, novo.Municipio, novo.InscricaoEstadual));
        Assert.Equal(("Av. Paulista", "01310100", true, false), (novo.Logradouro, novo.Cep, novo.Fornecedor, novo.Cliente));
        Assert.Equal(1, consulta.Chamadas);

        // cadastra (fornecedor) e na próxima busca vem do cadastro, sem consultar a base pública
        var cad = (await svc.CadastrarAsync(empresa, novo, adm, ct)).Valor;
        Assert.Equal((pub, "1"), (cad.CpfCnpj, cad.Codigo));
        Assert.True(await db.EscalarAsync<bool>("SELECT fornecedor AND NOT cliente FROM core.participante WHERE id = @p", ("p", cad.Id)));
        var de_novo = (await svc.BuscarAsync(empresa, pub, TipoOperacao.Saida, adm, ct)).Valor;
        Assert.Equal(cad.Id, de_novo.Cadastrado!.Id);
        Assert.Equal(1, consulta.Chamadas);

        // consulta pública indisponível: segue com o CNPJ, nome em branco e aviso
        var fora = CnpjsDeTeste.Valido(99);
        consulta.Falhar = fora;
        var semBase = (await svc.BuscarAsync(empresa, fora, TipoOperacao.Saida, adm, ct)).Valor;
        Assert.Equal(("", true), (semBase.Novo!.Nome, semBase.Aviso!.Contains("consulta pública falhou")));
        Assert.True(semBase.Novo.Cliente);

        // CPF: aceito, sem consulta pública
        var antes = consulta.Chamadas;
        var cpf = (await svc.BuscarAsync(empresa, "529.982.247-25", TipoOperacao.Saida, adm, ct)).Valor;
        Assert.Equal(("52998224725", ""), (cpf.Novo!.CpfCnpj, cpf.Novo.Nome));
        Assert.Contains("CPF", cpf.Aviso);
        Assert.Equal(antes, consulta.Chamadas);
        Assert.Contains("nome", (await svc.CadastrarAsync(empresa, cpf.Novo, adm, ct)).Erros[0]);
        Assert.Contains("inválido", (await svc.BuscarAsync(empresa, "529.982.247-26", TipoOperacao.Saida, adm, ct)).Erros[0]);
    }

    [Fact]
    public async Task Digitacao_busca_o_cnpj_sozinha_e_cadastra_o_participante_ao_gravar_a_nota()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var emp = CnpjsDeTeste.Valido(13);
        var empresa = await db.CriarEmpresaAsync(emp);
        var adm = await db.AdminAsync();
        var consulta = new ConsultaCnpjFalsa();
        await using var app = await VorticisApp.IniciarAsync(db.ConnectionString, consulta, ct, new ConsultaIeFalsa());
        var ctx = new ContextoDeTeste(app, adm, empresa);
        var pub = CnpjsDeTeste.Valido(21);

        var vm = new NotaEntradaViewModel(ctx);
        await vm.CarregarAsync();
        vm.Numero = "55";
        vm.Emissao = vm.DataLivro = new DateTime(2026, 3, 10);
        vm.Participante = Vorticis.Domain.Cadastros.Cnpj.Formatar(pub);   // ao completar o CNPJ, a busca é automática
        await Aguardar(() => vm.TemNovoParticipante || vm.Erros.Count > 0, ct);
        Assert.True(vm.TemNovoParticipante, string.Join("; ", vm.Erros) + vm.ParticipanteInfo);
        Assert.Equal(("Fornecedor Público SA", "SP", "São Paulo", "123456789012"), (vm.NovoNome, vm.NovoUf, vm.NovoMunicipio, vm.NovoIe));
        Assert.Contains("será cadastrado ao gravar a nota", vm.ParticipanteInfo);
        Assert.Equal(0L, await db.EscalarAsync<long>("SELECT count(*) FROM core.participante WHERE empresa_id = @e AND cpf_cnpj = @d", ("e", empresa), ("d", pub)));

        vm.Item.Descricao = "Parafuso";
        vm.Item.Cfop = "1102";
        vm.Item.Quantidade = "2";
        vm.Item.Unitario = "10";
        await vm.AdicionarItemCommand.ExecuteAsync(null);
        Assert.Single(vm.Itens);
        await vm.SalvarCommand.ExecuteAsync(null);   // o contexto de teste confirma o cadastro
        Assert.Empty(vm.Erros);
        Assert.Equal("Nota gravada.", vm.Mensagem);
        Assert.Equal(1L, await db.EscalarAsync<long>("SELECT count(*) FROM core.participante WHERE empresa_id = @e AND cpf_cnpj = @d AND fornecedor", ("e", empresa), ("d", pub)));
        Assert.Equal("Fornecedor Público SA", Assert.Single(vm.Lista).Participante);

        // já cadastrado: a busca acha o participante, sem nova consulta pública
        vm.NovoCommand.Execute(null);
        var chamadas = consulta.Chamadas;
        vm.Participante = pub;
        await Aguardar(() => vm.ParticipanteInfo.Contains("cadastrado: "), ct);
        Assert.False(vm.TemNovoParticipante);
        Assert.Equal(chamadas, consulta.Chamadas);

        // CPF: pede o nome antes de cadastrar
        vm.NovoCommand.Execute(null);
        vm.Numero = "56";
        vm.Participante = "529.982.247-25";
        await vm.BuscarParticipanteCommand.ExecuteAsync(null);   // o que acontece ao sair do campo
        Assert.True(vm.TemNovoParticipante);
        Assert.Equal("", vm.NovoNome);
        vm.Item.Descricao = "Porca";
        vm.Item.Cfop = "1102";
        vm.Item.Unitario = "5";
        await vm.AdicionarItemCommand.ExecuteAsync(null);
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.Contains(vm.Erros, e => e.Contains("Informe o nome"));
        vm.NovoNome = "Maria Pessoa Física";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.Empty(vm.Erros);
        Assert.Equal("Maria Pessoa Física", await db.EscalarAsync<string>("SELECT nome FROM core.participante WHERE empresa_id = @e AND cpf_cnpj = '52998224725'", ("e", empresa)));
    }
}
