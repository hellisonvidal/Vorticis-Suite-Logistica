namespace Vorticis.Domain.Seguranca;

/// <summary>
/// Funções do sistema que um perfil pode liberar, sempre no âmbito de uma empresa.
/// A administração do sistema (empresas, usuários, perfis, conexão, backup) não é delegável por perfil:
/// pertence ao usuário marcado como administrador.
/// </summary>
public enum Permissao
{
    LancamentoIncluir,
    LancamentoEstornar,
    SaldosIniciais,
    PeriodoFechar,
    PeriodoReabrir,
    ExercicioEncerrar,
    PlanoContasManter,
    CentroCustoManter,
    PlanoReferencialMapear,
    EmpresaResponsaveisManter,
    RelatorioBalancete,
    RelatorioRazao,
    RelatorioDiario,
    RelatorioDemonstracoes,
    RelatorioExportar,

    /// <summary>Manter o orçamento anual (Relatórios › Gerenciais › Orçado x Realizado).</summary>
    OrcamentoManter,
    SpedEcdGerar,
    SpedEcfGerar,

    /// <summary>Marcar obrigações como cumpridas e manter as obrigações personalizadas da empresa (calendário de obrigações).</summary>
    ObrigacoesManter,

    // Folha de pagamento
    FolhaConsultar,
    FolhaCadastrosManter,
    FolhaParametrosManter,
    FolhaEventosLancar,
    FolhaCalcular,
    FolhaFechar,
    FolhaReabrir,
    FolhaContabilizar,
    FolhaESocial,
    FolhaRelatorios,

    // Módulo Fiscal
    FiscalDocumentosImportar,
    FiscalDocumentosConsultar,
    FiscalContabilizar,
    FiscalApurar,
    FiscalLalurManter,
    FiscalRetencoesManter,
    FiscalRelatorios,
    FiscalSpedGerar,
    FiscalDuplicatasManter,
    FiscalDuplicatasBaixar,

    // Módulo Escritório
    EscritorioTarefas,
    EscritorioHonorarios,
    EscritorioRelatorios,

    // Módulo Gerencial (antigo ERP)
    ErpVendas,
    ErpCompras,
    ErpEstoque,
    ErpTransporte,
    ErpRelatorios,

    // Módulo Condomínio
    CondominioCadastros,
    CondominioFinanceiro,
    CondominioRelatorios,

    // Módulo Frota
    FrotaCadastros,
    FrotaOperacao,
    FrotaRelatorios,
    FrotaManutencao,
    FrotaAprovar,

    /// <summary>Exclusiva do administrador; não aparece no catálogo de perfis.</summary>
    AdministrarSistema,
}

public sealed record DefinicaoPermissao(Permissao Permissao, string Codigo, string Grupo, string Descricao);

/// <param name="Implantacao">
/// Sessão do usuário de implantação (vorticis/vorticis, só enquanto o banco não tem administrador): não é um usuário do
/// banco (Id 0), não é administrador e só pode cadastrar o primeiro administrador.
/// </param>
public sealed record SessaoUsuario(long Id, string Login, string Nome, bool Administrador, bool DeveTrocarSenha, bool Implantacao = false);

/// <summary>Permissões que um usuário tem em uma empresa (o administrador tem todas).</summary>
public sealed record PermissoesEfetivas(bool Administrador, IReadOnlySet<Permissao> Liberadas, string? NomePerfil)
{
    public static readonly PermissoesEfetivas Nenhuma = new(false, new HashSet<Permissao>(), null);

    public bool Pode(Permissao p) => Administrador || (p != Permissao.AdministrarSistema && Liberadas.Contains(p));

    public bool TemAcesso => Administrador || NomePerfil is not null;
}

