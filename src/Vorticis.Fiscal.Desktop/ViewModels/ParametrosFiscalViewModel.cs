using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Duplicatas;
using Vorticis.Fiscal.Sped;
using Vorticis.Infra;

namespace Vorticis.Fiscal.Desktop.ViewModels;

/// <summary>
/// Cadastros › Parâmetros (Fiscal): centros de custo nas notas e padrões da escrituração SPED (EFD) da empresa ativa.
/// Usa os mesmos serviços das funções de digitação e SPED; as contas dos tributos e as regras de contabilização,
/// por serem tabelas, ficam em suas telas e aparecem aqui como atalhos.
/// </summary>
public sealed class ParametrosFiscalViewModel(IContexto contexto) : ParametrosViewModel(contexto)
{
    public override string Titulo => "Parâmetros do Fiscal";

    public override string Descricao =>
        "Configurações fiscais da empresa ativa. Cada grupo é gravado pelo serviço da própria função, com a permissão correspondente.";

    protected override Permissao PermissaoGravar => Permissao.FiscalDocumentosImportar;

    private bool PodeSped => Contexto.Pode(Permissao.FiscalSpedGerar);

    private static readonly IReadOnlyList<OpcaoParametro> Perfis = [new("A", "Perfil A"), new("B", "Perfil B"), new("C", "Perfil C")];
    private static readonly IReadOnlyList<OpcaoParametro> AtividadesIcms = [new("0", "Industrial ou equiparado"), new("1", "Outros")];
    private static readonly IReadOnlyList<OpcaoParametro> NaturezasPj =
        [new("00", "00 – Pessoa jurídica em geral"), new("01", "01 – Sociedade cooperativa"), new("02", "02 – Entidade sujeita ao PIS/Pasep exclusivamente sobre a folha")];
    private static readonly IReadOnlyList<OpcaoParametro> AtividadesContrib =
        [new("0", "Industrial ou equiparado"), new("1", "Prestador de serviços"), new("2", "Comércio"), new("3", "Financeira"), new("4", "Imobiliária"), new("9", "Outros")];

