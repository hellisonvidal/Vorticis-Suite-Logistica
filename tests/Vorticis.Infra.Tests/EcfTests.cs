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

namespace Vorticis.Infra.Tests;

/// <summary>ECF (Escrituração Contábil Fiscal) gerada de uma empresa do Lucro Real e de outra do Lucro Presumido, trimestrais, e assinada.</summary>
public class EcfTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    private const string CpfContador = "52998224725";
    private const string CpfSocia = "39053344705";
    private const string CnpjReal = "34028316000103";
    private const string CnpjPresumido = "00360305000104";
    private const string CnpjAssina = "33000167000101";

    /// <summary>Plano referencial FICTÍCIO (só para o teste), com a estrutura do L300 da RFB: 3.01 (antes do IRPJ e da CSLL) e 3.02 (provisões).</summary>
    private const string Referencial = """
        CODIGO|DESCRICAO|TIPO|COD_SUP|NIVEL|COD_NAT
        1|ATIVO|S||1|01
        1.01|Ativo circulante|S|1|2|01
        1.01.01|Disponibilidades e outros|A|1.01|3|01
        2|PASSIVO E PATRIMÔNIO LÍQUIDO|S||1|02
        2.01|Passivo circulante|S|2|2|02
        2.01.01|Obrigações|A|2.01|3|02
        2.03|PATRIMÔNIO LÍQUIDO|S|2|2|03
        2.03.01|Capital social|A|2.03|3|03
        2.03.02|Lucros ou prejuízos acumulados|A|2.03|3|03
        3|RESULTADO LÍQUIDO DO PERÍODO|S||1|04
        3.01|RESULTADO ANTES DO IRPJ E DA CSLL|S|3|2|04
        3.01.01|Receitas|A|3.01|3|04
        3.01.02|Custos e despesas|A|3.01|3|04
        3.02|PROVISÃO PARA CSLL E IRPJ|S|3|2|04
        3.02.01.01.01|Provisão para CSLL e IRPJ|S|3.02|3|04
        3.02.01.01.01.01|Provisão para CSLL|A|3.02.01.01.01|4|04
        3.02.01.01.01.02|Provisão para IRPJ|A|3.02.01.01.01|4|04
        """;

    private static X509Certificate2 Certificado(string cn)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={cn}, O=ICP-Brasil, C=BR", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx, "x"), "x", X509KeyStorageFlags.Exportable);
    }

    private async Task Lancar(long empresa, long adm, DateOnly data, string debito, string credito, decimal valor, string hist)
        => Assert.True((await db.App.Lancamentos.EfetivarAsync(new LancamentoProposto(empresa, data, OrigemLancamento.Manual,
            [new(debito, IndicadorDC.D, valor, hist, null), new(credito, IndicadorDC.C, valor, hist)]), adm, TestContext.Current.CancellationToken)).Sucesso);

    private Task Receita(long e, long a, int mes, int dia, decimal v) => Lancar(e, a, new(2025, mes, dia), "1.1.01.002", "3.1.01.003", v, "Serviços prestados");
    private Task Despesa(long e, long a, int mes, int dia, decimal v) => Lancar(e, a, new(2025, mes, dia), "4.2.02.008", "1.1.01.002", v, "Serviços de terceiros");
    private Task ProvisaoIrpj(long e, long a, int mes, int dia, decimal v) => Lancar(e, a, new(2025, mes, dia), "4.5.01.001", "2.1.04.003", v, "Provisão do IRPJ");
    private Task ProvisaoCsll(long e, long a, int mes, int dia, decimal v) => Lancar(e, a, new(2025, mes, dia), "4.5.01.002", "2.1.04.003", v, "Provisão da CSLL");

    private static string? Ref(string codigo, string nat) => nat switch
    {
        "01" => "1.01.01",
        "02" => "2.01.01",
        "03" => codigo.StartsWith("2.3.01") ? "2.03.01" : "2.03.02",
        "04" => codigo.StartsWith("4.5.01.002") ? "3.02.01.01.01.01" : codigo.StartsWith("4.5.01.001") ? "3.02.01.01.01.02" : codigo.StartsWith("3.1") ? "3.01.01" : "3.01.02",
        _ => null,
    };

    /// <summary>Empresa de 2025 com cadastro completo, contador, sócia, plano referencial vinculado e capital integralizado; o regime é ajustado pelo chamador.</summary>
    private async Task<(long Empresa, long Adm)> PrepararAsync(string cnpj, string codPlanRef, string regime)
    {
        var ct = TestContext.Current.CancellationToken;
        var app = db.App;
        var empresa = await db.CriarEmpresaAsync(cnpj, 2025);
        var adm = await db.AdminAsync();
        await db.EscalarAsync<int>("""
            UPDATE core.empresa SET nire = '35212345678', municipio = 'SÃO PAULO', cep = '01310100', logradouro = 'AVENIDA PAULISTA', numero = '1000',
                   bairro = 'BELA VISTA', email = 'contato@exemplo.com.br', telefone = '(11) 3333-4444' WHERE id = @e RETURNING 1
            """, ("e", empresa));
        await db.EscalarAsync<int>("UPDATE core.empresa_regime SET regime = @r::core.regime_tributario WHERE empresa_id = @e RETURNING 1", ("r", regime), ("e", empresa));

        var admin = new SessaoUsuario(adm, "admin_teste", "Admin", true, false);
        Assert.True((await app.PlanoReferencial.ImportarAsync(admin, codPlanRef, 2025, Encoding.Latin1.GetBytes(Referencial), "ref.txt", ct)).Sucesso);
        Assert.True((await app.PlanoReferencial.DefinirPlanoAsync(empresa, 2025, codPlanRef, adm, ct)).Sucesso);
        var mapa = (await app.PlanoReferencial.CarregarAsync(empresa, 2025, adm, ct)).Valor.Contas;
        var v = await app.PlanoReferencial.VincularAsync(empresa, 2025, mapa.ToDictionary(c => c.ContaId, c => Ref(c.Codigo, c.CodNat)), adm, ct);
        Assert.True(v.Sucesso, string.Join("; ", v.Erros));

        Assert.True((await app.Responsaveis.VincularContadorAsync(empresa, new Contador(0, "José da Silva Contador", CpfContador, "1SP123456", "SP",
            "contador@exemplo.com.br", "(11) 3333-4444", null), new DateOnly(2020, 1, 1), true, adm, ct)).Sucesso);
        Assert.True((await app.Responsaveis.SalvarSocioAsync(empresa, new Socio(0, "Maria Sócia", 'F', CpfSocia, null, "Sócio-Administrador", 49, 100m,
            null, null, true, "maria@exemplo.com.br", "11999998888"), adm, ct)).Sucesso);
        await Lancar(empresa, adm, new(2025, 1, 2), "1.1.01.002", "2.3.01.001", 100_000m, "Integralização do capital");
        return (empresa, adm);
    }

    private async Task Apuracao(long empresa, string regime, int tri, decimal receita, decimal? lucro, decimal adIrpj, decimal exIrpj, decimal adCsll, decimal exCsll,
                                decimal compIrpj, decimal compCsll, decimal prejIrpj, decimal prejCsll, decimal baseIrpj, decimal baseCsll, decimal normal, decimal adicional,
                                decimal dedIrpj, decimal csll, decimal dedCsll, string memoria = "")
    {
        var ini = new DateOnly(2025, tri * 3 - 2, 1);
        var fim = ini.AddMonths(3).AddDays(-1);
        var json = "{\"memoria\":[" + (memoria.Length == 0 ? "" : string.Join(",", memoria.Split('\n').Select(l => "\"" + l + "\""))) + "]}";
        await db.EscalarAsync<int>("""
            INSERT INTO fiscal.apuracao_lucro (empresa_id, regime, ano, trimestre, periodo_inicio, periodo_fim, receita_bruta, lucro_contabil, adicoes_irpj, exclusoes_irpj,
                adicoes_csll, exclusoes_csll, compensacao_irpj, compensacao_csll, prejuizo_irpj, prejuizo_csll, base_irpj, base_csll, irpj_normal, irpj_adicional,
                irpj_deducoes, irpj_a_pagar, csll_devida, csll_deducoes, csll_a_pagar, vencimento, resultado)
            VALUES (@e, @reg, 2025, @t, @ini, @fim, @rec, @ll, @ai, @ei, @ac, @ec, @ci, @cc, @pi, @pc, @bi, @bc, @n, @ad, @di, @ip, @cs, @dc, @cp, @fim, @res::jsonb)
            RETURNING 1
            """, ("e", empresa), ("reg", regime), ("t", (short)tri), ("ini", ini), ("fim", fim), ("rec", receita), ("ll", (object?)lucro ?? DBNull.Value),
            ("ai", adIrpj), ("ei", exIrpj), ("ac", adCsll), ("ec", exCsll), ("ci", compIrpj), ("cc", compCsll), ("pi", prejIrpj), ("pc", prejCsll), ("bi", baseIrpj),
            ("bc", baseCsll), ("n", normal), ("ad", adicional), ("di", dedIrpj), ("ip", Math.Max(0, normal + adicional - dedIrpj)), ("cs", csll), ("dc", dedCsll),
            ("cp", Math.Max(0, csll - dedCsll)), ("res", json));
    }

    private async Task Lalur(long empresa, int tri, string tributo, string tipo, string desc, decimal valor, string codigo, string? contaCodigo)
        => await db.EscalarAsync<int>("""
            INSERT INTO fiscal.lalur_lancamento (empresa_id, ano, trimestre, tributo, tipo, descricao, valor, codigo_ecf, conta_id)
            VALUES (@e, 2025, @t, @tr, @tp, @d, @v, @c, (SELECT id FROM contabil.plano_contas WHERE empresa_id = @e AND codigo = @cc))
            RETURNING 1
            """, ("e", empresa), ("t", (short)tri), ("tr", tributo), ("tp", tipo), ("d", desc), ("v", valor), ("c", codigo), ("cc", (object?)contaCodigo ?? DBNull.Value));

    private async Task ParteB(long empresa, string tributo, string tipo, int? ano, int? tri, decimal valor)
        => await db.EscalarAsync<int>("""
            INSERT INTO fiscal.lalur_parte_b (empresa_id, tributo, tipo, ano, trimestre, valor) VALUES (@e, @tr, @tp, @a, @t, @v) RETURNING 1
            """, ("e", empresa), ("tr", tributo), ("tp", tipo), ("a", (object?)(ano is null ? null : (short)ano) ?? DBNull.Value),
            ("t", (object?)(tri is null ? null : (short)tri) ?? DBNull.Value), ("v", valor));

    /// <summary>Lucro Real trimestral: lucro no 1º tri, prejuízo no 2º, lucro com compensação no 3º e lucro com exclusão no 4º.</summary>
    private async Task<(long Empresa, long Adm)> EmpresaRealAsync(string cnpj = CnpjReal)
    {
        var ct = TestContext.Current.CancellationToken;
        var (e, a) = await PrepararAsync(cnpj, "1", "LUCRO_REAL");
        await Receita(e, a, 2, 10, 50_000m); await Despesa(e, a, 3, 15, 20_000m); await ProvisaoCsll(e, a, 3, 31, 2_790m); await ProvisaoIrpj(e, a, 3, 31, 4_650m);
        await Receita(e, a, 4, 10, 10_000m); await Despesa(e, a, 5, 20, 40_000m);
        await Receita(e, a, 7, 10, 60_000m); await Despesa(e, a, 8, 20, 15_000m); await ProvisaoCsll(e, a, 9, 30, 2_835m); await ProvisaoIrpj(e, a, 9, 30, 4_725m);
        await Receita(e, a, 10, 10, 20_000m); await Despesa(e, a, 11, 20, 10_000m); await ProvisaoCsll(e, a, 12, 30, 720m); await ProvisaoIrpj(e, a, 12, 30, 1_200m);
        Assert.True((await db.App.Encerramento.EncerrarAsync(e, 2025, new ContasEncerramento(), a, ct)).Sucesso);

        await Lalur(e, 1, "AMBOS", "ADICAO", "Multa de trânsito", 1_000m, "8.65", "4.2.02.008");
        await Lalur(e, 4, "AMBOS", "EXCLUSAO", "Juros sobre o capital próprio", 2_000m, "166.03", null);
        await ParteB(e, "IRPJ", "SALDO_INICIAL", null, null, 10_000m);
        await ParteB(e, "IRPJ", "PREJUIZO", 2025, 2, 30_000m); await ParteB(e, "IRPJ", "COMPENSACAO", 2025, 3, -13_500m);
        await ParteB(e, "CSLL", "PREJUIZO", 2025, 2, 30_000m); await ParteB(e, "CSLL", "COMPENSACAO", 2025, 3, -13_500m);
        await Apuracao(e, "REAL", 1, 50_000m, 30_000m, 1_000m, 0, 1_000m, 0, 0, 0, 0, 0, 31_000m, 31_000m, 4_650m, 0, 0, 2_790m, 0);
        await Apuracao(e, "REAL", 2, 10_000m, -30_000m, 0, 0, 0, 0, 0, 0, 30_000m, 30_000m, 0, 0, 0, 0, 0, 0, 0);
        await Apuracao(e, "REAL", 3, 60_000m, 45_000m, 0, 0, 0, 0, 13_500m, 13_500m, 0, 0, 31_500m, 31_500m, 4_725m, 0, 500m, 2_835m, 100m);
        await Apuracao(e, "REAL", 4, 20_000m, 10_000m, 0, 2_000m, 0, 2_000m, 0, 0, 0, 0, 8_000m, 8_000m, 1_200m, 0, 0, 720m, 0);
        return (e, a);
    }

    private static readonly OpcoesEcf Opcoes = new();

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

    /// <summary>Confere os totalizadores: 9900 por registro, xxx990 por bloco, 9990 e 9999.</summary>
    private static void ConferirTotalizadores(List<string[]> regs)
    {
        Assert.Equal("0000", regs[0][0]);
        Assert.Equal("9999", regs[^1][0]);
        Assert.Equal(regs.Count.ToString(), regs[^1][1]);
        foreach (var bloco in regs.Select(r => r[0][0]).Distinct())
        {
            var doBloco = regs.Where(r => r[0][0] == bloco).ToList();
            if (bloco == '9')
            {
                // O 9990 totaliza o bloco 9 inteiro (9001, 9900s, 9990 e 9999).
                Assert.Equal("9001", doBloco[0][0]);
                Assert.Equal(doBloco.Count.ToString(), doBloco.Single(r => r[0] == "9990")[1]);
                continue;
            }
            Assert.Equal(bloco == '0' ? "0000" : bloco + "001", doBloco[0][0]);
            Assert.Equal(bloco + "990", doBloco[^1][0]);
            Assert.Equal(doBloco.Count.ToString(), doBloco[^1][1]);
        }
        var contagem = regs.GroupBy(r => r[0]).ToDictionary(g => g.Key, g => g.Count());
        foreach (var r in regs.Where(r => r[0] == "9900")) Assert.Equal(contagem[r[1]].ToString(), r[2]);
        Assert.Equal(contagem.Count, regs.Count(r => r[0] == "9900"));
        Assert.Equal(contagem["9900"].ToString(), regs.Single(r => r[0] == "9900" && r[1] == "9900")[2]);
        Assert.Equal("1", regs.Single(r => r[0] == "9900" && r[1] == "9999")[2]);
    }

    private static decimal V(string s) => decimal.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);

    /// <summary>Registros filhos de um período (K030/L030/M030/N030/P030): do registro de período até o próximo.</summary>
    private static List<string[]> Periodo(List<string[]> regs, string regPeriodo, string codigo)
    {
        var i = regs.FindIndex(r => r[0] == regPeriodo && r[3] == codigo);
        Assert.True(i >= 0, $"{regPeriodo} {codigo} ausente");
        var j = regs.FindIndex(i + 1, r => r[0] == regPeriodo || r[0].EndsWith("990"));
        return regs.GetRange(i + 1, j - i - 1);
    }

    [Fact]
    public async Task Lucro_real_trimestral_gera_blocos_calculos_e_totalizadores()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (empresa, adm) = await EmpresaRealAsync();

        var previa = await db.App.Ecf.ValidarAsync(empresa, 2025, Opcoes, adm, ct);
        Assert.True(previa.Sucesso, string.Join("; ", previa.Erros));
        Assert.True(previa.Valor.PodeGerar, string.Join("; ", previa.Valor.Bloqueantes.Select(b => b.Mensagem)));
        Assert.Equal(FormaTributacaoEcf.Real, previa.Valor.Forma);
        Assert.DoesNotContain(previa.Valor.Pendencias, p => p.Codigo.StartsWith("DIVERGENCIA", StringComparison.Ordinal));

        var r = await db.App.Ecf.GerarAsync(empresa, 2025, Opcoes, adm, ct: ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var arq = r.Valor;
        Assert.Equal($"ECF_{CnpjReal}_20250101_20251231_LucroReal.txt", arq.NomeArquivo);
        var regs = Registros(arq.Conteudo, arq.Conteudo.Length);
        ConferirTotalizadores(regs);
        Assert.Equal(arq.Linhas, regs.Count);
        Assert.Equal(arq.HashSha1, Convert.ToHexString(SHA1.HashData(arq.Conteudo)));

        // Bloco 0
        Assert.Equal(["0000", "LECF", "0012", CnpjReal, "Empresa de Teste LTDA", "0", "0", "", "", "01012025", "31122025", "N", "", "0", ""], regs[0]);
        Assert.Equal(["0010", "", "N", "1", "T", "01", "RRRR", "", "", "", "", "", ""], regs.Single(x => x[0] == "0010"));
        Assert.Equal(32, regs.Single(x => x[0] == "0020").Length);
        Assert.Equal("1", regs.Single(x => x[0] == "0020")[1]);
        Assert.Equal(["0030", "2062", "4751201", "AVENIDA PAULISTA", "1000", "", "BELA VISTA", "SP", "3550308", "01310100", "1133334444", "contato@exemplo.com.br"],
                     regs.Single(x => x[0] == "0030"));
        Assert.Equal(["0930", "José da Silva Contador", CpfContador, "900", "1SP123456", "contador@exemplo.com.br", "1133334444"], regs.Single(x => x[0] == "0930" && x[3] == "900"));
        Assert.Equal(["0930", "Maria Sócia", CpfSocia, "205", "", "maria@exemplo.com.br", "11999998888"], regs.Single(x => x[0] == "0930" && x[3] == "205"));

        // Blocos C e E sem dados; blocos P, Q, S, T, U, V, W, X sem dados
        Assert.Equal(["C001", "1"], regs.Single(x => x[0] == "C001"));
        Assert.Equal(["E001", "1"], regs.Single(x => x[0] == "E001"));
        foreach (var b in "PQSTUVWX") Assert.Equal([$"{b}001", "1"], regs.Single(x => x[0] == $"{b}001"));
        Assert.DoesNotContain(regs, x => x[0] is "P030" or "P100");

        // J050/J051: plano do contribuinte e vínculo referencial
        Assert.Contains(regs, x => x[0] == "J050" && x[3] == "A" && x[5] == "4.5.01.002");
        Assert.Contains(regs, x => x[0] == "J051" && x[1] == "" && x[2] == "3.02.01.01.01.01");
        Assert.Equal(regs.Count(x => x[0] == "J050" && x[3] == "A" && x[2] is "01" or "02" or "03" or "04"), regs.Count(x => x[0] == "J051"));

        // Bloco K: um K030 por trimestre; cada K155 tem um K156 com os mesmos valores
        Assert.Equal(["T01", "T02", "T03", "T04"], regs.Where(x => x[0] == "K030").Select(x => x[3]));
        Assert.Equal(regs.Count(x => x[0] == "K155"), regs.Count(x => x[0] == "K156"));
        Assert.Equal(regs.Count(x => x[0] == "K355"), regs.Count(x => x[0] == "K356"));
        var k1 = Periodo(regs, "K030", "T01");
        var banco = k1.First(x => x[0] == "K155" && x[1] == "1.1.01.002");
        Assert.Equal(["K155", "1.1.01.002", "", "0,00", "D", "150000,00", "20000,00", "130000,00", "D"], banco);   // capital 100.000 + receita 50.000 − despesa 20.000
        Assert.Equal(["K156", "1.01.01", "0,00", "D", "150000,00", "20000,00", "130000,00", "D"], k1[k1.IndexOf(banco) + 1]);
        Assert.Contains(k1, x => x[0] == "K355" && x[1] == "3.1.01.003" && x[3] == "50000,00" && x[4] == "C");
        Assert.Contains(k1, x => x[0] == "K356" && x[1] == "3.01.01" && x[2] == "50000,00" && x[3] == "C");
        // O balanço fecha em cada trimestre e, depois do encerramento, as contas de resultado ficam zeradas no 4º
        var k4 = Periodo(regs, "K030", "T04").Where(x => x[0] == "K155").ToList();
        Assert.Equal(0m, k4.Sum(x => Com(x[7], x[8])));
        Assert.Equal(0m, Periodo(regs, "K030", "T01").Where(x => x[0] == "K155").Sum(x => Com(x[3], x[4])));

        // Bloco L: balanço e DRE referenciais por trimestre (a raiz 3 é o resultado líquido do período)
        var l1 = Periodo(regs, "L030", "T01");
        Assert.Equal(["L300", "3", "RESULTADO LÍQUIDO DO PERÍODO", "S", "1", "04", "", "22560,00", "C"], l1.Single(x => x[0] == "L300" && x[1] == "3"));   // 50.000 − 20.000 − 2.790 − 4.650
        Assert.Equal("30000,00", l1.Single(x => x[0] == "L300" && x[1] == "3.01")[7]);
        Assert.Equal("C", l1.Single(x => x[0] == "L300" && x[1] == "3.01")[8]);
        Assert.Equal("2790,00", l1.Single(x => x[0] == "L300" && x[1] == "3.02.01.01.01.01")[7]);
        Assert.Equal("D", l1.Single(x => x[0] == "L300" && x[1] == "3.02.01.01.01.01")[8]);
        var l100 = Periodo(regs, "L030", "T04").Where(x => x[0] == "L100").ToList();
        Assert.Equal(Com(l100.Single(x => x[1] == "1")[11], l100.Single(x => x[1] == "1")[12]),
                     -Com(l100.Single(x => x[1] == "2")[11], l100.Single(x => x[1] == "2")[12]));          // ativo = passivo + PL

        // Bloco M — 1º tri: lucro líquido, CSLL somada às adições, adição da multa (com a conta contábil), lucro real
        var m1 = Periodo(regs, "M030", "T01");
        string M300(List<string[]> l, string cod) => l.Single(x => x[0] == "M300" && x[1] == cod)[5];
        Assert.Equal("27210,00", M300(m1, "2"));
        Assert.Equal("2790,00", M300(m1, "9"));
        Assert.Equal("1000,00", M300(m1, "8.65"));
        Assert.Equal("3790,00", M300(m1, "93"));
        Assert.Equal("0,00", M300(m1, "168"));
        Assert.Equal("31000,00", M300(m1, "169"));
        Assert.Equal("31000,00", M300(m1, "175"));
        var multa = m1.Single(x => x[0] == "M300" && x[1] == "8.65");
        Assert.Equal("A", multa[3]);
        Assert.Equal("2", multa[4]);                                                               // relacionado a conta contábil
        Assert.Equal(["M310", "4.2.02.008", "", "1000,00", "D"], m1.Single(x => x[0] == "M310"));
        Assert.Equal("30000,00", m1.Single(x => x[0] == "M350" && x[1] == "2")[5]);
        Assert.Equal(["M360", "4.2.02.008", "", "1000,00", "D"], m1.Single(x => x[0] == "M360"));
        Assert.Equal("31000,00", m1.Single(x => x[0] == "M350" && x[1] == "175")[5]);
        // 2º tri: prejuízo fiscal e base negativa na Parte B (M410 PF/BC)
        var m2 = Periodo(regs, "M030", "T02");
        Assert.Equal("-30000,00", M300(m2, "175"));
        Assert.Equal(["M410", "PF_IRPJ", "I", "30000,00", "PF", "", "Prejuízo fiscal apurado no período", "N"], m2.Single(x => x[0] == "M410" && x[2] == "I"));
        Assert.Equal(["M410", "BC_CSLL", "C", "30000,00", "BC", "", "Base de cálculo negativa da CSLL apurada no período", "N"], m2.Single(x => x[0] == "M410" && x[2] == "C"));
        // 3º tri: compensação de 13.500 (M305/M355 na conta da Parte B)
        var m3 = Periodo(regs, "M030", "T03");
        Assert.Equal("42165,00", M300(m3, "2"));
        Assert.Equal("45000,00", M300(m3, "169"));
        Assert.Equal("13500,00", M300(m3, "173"));
        Assert.Equal("31500,00", M300(m3, "175"));
        Assert.Equal(["M305", "PF_IRPJ", "13500,00", "C"], m3.Single(x => x[0] == "M305"));
        Assert.Equal(["M355", "BC_CSLL", "13500,00", "C"], m3.Single(x => x[0] == "M355"));
        // 4º tri: exclusão (JCP) relacionada só ao e-Lalur/e-Lacs, sem conta
        var m4 = Periodo(regs, "M030", "T04");
        Assert.Equal("2000,00", M300(m4, "166.03"));
        Assert.Equal("E", m4.Single(x => x[0] == "M300" && x[1] == "166.03")[3]);
        Assert.Equal("4", m4.Single(x => x[0] == "M300" && x[1] == "166.03")[4]);
        Assert.Equal("8000,00", M300(m4, "175"));
        // Contas e controle de saldos da Parte B (saldo inicial 10.000 de prejuízos de anos anteriores)
        Assert.Equal(["M010", "PF_IRPJ", "Prejuízo Fiscal Operacional - Atividade Geral", "31122024", "1000", "", "I", "10000,00", "D", ""], regs.Single(x => x[0] == "M010" && x[1] == "PF_IRPJ"));
        Assert.Equal(["M010", "BC_CSLL", "Base de Cálculo Negativa da CSLL - Atividade Geral", "30062025", "1003", "", "C", "0,00", "D", ""], regs.Single(x => x[0] == "M010" && x[1] == "BC_CSLL"));
        Assert.Equal(["M500", "PF_IRPJ", "I", "10000,00", "D", "0,00", "D", "30000,00", "D", "40000,00", "D"], m2.Single(x => x[0] == "M500" && x[1] == "PF_IRPJ"));
        Assert.Equal(["M500", "PF_IRPJ", "I", "40000,00", "D", "13500,00", "C", "0,00", "D", "26500,00", "D"], m3.Single(x => x[0] == "M500" && x[1] == "PF_IRPJ"));
        Assert.Equal(["M500", "BC_CSLL", "C", "30000,00", "D", "13500,00", "C", "0,00", "D", "16500,00", "D"], m3.Single(x => x[0] == "M500" && x[1] == "BC_CSLL"));

        // Bloco N — cálculos do IRPJ e da CSLL
        string N(string reg, string tri, string cod) => Periodo(regs, "N030", tri).Single(x => x[0] == reg && x[1] == cod)[3];
        Assert.Equal("31000,00", N("N500", "T01", "1"));
        Assert.Equal("4650,00", N("N630", "T01", "3"));
        Assert.Equal("4650,00", N("N630", "T01", "26"));
        Assert.Equal("2790,00", N("N670", "T01", "21"));
        Assert.Equal("-30000,00", N("N500", "T02", "1"));
        Assert.Equal("0,00", N("N630", "T02", "3"));
        Assert.Equal("0,00", N("N670", "T02", "21"));
        Assert.Equal("4725,00", N("N630", "T03", "3"));
        Assert.Equal("500,00", N("N630", "T03", "20"));
        Assert.Equal("4225,00", N("N630", "T03", "26"));
        Assert.Equal("2835,00", N("N670", "T03", "4"));
        Assert.Equal("100,00", N("N670", "T03", "17"));
        Assert.Equal("2735,00", N("N670", "T03", "21"));
        Assert.Equal("1200,00", N("N630", "T04", "3"));
        Assert.Equal("720,00", N("N670", "T04", "21"));

        // Bloco Y: sócia (100%)
        Assert.Equal(["Y600", "01012020", "", "105", "PF", CpfSocia, "Maria Sócia", "02", "100", "100", "", "", "0,00", "0,00", "0,00", "0,00", "0,00"], regs.Single(x => x[0] == "Y600"));
        Assert.DoesNotContain(regs, x => x[0] == "Y672");
        Assert.Contains(ConsultarAvisos(previa.Valor), c => c == "SEM_ECD");
    }

    private static IEnumerable<string> ConsultarAvisos(PreviaEcf p) => p.Avisos.Select(a => a.Codigo);

    private static decimal Com(string valor, string ind) => ind == "D" ? V(valor) : -V(valor);

    [Fact]
    public async Task Lucro_presumido_trimestral_gera_bloco_p_com_bases_e_impostos()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (e, a) = await PrepararAsync(CnpjPresumido, "2", "LUCRO_PRESUMIDO");
        for (var t = 1; t <= 4; t++)
        {
            var mes = t * 3 - 2;
            await Receita(e, a, mes + 1, 10, t == 1 ? 101_000m : 100_000m);
            await Despesa(e, a, mes + 2, 15, 30_000m);
        }
        Assert.True((await db.App.Encerramento.EncerrarAsync(e, 2025, new ContasEncerramento(), a, ct)).Sucesso);
        const string memoria = "Lucro Presumido · trimestre\nPrestação de serviços em geral, intermediação, administração, locação e cessão de bens: receita 100.000,00 × 32% = base IRPJ 32.000,00 · × 32% = base CSLL 32.000,00";
        await Apuracao(e, "PRESUMIDO", 1, 100_000m, null, 0, 0, 0, 0, 0, 0, 0, 0, 33_000m, 33_000m, 4_950m, 0, 500m, 2_970m, 0,
            memoria + "\nOutras receitas (ganhos de capital, rendimentos financeiros etc.) integram 100% das bases: 1.000,00");
        for (var t = 2; t <= 4; t++) await Apuracao(e, "PRESUMIDO", t, 100_000m, null, 0, 0, 0, 0, 0, 0, 0, 0, 32_000m, 32_000m, 4_800m, 0, 0, 2_880m, 0, memoria);

        var previa = await db.App.Ecf.ValidarAsync(e, 2025, Opcoes, a, ct);
        Assert.True(previa.Sucesso, string.Join("; ", previa.Erros));
        Assert.True(previa.Valor.PodeGerar, string.Join("; ", previa.Valor.Bloqueantes.Select(b => b.Mensagem)));
        Assert.DoesNotContain(previa.Valor.Pendencias, p => p.Codigo.StartsWith("DIVERGENCIA", StringComparison.Ordinal));

        var arq = (await db.App.Ecf.GerarAsync(e, 2025, Opcoes, a, ct: ct)).Valor;
        Assert.EndsWith("_LucroPresumido.txt", arq.NomeArquivo);
        var regs = Registros(arq.Conteudo, arq.Conteudo.Length);
        ConferirTotalizadores(regs);
        Assert.Equal(["0010", "", "N", "5", "T", "01", "PPPP", "", "C", "", "", "", "2"], regs.Single(x => x[0] == "0010"));
        foreach (var b in "LMNQSTUVWX") Assert.Equal([$"{b}001", "1"], regs.Single(x => x[0] == $"{b}001"));
        Assert.DoesNotContain(regs, x => x[0] is "L030" or "M030" or "N030" or "M300" or "N630");
        Assert.Equal(["P030", "01012025", "31032025", "T01"], regs.First(x => x[0] == "P030"));
        Assert.Equal(["T01", "T02", "T03", "T04"], regs.Where(x => x[0] == "P030").Select(x => x[3]));

        string P(string reg, string tri, string cod) => Periodo(regs, "P030", tri).Single(x => x[0] == reg && x[1] == cod)[3];
        Assert.Equal("100000,00", P("P200", "T01", "8"));
        Assert.Equal("32000,00", P("P200", "T01", "10"));
        Assert.Equal("1000,00", P("P200", "T01", "20"));
        Assert.Equal("33000,00", P("P200", "T01", "26"));
        Assert.Equal("33000,00", P("P300", "T01", "1"));
        Assert.Equal("4950,00", P("P300", "T01", "3"));
        Assert.Equal("500,00", P("P300", "T01", "10"));
        Assert.Equal("4450,00", P("P300", "T01", "15"));
        Assert.Equal("100000,00", P("P400", "T01", "4"));
        Assert.Equal("33000,00", P("P400", "T01", "21"));
        Assert.Equal("2970,00", P("P500", "T01", "2"));
        Assert.Equal("2970,00", P("P500", "T01", "13"));
        Assert.Equal("32000,00", P("P200", "T04", "26"));
        Assert.Equal("4800,00", P("P300", "T04", "15"));
        Assert.Equal("2880,00", P("P500", "T04", "13"));
        // P100/P150: balanço e DRE referenciais por trimestre
        Assert.Contains(Periodo(regs, "P030", "T01"), x => x[0] == "P150" && x[1] == "3.01.01" && x[7] == "101000,00" && x[8] == "C");
        var p4 = Periodo(regs, "P030", "T04");
        Assert.Equal(Com(p4.Single(x => x[0] == "P100" && x[1] == "1")[11], p4.Single(x => x[0] == "P100" && x[1] == "1")[12]),
                     -Com(p4.Single(x => x[0] == "P100" && x[1] == "2")[11], p4.Single(x => x[0] == "P100" && x[1] == "2")[12]));
        // Y672: receita bruta e total do ativo
        var y = regs.Single(x => x[0] == "Y672");
        Assert.Equal(18, y.Length);
        Assert.Equal("400000,00", y[15]);   // VL_RECEITAS
        Assert.Equal("381000,00", y[16]);   // TOT_ATIVO
    }

    [Fact]
    public async Task Simples_nao_entrega_ecf_e_pendencias_apontam_o_que_falta()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (e, a) = await PrepararAsync("90400888000142", "1", "LUCRO_REAL");
        var previa = await db.App.Ecf.ValidarAsync(e, 2025, Opcoes, a, ct);
        Assert.True(previa.Sucesso, string.Join("; ", previa.Erros));
        var codigos = previa.Valor.Bloqueantes.Select(p => p.Codigo).ToHashSet();
        Assert.False(previa.Valor.PodeGerar);
        Assert.Contains("SEM_APURACAO", codigos);

        // apuração só do 1º trimestre, LALUR sem código e com código inexistente, vínculo referencial faltando
        await Receita(e, a, 2, 10, 50_000m);
        await Apuracao(e, "REAL", 1, 50_000m, 50_000m, 0, 0, 0, 0, 0, 0, 0, 0, 50_000m, 50_000m, 7_500m, 0, 0, 4_500m, 0);
        await Lalur(e, 1, "IRPJ", "ADICAO", "Sem código", 100m, "", null);
        await Lalur(e, 1, "IRPJ", "ADICAO", "Código inexistente", 100m, "99999", null);
        await Lalur(e, 1, "IRPJ", "ADICAO", "Código de exclusão", 100m, "166.03", null);
        await db.EscalarAsync<int>("DELETE FROM contabil.mapeamento_referencial WHERE conta_id = (SELECT id FROM contabil.plano_contas WHERE empresa_id = @e AND codigo = '3.1.01.003') RETURNING 1", ("e", e));
        await db.EscalarAsync<int>("UPDATE core.empresa SET cep = NULL, email = 'invalido' WHERE id = @e RETURNING 1", ("e", e));
        previa = await db.App.Ecf.ValidarAsync(e, 2025, Opcoes, a, ct);
        codigos = previa.Valor.Bloqueantes.Select(p => p.Codigo).ToHashSet();
        Assert.Contains("APURACAO_AUSENTE", codigos);
        Assert.Contains("LALUR_SEM_CODIGO", codigos);
        Assert.Contains("LALUR_CODIGO_INVALIDO", codigos);
        Assert.Contains("LALUR_TIPO_CODIGO", codigos);
        Assert.Contains("SEM_REFERENCIAL", codigos);
        Assert.Contains("ENDERECO_CEP", codigos);
        Assert.Contains("EMAIL_EMPRESA", codigos);
        Assert.Contains("BALANCO_FINAL", codigos);
        Assert.False((await db.App.Ecf.GerarAsync(e, 2025, Opcoes, a, ct: ct)).Sucesso);

        // Simples Nacional: mensagem em vez de arquivo
        await db.EscalarAsync<int>("UPDATE core.empresa_regime SET regime = 'SIMPLES_NACIONAL' WHERE empresa_id = @e RETURNING 1", ("e", e));
        var simples = await db.App.Ecf.GerarAsync(e, 2025, Opcoes, a, ct: ct);
        Assert.False(simples.Sucesso);
        Assert.Contains(simples.Erros, m => m.Contains("Simples Nacional não entregam a ECF"));

        // Sem permissão: perfil Consulta
        var usuario = await db.EscalarAsync<long>("""
            INSERT INTO core.usuario (login, nome, senha_hash, perfil_id) VALUES ('consulta_ecf', 'Consulta', 'x', (SELECT id FROM core.perfil WHERE nome = 'Consulta'))
            RETURNING id
            """);
        Assert.False((await db.App.Ecf.ValidarAsync(e, 2025, Opcoes, usuario, ct)).Sucesso);
    }

    [Fact]
    public async Task Assina_a_ecf_no_formato_do_pva_historico_e_hash_da_ecd()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var (empresa, adm) = await EmpresaRealAsync(CnpjAssina);
        using var certContador = Certificado($"JOSE DA SILVA CONTADOR:{CpfContador}");
        using var certSocia = Certificado($"MARIA SOCIA:{CpfSocia}");
        using var outro = Certificado("FULANO:11144477735");
        Assert.False((await db.App.Ecf.GerarAsync(empresa, 2025, Opcoes, adm, [new(outro, "900")], ct)).Sucesso);

        var r = await db.App.Ecf.GerarAsync(empresa, 2025, Opcoes, adm, [new(certContador, "900"), new(certSocia, "205")], ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var arq = r.Valor;
        Assert.Equal(2, arq.Assinaturas.Count);
        Assert.All(arq.Assinaturas, x => Assert.True(x.Valida, x.Erro));
        Assert.Equal([("SBRCAAEPDR01900", CpfContador), ("SBRCAAEPDR02205", CpfSocia)], arq.Assinaturas.Select(x => (x.Etiqueta, x.Documento)));

        var conteudo = arq.Conteudo;
        var texto = Encoding.Latin1.GetString(conteudo);
        var fim9999 = texto.IndexOf("\r\n|9999|", StringComparison.Ordinal);
        var semAssinatura = texto.IndexOf("\r\n", fim9999 + 2, StringComparison.Ordinal) + 2;
        Assert.Equal(arq.HashSha1, Convert.ToHexString(SHA1.HashData(conteudo.AsSpan(0, semAssinatura))));
        Assert.Equal("SBRCAAEPDR01900", texto.Substring(semAssinatura, 15));
        ConferirTotalizadores(Registros(conteudo, semAssinatura));
        var segunda = texto.IndexOf("SBRCAAEPDR02205", semAssinatura, StringComparison.Ordinal);
        foreach (var (inicio, fim, cert) in new[] { (semAssinatura + 15, segunda, certContador), (segunda + 15, conteudo.Length, certSocia) })
        {
            var cms = new SignedCms();
            cms.Decode(conteudo.AsSpan(inicio, fim - inicio));
            cms.CheckSignature(verifySignatureOnly: true);
            Assert.Equal(SHA1.HashData(conteudo.AsSpan(0, semAssinatura)), cms.ContentInfo.Content);
            Assert.Equal(cert.Thumbprint, cms.SignerInfos[0].Certificate!.Thumbprint);
        }
        var adulterado = (byte[])conteudo.Clone();
        adulterado[texto.IndexOf("|Y600|", StringComparison.Ordinal) + 3] = (byte)'7';
        Assert.All(db.App.Ecf.LerAssinaturas(adulterado), x => Assert.False(x.Valida));

        // Assinar depois: só o contador na geração; a sócia assina o arquivo já gerado
        var parcial = (await db.App.Ecf.GerarAsync(empresa, 2025, Opcoes, adm, [new(certContador, "900")], ct)).Valor;
        var completo = db.App.Ecf.Assinar(parcial.Conteudo, [new(certSocia, "205")]);
        Assert.True(completo.Sucesso, string.Join("; ", completo.Erros));
        Assert.Equal(["SBRCAAEPDR01900", "SBRCAAEPDR02205"], db.App.Ecf.LerAssinaturas(completo.Valor).Select(x => x.Etiqueta));
        Assert.False(db.App.Ecf.Assinar(parcial.Conteudo, [new(certSocia, "999")]).Sucesso);

        // Histórico das gerações
        var hist = (await db.App.Ecf.ListarGeradasAsync(empresa, adm, ct)).Valor;
        Assert.True(hist.Count >= 2);
        Assert.All(hist, h => { Assert.Equal("REAL", h.FormaTributacao); Assert.Equal(2025, h.Ano); });

        // Hash da ECD gerada no ano vai no C040
        const string hash = "0123456789ABCDEF0123456789ABCDEF01234567";
        await db.EscalarAsync<int>("INSERT INTO contabil.ecd_gerada (empresa_id, ano, dt_ini, dt_fin, num_ordem, nat_livro, hash_sha1, linhas) VALUES (@e, 2025, '2025-01-01', '2025-12-31', 3, 'DIÁRIO GERAL', @h, 100) RETURNING 1",
                                  ("e", empresa), ("h", hash));
        var comEcd = (await db.App.Ecf.GerarAsync(empresa, 2025, new OpcoesEcf(InformarEcd: true), adm, ct: ct)).Valor;
        var regs = Registros(comEcd.Conteudo, comEcd.Conteudo.Length);
        Assert.Equal(["C001", "0"], regs.Single(x => x[0] == "C001"));
        Assert.Equal(["C040", hash, "01012025", "31122025", "", CnpjAssina, "3", "35212345678", "DIÁRIO GERAL", "9.00", "G", "N", "N", "0", "0", "1"],
                     regs.Single(x => x[0] == "C040"));
    }

    [Fact]
    public async Task Assina_como_procurador_no_0930_com_o_certificado_do_procurador_e_avisa_do_ecac()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        const string cpfProcurador = "11144477735";
        var (empresa, adm) = await EmpresaRealAsync("11444777000161");
        var opcoes = new OpcoesEcf(AssinarComoProcurador: true);

        var sem = await db.App.Ecf.ValidarAsync(empresa, 2025, opcoes, adm, ct);
        Assert.Contains(sem.Valor.Pendencias, p => p.Codigo == "PROCURADOR_NAO_VIGENTE" && p.Bloqueante);
        Assert.False((await db.App.Ecf.GerarAsync(empresa, 2025, opcoes, adm, ct: ct)).Sucesso);

        var vinculo = await db.App.CadastrarProcuradorAsync(empresa, adm, "Pedro Procurador", cpfProcurador, new DateOnly(2025, 3, 1), new DateOnly(2030, 3, 1),
            "pedro@exemplo.com.br", "(11) 98888-7777", ["ECF"]);
        Assert.False((await db.App.Responsaveis.DefinirProcuracaoAsync(empresa, vinculo, new Procuracao(true, new DateOnly(2025, 3, 1), new DateOnly(2025, 1, 1), ["ECF"]), adm, ct)).Sucesso);  // fim antes do início
        Assert.False((await db.App.Responsaveis.DefinirProcuracaoAsync(empresa, vinculo, new Procuracao(true, new DateOnly(2025, 3, 1), null, []), adm, ct)).Sucesso);                         // sem abrangência

        var previa = await db.App.Ecf.ValidarAsync(empresa, 2025, opcoes, adm, ct);
        Assert.True(previa.Valor.PodeGerar, string.Join("; ", previa.Valor.Bloqueantes.Select(b => b.Mensagem)));
        Assert.Contains(previa.Valor.Avisos, p => p.Codigo == "PROCURACAO_ECAC" && p.Mensagem.Contains("e-CAC") && p.Mensagem.Contains("ECF"));

        using var certContador = Certificado($"JOSE DA SILVA CONTADOR:{CpfContador}");
        using var certProc = Certificado($"PEDRO PROCURADOR:{cpfProcurador}");
        using var certSocia = Certificado($"MARIA SOCIA:{CpfSocia}");
        Assert.False((await db.App.Ecf.GerarAsync(empresa, 2025, opcoes, adm, [new(certSocia, "309")], ct)).Sucesso);       // CPF do certificado ≠ CPF do procurador
        var r = await db.App.Ecf.GerarAsync(empresa, 2025, opcoes, adm, [new(certContador, "900"), new(certProc, "309")], ct);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        Assert.Equal([("SBRCAAEPDR01900", CpfContador), ("SBRCAAEPDR02309", cpfProcurador)], r.Valor.Assinaturas.Select(x => (x.Etiqueta, x.Documento)));
        Assert.All(r.Valor.Assinaturas, x => Assert.True(x.Valida, x.Erro));
        var regs = Registros(r.Valor.Conteudo, db.App.AssinadorSped.TamanhoSemAssinaturas(r.Valor.Conteudo));
        Assert.Equal(["0930", "Pedro Procurador", cpfProcurador, "309", "", "pedro@exemplo.com.br", "11988887777"], regs.Single(x => x[0] == "0930" && x[3] == "309"));
        Assert.DoesNotContain(regs, x => x[0] == "0930" && x[3] == "205");

        // Assinar depois: o procurador assina o arquivo gerado só com o contador
        var parcial = (await db.App.Ecf.GerarAsync(empresa, 2025, opcoes, adm, [new(certContador, "900")], ct)).Valor;
        var completo = db.App.Ecf.Assinar(parcial.Conteudo, [new(certProc, "309")]);
        Assert.True(completo.Sucesso, string.Join("; ", completo.Erros));
        Assert.Equal(["SBRCAAEPDR01900", "SBRCAAEPDR02309"], db.App.Ecf.LerAssinaturas(completo.Valor).Select(x => x.Etiqueta));
    }
}
