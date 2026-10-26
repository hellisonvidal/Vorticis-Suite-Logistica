using Vorticis.Domain.Seguranca;
using Vorticis.Infra;

namespace Vorticis.Desktop.ViewModels;

/// <summary>
/// Cadastros › Bloqueio de lançamentos: data até a qual nenhum lançamento pode ser incluído, alterado ou excluído na empresa ativa
/// (a mesma data da aba Contabilidade de Cadastros › Empresas).
/// </summary>
public sealed class BloqueioLancamentosViewModel(IContexto contexto) : ParametrosViewModel(contexto)
{
    public override string Titulo => "Bloqueio de lançamentos";

    public override string Descricao =>
        "Protege os meses já conferidos ou entregues: lançamentos com data até o dia informado são recusados (inclusão, alteração e exclusão).";

    protected override Permissao PermissaoGravar => Permissao.PlanoContasManter;

    protected override IReadOnlyList<GrupoParametros> MontarGrupos()
    {
        var campos = new CampoParametro[]
        {
            new("dataBloqueio", "Bloquear lançamentos até", TipoCampoParametro.Data,
                "Lançamentos com data até o dia informado são recusados. Deixe em branco para não bloquear."),
        };
        Observar(campos);
        return [new("Bloqueio por data", "Vale para todos os usuários da empresa ativa.", campos, [Atalho("Abrir Fechamento de períodos", Telas.Periodos)])];
    }

    protected override async Task CarregarValoresAsync(VorticisApp app, long empresa, long usuario)
        => Campo("dataBloqueio").Data = (await app.PlanoContas.ObterConfiguracaoAsync(empresa, usuario)).DataBloqueio?.ToDateTime(TimeOnly.MinValue);

    protected override async Task GravarValoresAsync(VorticisApp app, long empresa, long usuario)
    {
        var atual = await app.PlanoContas.ObterConfiguracaoAsync(empresa, usuario);
        var novo = atual with { DataBloqueio = Campo("dataBloqueio").Data is { } d ? DateOnly.FromDateTime(d) : null };
        DateOnly? inicio = null;
        try { inicio = (await app.Empresas.ObterAsync(empresa))?.DataInicioAtividades; } catch (Exception) { /* a conferência da data fica de fora */ }
        var r = await app.PlanoContas.SalvarConfiguracaoAsync(empresa, novo, inicio, usuario);
        if (!r.Sucesso) MostrarResultado(r, "");
    }
}
