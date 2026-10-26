using Vorticis.Application.Cadastros;
using Vorticis.Application.Contabil;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;

namespace Vorticis.Desktop.ViewModels;

/// <summary>
/// Cadastros › Parâmetros (Contábil): regras de lançamento, centros de custo, contabilidade gerencial, participantes e contas de encerramento da empresa ativa.
/// Reaproveita a configuração contábil da empresa (a mesma da aba Contabilidade de Cadastros › Empresas); nada é guardado em duplicidade.
/// </summary>
public sealed class ParametrosContabilViewModel(IContexto contexto) : ParametrosViewModel(contexto)
{
    public override string Titulo => "Parâmetros do Contábil";

    public override string Descricao =>
        "Regras de escrituração da empresa ativa. As mesmas opções aparecem na aba Contabilidade de Cadastros › Empresas; alterar aqui ou lá dá no mesmo.";

    protected override Permissao PermissaoGravar => Permissao.PlanoContasManter;

    protected override IReadOnlyList<GrupoParametros> MontarGrupos()
    {
        var lanc = new CampoParametro[]
        {
            new("usaEstorno", "Usar estorno de lançamentos", TipoCampoParametro.SimNao,
                "Ligado: lançamentos gravados são corrigidos por estorno. Desligado: Editar altera o próprio lançamento e Excluir o remove (sempre com registro na Auditoria)."),
            new("exigeDocumento", "Exigir número do documento nas partidas", TipoCampoParametro.SimNao,
                "Cada partida do lançamento precisa do número do documento (saldos iniciais, encerramento e estornos ficam isentos)."),
        };
        var cc = new CampoParametro[]
        {
            new("usaCentroCusto", "Usar centros de custo", TipoCampoParametro.SimNao,
                "Liga o uso de centros de custo nos lançamentos e nas telas que os mostram."),
            new("usaGerencial", "Contabilidade gerencial", TipoCampoParametro.SimNao,
                "Ligado: o menu Relatórios › Gerenciais (centro de custo, orçamento, orçado x realizado, custos e receitas por centro) aparece. Desligado: fica escondido."),
        };
        var part = new CampoParametro[]
        {
            new("usaParticipantes", "Plano de contas com participantes", TipoCampoParametro.SimNao,
                "Clientes e fornecedores ficam dentro das contas contábeis; essas contas exigem o código do participante."),
        };
        var enc = new CampoParametro[]
        {
            new("contaApuracao", "Conta de apuração do resultado", TipoCampoParametro.Texto, "Código da conta analítica, por exemplo 2.3.03.001."),
            new("contaLucros", "Conta de lucros ou superávit acumulados", TipoCampoParametro.Texto),
            new("contaPrejuizos", "Conta de prejuízos ou déficit acumulados", TipoCampoParametro.Texto),
        };
        var sped = new CampoParametro[]
        {
            new("grandePorte", "Empresa de grande porte", TipoCampoParametro.SimNao, "Informação usada na ECD e nas apurações."),
        };
        var tipo = new CampoParametro[]
        {
            new("tipoContabilizacao", "Tipo de contabilização", TipoCampoParametro.Escolha,
                "On-line: cada nota, baixa de duplicata e folha gera o lançamento na hora (como sempre foi). Em lote: os módulos só registram pendências e você gera os lançamentos em Integrações › Exportar › Contabilização em lote.",
                [new("ONLINE", "On-line (o lançamento é gerado na hora)"), new("LOTE", "Em lote (os módulos geram pendências)")]),
        };
        Observar([.. lanc, .. cc, .. part, .. enc, .. sped, .. tipo]);
        return
        [
            new("Lançamentos", "Como os lançamentos são digitados e corrigidos. O bloqueio por data fica em Cadastros › Bloqueio de lançamentos.", lanc,
                [Atalho("Abrir Bloqueio de lançamentos", Telas.BloqueioLancamentos)]),
            new("Centros de custo e contabilidade gerencial", "Recursos opcionais de gestão. A máscara dos centros fica na tela Centros de custo.", cc,
                [Atalho("Abrir Centros de custo", Telas.CentrosCusto)]),
            new("Tipo de contabilização", "Quando os documentos dos outros módulos viram lançamentos contábeis. Não altera os lançamentos já gerados.", tipo,
                [Atalho("Abrir Contabilização em lote", TelasComuns.ContabilizacaoLote)]),
            new("Participantes", "Clientes e fornecedores como contas do plano (controle por participante).", part),
            new("Encerramento do exercício", "Contas padrão usadas pela tela Encerramento do exercício.", enc,
                [Atalho("Abrir Encerramento do exercício", Telas.Encerramento)]),
            new("SPED Contábil (ECD) e ECF", "Dados da empresa usados na geração dos arquivos. O restante (responsáveis, contador e procuração eletrônica) fica nos cadastros.", sped,
                [Atalho("Abrir Empresas", Telas.Empresas)]),
        ];
    }

