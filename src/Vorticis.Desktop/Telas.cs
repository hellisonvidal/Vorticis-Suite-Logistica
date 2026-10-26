namespace Vorticis.Desktop.ViewModels;

/// <summary>
/// Chaves das telas do módulo Contábil (CommandParameter dos itens de menu), mais as comuns do casco
/// (<see cref="TelasComuns"/>) para que o código do módulo use um só nome.
/// </summary>
public static class Telas
{
    public const string Lancamento = "lancamento";
    public const string Estorno = "estorno";
    public const string Periodos = "periodos";
    public const string ReorganizarLancamentos = "reorganizarlancamentos";
    public const string ReorganizarSaldos = "reorganizarsaldos";
    public const string Balancete = "balancete";
    public const string Razao = "razao";
    public const string Diario = "diario";
    public const string PlanoContas = "planocontas";
    public const string CentrosCusto = "centroscusto";
    public const string SaldosIniciais = "saldosiniciais";
    public const string Encerramento = "encerramento";
    public const string Balanco = "balanco";
    public const string Dre = "dre";
    public const string LivrosPdf = "livrospdf";
    public const string ImportacaoReferencial = "importacaoreferencial";
    public const string MapeamentoReferencial = "mapeamentoreferencial";
    public const string ConsultaSaldos = "consultasaldos";
    public const string ConsultaLancamentos = "consultalancamentos";
    public const string ImportarPlano = "importarplano";
    public const string Historicos = "historicos";
    public const string BloqueioLancamentos = "bloqueiolancamentos";
    public const string RelacaoBens = "relacaobens";
    public const string ValorDepreciado = "valordepreciado";
    public const string ValorResidual = "valorresidual";
    public const string DepreciacaoPorGrupo = "depreciacaoporgrupo";
    public const string ExportarLancamentos = "exportarlancamentos";
    public const string Dmpl = "dmpl";
    public const string Dlpa = "dlpa";
    public const string CentroCustoRelatorio = "gerencial.centrocusto";
    public const string OrcadoRealizado = "gerencial.orcadorealizado";
    public const string Orcamento = "gerencial.orcamento";
    public const string RelacaoCustos = "gerencial.custos";
    public const string ReceitaCentro = "gerencial.receitacentro";
    public const string Termos = "termos";
    public const string Bens = "bens";
    public const string GruposDepreciacao = "gruposdepreciacao";
    public const string Depreciacao = "depreciacao";
    public const string Conciliacao = "conciliacao";
    public const string DiferencasSaldos = "diferencassaldos";
    public const string ImportarPlanilhas = "importarplanilhas";
    public const string ImportarExtratos = "importarextratos";
    public const string ImportarLancamentos = "importarlancamentos";
    public const string EcdAssistente = "ecd";
    public const string EcfAssistente = "ecf";

    // Telas comuns (definidas no casco)
    public const string Empresas = TelasComuns.Empresas;
    public const string Contabilidade = TelasComuns.Contabilidade;
    public const string Socios = TelasComuns.Socios;
        public const string Clientes = TelasComuns.Clientes;
    public const string Fornecedores = TelasComuns.Fornecedores;
    public const string Usuarios = TelasComuns.Usuarios;
    public const string Perfis = TelasComuns.Perfis;
    public const string AlterarSenha = TelasComuns.AlterarSenha;
    public const string Conexao = TelasComuns.Conexao;
    public const string Backup = TelasComuns.Backup;
    public const string ConfiguracoesGerais = TelasComuns.ConfiguracoesGerais;
    public const string Certificados = TelasComuns.Certificados;
    public const string Licenca = TelasComuns.Licenca;
    public const string Sobre = TelasComuns.Sobre;
    public const string Inicio = TelasComuns.Inicio;
}
