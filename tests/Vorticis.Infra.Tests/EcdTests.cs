using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Vorticis.Application.Cadastros;
using Vorticis.Application.Contabil;
using Vorticis.Application.Sped;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;
using Vorticis.Domain.Sped;
using Vorticis.Infra.Sped;

namespace Vorticis.Infra.Tests;

/// <summary>ECD (SPED Contábil) gerada de uma empresa com plano, lançamentos, centro de custo e encerramento; e assinada.</summary>
public class EcdTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private const string CpfContador = "52998224725";
    private const string CpfSocia = "39053344705";

    /// <summary>Plano referencial FICTÍCIO (só para o teste), com analíticas de todas as naturezas.</summary>
    private const string Referencial = """
        CODIGO|DESCRICAO|TIPO|COD_SUP|NIVEL|COD_NAT
        1|ATIVO|S||1|01
        1.01|Disponibilidades|A|1|2|01
        1.02|Outros ativos|A|1|2|01
        2|PASSIVO|S||1|02
        2.01|Obrigações|A|2|2|02
        2.03|PATRIMÔNIO LÍQUIDO|S|2|2|03
        2.03.01|Capital social|A|2.03|3|03
        2.03.02|Lucros ou prejuízos acumulados|A|2.03|3|03
        3|RESULTADO|S||1|04
        3.01|Receitas|A|3|2|04
        3.02|Custos e despesas|A|3|2|04
        """;

    private static X509Certificate2 Certificado(string cn)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={cn}, O=ICP-Brasil, C=BR", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx, "x"), "x", X509KeyStorageFlags.Exportable);
    }

    private async Task Lancar(long empresa, long adm, DateOnly data, string debito, string credito, decimal valor, string hist = "Teste",
                              string? centro = null)
        => Assert.True((await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, data, OrigemLancamento.Manual,
            [new(debito, IndicadorDC.D, valor, hist, centro), new(credito, IndicadorDC.C, valor, hist)]), adm, TestContext.Current.CancellationToken)).Sucesso);

    /// <summary>Empresa de 2025 com plano básico, contador, sócia, referencial, lançamentos (um com centro de custo) e encerramento.</summary>
    private async Task<(long Empresa, long Adm)> PrepararAsync(string cnpj, bool completa = true)
    {
        var ct = TestContext.Current.CancellationToken;
        var app = db.App;
        var empresa = await db.CriarEmpresaAsync(cnpj, 2025);
        var adm = await db.AdminAsync();
        await db.EscalarAsync<int>("UPDATE core.empresa SET nire = '35212345678', inscricao_estadual = '110042490114', municipio = 'SÃO PAULO' WHERE id = @e RETURNING 1",
                                   ("e", empresa));
        Assert.True((await app.PlanoContas.IncluirCentroAsync(empresa, "1", "ADMINISTRAÇÃO", null, adm, new DateOnly(2025, 1, 1), ct)).Sucesso);

        await Lancar(empresa, adm, new(2025, 1, 2), "1.1.01.002", "2.3.01.001", 50_000m, "Integralização do capital | sócia");
        await Lancar(empresa, adm, new(2025, 3, 10), "1.1.01.002", "3.1.01.003", 12_000m, "Serviços prestados – NF 12");
        await Lancar(empresa, adm, new(2025, 4, 5), "4.2.02.001", "1.1.01.002", 3_000m, "Aluguel de abril", "1");
        await Lancar(empresa, adm, new(2025, 4, 6), "3.2.01.005", "2.1.04.003", 600m, "ISS do mês");
        if (!completa) return (empresa, adm);

        Assert.True((await app.Encerramento.EncerrarAsync(empresa, 2025, new ContasEncerramento(), adm, ct)).Sucesso);

        var admin = new SessaoUsuario(adm, "admin_teste", "Admin", true, false);
        Assert.True((await app.PlanoReferencial.ImportarAsync(admin, "2", 2025, Encoding.Latin1.GetBytes(Referencial), "ref.txt", ct)).Sucesso);
        Assert.True((await app.PlanoReferencial.DefinirPlanoAsync(empresa, 2025, "2", adm, ct)).Sucesso);
        var mapa = (await app.PlanoReferencial.CarregarAsync(empresa, 2025, adm, ct)).Valor.Contas;
        var vinculos = mapa.ToDictionary(c => c.ContaId, c => (string?)(c.CodNat switch
        {
            "01" => c.Codigo.StartsWith("1.1.01") ? "1.01" : "1.02",
            "02" => "2.01",
            "03" => c.Codigo.StartsWith("2.3.01") ? "2.03.01" : "2.03.02",
            _ => c.Codigo.StartsWith('3') ? "3.01" : "3.02",
        }));
        var v = await app.PlanoReferencial.VincularAsync(empresa, 2025, vinculos, adm, ct);
        Assert.True(v.Sucesso, string.Join("; ", v.Erros));

        Assert.True((await app.Responsaveis.VincularContadorAsync(empresa, new Contador(0, "José da Silva Contador", CpfContador, "1SP123456", "SP",
            "contador@exemplo.com.br", "(11) 3333-4444", null), new DateOnly(2020, 1, 1), true, adm, ct)).Sucesso);
        Assert.True((await app.Responsaveis.SalvarSocioAsync(empresa, new Socio(0, "Maria Sócia", 'F', CpfSocia, null, "Sócio-Administrador", 49, 100m,
            null, null, true, "maria@exemplo.com.br", "11999998888"), adm, ct)).Sucesso);
        return (empresa, adm);
    }

    private static OpcoesEcd Opcoes => new(NumeroCertidaoCrc: "SP/2025/123456", ValidadeCertidaoCrc: new DateOnly(2026, 3, 31));

    private static List<string[]> Registros(byte[] arquivo, int tamanho)
    {
        var texto = Encoding.Latin1.GetString(arquivo, 0, tamanho);
        Assert.EndsWith("\r\n", texto);
        Assert.DoesNotContain("\n", texto.Replace("\r\n", ""));
        return texto[..^2].Split("\r\n").Select(l =>
        {
            Assert.StartsWith("|", l);
            Assert.EndsWith("|", l);
            return l[1..^1].Split('|');
        }).ToList();
    }

    private static decimal V(string s) => decimal.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);
    private static decimal Com(string valor, string ind) => ind == "D" ? V(valor) : -V(valor);

    [Fact]
    public async Task Pendencias_apontam_o_que_falta_antes_de_gerar()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (empresa, adm) = await PrepararAsync("11222333000181", completa: false);
        await db.EscalarAsync<int>("UPDATE core.empresa SET nire = '31212345678' WHERE id = @e RETURNING 1", ("e", empresa));   // NIRE de MG numa empresa de SP

        var previa = await db.App.Ecd.ValidarAsync(empresa, 2025, new OpcoesEcd(), adm, ct);
        Assert.True(previa.Sucesso, string.Join("; ", previa.Erros));
        var codigos = previa.Valor.Pendencias.Select(p => p.Codigo).ToHashSet();
        Assert.False(previa.Valor.PodeGerar);
        Assert.Contains("NAO_ENCERRADO", codigos);
        Assert.Contains("RESULTADO_NAO_ZERADO", codigos);
        Assert.Contains("SEM_CONTADOR", codigos);
        Assert.Contains("SEM_RESPONSAVEL", codigos);
        Assert.Contains("NIRE_UF", codigos);
        Assert.Contains("SEM_PLANO_REFERENCIAL", codigos);           // aviso
        Assert.Contains("PERIODOS_ABERTOS", codigos);                 // aviso
        Assert.Contains(previa.Valor.Pendencias, p => p.Codigo == "SEM_PLANO_REFERENCIAL" && !p.Bloqueante);

        var gerar = await db.App.Ecd.GerarAsync(empresa, 2025, new OpcoesEcd(), adm, ct: ct);
        Assert.False(gerar.Sucesso);
        Assert.Contains(gerar.Erros, e => e.Contains("não foi encerrado"));

        // Sem permissão: perfil Consulta
        var usuario = await db.EscalarAsync<long>("""
            INSERT INTO core.usuario (login, nome, senha_hash, perfil_id) VALUES ('consulta_ecd', 'Consulta', 'x', (SELECT id FROM core.perfil WHERE nome = 'Consulta'))
            RETURNING id
            """);
        Assert.False((await db.App.Ecd.ValidarAsync(empresa, 2025, new OpcoesEcd(), usuario, ct)).Sucesso);
    }

    [Fact]
    public async Task Gera_a_ecd_do_livro_G_com_estrutura_totalizadores_e_formatacao_do_leiaute_9()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (empresa, adm) = await PrepararAsync("33000167000101");

        var previa = (await db.App.Ecd.ValidarAsync(empresa, 2025, Opcoes, adm, ct)).Valor;
        Assert.True(previa.PodeGerar, string.Join("\n", previa.Bloqueantes));
        Assert.Equal((1, "2"), (previa.NumeroOrdem, previa.CodPlanRef));
        Assert.Equal(["900", "205"], previa.Signatarios.Select(s => s.CodigoQualificacao));

        var r = await db.App.Ecd.GerarAsync(empresa, 2025, Opcoes, adm, ct: ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var arq = r.Valor;
        Assert.Empty(arq.Assinaturas);
        Assert.Equal(Convert.ToHexString(SHA1.HashData(arq.Conteudo)), arq.HashSha1);
        var regs = Registros(arq.Conteudo, arq.Conteudo.Length);
        var texto = Encoding.Latin1.GetString(arq.Conteudo);

        // ISO-8859-1: "Ç" e "Ã" em um byte; nada de "|" nem travessão dentro dos campos
        Assert.Contains((byte)0xC3, arq.Conteudo);                                   // Ã de "SÃO PAULO"
        Assert.Contains("|Integralização do capital - sócia|", texto);
        Assert.Contains("|Serviços prestados - NF 12|", texto);

        // Abertura e identificação
        Assert.Equal("0000|LECD|01012025|31122025|Empresa de Teste LTDA|33000167000101|SP|110042490114|3550308|||0|1|0||0|0||N|N|0|0|2",
                     string.Join('|', regs[0]));
        Assert.Equal(["0007", "SP", "110042490114"], regs.Single(x => x[0] == "0007"));
        Assert.Equal(["I010", "G", "9.00"], regs.Single(x => x[0] == "I010"));
        var i030 = regs.Single(x => x[0] == "I030");
        Assert.Equal(["I030", "TERMO DE ABERTURA", "1", "DIÁRIO GERAL", regs.Count.ToString(), "Empresa de Teste LTDA", "35212345678",
                      "33000167000101", "01012020", "", "SÃO PAULO", "31122025"], i030);
        var j900 = regs.Single(x => x[0] == "J900");
        Assert.Equal(["J900", "TERMO DE ENCERRAMENTO", "1", "DIÁRIO GERAL", "Empresa de Teste LTDA", regs.Count.ToString(), "01012025", "31122025"], j900);

        // Ordem dos blocos e contadores
        Assert.Equal("0000", regs[0][0]);
        Assert.Equal(["9999", regs.Count.ToString()], regs[^1]);
        string Bloco(string reg) => reg[..1];
        var ordemBlocos = regs.Select(x => Bloco(x[0])).Distinct().ToList();
        Assert.Equal(["0", "I", "J", "9"], ordemBlocos);
        Assert.Equal((regs.Count(x => Bloco(x[0]) == "0")).ToString(), regs.Single(x => x[0] == "0990")[1]);
        Assert.Equal((regs.Count(x => Bloco(x[0]) == "I")).ToString(), regs.Single(x => x[0] == "I990")[1]);
        Assert.Equal((regs.Count(x => Bloco(x[0]) == "J")).ToString(), regs.Single(x => x[0] == "J990")[1]);
        Assert.Equal((regs.Count(x => Bloco(x[0]) == "9")).ToString(), regs.Single(x => x[0] == "9990")[1]);
        var contagem = regs.GroupBy(x => x[0]).ToDictionary(g => g.Key, g => g.Count());
        var r9900 = regs.Where(x => x[0] == "9900").ToDictionary(x => x[1], x => int.Parse(x[2]));
        Assert.Equal(contagem.OrderBy(k => k.Key).ToList(), r9900.OrderBy(k => k.Key).ToList());
        Assert.Equal(contagem, arq.Registros);
        Assert.DoesNotContain(regs, x => x[0].StartsWith('K') || x[0].StartsWith('C'));

        // Plano de contas: só contas com saldo/movimento e as superiores; superior antes da filha; I051 e I052 nas analíticas
        var i050 = regs.Where(x => x[0] == "I050").ToList();
        var codigos = i050.Select(x => x[5]).ToList();
        Assert.Contains("1.1.01.002", codigos);
        Assert.Contains("5.1.01.001", codigos);                       // apuração (movimentada no encerramento)
        Assert.DoesNotContain("1.1.01.001", codigos);                 // caixa, sem movimento
        foreach (var c in i050.Where(x => x[6] != "")) Assert.True(codigos.IndexOf(c[6]) < codigos.IndexOf(c[5]));
        var analiticas = i050.Count(x => x[3] == "A");
        Assert.Equal(analiticas, regs.Count(x => x[0] == "I051"));
        Assert.Equal(analiticas, regs.Count(x => x[0] == "I052"));
        Assert.Equal(["I100", "01012025", "1", "ADMINISTRAÇÃO"], regs.Single(x => x[0] == "I100"));

        // Saldos periódicos: 12 meses; cada mês fecha (débitos = créditos, saldos somam zero); final = inicial + D − C;
        // inicial do mês = final do anterior; débitos e créditos por conta = soma das partidas (I250) do mês
        var meses = new List<(string Ini, List<string[]> Linhas)>();
        var lancamentosPorMes = new Dictionary<string, Dictionary<string, (decimal D, decimal C)>>();
        string? dataLanc = null;
        foreach (var x in regs)
        {
            if (x[0] == "I150") meses.Add((x[1], []));
            else if (x[0] == "I155") meses[^1].Linhas.Add(x);
            else if (x[0] == "I200") dataLanc = x[2][2..];
            else if (x[0] == "I250")
            {
                var m = lancamentosPorMes.TryGetValue(dataLanc!, out var d) ? d : lancamentosPorMes[dataLanc!] = [];
                var (deb, cred) = m.GetValueOrDefault($"{x[1]}|{x[2]}");
                m[$"{x[1]}|{x[2]}"] = x[4] == "D" ? (deb + V(x[3]), cred) : (deb, cred + V(x[3]));
            }
        }
        Assert.Equal(12, meses.Count);
        Assert.Equal(Enumerable.Range(1, 12).Select(m => $"01{m:00}2025"), meses.Select(m => m.Ini));
        Dictionary<string, decimal> finalAnterior = [];
        foreach (var (ini, linhas) in meses)
        {
            Assert.Equal(0m, linhas.Sum(l => Com(l[3], l[4])));
            Assert.Equal(0m, linhas.Sum(l => Com(l[7], l[8])));
            Assert.Equal(linhas.Sum(l => V(l[5])), linhas.Sum(l => V(l[6])));
            foreach (var l in linhas)
            {
                Assert.Equal(Com(l[3], l[4]) + V(l[5]) - V(l[6]), Com(l[7], l[8]));
                Assert.Equal(finalAnterior.GetValueOrDefault($"{l[1]}|{l[2]}"), Com(l[3], l[4]));
                var mov = lancamentosPorMes.GetValueOrDefault(ini[2..]) ?? [];
                Assert.Equal(mov.GetValueOrDefault($"{l[1]}|{l[2]}"), (V(l[5]), V(l[6])));
            }
            foreach (var chave in finalAnterior.Keys.Where(k => finalAnterior[k] != 0)) Assert.Contains(linhas, l => $"{l[1]}|{l[2]}" == chave);
            finalAnterior = linhas.ToDictionary(l => $"{l[1]}|{l[2]}", l => Com(l[7], l[8]));
        }
        var abril = meses[3].Linhas.Single(l => l[1] == "4.2.02.001");
        Assert.Equal(["I155", "4.2.02.001", "1", "0,00", "D", "3000,00", "0,00", "3000,00", "D"], abril);

        // Lançamentos: valor = soma dos débitos; encerramento com IND_LCTO = E na data final
        var i200 = regs.Where(x => x[0] == "I200").ToList();
        Assert.Equal(6, i200.Count);
        Assert.Equal(["I200", "1", "02012025", "50000,00", "N", ""], i200[0]);
        Assert.All(i200.Where(x => x[4] == "E"), x => Assert.Equal("31122025", x[2]));
        Assert.Equal(2, i200.Count(x => x[4] == "E"));
        Assert.Contains(regs, x => x is ["I250", "4.2.02.001", "1", "3000,00", "D", "", "", "Aluguel de abril", ""]);

        // Antes do encerramento (I355): o resultado das contas e a apuração, zerados pelo E
        var i355 = regs.Where(x => x[0] == "I355").ToList();
        Assert.Equal(["I350", "31122025"], regs.Single(x => x[0] == "I350"));
        Assert.Contains(["I355", "3.1.01.003", "", "12000,00", "C"], i355);
        Assert.Contains(["I355", "4.2.02.001", "1", "3000,00", "D"], i355);
        Assert.Contains(["I355", "5.1.01.001", "", "0,00", "C"], i355);

        // Balanço: duas linhas de nível 1 (A e P) que fecham; totalizadores = soma das parcelas
        var j100 = regs.Where(x => x[0] == "J100").ToList();
        Assert.Equal(2, j100.Count(x => x[3] == "1"));
        var ativo = j100.Single(x => x[3] == "1" && x[5] == "A");
        var passivo = j100.Single(x => x[3] == "1" && x[5] == "P");
        Assert.Equal(59_000m, Com(ativo[9], ativo[10]));
        Assert.Equal(-59_000m, Com(passivo[9], passivo[10]));
        Assert.Equal(0m, Com(ativo[7], ativo[8]) + Com(passivo[7], passivo[8]));
        foreach (var t in j100.Where(x => x[2] == "T"))
        {
            var filhos = j100.Where(x => x[4] == t[1]).ToList();
            Assert.NotEmpty(filhos);
            Assert.Equal(Com(t[9], t[10]), filhos.Sum(f => Com(f[9], f[10])));
        }
        Assert.Equal(j100.Where(x => x[2] == "D" && x[5] == "A").Sum(x => Com(x[9], x[10])), -j100.Where(x => x[2] == "D" && x[5] == "P").Sum(x => Com(x[9], x[10])));
        var i052 = regs.Where(x => x[0] == "I052").Select(x => x[2]).ToHashSet();
        Assert.All(j100.Where(x => x[2] == "D"), x => Assert.Contains(x[1], i052));          // detalhe tem I052
        Assert.All(j100.Where(x => x[2] == "T"), x => Assert.DoesNotContain(x[1], i052));     // totalizador não
        Assert.Contains(j100, x => x is ["J100", "2.3.03", "D", "3", "2.3", "P", _, "0,00", "C", "8400,00", "C", ""]);

        // DRE: uma linha de nível 1 com o resultado (lucro de 8.400 = 12.000 − 3.000 − 600), soma das parcelas
        var j150 = regs.Where(x => x[0] == "J150").ToList();
        var raiz = Assert.Single(j150, x => x[4] == "1");
        Assert.Equal(("T", -8_400m, "R"), (raiz[3], Com(raiz[9], raiz[10]), raiz[11]));
        foreach (var t in j150.Where(x => x[3] == "T"))
            Assert.Equal(Com(t[9], t[10]), j150.Where(x => x[5] == t[2]).Sum(f => Com(f[9], f[10])));
        Assert.Equal(j150.Count, j150.Select(x => x[1]).Distinct().Count());
        Assert.All(j150, x => Assert.Equal(("", ""), (x[7], x[8])));                          // sem exercício anterior

        // DMPL: saldo final = inicial + créditos − débitos dos fatos (J215)
        var j210 = regs.Where(x => x[0] == "J210").ToList();
        Assert.NotEmpty(j210);
        Assert.All(j210, x => Assert.Equal("1", x[1]));
        var linhaJ = regs.Select((x, i) => (x, i)).ToList();
        foreach (var (x, i) in linhaJ.Where(t => t.x[0] == "J210"))
        {
            var fatos = regs.Skip(i + 1).TakeWhile(f => f[0] == "J215").ToList();
            Assert.Equal(-Com(x[6], x[7]), -Com(x[4], x[5]) + fatos.Sum(f => f[4] == "C" ? V(f[3]) : -V(f[3])));
        }
        Assert.Contains(regs, x => x is ["J215", "1", _, "8400,00", "C"]);
        Assert.Contains(regs, x => x is ["J215", "3", _, "50000,00", "C"]);

        // Signatários
        var j930 = regs.Where(x => x[0] == "J930").ToList();
        Assert.Equal(["J930", "José da Silva Contador", CpfContador, "Contador", "900", "1SP123456", "contador@exemplo.com.br", "1133334444", "SP",
                      "SP/2025/123456", "31032026", "N"], j930[0]);
        Assert.Equal(["J930", "Maria Sócia", CpfSocia, "Administrador", "205", "", "maria@exemplo.com.br", "11999998888", "", "", "", "S"], j930[1]);

        // Histórico e número do livro sugerido para a próxima geração do mesmo ano (mesmo livro)
        var geradas = (await db.App.Ecd.ListarGeradasAsync(empresa, adm, ct)).Valor;
        Assert.Equal((2025, 1, arq.HashSha1, regs.Count), (geradas[0].Ano, geradas[0].NumeroOrdem, geradas[0].HashSha1, geradas[0].Linhas));
        Assert.Equal(1, (await db.App.Ecd.ValidarAsync(empresa, 2025, Opcoes, adm, ct)).Valor.NumeroOrdem);
    }

    [Fact]
    public async Task Assina_com_contador_e_responsavel_no_formato_do_pva_e_a_assinatura_confere()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (empresa, adm) = await PrepararAsync("60701190000104");
        using var certContador = Certificado($"JOSE DA SILVA CONTADOR:{CpfContador}");
        using var certSocia = Certificado($"MARIA SOCIA:{CpfSocia}");

        // Certificado de outra pessoa é recusado
        using var outro = Certificado("FULANO:11144477735");
        var recusa = await db.App.Ecd.GerarAsync(empresa, 2025, Opcoes, adm, [new(outro, "900")], ct);
        Assert.False(recusa.Sucesso);
        Assert.Contains(recusa.Erros, e => e.Contains("não é de nenhum signatário"));

        var r = await db.App.Ecd.GerarAsync(empresa, 2025, Opcoes, adm, [new(certContador, "900"), new(certSocia, "205")], ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var arq = r.Valor;
        Assert.Equal(2, arq.Assinaturas.Count);
        Assert.All(arq.Assinaturas, a => Assert.True(a.Valida, a.Erro));
        Assert.Equal([("SBRCAAEPDR01900", CpfContador), ("SBRCAAEPDR02205", CpfSocia)], arq.Assinaturas.Select(a => (a.Etiqueta, a.Documento)));

        // Estrutura: texto até o CR LF do 9999, depois etiqueta + PKCS#7 de cada assinatura
        var conteudo = arq.Conteudo;
        var texto = Encoding.Latin1.GetString(conteudo);
        var fim9999 = texto.IndexOf("\r\n|9999|", StringComparison.Ordinal);
        var semAssinatura = texto.IndexOf("\r\n", fim9999 + 2, StringComparison.Ordinal) + 2;
        Assert.Equal(arq.HashSha1, Convert.ToHexString(SHA1.HashData(conteudo.AsSpan(0, semAssinatura))));
        Assert.Equal("SBRCAAEPDR01900", texto.Substring(semAssinatura, 15));
        Registros(conteudo, semAssinatura);                                        // a parte texto continua íntegra

        // Conferência independente com SignedCms: conteúdo = SHA-1 do arquivo sem assinaturas, assinatura e certificado conferem
        var segunda = texto.IndexOf("SBRCAAEPDR02205", semAssinatura, StringComparison.Ordinal);
        Assert.True(segunda > semAssinatura);
        foreach (var (inicio, fim, cert) in new[] { (semAssinatura + 15, segunda, certContador), (segunda + 15, conteudo.Length, certSocia) })
        {
            var cms = new SignedCms();
            cms.Decode(conteudo.AsSpan(inicio, fim - inicio));
            cms.CheckSignature(verifySignatureOnly: true);
            Assert.Equal(SHA1.HashData(conteudo.AsSpan(0, semAssinatura)), cms.ContentInfo.Content);
            Assert.Equal("1.2.840.113549.1.7.1", cms.ContentInfo.ContentType.Value);   // id-data
            Assert.Equal(cert.Thumbprint, cms.SignerInfos[0].Certificate!.Thumbprint);
            Assert.Equal("1.3.14.3.2.26", cms.SignerInfos[0].DigestAlgorithm.Value);   // SHA-1, como o PVA
            Assert.Contains(cms.SignerInfos[0].SignedAttributes.Cast<CryptographicAttributeObject>(), a => a.Oid.Value == "1.2.840.113549.1.9.5");
        }

        // Arquivo alterado depois de assinado: a assinatura deixa de conferir
        var adulterado = (byte[])conteudo.Clone();
        var pos = texto.IndexOf("|50000,00|", StringComparison.Ordinal) + 1;
        adulterado[pos] = (byte)'6';
        Assert.All(db.App.AssinadorSped.LerAssinaturas(adulterado), a => Assert.False(a.Valida));

        // Assinar depois: gera só com o contador; a sócia assina o arquivo já gerado (ordem 02, mesmo hash)
        var parcial = (await db.App.Ecd.GerarAsync(empresa, 2025, Opcoes, adm, [new(certContador, "900")], ct)).Valor;
        Assert.Single(parcial.Assinaturas);
        var completo = db.App.Ecd.Assinar(parcial.Conteudo, [new(certSocia, "205")]);
        Assert.True(completo.Sucesso, string.Join("; ", completo.Erros));
        var lidas = db.App.Ecd.LerAssinaturas(completo.Valor);
        Assert.Equal(["SBRCAAEPDR01900", "SBRCAAEPDR02205"], lidas.Select(a => a.Etiqueta));
        Assert.All(lidas, a => Assert.True(a.Valida, a.Erro));
        Assert.Equal(parcial.HashSha1, Convert.ToHexString(SHA1.HashData(completo.Valor.AsSpan(0, db.App.AssinadorSped.TamanhoSemAssinaturas(completo.Valor)))));
        Assert.False(db.App.Ecd.Assinar(parcial.Conteudo, [new(certSocia, "999")]).Sucesso);    // qualificação fora do J930

        // Sem assinatura continua possível; SHA-256 como alternativa
        var semAss = (await db.App.Ecd.GerarAsync(empresa, 2025, Opcoes, adm, ct: ct)).Valor;
        Assert.Empty(db.App.AssinadorSped.LerAssinaturas(semAss.Conteudo));
        var sha256 = new AssinadorSpedCms(HashAlgorithmName.SHA256).Assinar(semAss.Conteudo, [new(certContador, "900")]);
        Assert.True(Assert.Single(db.App.AssinadorSped.LerAssinaturas(sha256)).Valida);
    }

    private const string CpfProcurador = "11144477735";

    [Fact]
    public async Task Assina_como_procurador_com_procuracao_vigente_e_certificado_do_procurador()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (empresa, adm) = await PrepararAsync("56007569000133");
        var opcoes = Opcoes with { AssinarComoProcurador = true };

        // Sem procurador cadastrado: bloqueia (no lugar de "sem responsável")
        var sem = await db.App.Ecd.ValidarAsync(empresa, 2025, opcoes, adm, ct);
        Assert.Contains(sem.Valor.Pendencias, p => p.Codigo == "PROCURADOR_NAO_VIGENTE" && p.Bloqueante && p.Mensagem.Contains("Nenhum procurador"));
        Assert.DoesNotContain(sem.Valor.Pendencias, p => p.Codigo == "SEM_RESPONSAVEL");
        Assert.False((await db.App.Ecd.GerarAsync(empresa, 2025, opcoes, adm, ct: ct)).Sucesso);

        // Procuração vencida: bloqueia explicando a vigência
        var id = await db.App.CadastrarProcuradorAsync(empresa, adm, "Pedro Procurador", CpfProcurador, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31),
            "pedro@exemplo.com.br", "(11) 98888-7777");
        var vencida = await db.App.Ecd.ValidarAsync(empresa, 2025, opcoes, adm, ct);
        Assert.Contains(vencida.Valor.Pendencias, p => p.Codigo == "PROCURADOR_NAO_VIGENTE" && p.Mensagem.Contains("não está vigente"));

        // Vigente: signatário 309 com IND_RESP_LEGAL = S no lugar do sócio, aviso do e-CAC
        // Procuração só da ECF: não vale para a ECD
        Assert.True((await db.App.Responsaveis.DefinirProcuracaoAsync(empresa, id, new Procuracao(true, new DateOnly(2024, 1, 1), null, ["ECF"]), adm, ct)).Sucesso);
        var semAbrangencia = await db.App.Ecd.ValidarAsync(empresa, 2025, opcoes, adm, ct);
        Assert.Contains(semAbrangencia.Valor.Pendencias, p => p.Codigo == "PROCURADOR_NAO_VIGENTE" && p.Mensagem.Contains("não abrange a ECD"));
        Assert.True((await db.App.Responsaveis.DefinirProcuracaoAsync(empresa, id, new Procuracao(true, new DateOnly(2024, 1, 1), null, ["ECD", "ECF"]), adm, ct)).Sucesso);
        var previa = await db.App.Ecd.ValidarAsync(empresa, 2025, opcoes, adm, ct);
        Assert.True(previa.Valor.PodeGerar, string.Join("; ", previa.Valor.Bloqueantes.Select(b => b.Mensagem)));
        Assert.Contains(previa.Valor.Avisos, p => p.Codigo == "PROCURACAO_ECAC" && p.Mensagem.Contains("e-CAC"));
        Assert.Equal(["900", "309"], previa.Valor.Signatarios.Select(s => s.CodigoQualificacao));

        using var certContador = Certificado($"JOSE DA SILVA CONTADOR:{CpfContador}");
        using var certProc = Certificado($"PEDRO PROCURADOR:{CpfProcurador}");
        using var certSocia = Certificado($"MARIA SOCIA:{CpfSocia}");
        // O certificado da sócia não vale para a qualificação 309 (CPF diferente do procurador)
        var recusa = await db.App.Ecd.GerarAsync(empresa, 2025, opcoes, adm, [new(certContador, "900"), new(certSocia, "309")], ct);
        Assert.False(recusa.Sucesso);
        Assert.Contains(recusa.Erros, e => e.Contains("não é de nenhum signatário"));

        var r = await db.App.Ecd.GerarAsync(empresa, 2025, opcoes, adm, [new(certContador, "900"), new(certProc, "309")], ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.All(r.Valor.Assinaturas, a => Assert.True(a.Valida, a.Erro));
        Assert.Equal([("SBRCAAEPDR01900", CpfContador), ("SBRCAAEPDR02309", CpfProcurador)], r.Valor.Assinaturas.Select(a => (a.Etiqueta, a.Documento)));
        var regs = Registros(r.Valor.Conteudo, db.App.AssinadorSped.TamanhoSemAssinaturas(r.Valor.Conteudo));
        var j930 = regs.Where(x => x[0] == "J930").ToList();
        Assert.Equal(2, j930.Count);
        Assert.Equal(["J930", "Pedro Procurador", CpfProcurador, "Procurador", "309", "", "pedro@exemplo.com.br", "11988887777", "", "", "", "S"], j930.Single(x => x[4] == "309"));
        Assert.DoesNotContain(j930, x => x[2] == CpfSocia);
    }
}
