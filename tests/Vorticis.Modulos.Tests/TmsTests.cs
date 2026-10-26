using Vorticis.Frota;
using Vorticis.Infra.Tests;

namespace Vorticis.Modulos.Tests;

public class RegrasTmsTests
{
    internal static readonly ParadaRota Campinas = new("SP", "3509502", "Campinas");
    internal static readonly ParadaRota Rio = new("RJ", "3304557", "Rio de Janeiro");

    internal static TabelaFrete Tabela(decimal icms = 12m) =>
        new(0, 1, "SP x RJ", null, "SP", "RJ", ValorKg: 0.50m, FreteMinimo: 150m, FatorCubagem: 300m, AdValoremPct: 0.3m, GrisPct: 0.2m, PedagioFracao: 5m,
            TaxaColeta: 30m, TaxaEntrega: 40m, OutrasTaxas: 0m, AliquotaIcms: icms, PrazoDias: 3);

    /// <summary>Chave de acesso válida montada como a SEFAZ: UF, AAMM, CNPJ, modelo, série, número, tipo de emissão, código e dígito verificador.</summary>
    internal static string Chave(string cnpj, string modelo, int serie, int numero)
    {
        var c43 = $"352603{cnpj}{modelo}{serie:000}{numero:000000000}1{12345678}";
        return c43 + RegrasTms.DigitoChave(c43);
    }

    [Fact]
    public void Frete_pela_tabela_conferido_a_mao()
    {
        // 1.000 kg em 4 m³: cubado 4 × 300 = 1.200 kg (maior que o real) → frete-peso 1.200 × 0,50 = 600,00
        // ad valorem 0,3% e GRIS 0,2% de 50.000,00 = 150,00 e 100,00; pedágio 12 frações de 100 kg × 5,00 = 60,00; coleta 30 + entrega 40 = 70,00
        // soma 980,00; ICMS 12% por dentro: 980 / 0,88 = 1.113,64 e ICMS = 133,64
        var f = RegrasTms.Calcular(Tabela(), 1000m, 4m, 50000m, coleta: true, entrega: true);
        Assert.Equal((1200m, 600m, 150m, 100m, 60m, 70m), (f.PesoTaxado, f.FretePeso, f.AdValorem, f.Gris, f.Pedagio, f.Taxas));
        Assert.Equal((980m, 133.64m, 1113.64m), (f.SemIcms, f.Icms, f.Total));
        // sem ICMS (alíquota zero informada): o total é a soma
        Assert.Equal(980m, RegrasTms.Calcular(Tabela(), 1000m, 4m, 50000m, true, true, aliquotaIcms: 0).Total);
        // frete mínimo: 100 kg × 0,50 = 50 < 150; 101 kg já são 2 frações de pedágio; sem coleta nem entrega não cobra as taxas
        var min = RegrasTms.Calcular(Tabela(0), 101m, 0m, 0m, false, false);
        Assert.Equal((150m, 10m, 0m, 160m), (min.FretePeso, min.Pedagio, min.Taxas, min.Total));
        Assert.Equal(0m, RegrasTms.Calcular(Tabela(0), 0m, 0m, 0m, false, false).Pedagio);
    }

    [Fact]
    public void Tabela_atende_pela_rota_ou_pelas_UFs()
    {
        var t = Tabela();
        Assert.True(RegrasTms.Atende(t, "SP", "RJ"));
        Assert.False(RegrasTms.Atende(t, "SP", "MG"));
        Assert.True(RegrasTms.Atende(t with { UfDestino = null }, "sp", "MG"));       // UF em branco vale para qualquer uma
        Assert.True(RegrasTms.Atende(t with { RotaId = 7 }, "BA", "PE", 7));             // com rota, vale a rota
        Assert.False(RegrasTms.Atende(t with { RotaId = 7 }, "SP", "RJ", 8));
        Assert.False(RegrasTms.Atende(t with { Ativa = false }, "SP", "RJ"));
        Assert.Empty(RegrasTms.Validar(t));
        Assert.Contains(RegrasTms.Validar(t with { ValorKg = 0, FreteMinimo = 0 }), e => e.Contains("valor por kg"));
        Assert.Contains(RegrasTms.Validar(t with { UfOrigem = "XX" }), e => e.Contains("UF inválida"));
        Assert.Contains(RegrasTms.Validar(t with { GrisPct = 120 }), e => e.Contains("GRIS"));
    }

