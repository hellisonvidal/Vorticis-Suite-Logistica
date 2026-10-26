using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Documentos;

namespace Vorticis.Fiscal.Tests;

public class RegrasDocumentosTests
{
    private static readonly DateOnly D = new(2026, 3, 10);

    private static DocumentoLido Doc(string emit, string? dest, int tpNf, ModeloDocumento modelo = ModeloDocumento.NFe55) =>
        new(modelo, "1", "1", "1", D, null, null, SituacaoDocumento.Autorizada, tpNf, new ParteDocumento(emit, "E", "SP"),
            dest is null ? null : new ParteDocumento(dest, "D", "RJ"), new TotaisDocumento(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0), [], []);

    [Fact]
    public void Identifica_entrada_e_saida_pelo_cnpj_da_empresa()
    {
        var meus = new[] { "61.695.227/0001-93", "61695227000274" };
        var saida = IdentificadorOperacao.Identificar(Doc("61695227000193", "11222333000181", 1), meus).Valor;
        Assert.Equal((TipoOperacao.Saida, "11222333000181"), (saida.Operacao, saida.Contraparte!.Documento));
        var entrada = IdentificadorOperacao.Identificar(Doc("45997418000153", "61695227000274", 1), meus).Valor;
        Assert.Equal((TipoOperacao.Entrada, "45997418000153"), (entrada.Operacao, entrada.Contraparte!.Documento));
        // nota de entrada emitida pela própria empresa (tpNF = 0): entrada, contraparte = remetente
        var propria = IdentificadorOperacao.Identificar(Doc("61695227000193", "45997418000153", 0), meus).Valor;
        Assert.Equal((TipoOperacao.Entrada, "45997418000153"), (propria.Operacao, propria.Contraparte!.Documento));
        // NFS-e prestada e tomada
        Assert.Equal(TipoOperacao.Saida, IdentificadorOperacao.Identificar(Doc("61695227000193", "11222333000181", 1, ModeloDocumento.NFSe) with { TipoNf = null }, meus).Valor.Operacao);
        Assert.Equal(TipoOperacao.Entrada, IdentificadorOperacao.Identificar(Doc("45997418000153", "61695227000193", 1, ModeloDocumento.NFSe) with { TipoNf = null }, meus).Valor.Operacao);
    }

    [Fact]
    public void Documento_de_terceiros_e_recusado()
    {
        var r = IdentificadorOperacao.Identificar(Doc("45997418000153", "11222333000181", 1), ["61695227000193"]);
        Assert.False(r.Sucesso);
        Assert.Contains("não pertence à empresa", r.Erros[0]);
    }

    [Theory]
    [InlineData("5102", true)]
    [InlineData("6101", true)]
    [InlineData("5405", true)]
    [InlineData("5933", true)]
    [InlineData("7101", true)]
    [InlineData("NFSE", true)]
    [InlineData("5152", false)]   // transferência
    [InlineData("5949", false)]
    [InlineData("5202", false)]   // devolução de compra
    [InlineData("1102", false)]
    public void Cfop_de_receita(string cfop, bool esperado) => Assert.Equal(esperado, Cfop.GeraReceita(cfop));

    [Fact]
    public void Cfop_sentido_devolucao_e_credito()
    {
        Assert.Equal(TipoOperacao.Entrada, Cfop.Operacao("2102"));
        Assert.Equal(TipoOperacao.Saida, Cfop.Operacao("6102"));
        Assert.Null(Cfop.Operacao("NFSE"));
        Assert.True(Cfop.EhDevolucaoDeVenda("1202"));
        Assert.True(Cfop.EhDevolucaoDeVenda("2411"));
        Assert.False(Cfop.EhDevolucaoDeVenda("1102"));
        Assert.True(Cfop.GeraCreditoPisCofins("1102"));
        Assert.True(Cfop.GeraCreditoPisCofins("2101"));
        Assert.False(Cfop.GeraCreditoPisCofins("1556"));   // uso e consumo
        Assert.True(Cfop.Casa("51", "5102"));
        Assert.False(Cfop.Casa("6", "5102"));
    }

    private static LinhaItemFiscal Linha(long doc, TipoOperacao op, string cfop, decimal valor, decimal icms = 0, SituacaoDocumento sit = SituacaoDocumento.Autorizada,
                                        string numero = "1", decimal ipi = 0) =>
        new(doc, ModeloDocumento.NFe55, op, numero, "1", D, D, sit, "Part", "123", "SP", "k" + doc, false,
            new ItemFiscal(1, null, null, null, null, cfop, null, 1, valor, valor, 0, 0, 0, 0, valor + ipi, null, null, icms > 0 ? valor : 0, 18, icms, 0, 0,
                null, 0, 0, ipi, null, 0, 0, 0, null, 0, 0, 0, 0, 0, 0));