    protected override IReadOnlyList<GrupoParametros> MontarGrupos()
    {
        var notas = new CampoParametro[]
        {
            new("usaCentroCusto", "Informar centro de custo nos itens das notas", TipoCampoParametro.SimNao,
                "As notas aceitam centro de custo no documento e nos itens, e a contabilização leva o centro de custo às partidas. Só pode ser ligado se a empresa usa centros de custo (Cadastros › Empresas, aba Contabilidade)."),
            new("contabilizacaoOnline", "Contabilizar as notas on-line (ao gravar ou importar)", TipoCampoParametro.SimNao,
                "Ligado: cada nota é contabilizada assim que é gravada ou importada, pelas Regras de contabilização, e o menu Contabilizar notas fiscais some. " +
                "Desligado (em lote): as notas são contabilizadas depois, pela função Documentos fiscais › Contabilizar notas fiscais. Exige a permissão de contabilizar."),
        };
        var icms = new CampoParametro[]
        {
            new("icmsPerfil", "Perfil da EFD ICMS/IPI", TipoCampoParametro.Escolha, null, Perfis),
            new("icmsAtividade", "Atividade (ICMS/IPI)", TipoCampoParametro.Escolha, null, AtividadesIcms),
            new("icmsReceita", "Código de receita do ICMS próprio", TipoCampoParametro.Texto, "Código definido pela UF da empresa."),
            new("icmsReceitaSt", "Código de receita do ICMS-ST", TipoCampoParametro.Texto),
            new("icmsDia", "Dia de vencimento do ICMS (1 a 28)", TipoCampoParametro.Texto),
        };
        var contrib = new CampoParametro[]
        {
            new("contribNatureza", "Natureza da pessoa jurídica", TipoCampoParametro.Escolha, null, NaturezasPj),
            new("contribAtividade", "Atividade (PIS/COFINS)", TipoCampoParametro.Escolha, null, AtividadesContrib),
        };
        var duplicatas = new CampoParametro[]
        {
            new("dupAVista", "Gerar duplicata à vista nas notas sem cobrança", TipoCampoParametro.SimNao,
                "Nota sem parcelas (cobrança) no XML, ou digitada sem parcelas, gera uma duplicata única. NFC-e só gera se o XML trouxer cobrança."),
            new("dupAVistaDias", "Dias até o vencimento da duplicata à vista", TipoCampoParametro.Texto, "0 = vence na data de emissão."),
            new("contaFornecedores", "Conta de fornecedores", TipoCampoParametro.Texto,
                "Código da conta analítica. É a contrapartida das notas de entrada com duplicatas e a conta debitada no pagamento."),
            new("contaClientes", "Conta de clientes", TipoCampoParametro.Texto,
                "Código da conta analítica. É a contrapartida das notas de saída com duplicatas e a conta creditada no recebimento."),
            new("contaJurosPagos", "Juros pagos (despesa)", TipoCampoParametro.Texto),
            new("contaMultaPaga", "Multa paga (despesa)", TipoCampoParametro.Texto),
            new("contaDescontoObtido", "Descontos obtidos (receita)", TipoCampoParametro.Texto),
            new("contaJurosRecebidos", "Juros recebidos (receita)", TipoCampoParametro.Texto),
            new("contaMultaRecebida", "Multa recebida (receita)", TipoCampoParametro.Texto),
            new("contaDescontoConcedido", "Descontos concedidos (despesa)", TipoCampoParametro.Texto),
            new("historicoPagamento", "Histórico do pagamento", TipoCampoParametro.Texto, "Pode usar {DOCUMENTO}, {PARTICIPANTE}, {DATA} e {VALOR}."),
            new("historicoRecebimento", "Histórico do recebimento", TipoCampoParametro.Texto, "Pode usar {DOCUMENTO}, {PARTICIPANTE}, {DATA} e {VALOR}."),
        };
        Observar([.. notas, .. duplicatas, .. icms, .. contrib]);
        return
        [
            new("Lançamento das notas", "Opções usadas na digitação de notas de entrada e saída.", notas),
            new("Duplicatas e baixas (contas a pagar e a receber)", "Contas contábeis e históricos usados na contabilização das notas com duplicatas e nas baixas. Exige a função de manter duplicatas.", duplicatas),
            new("EFD ICMS/IPI (SPED Fiscal)", "Escolhas que se repetem a cada mês; o assistente já abre preenchido. Exige permissão de gerar SPED.", icms),
            new("EFD-Contribuições (PIS/COFINS)", "Escolhas que se repetem a cada mês na EFD-Contribuições. Exige permissão de gerar SPED.", contrib),
            new("Contabilização e tributos", "Tabelas que têm tela própria.", [],
                [Atalho("Abrir Regras de contabilização", Telas.RegrasContabilizacao), Atalho("Abrir Contas dos tributos", Telas.ContasTributos),
                 Atalho("Abrir Fechamento do período fiscal", Telas.FechamentoFiscal)]),
        ];
    }

    protected override async Task CarregarValoresAsync(VorticisApp app, long empresa, long usuario)
    {
        var f = await app.Fiscal().Digitacao.ObterConfiguracaoAsync(empresa, usuario);
        if (f.Sucesso) Campo("usaCentroCusto").Marcado = f.Valor.UsaCentroCusto;
        var online = await app.Fiscal().Contabilizacao.ContabilizacaoOnlineAsync(empresa, usuario);
        if (online.Sucesso) Campo("contabilizacaoOnline").Marcado = online.Valor;
        var d = await app.Fiscal().Duplicatas.ObterConfiguracaoAsync(empresa, usuario);
        if (d.Sucesso)
        {
            var x = d.Valor;
            Campo("dupAVista").Marcado = x.GerarAVista;
            Campo("dupAVistaDias").Texto = x.AVistaDias.ToString();
            Campo("contaFornecedores").Texto = x.ContaFornecedores ?? "";
            Campo("contaClientes").Texto = x.ContaClientes ?? "";
            Campo("contaJurosPagos").Texto = x.ContaJurosPagos ?? "";
            Campo("contaMultaPaga").Texto = x.ContaMultaPaga ?? "";
            Campo("contaDescontoObtido").Texto = x.ContaDescontoObtido ?? "";
            Campo("contaJurosRecebidos").Texto = x.ContaJurosRecebidos ?? "";
            Campo("contaMultaRecebida").Texto = x.ContaMultaRecebida ?? "";
            Campo("contaDescontoConcedido").Texto = x.ContaDescontoConcedido ?? "";
            Campo("historicoPagamento").Texto = x.HistoricoPagamento;
            Campo("historicoRecebimento").Texto = x.HistoricoRecebimento;
        }
        var e = await app.Fiscal().Efd.ConfiguracaoAsync(empresa, usuario);
        var c = e.Sucesso ? e.Valor : new ConfiguracaoEfd();
        Campo("icmsPerfil").DefinirEscolha(c.IcmsPerfil.ToString());
        Campo("icmsAtividade").DefinirEscolha(c.IcmsIndicadorAtividade.ToString());
        Campo("icmsReceita").Texto = c.IcmsCodigoReceita ?? "";
        Campo("icmsReceitaSt").Texto = c.IcmsCodigoReceitaSt ?? "";
        Campo("icmsDia").Texto = c.IcmsDiaVencimento.ToString();
        Campo("contribNatureza").DefinirEscolha(c.ContribNaturezaPj);
        Campo("contribAtividade").DefinirEscolha(c.ContribIndicadorAtividade.ToString());
    }