    [Fact]
    public void Chave_de_acesso_confere_digito_e_modelo()
    {
        // exemplo do manual da NF-e: 43 dígitos "5206043300991100250655012000000780026730161" → DV 5
        Assert.Equal(5, RegrasTms.DigitoChave("5206043300991100250655012000000780026730161"));
        var cte = Chave("19131243000197", "57", 1, 15);
        Assert.True(RegrasTms.ChaveValida(cte, RegrasTms.ModeloCte));
        Assert.False(RegrasTms.ChaveValida(cte, RegrasTms.ModeloMdfe));                         // modelo errado
        Assert.False(RegrasTms.ChaveValida(cte[..43] + (cte[43] == '9' ? '0' : (char)(cte[43] + 1))));   // dígito errado
        Assert.False(RegrasTms.ChaveValida(cte[..40]));
        Assert.True(RegrasTms.ChaveValida(string.Join(" ", cte.Chunk(4).Select(c => new string(c)))));   // espaços são ignorados
        Assert.Equal(["111", "222"], RegrasTms.ChavesNfe("111, 222\n111"));
    }

    [Fact]
    public void Cte_valida_partes_municipios_cfop_e_chaves_das_notas()
    {
        var c = NovoCte(1, 2, 3);
        Assert.Empty(RegrasTms.Validar(c));
        Assert.Contains(RegrasTms.Validar(c with { TomadorId = 0 }), e => e.Contains("tomador"));
        Assert.Contains(RegrasTms.Validar(c with { Destino = new ParadaRota("SP", "3304557", "Rio") }), e => e.Contains("destino"));
        Assert.Contains(RegrasTms.Validar(c with { Cfop = "1353" }), e => e.Contains("CFOP"));
        Assert.Empty(RegrasTms.Validar(c with { Cfop = "6353" }));
        Assert.Contains(RegrasTms.Validar(c with { ChavesNfe = "123" }), e => e.Contains("Chave de NF-e inválida"));
        Assert.Empty(RegrasTms.Validar(c with { ChavesNfe = Chave("11222333000181", "55", 1, 99) }));
        Assert.Contains(RegrasTms.Validar(c with { Frete = RegrasTms.ComIcms(0, 0, 0, 0, 0, 0) }), e => e.Contains("valor do frete"));
        Assert.Contains(RegrasTms.Validar(c with { PrevisaoEntrega = c.Emissao.AddDays(-1) }), e => e.Contains("previsão"));
    }

    internal static Cte NovoCte(long remetente, long destinatario, long tomador, long? cotacao = null) =>
        new(0, 1, 0, new DateOnly(2026, 3, 2), null, remetente, destinatario, tomador, Campinas, Rio, null, cotacao, null, null, null, null, "Peças", 10, 1000m, 4m, 50000m,
            null, RegrasTms.Calcular(Tabela(), 1000m, 4m, 50000m, true, true), null, null, null, "DIGITADO", null, null, null, null);