    [Fact]
    public void Livro_agrupa_por_documento_e_cfop_e_zera_cancelados()
    {
        var itens = new[]
        {
            Linha(1, TipoOperacao.Saida, "5102", 100m, 18m, numero: "10"),
            Linha(1, TipoOperacao.Saida, "5102", 50m, 9m, numero: "10"),
            Linha(1, TipoOperacao.Saida, "5405", 30m, numero: "10"),
            Linha(2, TipoOperacao.Saida, "5102", 70m, 12.6m, SituacaoDocumento.Cancelada, "9"),
            Linha(3, TipoOperacao.Entrada, "1102", 999m),
        };
        var livro = LivrosFiscais.Montar(itens, TipoOperacao.Saida);
        Assert.Equal(3, livro.Count);
        Assert.Equal("9", livro[0].Numero);   // ordenado por número
        Assert.Equal(("CANCELADA", 0m), (livro[0].Observacao, livro[0].ValorContabil));
        var l5102 = livro.Single(l => l.Cfop == "5102" && l.Numero == "10");
        Assert.Equal((150m, 150m, 18m, 27m), (l5102.ValorContabil, l5102.BcIcms, l5102.AliqIcms, l5102.Icms));

        var resumo = LivrosFiscais.ResumirPorCfop(itens);
        Assert.Equal(["1102", "5102", "5405"], resumo.Select(r => r.Cfop));
        Assert.Equal((1, 150m), (resumo[1].Documentos, resumo[1].ValorContabil));   // o cancelado fica de fora
        Assert.Equal(1179m, LivrosFiscais.Total(resumo).ValorContabil);
    }

    [Fact]
    public void Receita_liquida_considera_so_cfop_de_receita_e_desconta_devolucoes()
    {
        var itens = new[]
        {
            Linha(1, TipoOperacao.Saida, "5102", 1000m, ipi: 50m),   // IPI não é receita
            Linha(2, TipoOperacao.Saida, "5152", 400m),               // transferência: fora
            Linha(3, TipoOperacao.Saida, "NFSE", 300m),
            Linha(4, TipoOperacao.Entrada, "1202", 100m),             // devolução de venda
            Linha(5, TipoOperacao.Saida, "5102", 500m, sit: SituacaoDocumento.Cancelada),
        };
        Assert.Equal(1200m, LivrosFiscais.ReceitaLiquida(itens));
    }
}

