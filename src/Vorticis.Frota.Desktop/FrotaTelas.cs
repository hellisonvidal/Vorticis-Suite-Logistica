namespace Vorticis.Frota.Desktop;

public static class FrotaTelas
{
    public const string Veiculos = "Frota.Veiculos";
    public const string Motoristas = "Frota.Motoristas";
    public const string Frotas = "Frota.Frotas";
    public const string TabelasFrete = "Frota.TabelasFrete";
    public const string DocumentosFrota = "Frota.DocumentosFrota";
    public const string Rotas = "Frota.Rotas";
    public const string PontosParada = "Frota.PontosParada";

    public const string Painel = "Frota.Painel";
    public const string Abastecimentos = "Frota.Abastecimentos";
    public const string Viagens = "Frota.Viagens";
    public const string Diarias = "Frota.Diarias";
    public const string Coletas = "Frota.Coletas";
    public const string Entregas = "Frota.Entregas";
    public const string Pedagio = "Frota.Pedagio";
    public const string LayoutsPedagio = "Frota.LayoutsPedagio";

    public const string Cotacoes = "Frota.Cotacoes";
    public const string Ctes = "Frota.Ctes";
    public const string Manifestos = "Frota.Manifestos";
    public const string FaturasFrete = "Frota.FaturasFrete";

    public const string PlanosManutencao = "Frota.PlanosManutencao";
    public const string ItensManutencao = "Frota.ItensManutencao";
    public const string OrdensServico = "Frota.OrdensServico";
    public const string AutorizacaoServicos = "Frota.AutorizacaoServicos";
    public const string NotasCompra = "Frota.NotasCompra";

    public const string MonitorMultas = "Frota.MonitorMultas";
    public const string Infracoes = "Frota.Infracoes";
    public const string IndicacaoCondutor = "Frota.IndicacaoCondutor";
    public const string RecursosMultas = "Frota.RecursosMultas";
    public const string ParcelamentoMultas = "Frota.ParcelamentoMultas";

    public const string RelCustosVeiculo = "Frota.RelCustosVeiculo";
    public const string RelConsumoCombustivel = "Frota.RelConsumoCombustivel";
    public const string RelViagens = "Frota.RelViagens";
    public const string RelVencimentos = "Frota.RelVencimentos";
    public const string RelOrdensServico = "Frota.RelOrdensServico";
    public const string RelAprovacoes = "Frota.RelAprovacoes";
    public const string RelNotasCompra = "Frota.RelNotasCompra";
    public const string RelManutencaoPreventiva = "Frota.RelManutencaoPreventiva";
    public const string RelHistoricoVeiculos = "Frota.RelHistoricoVeiculos";

    public const string RelMultasVeiculo = "Frota.RelMultasVeiculo";
    public const string RelMultasCondutor = "Frota.RelMultasCondutor";
    public const string RelCustosMultas = "Frota.RelCustosMultas";
    public const string RelPontuacaoCnh = "Frota.RelPontuacaoCnh";

    public const string RelTabelasFrete = "Frota.RelTabelasFrete";
    public const string RelCtes = "Frota.RelCtes";
    public const string RelManifestos = "Frota.RelManifestos";
    public const string RelPrazosEntrega = "Frota.RelPrazosEntrega";
    public const string RelFaturasFrete = "Frota.RelFaturasFrete";
    public const string RelColetas = "Frota.RelColetas";
    public const string RelDiarias = "Frota.RelDiarias";
    public const string RelResumoOperacional = "Frota.RelResumoOperacional";

    public static class RelatoriosFrota
    {
        public static readonly string[] Todos =
        {
            RelCustosVeiculo, RelConsumoCombustivel, RelViagens, RelVencimentos,
            RelOrdensServico, RelAprovacoes, RelNotasCompra, RelManutencaoPreventiva,
            RelHistoricoVeiculos, RelMultasVeiculo, RelMultasCondutor, RelCustosMultas,
            RelPontuacaoCnh, RelTabelasFrete, RelCtes, RelManifestos, RelPrazosEntrega,
            RelFaturasFrete, RelColetas, RelDiarias, RelResumoOperacional
        };
    }
}