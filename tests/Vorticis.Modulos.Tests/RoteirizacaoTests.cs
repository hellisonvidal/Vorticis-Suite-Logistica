using Vorticis.Frota;

namespace Vorticis.Modulos.Tests;

public class RoteirizacaoTests
{
    internal static ParadaRota C(string ibge) => BaseMunicipios.Obter(ibge) is { } m ? new ParadaRota(m.Uf, m.Ibge, m.Nome) : throw new ArgumentException(ibge);
    internal static readonly ParadaRota SaoPaulo = C("3550308"), Campinas = C("3509502"), Ribeirao = C("3543402"), Rio = C("3304557"), Curitiba = C("4106902"),
                                       Bh = C("3106200"), Santos = C("3548500"), Sorocaba = C("3552205"), Brasilia = C("5300108"), PortoAlegre = C("4314902"),
                                       Floripa = C("4205407"), Uberlandia = C("3170206"), Cariacica = C("3201308"), Sjc = C("3549904");

    [Fact]
    public void Ordem_mais_curta_com_origem_e_destino_fixos()
    {
        // de São Paulo a Ribeirão Preto passando por Campinas: Campinas fica no caminho
        Assert.Equal([SaoPaulo, Campinas, Ribeirao], RegrasRoteirizacao.Otimizar([SaoPaulo, Ribeirao, Campinas]));
        // destino final fixo no Rio: São José dos Campos fica antes dele, Campinas logo depois de São Paulo
        Assert.Equal([SaoPaulo, Campinas, Sjc, Rio], RegrasRoteirizacao.Otimizar([SaoPaulo, Sjc, Campinas, Rio], fixarDestino: true));
        // sem o destino fixo, a ordem pode terminar em qualquer parada
        var livre = RegrasRoteirizacao.Otimizar([SaoPaulo, Rio, Campinas, Sjc]);
        Assert.Equal(SaoPaulo, livre[0]);
        Assert.True(RegrasRoteirizacao.Distancia(livre, false) <= RegrasRoteirizacao.Distancia([SaoPaulo, Rio, Campinas, Sjc], false));
        // até duas paradas, nada muda
        Assert.Equal([SaoPaulo, Rio], RegrasRoteirizacao.Otimizar([SaoPaulo, Rio]));
    }

    [Fact]
    public void Muitas_paradas_usam_vizinho_mais_proximo_e_2opt()
    {
        ParadaRota[] baguncada = [SaoPaulo, PortoAlegre, Campinas, Brasilia, Santos, Curitiba, Bh, Floripa, Uberlandia, Sorocaba, Cariacica, Ribeirao, Rio];
        var otima = RegrasRoteirizacao.Otimizar(baguncada, voltarOrigem: true);
        Assert.Equal(SaoPaulo, otima[0]);
        Assert.Equal(baguncada.OrderBy(p => p.MunicipioIbge), otima.OrderBy(p => p.MunicipioIbge));     // as mesmas paradas
        Assert.True(RegrasRoteirizacao.Distancia(otima, true) < RegrasRoteirizacao.Distancia(baguncada, true) * 0.6);
        // o 2-opt não deixa cruzamentos: inverter qualquer trecho não encurta
        for (var i = 1; i < otima.Count - 1; i++)
            for (var j = i + 1; j < otima.Count; j++)
            {
                var troca = otima.Take(i).Concat(otima.Skip(i).Take(j - i + 1).Reverse()).Concat(otima.Skip(j + 1)).ToList();
                Assert.True(RegrasRoteirizacao.Distancia(troca, true) >= RegrasRoteirizacao.Distancia(otima, true) - 1e-6);
            }
    }

    [Fact]
    public void Trechos_totais_custo_e_km_informado()
    {
        var trechos = RegrasRoteirizacao.Trechos([SaoPaulo, Campinas, Ribeirao], 1.25m, voltarOrigem: true);
        Assert.Equal(3, trechos.Count);
        Assert.Equal((Ribeirao, SaoPaulo), (trechos[2].De, trechos[2].Para));                          // a volta à origem
        Assert.InRange(trechos[0].KmEstimado, 90m, 120m);                                              // SP–Campinas ~ 85 km em linha reta × 1,25
        var ajustados = trechos.Select((t, i) => i == 0 ? t with { KmEstrada = 95m, KmInformado = 100m } : i == 1 ? t with { KmEstrada = 220m } : t).ToList();
        Assert.Equal((100m, "informado", 220m, "estrada", "estimado"), (ajustados[0].Km, ajustados[0].Origem, ajustados[1].Km, ajustados[1].Origem, ajustados[2].Origem));
        var r = RegrasRoteirizacao.Resumir([new TrechoRoteiro(SaoPaulo, Rio, 400m), new TrechoRoteiro(Rio, Bh, 300m, KmInformado: 440m)], 60m, 4.5m, 120m);
        Assert.Equal((840m, 14m, 3900m), (r.Km, r.Horas, r.CustoTotal));                               // 840 × 4,50 + 120
    }

    [Fact]
    public void Custo_por_km_do_veiculo_e_link_do_google_maps()
    {
        Abastecimento A(decimal km, decimal valor) => new(0, 1, null, new(2026, 3, 1), km, 100, valor, "DIESEL_S10", null);
        Assert.Equal(3.5m, RegrasRoteirizacao.CustoPorKm([A(10000, 1500), A(11000, 1500)], 500));   // (3.000 + 500) / 1.000 km
        Assert.Null(RegrasRoteirizacao.CustoPorKm([A(10000, 1500)], 0));
        var url = RegrasRoteirizacao.UrlGoogleMaps([SaoPaulo, Rio], voltarOrigem: true);
        Assert.Equal("https://www.google.com/maps/dir/-23.5329,-46.6395/-22.9129,-43.2003/-23.5329,-46.6395/data=!4m2!4m1!3e0", url);
    }

    [Fact]
    public void Le_o_trajeto_do_servico_de_rotas()
    {
        const string json = """
            {"code":"Ok","routes":[{"distance":432100.5,"duration":21600,"legs":[{"distance":95300,"duration":3600},{"distance":336800.5,"duration":18000}],
             "geometry":{"type":"LineString","coordinates":[[-46.6395,-23.5329],[-47.0659,-22.9053],[-43.2003,-22.9129]]}}]}
            """;
        var t = RegrasRoteirizacao.LerTrajeto(json)!;
        Assert.Equal((432.1m, 6m), (t.Km, t.Horas));
        Assert.Equal([95.3m, 336.8m], t.KmTrechos);
        Assert.Equal((-22.9053, -47.0659), t.Pontos[1]);
        Assert.Null(RegrasRoteirizacao.LerTrajeto("""{"code":"NoRoute","routes":[]}"""));
    }
}
