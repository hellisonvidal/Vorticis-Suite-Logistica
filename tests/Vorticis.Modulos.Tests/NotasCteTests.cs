using System.Text;
using Vorticis.Frota;
using Vorticis.Infra.Tests;

namespace Vorticis.Modulos.Tests;

public class NotasCteTests
{
    /// <summary>XML de NF-e (nfeProc) com o necessário para o transporte: emitente, destinatário, itens, total e volumes.</summary>
    internal static byte[] Xml(string chave, int numero, string destCnpj, decimal valor, params (int Qtd, decimal PesoB, decimal PesoL)[] vols) => Encoding.UTF8.GetBytes($"""
        <?xml version="1.0" encoding="UTF-8"?>
        <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00"><NFe><infNFe Id="NFe{chave}" versao="4.00">
          <ide><cUF>35</cUF><mod>55</mod><serie>1</serie><nNF>{numero}</nNF><dhEmi>2026-03-02T10:00:00-03:00</dhEmi></ide>
          <emit><CNPJ>11222333000181</CNPJ><xNome>Indústria Alfa</xNome><enderEmit><cMun>3509502</cMun><xMun>Campinas</xMun><UF>SP</UF></enderEmit></emit>
          <dest><CNPJ>{destCnpj}</CNPJ><xNome>Comércio Beta</xNome><enderDest><cMun>3304557</cMun><xMun>Rio de Janeiro</xMun><UF>RJ</UF></enderDest></dest>
          <det nItem="1"><prod><xProd>Parafusos</xProd><vProd>{(valor * 0.3m).ToString(System.Globalization.CultureInfo.InvariantCulture)}</vProd></prod></det>
          <det nItem="2"><prod><xProd>Peças de motor</xProd><vProd>{(valor * 0.7m).ToString(System.Globalization.CultureInfo.InvariantCulture)}</vProd></prod></det>
          <total><ICMSTot><vNF>{valor.ToString(System.Globalization.CultureInfo.InvariantCulture)}</vNF></ICMSTot></total>
          <transp><modFrete>0</modFrete>{string.Concat(vols.Select(v => $"<vol><qVol>{v.Qtd}</qVol><pesoL>{v.PesoL.ToString(System.Globalization.CultureInfo.InvariantCulture)}</pesoL><pesoB>{v.PesoB.ToString(System.Globalization.CultureInfo.InvariantCulture)}</pesoB></vol>"))}</transp>
        </infNFe></NFe></nfeProc>
        """);