    [Fact]
    public void Ocorrencias_andam_a_situacao_e_prazo_e_conferido()
    {
        Assert.Equal("EM_TRANSITO", RegrasTms.SituacaoAposOcorrencia("EMITIDO", "SAIDA"));
        Assert.Equal("ENTREGUE", RegrasTms.SituacaoAposOcorrencia("EM_TRANSITO", "ENTREGUE"));
        Assert.Equal("EM_TRANSITO", RegrasTms.SituacaoAposOcorrencia("EM_TRANSITO", "TENTATIVA"));
        Assert.Null(RegrasTms.SituacaoAposOcorrencia("DIGITADO", "SAIDA"));
        Assert.Null(RegrasTms.SituacaoAposOcorrencia("ENTREGUE", "AVARIA"));
        Assert.True(RegrasTms.CteEditavel("DIGITADO"));
        Assert.False(RegrasTms.CteEditavel("EMITIDO"));

        var hoje = new DateOnly(2026, 3, 10);
        var prev = new DateOnly(2026, 3, 5);
        Assert.Equal("Atrasado", RegrasTms.SituacaoPrazo(prev, "EM_TRANSITO", null, hoje));
        Assert.Equal("No prazo", RegrasTms.SituacaoPrazo(prev, "EMITIDO", null, new DateOnly(2026, 3, 5)));
        Assert.Equal("Entregue no prazo", RegrasTms.SituacaoPrazo(prev, "ENTREGUE", new DateTime(2026, 3, 5, 18, 0, 0), hoje));
        Assert.Equal("Entregue com atraso", RegrasTms.SituacaoPrazo(prev, "ENTREGUE", new DateTime(2026, 3, 6, 9, 0, 0), hoje));
        Assert.Equal("Sem previsão", RegrasTms.SituacaoPrazo(null, "EMITIDO", null, hoje));
        Assert.Equal(new DateOnly(2026, 3, 5), RegrasTms.Previsao(new DateOnly(2026, 3, 2), 3));
        Assert.Null(RegrasTms.Previsao(new DateOnly(2026, 3, 2), 0));
    }

    [Fact]
    public void Manifesto_exige_veiculo_motorista_ufs_e_ctes()
    {
        var m = new Manifesto(0, 1, 0, new DateOnly(2026, 3, 2), "SP", "RJ", "MG", 1, 2, null, null, "ABERTO", null, null, [5]);
        Assert.Empty(RegrasTms.Validar(m));
        Assert.Contains(RegrasTms.Validar(m with { Ctes = [] }), e => e.Contains("ao menos um CT-e"));
        Assert.Contains(RegrasTms.Validar(m with { UfsPercurso = "MG, ZZ" }), e => e.Contains("ZZ"));
        Assert.Contains(RegrasTms.Validar(m with { Chave = Chave("19131243000197", "57", 1, 1) }), e => e.Contains("MDF-e"));
        Assert.Empty(RegrasTms.Validar(m with { Chave = Chave("19131243000197", "58", 1, 1) }));
    }

    [Fact]
    public void Minuta_do_cte_e_relatorios_saem_com_totais()
    {
        var c = NovoCte(1, 2, 3) with { Id = 9, Numero = 15, Situacao = "ENTREGUE", Tomador = "Indústria Alfa", Destinatario = "Comércio Beta",
                                        PrevisaoEntrega = new DateOnly(2026, 3, 5), EntregueEm = new DateTime(2026, 3, 6, 10, 0, 0) };
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(ServicoFrota.MinutaCtePdf(c, "Transportadora Teste", "Ana"), 0, 4));
        var cancelado = c with { Id = 10, Numero = 16, Situacao = "CANCELADO" };
        var porCliente = TabelasTms.Gerar(TabelasTms.TituloFretesCliente, [c, cancelado], new DateOnly(2026, 3, 10));
        Assert.Equal(2, porCliente.Linhas.Count);                                            // o cancelado não entra
        Assert.Equal(1113.64m, porCliente.Linhas[^1].Valores[5]);
        var prazo = TabelasTms.Gerar(TabelasTms.TituloPrazoEntrega, [c], new DateOnly(2026, 3, 10));
        Assert.Equal("Entregue com atraso", prazo.Linhas[0].Valores[^1]);
        Assert.Contains("0,0% no prazo", (string)prazo.Linhas[^1].Valores[^1]!);
        var lista = TabelasTms.Gerar(TabelasTms.TituloConhecimentos, [c, cancelado], new DateOnly(2026, 3, 10));
        Assert.Equal(3, lista.Linhas.Count);
        Assert.True(lista.Linhas[1].Destaque);                                               // cancelado em destaque
    }
}