public static class Permissoes
{
    /// <summary>Catálogo exibido na tela de perfis. O código é o valor gravado em core.perfil_permissao.</summary>
    public static readonly IReadOnlyList<DefinicaoPermissao> Catalogo =
    [
        new(Permissao.LancamentoIncluir, "LANCAMENTO_INCLUIR", "Lançamentos", "Incluir lançamentos contábeis"),
        new(Permissao.LancamentoEstornar, "LANCAMENTO_ESTORNAR", "Lançamentos", "Estornar lançamentos"),
        new(Permissao.SaldosIniciais, "SALDOS_INICIAIS", "Lançamentos", "Lançar saldos iniciais (implantação)"),
        new(Permissao.PeriodoFechar, "PERIODO_FECHAR", "Lançamentos", "Fechar períodos"),
        new(Permissao.PeriodoReabrir, "PERIODO_REABRIR", "Lançamentos", "Reabrir períodos fechados"),
        new(Permissao.ExercicioEncerrar, "EXERCICIO_ENCERRAR", "Lançamentos", "Encerrar o exercício (apuração do resultado) e desfazer o encerramento"),
        new(Permissao.PlanoContasManter, "PLANO_CONTAS_MANTER", "Cadastros", "Incluir, alterar e excluir contas do plano de contas"),
        new(Permissao.CentroCustoManter, "CENTRO_CUSTO_MANTER", "Cadastros", "Incluir, alterar e excluir centros de custo"),
        new(Permissao.PlanoReferencialMapear, "PLANO_REFERENCIAL_MAPEAR", "Cadastros", "Vincular contas ao plano referencial da RFB"),
        new(Permissao.EmpresaResponsaveisManter, "EMPRESA_RESPONSAVEIS_MANTER", "Cadastros", "Manter sócios, contadores e escritório contábil da empresa"),
        new(Permissao.RelatorioBalancete, "RELATORIO_BALANCETE", "Relatórios", "Balancete de verificação"),
        new(Permissao.RelatorioRazao, "RELATORIO_RAZAO", "Relatórios", "Livro Razão"),
        new(Permissao.RelatorioDiario, "RELATORIO_DIARIO", "Relatórios", "Livro Diário"),
        new(Permissao.RelatorioDemonstracoes, "RELATORIO_DEMONSTRACOES", "Relatórios", "Balanço Patrimonial, DRE e demais demonstrações"),
        new(Permissao.RelatorioExportar, "RELATORIO_EXPORTAR", "Relatórios", "Exportar relatórios para planilha (CSV)"),
        new(Permissao.OrcamentoManter, "ORCAMENTO_MANTER", "Relatórios", "Manter o orçamento anual (Orçado x Realizado)"),
        new(Permissao.SpedEcdGerar, "SPED_ECD_GERAR", "Integrações", "Gerar e assinar o arquivo da ECD (SPED Contábil)"),
        new(Permissao.SpedEcfGerar, "SPED_ECF_GERAR", "Integrações", "Gerar e assinar o arquivo da ECF (Escrituração Contábil Fiscal)"),
        new(Permissao.ObrigacoesManter, "OBRIGACOES_MANTER", "Cadastros", "Calendário de obrigações: marcar como cumpridas e manter obrigações personalizadas"),
        new(Permissao.FolhaConsultar, "FOLHA_CONSULTAR", "Folha de pagamento", "Consultar funcionários, rubricas e a folha calculada"),
        new(Permissao.FolhaCadastrosManter, "FOLHA_CADASTROS_MANTER", "Folha de pagamento", "Manter funcionários, dependentes, cargos, departamentos e rubricas"),
        new(Permissao.FolhaParametrosManter, "FOLHA_PARAMETROS_MANTER", "Folha de pagamento", "Manter parâmetros de encargos (RAT, FAP, terceiros) e regras de contabilização da folha"),
        new(Permissao.FolhaEventosLancar, "FOLHA_EVENTOS_LANCAR", "Folha de pagamento", "Lançar eventos variáveis (horas extras, faltas, comissões...)"),
        new(Permissao.FolhaCalcular, "FOLHA_CALCULAR", "Folha de pagamento", "Calcular folha mensal, férias, 13º salário, rescisões e provisões"),
        new(Permissao.FolhaFechar, "FOLHA_FECHAR", "Folha de pagamento", "Fechar a competência da folha"),
        new(Permissao.FolhaReabrir, "FOLHA_REABRIR", "Folha de pagamento", "Reabrir a competência da folha"),
        new(Permissao.FolhaContabilizar, "FOLHA_CONTABILIZAR", "Folha de pagamento", "Contabilizar a folha e as provisões e desfazer a contabilização"),
        new(Permissao.FolhaESocial, "FOLHA_ESOCIAL", "Folha de pagamento", "Gerar e exportar os eventos do eSocial"),
        new(Permissao.FolhaRelatorios, "FOLHA_RELATORIOS", "Folha de pagamento", "Holerites, resumo da folha, encargos, provisões e recibos"),
        new(Permissao.FiscalDocumentosImportar, "FISCAL_DOCUMENTOS_IMPORTAR", "Fiscal", "Importar XML e digitar notas fiscais (NF-e, NFC-e, NFS-e), manter produtos e excluir documentos"),
        new(Permissao.FiscalDocumentosConsultar, "FISCAL_DOCUMENTOS_CONSULTAR", "Fiscal", "Consultar documentos fiscais importados"),
        new(Permissao.FiscalContabilizar, "FISCAL_CONTABILIZAR", "Fiscal", "Manter regras de contabilização e contabilizar (ou desfazer) notas fiscais"),
        new(Permissao.FiscalApurar, "FISCAL_APURAR", "Fiscal", "Apurar tributos (Simples Nacional, Lucro Presumido/Real, PIS/COFINS) e contabilizar as provisões"),
        new(Permissao.FiscalLalurManter, "FISCAL_LALUR_MANTER", "Fiscal", "Manter LALUR/LACS: adições, exclusões e prejuízos fiscais"),
        new(Permissao.FiscalRetencoesManter, "FISCAL_RETENCOES_MANTER", "Fiscal", "Manter retenções, guias de recolhimento e histórico de receita/folha"),
        new(Permissao.FiscalRelatorios, "FISCAL_RELATORIOS", "Fiscal", "Relatórios fiscais: livros de entradas e saídas, resumos, apurações e guias"),
        new(Permissao.FiscalSpedGerar, "FISCAL_SPED_GERAR", "Fiscal", "Gerar e assinar os arquivos do SPED Fiscal (EFD ICMS/IPI) e da EFD-Contribuições"),
        new(Permissao.FiscalDuplicatasManter, "FISCAL_DUPLICATAS_MANTER", "Fiscal", "Manter duplicatas (contas a pagar e a receber): incluir, alterar, cancelar e excluir parcelas"),
        new(Permissao.FiscalDuplicatasBaixar, "FISCAL_DUPLICATAS_BAIXAR", "Fiscal", "Baixar duplicatas (pagamento e recebimento), estornar baixas e conciliar com o extrato bancário"),
        new(Permissao.EscritorioTarefas, "ESCRITORIO_TAREFAS", "Escritório", "Tarefas, prazos e protocolos de documentos do escritório"),
        new(Permissao.EscritorioHonorarios, "ESCRITORIO_HONORARIOS", "Escritório", "Contratos de honorários, geração e recebimento das cobranças"),
        new(Permissao.EscritorioRelatorios, "ESCRITORIO_RELATORIOS", "Escritório", "Relatórios do escritório: carteira de clientes, honorários e tarefas"),
        new(Permissao.ErpVendas, "ERP_VENDAS", "Gerencial", "Pedidos de venda e faturamento (nota fiscal de saída e contas a receber)"),
        new(Permissao.ErpCompras, "ERP_COMPRAS", "Gerencial", "Pedidos de compra e recebimento de mercadorias (nota de entrada e contas a pagar)"),
        new(Permissao.ErpEstoque, "ERP_ESTOQUE", "Gerencial", "Estoque: posição, movimentos e ajustes"),
        new(Permissao.ErpTransporte, "ERP_TRANSPORTE", "Gerencial", "Transporte: veículos, motoristas, conhecimentos (CT-e) e manifestos (MDF-e)"),
        new(Permissao.ErpRelatorios, "ERP_RELATORIOS", "Gerencial", "Painel do empresário e relatórios gerenciais"),
        new(Permissao.CondominioCadastros, "CONDOMINIO_CADASTROS", "Condomínio", "Unidades, condôminos, áreas comuns, reservas e ocorrências"),
        new(Permissao.CondominioFinanceiro, "CONDOMINIO_FINANCEIRO", "Condomínio", "Taxas condominiais, cobranças, recebimentos e despesas do condomínio"),
        new(Permissao.CondominioRelatorios, "CONDOMINIO_RELATORIOS", "Condomínio", "Prestação de contas e inadimplência"),
        new(Permissao.FrotaCadastros, "FROTA_CADASTROS", "Frota", "Veículos, motoristas e documentos com vencimento"),
        new(Permissao.FrotaOperacao, "FROTA_OPERACAO", "Frota", "Abastecimentos e viagens"),
        new(Permissao.FrotaRelatorios, "FROTA_RELATORIOS", "Frota", "Painel da frota e relatórios de custos, consumo, viagens, vencimentos e manutenção"),
        new(Permissao.FrotaManutencao, "FROTA_MANUTENCAO", "Frota", "Manutenção: planos, peças e serviços, ordens de serviço e notas de compra"),
        new(Permissao.FrotaAprovar, "FROTA_APROVAR", "Frota", "Autorizar ou reprovar ordens de serviço de manutenção"),
    ];

    private static readonly Dictionary<string, Permissao> PorCodigo = Catalogo.ToDictionary(d => d.Codigo, d => d.Permissao);
    private static readonly Dictionary<Permissao, DefinicaoPermissao> PorPermissao = Catalogo.ToDictionary(d => d.Permissao);

    public static string Codigo(this Permissao p) => PorPermissao.TryGetValue(p, out var d)
        ? d.Codigo
        : throw new ArgumentException($"{p} não é configurável por perfil.", nameof(p));

    public static string Descricao(this Permissao p) => p == Permissao.AdministrarSistema
        ? "Administração do sistema"
        : PorPermissao[p].Descricao;

    /// <summary>Converte códigos gravados no banco; códigos desconhecidos (de versões futuras) são ignorados.</summary>
    public static IReadOnlySet<Permissao> DeCodigos(IEnumerable<string> codigos) =>
        codigos.Select(c => PorCodigo.TryGetValue(c, out var p) ? (Permissao?)p : null).OfType<Permissao>().ToHashSet();
}