public class ImportacaoDocumentosTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private static readonly DateOnly Mar = new(2026, 3, 10);

    private static ArquivoXml Arq(string nome, string xml) => new(nome, XmlsDeTeste.Bytes(xml));

    [Fact]
    public async Task Importa_nfe_e_nfse_deduplica_vincula_participantes_e_aplica_cancelamento()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync(XmlsDeTeste.CnpjEmpresa);
        var adm = await db.AdminAsync();
        var svc = db.App.Fiscal().Documentos;

        var venda = Arq("venda.xml", XmlsDeTeste.Nfe(XmlsDeTeste.CnpjEmpresa, XmlsDeTeste.CnpjCliente, 1, 100, Mar,
            [new ItemNfe("5102", 1000m, 180m, Pis: 16.5m, Cofins: 76m), new ItemNfe("5405", 200m, Ipi: 10m)]));
        var compra = Arq("compra.xml", XmlsDeTeste.Nfe(XmlsDeTeste.CnpjFornecedor, XmlsDeTeste.CnpjEmpresa, 1, 200, Mar,
            [new ItemNfe("1102", 3000m, 540m)], nomeEmit: "Fornecedor Alfa SA"));
        var servico = Arq("servico.xml", XmlsDeTeste.Nfse(XmlsDeTeste.CnpjEmpresa, XmlsDeTeste.CnpjCliente, 15, Mar, 10_000m, iss: 500m));
        var terceiros = Arq("terceiros.xml", XmlsDeTeste.Nfe("99887766000155", "55443322000199", 1, 300, Mar, [new ItemNfe("5102", 10m)]));
        var lixo = Arq("lixo.xml", "<x/>");

        var r = await svc.ImportarAsync(empresa, [venda, compra, servico, terceiros, lixo, venda], adm, ct: ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var arquivos = r.Valor.Arquivos;
        Assert.Equal(3, r.Valor.Importados);
        Assert.Equal(1, r.Valor.Duplicados);
        Assert.Equal((1, 1), (r.Valor.Erros, r.Valor.OutrasEmpresas));
        Assert.Contains("não pertence à empresa", arquivos.Single(a => a.Arquivo == "terceiros.xml").Mensagem);
        Assert.Equal(TipoOperacao.Saida, arquivos.First(a => a.Arquivo == "venda.xml").Operacao);
        Assert.Equal(TipoOperacao.Entrada, arquivos.Single(a => a.Arquivo == "compra.xml").Operacao);

        // participantes criados e vinculados (o cliente serve a duas notas; um só cadastro)
        Assert.Equal(2L, await db.EscalarAsync<long>("SELECT count(*) FROM core.participante WHERE empresa_id = @e", ("e", empresa)));
        Assert.True(await db.EscalarAsync<bool>("SELECT cliente AND NOT fornecedor FROM core.participante WHERE empresa_id = @e AND cpf_cnpj = @d", ("e", empresa), ("d", XmlsDeTeste.CnpjCliente)));
        Assert.True(await db.EscalarAsync<bool>("SELECT fornecedor AND NOT cliente FROM core.participante WHERE empresa_id = @e AND cpf_cnpj = @d", ("e", empresa), ("d", XmlsDeTeste.CnpjFornecedor)));

        var todos = (await svc.ConsultarAsync(empresa, new FiltroDocumentos(), adm, ct)).Valor;
        Assert.Equal(3, todos.Count);
        var nfeVenda = todos.Single(d => d.Numero == "100");
        Assert.Equal((TipoOperacao.Saida, 1210m, "1"), (nfeVenda.Operacao, nfeVenda.Totais.Total, nfeVenda.ParticipanteCodigo));
        var itens = (await svc.ItensAsync(empresa, nfeVenda.Id, adm, ct)).Valor;
        Assert.Equal(["5102", "5405"], itens.Select(i => i.Cfop));

        // filtros
        Assert.Single((await svc.ConsultarAsync(empresa, new FiltroDocumentos(Operacao: TipoOperacao.Entrada), adm, ct)).Valor);
        Assert.Single((await svc.ConsultarAsync(empresa, new FiltroDocumentos(Modelo: ModeloDocumento.NFSe), adm, ct)).Valor);
        Assert.Single((await svc.ConsultarAsync(empresa, new FiltroDocumentos(Cfop: "1102"), adm, ct)).Valor);
        Assert.Single((await svc.ConsultarAsync(empresa, new FiltroDocumentos(Texto: "Fornecedor Alfa"), adm, ct)).Valor);
        Assert.Equal(2, (await svc.ConsultarAsync(empresa, new FiltroDocumentos(Texto: "11.222.333/0001-81"), adm, ct)).Valor.Count);
        Assert.Empty((await svc.ConsultarAsync(empresa, new FiltroDocumentos(Inicio: new DateOnly(2026, 4, 1)), adm, ct)).Valor);
        Assert.Equal(3, (await svc.ConsultarAsync(empresa, new FiltroDocumentos(Contabilizado: false), adm, ct)).Valor.Count);

        // livro de saídas: duas linhas da NF-e + uma da NFS-e
        var livro = (await svc.LivroAsync(empresa, TipoOperacao.Saida, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), adm, ct)).Valor;
        Assert.Equal(3, livro.Count);
        Assert.Equal(11_210m, livro.Sum(l => l.ValorContabil));
        var resumo = (await svc.ResumoPorCfopAsync(empresa, null, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), adm, ct)).Valor;
        Assert.Equal(["1102", "5102", "5405", "NFSE"], resumo.Select(x => x.Cfop));

        // XML original guardado e recuperável
        var xml = (await svc.ObterXmlAsync(empresa, nfeVenda.Id, adm, ct)).Valor;
        Assert.Equal(venda.Conteudo, xml);

        // cancelamento antes e depois da importação
        var chaveFutura = XmlsDeTeste.Chave(400);
        var antes = await svc.ImportarAsync(empresa, [Arq("canc.xml", XmlsDeTeste.CancelamentoNfe(chaveFutura))], adm, ct: ct);
        Assert.Equal(SituacaoImportacao.Ignorado, antes.Valor.Arquivos[0].Situacao);
        await svc.ImportarAsync(empresa, [Arq("n400.xml", XmlsDeTeste.Nfe(XmlsDeTeste.CnpjEmpresa, XmlsDeTeste.CnpjCliente, 1, 400, Mar, [new ItemNfe("5102", 70m)]))], adm, ct: ct);
        Assert.Equal(SituacaoDocumento.Cancelada, (await svc.ConsultarAsync(empresa, new FiltroDocumentos(Texto: "400"), adm, ct)).Valor.Single(d => d.Numero == "400").Situacao);

        var depois = await svc.ImportarAsync(empresa, [Arq("c100.xml", XmlsDeTeste.CancelamentoNfe(nfeVenda.Chave))], adm, ct: ct);
        Assert.Equal(SituacaoImportacao.Cancelado, depois.Valor.Arquivos[0].Situacao);
        Assert.Equal(SituacaoDocumento.Cancelada, (await svc.ConsultarAsync(empresa, new FiltroDocumentos(Texto: "100"), adm, ct)).Valor.Single(d => d.Numero == "100").Situacao);
        var livro2 = (await svc.LivroAsync(empresa, TipoOperacao.Saida, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), adm, ct)).Valor;
        Assert.Contains(livro2, l => l.Observacao == "CANCELADA" && l.ValorContabil == 0);

        // exclusão
        Assert.Equal(1, (await svc.ExcluirAsync(empresa, [nfeVenda.Id], adm, ct)).Valor);
        Assert.True((await db.App.Empresas.ExcluirAsync(empresa, ct)).Sucesso);
        Assert.Equal(0L, await db.EscalarAsync<long>("SELECT count(*) FROM fiscal.documento WHERE empresa_id = @e", ("e", empresa)));
    }

    [Fact]
    public async Task Importa_pasta_e_zip_e_respeita_permissoes()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var empresa = await db.CriarEmpresaAsync("11444777000161");
        var adm = await db.AdminAsync();
        var svc = db.App.Fiscal().Documentos;
        var pasta = Directory.CreateTempSubdirectory("fiscal_xml").FullName;
        try
        {
            var emp = "11444777000161";
            await File.WriteAllBytesAsync(Path.Combine(pasta, "a.xml"), XmlsDeTeste.Bytes(XmlsDeTeste.Nfe(emp, XmlsDeTeste.CnpjCliente, 1, 1, Mar, [new ItemNfe("5102", 10m)])), ct);
            Directory.CreateDirectory(Path.Combine(pasta, "sub"));
            await File.WriteAllBytesAsync(Path.Combine(pasta, "sub", "b.xml"), XmlsDeTeste.Bytes(XmlsDeTeste.Nfe(emp, XmlsDeTeste.CnpjCliente, 1, 2, Mar, [new ItemNfe("5102", 20m)])), ct);
            using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(pasta, "lote.zip"), System.IO.Compression.ZipArchiveMode.Create))
            {
                var e = zip.CreateEntry("c.xml");
                await using var s = e.Open();
                await s.WriteAsync(XmlsDeTeste.Bytes(XmlsDeTeste.Nfe(emp, XmlsDeTeste.CnpjCliente, 1, 3, Mar, [new ItemNfe("5102", 30m)])), ct);
            }
            var semSub = await svc.ImportarPastaAsync(empresa, pasta, false, adm, ct: ct);
            Assert.Equal(2, semSub.Valor.Importados);   // a.xml + c.xml (do zip)
            var comSub = await svc.ImportarPastaAsync(empresa, pasta, true, adm, ct: ct);
            Assert.Equal((1, 2), (comSub.Valor.Importados, comSub.Valor.Duplicados));
            Assert.False((await svc.ImportarPastaAsync(empresa, Path.Combine(pasta, "nao_existe"), false, adm, ct: ct)).Sucesso);
        }
        finally { Directory.Delete(pasta, true); }

        // usuário só com perfil Consulta: pode consultar, não pode importar nem ver livros sem a função
        var sessaoAdm = new SessaoUsuario(adm, "a", "A", true, false);
        var consulta = await db.PerfilIdAsync("Consulta");
        var uid = (await db.App.Usuarios.CriarUsuarioAsync(sessaoAdm, "fiscal_leitor", "Leitor", "Inicial123", consulta, ct)).Valor;
        await db.App.Usuarios.DefinirAcessoAsync(sessaoAdm, uid, empresa, consulta, ct);
        Assert.True((await svc.ConsultarAsync(empresa, new FiltroDocumentos(), uid, ct)).Sucesso);
        Assert.True((await svc.LivroAsync(empresa, TipoOperacao.Saida, Mar, Mar, uid, ct)).Sucesso);
        var negado = await svc.ImportarAsync(empresa, [], uid, ct: ct);
        Assert.Contains("não permite", negado.Erros[0]);
        Assert.False((await svc.ExcluirAsync(empresa, [1], uid, ct)).Sucesso);
        var semAcesso = await svc.ConsultarAsync(empresa, new FiltroDocumentos(), 987654, ct);
        Assert.Contains("acesso", semAcesso.Erros[0]);
    }
}