    protected override async Task GravarValoresAsync(VorticisApp app, long empresa, long usuario)
    {
        var f = await app.Fiscal().Digitacao.GravarConfiguracaoAsync(empresa, Campo("usaCentroCusto").Marcado, usuario);
        if (!f.Sucesso) { MostrarResultado(f, ""); return; }
        if (Contexto.Pode(Permissao.FiscalContabilizar))
        {
            var o = await app.Fiscal().Contabilizacao.GravarContabilizacaoOnlineAsync(empresa, Campo("contabilizacaoOnline").Marcado, usuario);
            if (!o.Sucesso) { MostrarResultado(o, ""); return; }
            await Contexto.AtualizarRecursosAsync();   // mostra ou esconde o menu Contabilizar notas fiscais
        }
        if (Contexto.Pode(Permissao.FiscalDuplicatasManter))
        {
            if (!int.TryParse(Campo("dupAVistaDias").Texto.Trim(), out var diasAVista)) { Erros.Add("Informe os dias até o vencimento da duplicata à vista em número (0 a 365)."); return; }
            string? Conta(string chave) => Campo(chave).Texto;
            var dup = await app.Fiscal().Duplicatas.GravarConfiguracaoAsync(empresa, new ConfiguracaoDuplicatas(Campo("dupAVista").Marcado, diasAVista, Conta("contaFornecedores"),
                Conta("contaClientes"), Conta("contaJurosPagos"), Conta("contaMultaPaga"), Conta("contaDescontoObtido"), Conta("contaJurosRecebidos"), Conta("contaMultaRecebida"),
                Conta("contaDescontoConcedido"), Campo("historicoPagamento").Texto, Campo("historicoRecebimento").Texto), usuario);
            if (!dup.Sucesso) { MostrarResultado(dup, ""); return; }
        }
        if (!PodeSped) return;
        if (!int.TryParse(Campo("icmsDia").Texto.Trim(), out var dia)) { Erros.Add("Informe o dia de vencimento do ICMS em número (1 a 28)."); return; }
        static string? T(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var atual = (await app.Fiscal().Efd.ConfiguracaoAsync(empresa, usuario)).Valor ?? new ConfiguracaoEfd();
        var r = await app.Fiscal().Efd.SalvarConfiguracaoAsync(empresa, atual with
        {
            IcmsPerfil = Campo("icmsPerfil").ValorEscolha![0], IcmsIndicadorAtividade = Campo("icmsAtividade").ValorEscolha![0],
            IcmsCodigoReceita = T(Campo("icmsReceita").Texto), IcmsCodigoReceitaSt = T(Campo("icmsReceitaSt").Texto), IcmsDiaVencimento = dia,
            ContribNaturezaPj = Campo("contribNatureza").ValorEscolha!, ContribIndicadorAtividade = Campo("contribAtividade").ValorEscolha![0],
        }, usuario);
        if (!r.Sucesso) MostrarResultado(r, "");
    }
}
