using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Contabilizacao;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Tests;

public class MotorContabilizacaoTests
{
    private static RegraContabilizacao R(long id, string? cfop, TipoOperacao? op = null, long? part = null, int pri = 100, bool ativo = true, string hist = "H") =>
        new(id, pri, op, cfop, part, null, "1.1", "3.1", hist, ativo);

    [Fact]
    public void Escolhe_a_regra_mais_especifica()
    {
        var regras = new[]
        {
            R(1, null),                                  // qualquer
            R(2, "5"),                                   // prefixo de 1 dígito
            R(3, "510"),                                 // prefixo de 3 dígitos
            R(4, "5102"),                                // CFOP completo
            R(5, "5102", part: 77),                      // CFOP + participante
            R(6, "5102", TipoOperacao.Saida, pri: 1),    // CFOP + sentido
            R(7, "5102", ativo: false),
        };
        Assert.Equal(5, MotorContabilizacao.Escolher(regras, TipoOperacao.Saida, "5102", 77)!.Id);
        Assert.Equal(6, MotorContabilizacao.Escolher(regras, TipoOperacao.Saida, "5102", 1)!.Id);
        Assert.Equal(4, MotorContabilizacao.Escolher(regras, TipoOperacao.Entrada, "5102", 1)!.Id);
        Assert.Equal(3, MotorContabilizacao.Escolher(regras, TipoOperacao.Saida, "5101", null)!.Id);
        Assert.Equal(2, MotorContabilizacao.Escolher(regras, TipoOperacao.Saida, "5405", null)!.Id);
        Assert.Equal(1, MotorContabilizacao.Escolher(regras, TipoOperacao.Saida, "6102", null)!.Id);
        Assert.Null(MotorContabilizacao.Escolher([R(9, "1102")], TipoOperacao.Saida, "5102", null));
    }

    [Fact]
    public void Empate_e_decidido_pela_prioridade()
    {
        var regras = new[] { R(1, "5102", pri: 50), R(2, "5102", pri: 10) };
        Assert.Equal(2, MotorContabilizacao.Escolher(regras, TipoOperacao.Saida, "5102", null)!.Id);
    }

    private static DocumentoFiscal Doc(SituacaoDocumento sit = SituacaoDocumento.Autorizada) =>
        new(10, ModeloDocumento.NFe55, new string('1', 44), TipoOperacao.Saida, "321", "1", new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 10), null, sit,
            new ParteDocumento("1", "Eu", "SP"), new ParteDocumento("2", "Cliente X", "RJ"), 5, "7",
            new TotaisDocumento(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0), null, false);

    private static ItemFiscal Item(string cfop, decimal contabil) =>
        new(1, null, null, null, null, cfop, null, 1, contabil, contabil, 0, 0, 0, 0, contabil, null, null, 0, 0, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, null, 0, 0, 0, 0, 0, 0);

    [Fact]
    public void Monta_um_lancamento_por_nota_com_uma_dupla_de_partidas_por_cfop()
    {
        var regras = new[]
        {
            new RegraContabilizacao(1, 100, null, "5102", null, null, "1.1.02.001", "3.1.01.001", "NF {DOCUMENTO} - {PARTICIPANTE}"),
            new RegraContabilizacao(2, 100, null, "5405", null, null, "1.1.02.001", "3.1.01.002", "Venda ST NF {DOCUMENTO}"),
        };
        var p = MotorContabilizacao.Montar(1, Doc(), [Item("5102", 600m), Item("5102", 400m), Item("5405", 50.5m)], regras);
        Assert.True(p.Pronta, string.Join(";", p.Erros));
        var l = p.Lancamento!;
        Assert.Equal((new DateOnly(2026, 3, 10), Vorticis.Domain.Contabil.OrigemLancamento.NFe, 10L), (l.Data, l.Origem, l.DocumentoOrigemId));
        Assert.Equal(4, l.Partidas.Count);
        Assert.Equal(1000m, l.Partidas[0].Valor);
        Assert.Equal("NF 321 - Cliente X", l.Partidas[0].Historico);
        Assert.Equal("Venda ST NF 321", l.Partidas[2].Historico);
        Assert.All(l.Partidas, x => Assert.Equal(("321", "7"), (x.NumeroArquivo, x.CodigoParticipante)));
        Assert.Equal(l.Partidas.Where(x => x.DC == Vorticis.Domain.Contabil.IndicadorDC.D).Sum(x => x.Valor), l.Partidas.Where(x => x.DC == Vorticis.Domain.Contabil.IndicadorDC.C).Sum(x => x.Valor));
    }

    [Fact]
    public void Cfop_sem_regra_e_documento_cancelado_nao_geram_lancamento()
    {
        var sem = MotorContabilizacao.Montar(1, Doc(), [Item("5102", 10m)], []);
        Assert.False(sem.Pronta);
        Assert.Contains("nenhuma regra", sem.Erros[0]);
        var cancelado = MotorContabilizacao.Montar(1, Doc(SituacaoDocumento.Cancelada), [Item("5102", 10m)], []);
        Assert.Contains("não é contabilizado", cancelado.Erros[0]);
    }
}

public class ContabilizacaoFiscalTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static readonly DateOnly Mar = new(2026, 3, 10);
    private const string Cnpj = "00000000000191";

    private static ArquivoXml Arq(string nome, string xml) => new(nome, XmlsDeTeste.Bytes(xml));

    [Fact]
    public async Task Regras_contabilizacao_e_desfazer_geram_e_estornam_lancamentos()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync(Cnpj);
        var adm = await db.AdminAsync();
        var docs = db.App.Fiscal().Documentos;
        var cont = db.App.Fiscal().Contabilizacao;

        await docs.ImportarAsync(empresa,
        [
            Arq("v.xml", XmlsDeTeste.Nfe(Cnpj, XmlsDeTeste.CnpjCliente, 1, 100, Mar, [new ItemNfe("5102", 1000m), new ItemNfe("5405", 200m)])),
            Arq("c.xml", XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, Cnpj, 1, 200, Mar, [new ItemNfe("1102", 3000m)], nomeEmit: "Fornecedor Alfa")),
            Arq("s.xml", XmlsDeTeste.Nfse(Cnpj, XmlsDeTeste.CnpjCliente, 15, Mar, 10_000m, iss: 500m)),
        ], adm, ct: ct);
        var ini = new DateOnly(2026, 3, 1);
        var fim = new DateOnly(2026, 3, 31);

        // Regras: validações
        var invalida = await cont.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, null, "5102", null, null, "1.1.02.001", "1.1.02.001", "x {XYZ}"), adm, ct);
        Assert.Contains(invalida.Erros, e => e.Contains("diferentes"));
        Assert.Contains(invalida.Erros, e => e.Contains("Variável desconhecida"));
        Assert.Contains((await cont.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, null, "5102", null, null, "9.9.9", "3.1.01.001", "h"), adm, ct)).Erros, e => e.Contains("não encontrada"));
        Assert.Contains((await cont.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, null, "5102", null, null, "1.1", "3.1.01.001", "h"), adm, ct)).Erros, e => e.Contains("sintética"));
        Assert.Contains((await cont.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, null, "ABC", null, null, "1.1.02.001", "3.1.01.001", "h"), adm, ct)).Erros, e => e.Contains("CFOP"));

        var venda = await cont.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, TipoOperacao.Saida, "5102", null, null, "1.1.02.001", "3.1.01.001", RegraContabilizacao.HistoricoPadrao), adm, ct);
        Assert.True(venda.Sucesso, string.Join(";", venda.Erros));
        Assert.True((await cont.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, null, "1102", null, null, "1.1.04.001", "2.1.01.001", "Compra NF {DOCUMENTO} de {PARTICIPANTE}"), adm, ct)).Sucesso);
        Assert.True((await cont.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, null, "NFSE", null, null, "1.1.02.001", "3.1.01.003", "Serviço NFS-e {DOCUMENTO}"), adm, ct)).Sucesso);
        Assert.Equal(3, (await cont.ListarRegrasAsync(empresa, adm, ct)).Valor.Count);

        // Previsão: a nota com CFOP 5405 não tem regra
        var previsao = (await cont.PrevisaoAsync(empresa, ini, fim, null, null, adm, ct)).Valor;
        Assert.Equal(3, previsao.Count);
        Assert.Equal(2, previsao.Count(p => p.Pronta));
        // A nota de venda tem dois CFOP, um sem regra: fica pendente inteira
        var r1 = (await cont.ContabilizarAsync(empresa, ini, fim, null, null, adm, ct)).Valor;
        Assert.Equal(2, r1.Contabilizados);
        Assert.Equal(13_000m, r1.Valor);
        Assert.Equal("100", Assert.Single(r1.Falhas).Documento);

        var contabilizadas = (await docs.ConsultarAsync(empresa, new FiltroDocumentos(Contabilizado: true), adm, ct)).Valor;
        Assert.Equal(["15", "200"], contabilizadas.Select(d => d.Numero).OrderBy(x => x.Length).ToArray());
        var lancamentoCompra = contabilizadas.Single(d => d.Numero == "200").LancamentoId!.Value;
        Assert.Equal("NFE", await db.EscalarAsync<string>("SELECT origem::text FROM contabil.lancamento WHERE id = @l", ("l", lancamentoCompra)));
        Assert.Equal("EFETIVADO", await db.EscalarAsync<string>("SELECT status::text FROM contabil.lancamento WHERE id = @l", ("l", lancamentoCompra)));
        Assert.Equal("200", await db.EscalarAsync<string>("SELECT max(num_arquivo) FROM contabil.partida WHERE lancamento_id = @l", ("l", lancamentoCompra)));
        Assert.Equal("Compra NF 200 de Fornecedor Alfa", await db.EscalarAsync<string>("SELECT max(historico) FROM contabil.partida WHERE lancamento_id = @l", ("l", lancamentoCompra)));
        Assert.Equal(Mar, await db.EscalarAsync<DateOnly>("SELECT data_lancamento FROM contabil.lancamento WHERE id = @l", ("l", lancamentoCompra)));
        Assert.Equal("NFSE", await db.EscalarAsync<string>("SELECT l.origem::text FROM contabil.lancamento l JOIN fiscal.documento d ON d.lancamento_id = l.id WHERE d.numero = '15' AND d.empresa_id = @e", ("e", empresa)));

        // Já contabilizadas não entram de novo; excluir nota contabilizada é recusado
        Assert.Contains("Nenhuma nota pendente", (await cont.ContabilizarAsync(empresa, ini, fim, null, new HashSet<long> { contabilizadas[0].Id }, adm, ct)).Erros[0]);
        Assert.False((await docs.ExcluirAsync(empresa, [contabilizadas[0].Id], adm, ct)).Sucesso);

        // Nova regra para 5405 e contabiliza a nota pendente
        Assert.True((await cont.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, null, "54", null, null, "1.1.02.001", "3.1.01.001", "ST {DOCUMENTO}"), adm, ct)).Sucesso);
        var r2 = (await cont.ContabilizarAsync(empresa, ini, fim, null, null, adm, ct)).Valor;
        Assert.Equal((1, 1200m, 0), (r2.Contabilizados, r2.Valor, r2.Falhas.Count));

        // Desfazer: estorna o lançamento e libera a nota
        var todas = (await docs.ConsultarAsync(empresa, new FiltroDocumentos(Contabilizado: true), adm, ct)).Valor;
        Assert.Equal(3, todas.Count);
        var desfazer = (await cont.DesfazerAsync(empresa, [todas.Single(d => d.Numero == "200").Id], null, adm, ct)).Valor;
        Assert.Equal((1, 0), (desfazer.Contabilizados, desfazer.Falhas.Count));
        Assert.Equal("ESTORNADO", await db.EscalarAsync<string>("SELECT status::text FROM contabil.lancamento WHERE id = @l", ("l", lancamentoCompra)));
        Assert.Single((await docs.ConsultarAsync(empresa, new FiltroDocumentos(Contabilizado: false), adm, ct)).Valor);
        Assert.Single((await cont.PrevisaoAsync(empresa, ini, fim, null, null, adm, ct)).Valor);
        // ... e pode ser contabilizada de novo
        Assert.Equal(1, (await cont.ContabilizarAsync(empresa, ini, fim, null, null, adm, ct)).Valor.Contabilizados);

        // Saldo contábil: clientes (D) = 1.000 + 200 + 10.000; estoque 3.000 já com estorno + novo lançamento
        var saldoClientes = await db.EscalarAsync<decimal>("""
            SELECT COALESCE(SUM(CASE p.ind_dc WHEN 'D' THEN p.valor ELSE -p.valor END), 0)
              FROM contabil.partida p JOIN contabil.lancamento l ON l.id = p.lancamento_id JOIN contabil.plano_contas c ON c.id = p.conta_id
             WHERE l.empresa_id = @e AND c.codigo = '1.1.02.001' AND l.status <> 'ESTORNADO' AND l.origem <> 'ESTORNO'
            """, ("e", empresa));
        Assert.Equal(11_200m, saldoClientes);
    }

    [Fact]
    public async Task Permissoes_e_periodo_fechado()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("33000167000101");
        var adm = await db.AdminAsync();
        var sessaoAdm = new SessaoUsuario(adm, "a", "A", true, false);
        var cont = db.App.Fiscal().Contabilizacao;
        var consulta = await db.PerfilIdAsync("Consulta");
        var uid = (await db.App.Usuarios.CriarUsuarioAsync(sessaoAdm, "fiscal_consulta2", "Consulta", "Inicial123", consulta, ct)).Valor;
        await db.App.Usuarios.DefinirAcessoAsync(sessaoAdm, uid, empresa, consulta, ct);
        Assert.Contains("não permite", (await cont.GravarRegraAsync(empresa, new RegraContabilizacao(0, 1, null, "5102", null, null, "1.1.02.001", "3.1.01.001", "h"), uid, ct)).Erros[0]);
        Assert.Contains("não permite", (await cont.ContabilizarAsync(empresa, Mar, Mar, null, null, uid, ct)).Erros[0]);
        Assert.Contains("não permite", (await cont.DesfazerAsync(empresa, [1], null, uid, ct)).Erros[0]);

        // Período fechado: a nota vira pendência, não derruba as demais
        await db.App.Fiscal().Documentos.ImportarAsync(empresa,
            [Arq("x.xml", XmlsDeTeste.Nfe("33000167000101", XmlsDeTeste.CnpjCliente, 1, 5, Mar, [new ItemNfe("5102", 100m)]))], adm, ct: ct);
        Assert.True((await cont.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, null, "5102", null, null, "1.1.02.001", "3.1.01.001", "h"), adm, ct)).Sucesso);
        foreach (var mes in new[] { 1, 2, 3 })
            Assert.True((await db.App.Periodos.FecharAsync(empresa, new DateOnly(2026, mes, 1), adm, ct)).Sucesso);
        var r = (await cont.ContabilizarAsync(empresa, Mar, Mar, null, null, adm, ct)).Valor;
        Assert.Equal(0, r.Contabilizados);
        Assert.Contains("fechado", r.Falhas.Single().Erros[0]);
    }

    [Fact]
    public async Task Fechamento_do_periodo_fiscal_trava_as_notas_do_mes_e_so_vale_no_regime_normal()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var cnpj = CnpjsDeTeste.Valido(41);
        var empresa = await db.CriarEmpresaAsync(cnpj);   // Lucro Presumido
        var adm = await db.AdminAsync();
        var fech = db.App.Fiscal().Fechamento;
        var docs = db.App.Fiscal().Documentos;
        await docs.ImportarAsync(empresa, [Arq("a.xml", XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 11, Mar, [new ItemNfe("5102", 100m)]))], adm, ct: ct);

        Assert.Equal(1, (await fech.FecharAsync(empresa, Mar, adm, ct)).Valor);   // 1 nota ainda não contabilizada (aviso)
        var mar = (await fech.ListarAsync(empresa, 2026, adm, ct)).Valor.Single(m => m.Competencia.Month == 3);
        Assert.Equal((true, 1, 1), (mar.Fechado, mar.Notas, mar.NaoContabilizadas));
        // nota nova no mês fechado é recusada (com o motivo); a do mês seguinte entra
        var r = (await docs.ImportarAsync(empresa, [Arq("b.xml", XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 12, Mar, [new ItemNfe("5102", 50m)]))], adm, ct: ct)).Valor;
        Assert.Contains("está fechado", Assert.Single(r.Arquivos).Mensagem);
        var abr = new DateOnly(2026, 4, 2);
        Assert.Equal(1, (await docs.ImportarAsync(empresa, [Arq("c.xml", XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 13, abr, [new ItemNfe("5102", 50m)]))], adm, ct: ct)).Valor.Importados);
        var nota = (await docs.ConsultarAsync(empresa, new FiltroDocumentos(Mar, Mar), adm, ct)).Valor.Single();
        Assert.Contains("está fechado", (await db.App.Fiscal().Contabilizacao.ExcluirDocumentosAsync(empresa, [nota.Id], false, adm, ct)).Erros[0]);
        // contabilizar continua permitido no mês fechado
        Assert.True((await db.App.Fiscal().Contabilizacao.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, null, "5102", null, null, "1.1.02.001", "3.1.01.001", "h"), adm, ct)).Sucesso);
        Assert.Equal(1, (await db.App.Fiscal().Contabilizacao.ContabilizarAsync(empresa, Mar, Mar, null, null, adm, ct)).Valor.Contabilizados);

        Assert.True((await fech.ReabrirAsync(empresa, Mar, adm, ct)).Sucesso);
        Assert.False((await fech.ReabrirAsync(empresa, Mar, adm, ct)).Sucesso);
        Assert.Equal(1, (await docs.ImportarAsync(empresa, [Arq("b.xml", XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 12, Mar, [new ItemNfe("5102", 50m)]))], adm, ct: ct)).Valor.Importados);

        // Simples Nacional: o fechamento não se aplica
        await db.EscalarAsync<int>("WITH x AS (UPDATE core.empresa_regime SET regime = 'SIMPLES_NACIONAL' WHERE empresa_id = @e RETURNING 1) SELECT count(*)::int FROM x", ("e", empresa));
        Assert.Contains("regime normal", (await fech.FecharAsync(empresa, Mar, adm, ct)).Erros[0]);
    }

    [Fact]
    public async Task Contabilizacao_on_line_contabiliza_ao_importar_e_some_quando_desligada()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var cnpj = CnpjsDeTeste.Valido(42);
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var adm = await db.AdminAsync();
        var cont = db.App.Fiscal().Contabilizacao;
        Assert.False((await cont.ContabilizacaoOnlineAsync(empresa, adm, ct)).Valor);
        Assert.Null(await cont.ContabilizarSeOnlineAsync(empresa, Mar, Mar, null, adm, ct));   // em lote: não faz nada
        Assert.True((await cont.GravarRegraAsync(empresa, new RegraContabilizacao(0, 100, null, "5102", null, null, "1.1.02.001", "3.1.01.001", "h"), adm, ct)).Sucesso);
        Assert.True((await cont.GravarContabilizacaoOnlineAsync(empresa, true, adm, ct)).Sucesso);

        var pasta = Directory.CreateTempSubdirectory("online").FullName;
        try
        {
            var venda = Path.Combine(pasta, "venda.xml");
            var semRegra = Path.Combine(pasta, "outra.xml");
            await File.WriteAllBytesAsync(venda, XmlsDeTeste.Bytes(XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 21, Mar, [new ItemNfe("5102", 300m)])), ct);
            await File.WriteAllBytesAsync(semRegra, XmlsDeTeste.Bytes(XmlsDeTeste.Nfe(cnpj, XmlsDeTeste.CnpjCliente, 1, 22, Mar, [new ItemNfe("5949", 10m)])), ct);
            var vm = new Vorticis.Fiscal.Desktop.ViewModels.ImportacaoXmlViewModel(new ContextoDeTeste(db.App, adm, empresa)) { CompletarPeloCnpj = false };
            await vm.ImportarArquivosAsync([venda, semRegra]);
            Assert.Empty(vm.Erros);
            Assert.Contains("Contabilização on-line: 1 nota(s) contabilizada(s)", vm.Resumo);
            Assert.Contains("1 nota(s) ficaram pendentes", vm.Resumo);
            var lista = (await db.App.Fiscal().Documentos.ConsultarAsync(empresa, new FiltroDocumentos(Mar, Mar), adm, ct)).Valor;
            Assert.NotNull(lista.Single(d => d.Numero == "21").LancamentoId);
            Assert.Null(lista.Single(d => d.Numero == "22").LancamentoId);
        }
        finally { Directory.Delete(pasta, true); }

        // menu: com on-line ligado, "Contabilizar notas fiscais" some
        var modulo = new Vorticis.Fiscal.Desktop.ModuloFiscal();
        Assert.Contains(Folhas(modulo.Menus), i => i.Recurso == Vorticis.Fiscal.Desktop.ModuloFiscal.RecursoContabilizacaoLote);
    }

    private static IEnumerable<Vorticis.Desktop.Comum.Modulos.ItemMenu> Folhas(IEnumerable<Vorticis.Desktop.Comum.Modulos.ItemMenu> itens) =>
        itens.SelectMany(i => i.Filhos is null ? [i] : Folhas(i.Filhos));
}
