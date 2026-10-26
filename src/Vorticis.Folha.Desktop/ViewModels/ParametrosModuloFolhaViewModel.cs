using Vorticis.Desktop.Comum;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Cadastros;
using Vorticis.Infra;

namespace Vorticis.Folha.Desktop.ViewModels;

/// <summary>
/// Cadastros › Parâmetros (Folha): encargos patronais, dia de pagamento e padrões do eSocial da empresa ativa. Os encargos usam a mesma
/// gravação de Funcionários › Parâmetros e tabelas legais (que também guarda FPAS, Simples e responsável); o restante fica em
/// folha.configuracao_empresa. A contabilização tem tela própria e aparece como atalho.
/// </summary>
public sealed class ParametrosModuloFolhaViewModel(IContexto contexto) : ParametrosViewModel(contexto)
{
    public override string Titulo => "Parâmetros da Folha";

    public override string Descricao => "Padrões da folha de pagamento da empresa ativa. Cada grupo tem um texto de ajuda; use os botões para abrir as telas com mais detalhes.";

    protected override Permissao PermissaoGravar => Permissao.FolhaParametrosManter;

    private static readonly IReadOnlyList<OpcaoParametro> Graus = [new("1", "1% – risco leve"), new("2", "2% – risco médio"), new("3", "3% – risco grave")];
    private static readonly IReadOnlyList<OpcaoParametro> Ambientes = [new("2", "Produção restrita (testes)"), new("1", "PRODUÇÃO (valendo)")];
    private static readonly IReadOnlyList<OpcaoParametro> Transmissores =
        [new("EMPREGADOR", "Empregador (e-CNPJ da empresa)"), new("PROCURADOR", "Contador procurador (procuração eletrônica)"), new("CONTADOR", "Contador / escritório")];

    protected override IReadOnlyList<GrupoParametros> MontarGrupos()
    {
        var enc = new CampoParametro[]
        {
            new("rat", "RAT (grau de risco)", TipoCampoParametro.Escolha, "Alíquota do seguro de acidentes conforme o risco da atividade.", Graus),
            new("fap", "FAP (0,5000 a 2,0000)", TipoCampoParametro.Texto, "Fator Acidentário de Prevenção informado pela Previdência para a empresa."),
            new("terceiros", "Terceiros (%)", TipoCampoParametro.Texto, "Alíquota das contribuições a outras entidades (Sistema S etc.), conforme o FPAS."),
        };
        var pag = new CampoParametro[]
        {
            new("diaPagamento", "Dia de pagamento dos salários (1 a 31)", TipoCampoParametro.Texto, "Dia do mês usado como padrão para o pagamento da folha."),
        };
        var esocial = new CampoParametro[]
        {
            new("ambiente", "Ambiente padrão", TipoCampoParametro.Escolha, "A tela de transmissão já abre neste ambiente. Teste sempre na produção restrita primeiro.", Ambientes),
            new("transmissor", "Transmissor padrão", TipoCampoParametro.Escolha, "Quem assina e envia os eventos, com o certificado digital correspondente.", Transmissores),
        };
        Observar([.. enc, .. pag, .. esocial]);
        return
        [
            new("Encargos patronais", "Percentuais usados no cálculo dos encargos e das provisões.", enc,
                [Atalho("Abrir Parâmetros e tabelas legais", FolhaTelas.Parametros)]),
            new("Pagamento", "Datas padrão da folha.", pag),
            new("eSocial", "Valores que a tela Obrigações › eSocial › Transmissão já abre selecionados.", esocial,
                [Atalho("Abrir Transmissão do eSocial", FolhaTelas.Transmissao)]),
            new("Contabilização", "As regras de contabilização da folha (contas e histórico) ficam na própria tela.", [],
                [Atalho("Abrir Contabilização", FolhaTelas.Contabilizacao)]),
        ];
    }

    protected override async Task CarregarValoresAsync(VorticisApp app, long empresa, long usuario)
    {
        var cad = app.Folha().Cadastros;
        var p = await cad.ObterParametrosAsync(empresa, usuario);
        var c = await cad.ObterConfiguracaoAsync(empresa, usuario);
        Campo("rat").DefinirEscolha(((int)p.RatAliquota).ToString());
        Campo("fap").Texto = p.Fap.ToString("0.0000", Formato.PtBr);
        Campo("terceiros").Texto = p.TerceirosAliquota.ToString("0.00", Formato.PtBr);
        Campo("diaPagamento").Texto = c.DiaPagamento.ToString();
        Campo("ambiente").DefinirEscolha(c.AmbienteEsocial.ToString());
        Campo("transmissor").DefinirEscolha(c.TransmissorEsocial);
    }

    protected override async Task GravarValoresAsync(VorticisApp app, long empresa, long usuario)
    {
        var cad = app.Folha().Cadastros;
        var fap = Numeros.LerValor(Campo("fap").Texto);
        var terceiros = Numeros.LerValor(Campo("terceiros").Texto);
        if (fap is null || terceiros is null) { Erros.Add("Informe o FAP e a alíquota de terceiros em números."); return; }
        if (!int.TryParse(Campo("diaPagamento").Texto.Trim(), out var dia)) { Erros.Add("Informe o dia de pagamento em número (1 a 31)."); return; }
        var atual = await cad.ObterParametrosAsync(empresa, usuario);
        var r = await cad.GravarParametrosAsync(empresa, atual with { RatAliquota = decimal.Parse(Campo("rat").ValorEscolha ?? "2"), Fap = fap.Value, TerceirosAliquota = terceiros.Value }, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var g = await cad.GravarConfiguracaoAsync(empresa, new ConfiguracaoFolha(dia, int.Parse(Campo("ambiente").ValorEscolha ?? "2"), Campo("transmissor").ValorEscolha ?? "EMPREGADOR"), usuario);
        if (!g.Sucesso) MostrarResultado(g, "");
    }
}