    protected override async Task CarregarValoresAsync(VorticisApp app, long empresa, long usuario)
    {
        var c = await app.PlanoContas.ObterConfiguracaoAsync(empresa, usuario);
        var usaPart = await app.Participantes.UsaParticipantesAsync(empresa);
        Campo("usaEstorno").Marcado = c.UsaEstorno;
        Campo("exigeDocumento").Marcado = c.ExigeDocumento;
        Campo("usaCentroCusto").Marcado = c.UsaCentroCusto;
        Campo("usaGerencial").Marcado = c.UsaGerencial;
        Campo("usaParticipantes").Marcado = usaPart;
        Campo("contaApuracao").Texto = c.ContaApuracao ?? "";
        Campo("contaLucros").Texto = c.ContaLucros ?? "";
        Campo("contaPrejuizos").Texto = c.ContaPrejuizos ?? "";
        Campo("grandePorte").Marcado = c.GrandePorte;
        var modo = await app.Pendencias.ModoAsync(empresa, usuario);
        Campo("tipoContabilizacao").DefinirEscolha(modo.Sucesso && modo.Valor == ModoContabilizacao.Lote ? "LOTE" : "ONLINE");
    }

    protected override async Task GravarValoresAsync(VorticisApp app, long empresa, long usuario)
    {
        var atual = await app.PlanoContas.ObterConfiguracaoAsync(empresa, usuario);
        var novo = atual with
        {
            UsaEstorno = Campo("usaEstorno").Marcado,
            ExigeDocumento = Campo("exigeDocumento").Marcado,
            UsaCentroCusto = Campo("usaCentroCusto").Marcado,
            UsaGerencial = Campo("usaGerencial").Marcado,
            ContaApuracao = Campo("contaApuracao").Texto,
            ContaLucros = Campo("contaLucros").Texto,
            ContaPrejuizos = Campo("contaPrejuizos").Texto,
            GrandePorte = Campo("grandePorte").Marcado,
        };
        DateOnly? inicio = null;
        try { inicio = (await app.Empresas.ObterAsync(empresa))?.DataInicioAtividades; } catch (Exception) { /* a conferência da data fica de fora */ }
        var r = await app.PlanoContas.SalvarConfiguracaoAsync(empresa, novo, inicio, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var modoNovo = Campo("tipoContabilizacao").ValorEscolha == "LOTE" ? ModoContabilizacao.Lote : ModoContabilizacao.Online;
        var m = await app.Pendencias.GravarModoAsync(empresa, modoNovo, usuario);
        if (!m.Sucesso) { MostrarResultado(m, ""); return; }
        await Contexto.AtualizarRecursosAsync();
        var usa = Campo("usaParticipantes").Marcado;
        if (usa != await app.Participantes.UsaParticipantesAsync(empresa))
        {
            var p = await app.Participantes.DefinirUsaParticipantesAsync(empresa, usa, usuario);
            if (!p.Sucesso) { MostrarResultado(p, ""); return; }
        }
    }
}