    [Fact]
    public void Le_a_nfe_com_pesos_volumes_e_municipios()
    {
        var chave = RegrasTmsTests.Chave("11222333000181", "55", 1, 1501);
        var (nota, erro) = LeitorNfeTransporte.Ler(Xml(chave, 1501, "22333444000181", 10000m, (3, 120.5m, 110m), (2, 80m, 75m)));
        Assert.Null(erro);
        Assert.Equal((chave, 1501, new DateOnly(2026, 3, 2), "11222333000181", "22333444000181"), (nota!.Chave, nota.Numero, nota.Emissao, nota.EmitenteDocumento, nota.DestinatarioDocumento));
        Assert.Equal((5, 200.5m, 185m, 200.5m, 10000m, "Peças de motor"), (nota.Volumes, nota.PesoBruto, nota.PesoLiquido, nota.Peso, nota.Valor, nota.Produto));
        Assert.Equal(new ParadaRota("SP", "3509502", "Campinas"), nota.Origem);
        Assert.Equal(new ParadaRota("RJ", "3304557", "Rio de Janeiro"), nota.Destino);
        // sem peso bruto, vale o líquido; chave errada, XML que não é NF-e e entidades externas são recusados
        Assert.Equal(75m, LeitorNfeTransporte.Ler(Xml(chave, 1, "1", 1m, (1, 0m, 75m))).Nota!.Peso);
        var errada = chave[..43] + (char)('0' + (chave[43] - '0' + 1) % 10);
        Assert.Contains("Chave", LeitorNfeTransporte.Ler(Xml(errada, 1, "1", 1m)).Erro);
        Assert.Contains("não é uma NF-e", LeitorNfeTransporte.Ler(Encoding.UTF8.GetBytes("<cteProc/>")).Erro);
        Assert.Contains("XML inválido", LeitorNfeTransporte.Ler(Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><NFe>&e;</NFe>")).Erro);
    }

    [Fact]
    public void Painel_soma_as_notas()
    {
        var a = LeitorNfeTransporte.Ler(Xml(RegrasTmsTests.Chave("11222333000181", "55", 1, 1), 1, "22333444000181", 6000m, (3, 100m, 90m))).Nota!;
        var b = LeitorNfeTransporte.Ler(Xml(RegrasTmsTests.Chave("11222333000181", "55", 1, 2), 2, "33444555000181", 4000m, (2, 50m, 45m))).Nota!;
        var r = ResumoNotasCte.De([a, b], frete: 500m);
        Assert.Equal((2, 5, 150m, 135m, 10000m, 1, 2, 5m), (r.Notas, r.Volumes, r.PesoBruto, r.PesoLiquido, r.Valor, r.Remetentes, r.Destinatarios, r.FretePorValor));
        Assert.Equal("Peças de motor", r.Produto);
        Assert.Equal(0, ResumoNotasCte.De([]).Notas);
    }
}

public class NotasCteIntegracaoTests(BancoDeTeste db) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public async Task Notas_ficam_no_cte_digitado_e_nao_vao_em_dois_ctes()
    {
        BancoDeTeste.ExigirBanco();
        var ct = TestContext.Current.CancellationToken;
        var adm = await db.AdminAsync();
        var empresa = await db.CriarEmpresaAsync("46371829000100");
        var svc = ServicosFrota.De(db.App);
        var alfa = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome, cpf_cnpj) VALUES (@e, 'C1', 'Indústria Alfa', '11222333000181') RETURNING id", ("e", empresa));
        var beta = await db.EscalarAsync<long>("INSERT INTO core.participante (empresa_id, codigo, nome) VALUES (@e, 'C2', 'Comércio Beta') RETURNING id", ("e", empresa));
        Assert.Contains(await svc.ParticipantesAsync(empresa, adm, ct), p => p.Id == alfa && p.Documento == "11222333000181");   // para achar o remetente pelo CNPJ
        var n1 = LeitorNfeTransporte.Ler(NotasCteTests.Xml(RegrasTmsTests.Chave("11222333000181", "55", 1, 10), 10, "22333444000181", 6000m, (3, 100m, 90m))).Nota!;
        var n2 = LeitorNfeTransporte.Ler(NotasCteTests.Xml(RegrasTmsTests.Chave("11222333000181", "55", 1, 11), 11, "22333444000181", 4000m, (2, 50m, 45m))).Nota!;

        var cte1 = (await svc.GravarCteAsync(empresa, RegrasTmsTests.NovoCte(alfa, beta, alfa), adm, ct)).Valor;
        var r = await svc.GravarNotasCteAsync(empresa, cte1.Id, [n1, n2], adm, ct);
        Assert.True(r.Sucesso, string.Join(" ", r.Erros));
        Assert.Equal([10, 11], (await svc.NotasCteAsync(empresa, cte1.Id, adm, ct)).Select(n => n.Numero));
        var salvo = (await svc.CtesAsync(empresa, new(2026, 3, 1), new(2026, 3, 31), null, adm, ct)).Single(c => c.Id == cte1.Id);
        Assert.Equal([n1.Chave, n2.Chave], RegrasTms.ChavesNfe(salvo.ChavesNfe));

        // a mesma NF-e em outro CT-e ativo é recusada; retirar uma nota vale ao gravar de novo
        var cte2 = (await svc.GravarCteAsync(empresa, RegrasTmsTests.NovoCte(alfa, beta, alfa), adm, ct)).Valor;
        Assert.Contains($"já está no CT-e 1-{cte1.Numero}", (await svc.GravarNotasCteAsync(empresa, cte2.Id, [n2], adm, ct)).Erros[0]);
        Assert.True((await svc.GravarNotasCteAsync(empresa, cte1.Id, [n1], adm, ct)).Sucesso);
        Assert.True((await svc.GravarNotasCteAsync(empresa, cte2.Id, [n2], adm, ct)).Sucesso);
        Assert.Single(await svc.NotasCteAsync(empresa, cte1.Id, adm, ct));

        // emitido: as notas não mudam mais
        await db.EscalarAsync<int>("UPDATE tms.cte SET situacao = 'EMITIDO', chave = @k WHERE id = @c RETURNING 1", ("c", cte1.Id), ("k", RegrasTmsTests.Chave("46371829000100", "57", 1, cte1.Numero)));
        Assert.Contains("digitado", (await svc.GravarNotasCteAsync(empresa, cte1.Id, [], adm, ct)).Erros[0]);
    }
}