public class TmsIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Cotacao_vira_cte_emitido_vai_no_manifesto_e_entregue_e_faturado()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        const string cnpj = "19131243000197";
        var empresa = await db.CriarEmpresaAsync(cnpj);
        var svc = ServicosFrota.De(db.App);
        var alfa = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome) VALUES (@e, 'C1', 'Indústria Alfa') RETURNING id", ("e", empresa));
        var beta = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome) VALUES (@e, 'C2', 'Comércio Beta') RETURNING id", ("e", empresa));
        var motorista = (await svc.GravarMotoristaAsync(empresa, new Motorista(0, 1, "Pedro Alves", "52998224725", null, null, null, null), adm, ct)).Valor;
        var veiculo = (await svc.GravarVeiculoAsync(empresa, new Veiculo(0, 1, "BRA2E19", null, "CAVALO", null, null, null, null, "DIESEL_S10", null, 0, null), adm, ct)).Valor;

        // tabela e cotação: o valor é recalculado pelo serviço
        Assert.False((await svc.GravarTabelaFreteAsync(empresa, RegrasTmsTests.Tabela() with { Descricao = "" }, adm, ct)).Sucesso);
        var tabela = (await svc.GravarTabelaFreteAsync(empresa, RegrasTmsTests.Tabela(), adm, ct)).Valor;
        var cot = await svc.GravarCotacaoAsync(empresa, new Cotacao(0, 0, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 15), alfa, tabela, "Campinas/SP", "Rio de Janeiro/RJ",
                                                                   1000m, 4m, 50000m, true, true, 1m, "ABERTA", null), adm, ct);
        Assert.True(cot.Sucesso, string.Join(" ", cot.Erros));
        Assert.Equal((1, 1113.64m), (cot.Valor.Numero, cot.Valor.ValorTotal));
        // cotação aberta ainda não vira CT-e
        Assert.False((await svc.GravarCteAsync(empresa, RegrasTmsTests.NovoCte(alfa, beta, alfa, cot.Valor.Id), adm, ct)).Sucesso);
        Assert.True((await svc.MudarSituacaoCotacaoAsync(empresa, cot.Valor.Id, "APROVADA", adm, ct)).Sucesso);
        Assert.False((await svc.MudarSituacaoCotacaoAsync(empresa, cot.Valor.Id, "RECUSADA", adm, ct)).Sucesso);

        // CT-e digitado a partir da cotação aprovada: número 1 da série, previsão pela tabela (3 dias), cotação convertida
        var cte = await svc.GravarCteAsync(empresa, RegrasTmsTests.NovoCte(alfa, beta, alfa, cot.Valor.Id) with { TabelaId = tabela }, adm, ct);
        Assert.True(cte.Sucesso, string.Join(" ", cte.Erros));
        Assert.Equal((1, new DateOnly(2026, 3, 5)), (cte.Valor.Numero, cte.Valor.PrevisaoEntrega));
        Assert.Equal("CONVERTIDA", (await svc.CotacoesAsync(empresa, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, adm, ct)).Single().Situacao);
        var segundo = (await svc.GravarCteAsync(empresa, RegrasTmsTests.NovoCte(alfa, beta, alfa), adm, ct)).Valor;
        Assert.Equal(2, segundo.Numero);
        Assert.True((await svc.ExcluirCteAsync(empresa, segundo.Id, adm, ct)).Sucesso);

        // emissão: a chave tem de ser deste CT-e (CNPJ, série e número)
        Assert.False((await svc.EmitirCteAsync(empresa, cte.Valor.Id, "123", null, adm, ct)).Sucesso);
        Assert.Contains("série e o número", (await svc.EmitirCteAsync(empresa, cte.Valor.Id, RegrasTmsTests.Chave(cnpj, "57", 1, 2), null, adm, ct)).Erros[0]);
        Assert.Contains("CNPJ", (await svc.EmitirCteAsync(empresa, cte.Valor.Id, RegrasTmsTests.Chave("11222333000181", "57", 1, 1), null, adm, ct)).Erros[0]);
        Assert.True((await svc.EmitirCteAsync(empresa, cte.Valor.Id, RegrasTmsTests.Chave(cnpj, "57", 1, 1), "135260000012345", adm, ct)).Sucesso);
        Assert.False((await svc.GravarCteAsync(empresa, cte.Valor with { Produto = "Outro" }, adm, ct)).Sucesso);    // emitido não se altera

        // manifesto: o CT-e entra e fica em trânsito; não pode ser cancelado enquanto o manifesto está aberto
        var man = await svc.GravarManifestoAsync(empresa, new Manifesto(0, 1, 0, new DateOnly(2026, 3, 2), "SP", "RJ", null, veiculo, motorista, null, null, "ABERTO", null, null,
                                                                        [cte.Valor.Id]), adm, ct);
        Assert.True(man.Sucesso, string.Join(" ", man.Erros));
        var de = new DateOnly(2026, 1, 1);
        var ate = new DateOnly(2026, 12, 31);
        Assert.Equal("EM_TRANSITO", (await svc.CtesAsync(empresa, de, ate, null, adm, ct)).Single().Situacao);
        Assert.Contains("em trânsito", (await svc.CancelarCteAsync(empresa, cte.Valor.Id, adm, ct)).Erros[0]);
        Assert.Empty(await svc.CtesParaManifestoAsync(empresa, 0, adm, ct));                     // já está num manifesto aberto
        Assert.Single(await svc.CtesParaManifestoAsync(empresa, man.Valor.Id, adm, ct));         // o próprio manifesto o vê
        var pdf = await svc.ManifestoPdfAsync(empresa, man.Valor.Id, "Transportadora", "Ana", adm, ct);
        Assert.True(pdf.Sucesso);

        // entrega: exige o recebedor e fecha o CT-e
        Assert.False((await svc.RegistrarOcorrenciaAsync(empresa, cte.Valor.Id, "ENTREGUE", new DateTime(2026, 3, 4, 15, 0, 0), null, " ", adm, ct)).Sucesso);
        Assert.False((await svc.RegistrarOcorrenciaAsync(empresa, cte.Valor.Id, "SAIDA", new DateTime(2026, 3, 1, 8, 0, 0), null, null, adm, ct)).Sucesso);   // antes da emissão
        Assert.Equal("ENTREGUE", (await svc.RegistrarOcorrenciaAsync(empresa, cte.Valor.Id, "ENTREGUE", new DateTime(2026, 3, 4, 15, 0, 0), "Doca 2", "Maria – RG 123", adm, ct)).Valor);
        var entregue = (await svc.CtesAsync(empresa, de, ate, null, adm, ct)).Single();
        Assert.Equal(("ENTREGUE", "Maria – RG 123"), (entregue.Situacao, entregue.Recebedor));
        Assert.Single(await svc.OcorrenciasAsync(empresa, cte.Valor.Id, adm, ct));
        Assert.False((await svc.RegistrarOcorrenciaAsync(empresa, cte.Valor.Id, "AVARIA", new DateTime(2026, 3, 5, 9, 0, 0), null, null, adm, ct)).Sucesso);
        Assert.True((await svc.EncerrarManifestoAsync(empresa, man.Valor.Id, new DateOnly(2026, 3, 4), adm, ct)).Sucesso);

        // fatura: só CT-e do tomador; gera a conta a receber; cancelada, libera o CT-e
        Assert.Empty(await svc.CtesAFaturarAsync(empresa, beta, adm, ct));
        Assert.False((await svc.GerarFaturaAsync(empresa, beta, [cte.Valor.Id], new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30), null, adm, ct)).Sucesso);
        var fat = await svc.GerarFaturaAsync(empresa, alfa, [cte.Valor.Id], new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30), null, adm, ct);
        Assert.True(fat.Sucesso, string.Join(" ", fat.Erros));
        Assert.Equal((1, 1113.64m), (fat.Valor.Numero, fat.Valor.ValorTotal));
        Assert.Equal(1113.64m, await db.EscalarAsync<decimal>("SELECT saldo FROM fiscal.duplicata WHERE id = @d AND tipo = 'RECEBER' AND participante_id = @p",
                                                              ("d", fat.Valor.DuplicataId!.Value), ("p", alfa)));
        Assert.Empty(await svc.CtesAFaturarAsync(empresa, alfa, adm, ct));
        Assert.Contains("entregue", (await svc.CancelarCteAsync(empresa, cte.Valor.Id, adm, ct)).Erros[0]);
        Assert.True((await svc.FaturaPdfAsync(empresa, fat.Valor, "Transportadora", "Ana", adm, ct)).Sucesso);
        var rel = await svc.RelatorioAsync(empresa, TabelasTms.TituloFaturas, de, ate, adm, ct);
        Assert.True(rel.Sucesso);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today) > new DateOnly(2026, 4, 30) ? "Vencida" : "A receber", rel.Valor.Linhas[0].Valores[^1]);
        Assert.Equal(1113.64m, rel.Valor.Linhas[0].Valores[6]);                                 // saldo a receber vindo da duplicata
        Assert.True((await svc.CancelarFaturaAsync(empresa, fat.Valor.Id, adm, ct)).Sucesso);
        Assert.Equal(0, await db.EscalarAsync<int>("SELECT count(*)::int FROM fiscal.duplicata WHERE id = @d", ("d", fat.Valor.DuplicataId!.Value)));
        Assert.Single(await svc.CtesAFaturarAsync(empresa, alfa, adm, ct));
        Assert.False((await svc.CancelarFaturaAsync(empresa, fat.Valor.Id, adm, ct)).Sucesso);
        Assert.True((await svc.RelatorioAsync(empresa, TabelasTms.TituloFretesCliente, de, ate, adm, ct)).Sucesso);
    }

    [Fact]
    public async Task Sem_a_funcao_FROTA_TMS_o_usuario_so_consulta()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("27183946000359");
        await db.EscalarAsync<int>("UPDATE core.empresa SET modulo_frota = TRUE WHERE id = @e RETURNING 1", ("e", empresa));
        var svc = ServicosFrota.De(db.App);
        // perfil padrão Consulta: tem FROTA_RELATORIOS, não tem FROTA_TMS
        var usuario = await db.EscalarAsync<long>("""
            INSERT INTO core.usuario (login, nome, senha_hash, perfil_id) VALUES ('consulta_tms', 'Consulta', 'x', (SELECT id FROM core.perfil WHERE nome = 'Consulta')) RETURNING id
            """);
        await db.EscalarAsync<int>("INSERT INTO core.usuario_empresa (usuario_id, empresa_id, perfil_id) SELECT @u, @e, perfil_id FROM core.usuario WHERE id = @u RETURNING 1",
                                   ("u", usuario), ("e", empresa));
        var r = await svc.GravarTabelaFreteAsync(empresa, RegrasTmsTests.Tabela(), usuario, ct);
        Assert.False(r.Sucesso);
        Assert.Contains("TMS", r.Erros[0]);
        Assert.True((await svc.GravarTabelaFreteAsync(empresa, RegrasTmsTests.Tabela(), adm, ct)).Sucesso);
        Assert.Single(await svc.TabelasFreteAsync(empresa, usuario, ct));
    }
}
