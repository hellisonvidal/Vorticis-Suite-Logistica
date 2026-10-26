using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Contabil;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.ViewModels;

public sealed record CentroLinha(CentroCustoDetalhe Centro, string? CodigoPai)
{
    public string Numero => Centro.Id.ToString("0000");
    public string Codigo => Centro.Codigo;
    /// <summary>Código recuado pelo nível, para mostrar o plano em árvore.</summary>
    public string Estrutura => new string('\u00A0', 4 * Math.Max(0, Centro.Nivel - 1)) + Centro.Codigo;
    public string Tipo => Centro.Analitico ? "Analítico" : "Sintético";
    public short Nivel => Centro.Nivel;
    public string Descricao => Centro.Descricao;
    public string Superior => CodigoPai ?? "";
    public string Situacao => (Centro.Ativo ? "Ativo" : "Inativo") + (Centro.TemMovimento ? " · com lançamentos" : "");
}

/// <summary>
/// Cadastros › Centros de custo (Incluir / Alterar / Excluir): plano hierárquico com máscara por empresa (ex.: 9.99.999);
/// grupos sintéticos e centros analíticos — só estes aceitam lançamento.
/// </summary>
public partial class CentrosCustoViewModel(IContexto contexto) : CadastroViewModel(contexto)
{
    public ObservableCollection<CentroLinha> Lista { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TemSelecionado))] private CentroLinha? _selecionado;
    [ObservableProperty] private string _codigo = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _codigoPai = "";
    [ObservableProperty] private bool _ativo = true;
    /// <summary>Campo opcional "Frota": o centro de custo representa uma frota (Id 0 = nenhuma).</summary>
    public ObservableCollection<FrotaDoCentro> FrotasOpcoes { get; } = [new(0, "(nenhuma)")];
    [ObservableProperty] private FrotaDoCentro? _frota;
    [ObservableProperty] private bool _sintetico;
    [ObservableProperty] private string _mascara = "";          // máscara em uso (vazia = cadastro livre); definida na aba Contabilidade da empresa
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AvisoNaoUsa))] private bool _empresaUsaCentros = true;

    /// <summary>A empresa ainda não ativou "Usar centros de custo" (aba Contabilidade do cadastro da empresa).</summary>
    public bool AvisoNaoUsa => !EmpresaUsaCentros;

    /// <summary>A máscara e o uso de centros de custo são definidos no cadastro da empresa, aba Contabilidade (só administrador).</summary>
    public bool PodeAbrirEmpresa => Contexto.Pode(Permissao.AdministrarSistema);

    [RelayCommand]
    private void AbrirCadastroEmpresa() => Contexto.AbrirTela(TelasComuns.Empresas);
    public string ResumoMascara => Mascara.Length == 0 ? "Sem máscara: códigos livres." : $"Máscara {Mascara}: ex. {Exemplo(Mascara)}";

    private static string Exemplo(string mascara) =>
        MascaraPlano.Ler(mascara) is { Sucesso: true } m ? string.Join('.', m.Valor.Segmentos.Select(n => "1".PadLeft(n, '0'))) : "";

    partial void OnMascaraChanged(string value) => OnPropertyChanged(nameof(ResumoMascara));

    /// <summary>Com máscara, o superior é o código sem o último nível: preenchido sozinho ao digitar o código.</summary>
    partial void OnCodigoChanged(string value)
    {
        if (EmInclusao && Mascara.Length > 0) CodigoPai = PlanoCentrosCusto.CodigoPaiDe(value.Trim()) ?? "";
    }

    public bool TemSelecionado => Selecionado is not null;
    public bool PodeEditar => Contexto.Pode(Permissao.CentroCustoManter);
    public override bool PodeManter => PodeEditar;
    protected override string NomeRegistro => "centro de custo";
    protected override string? DescricaoSelecionado => Selecionado is { } s ? $"{s.Codigo} – {s.Descricao}" : null;
    protected override string DetalheExclusao => "\n\nCentros com lançamentos não podem ser excluídos: use Alterar e desmarque Ativo.";

    public void AtualizarPermissoes()
    {
        OnPropertyChanged(nameof(PodeEditar));
        AtualizarBarra();
    }

    public async Task CarregarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var geracao = NovaGeracao();
        var centros = await app.PlanoContas.ListarCentrosAsync(empresa, sessao.Id);
        var mascara = await app.PlanoContas.ObterMascaraCentrosAsync(empresa, sessao.Id) ?? "";
        var usa = (await app.PlanoContas.ObterConfiguracaoAsync(empresa, sessao.Id)).UsaCentroCusto;
        var frotas = await app.PlanoContas.ListarFrotasAsync(empresa, sessao.Id);
        if (!EhAtual(geracao)) return;
        FrotasOpcoes.Clear();
        FrotasOpcoes.Add(new FrotaDoCentro(0, "(nenhuma)"));
        foreach (var f in frotas) FrotasOpcoes.Add(f);
        Mascara = mascara;
        EmpresaUsaCentros = usa;
        var id = Selecionado?.Centro.Id;
        var codigos = centros.ToDictionary(c => c.Id, c => c.Codigo);
        Lista.Clear();
        foreach (var c in centros) Lista.Add(new CentroLinha(c, c.PaiId is long p ? codigos.GetValueOrDefault(p) : null));
        Selecionado = Lista.FirstOrDefault(l => l.Centro.Id == id);
    }

    partial void OnSelecionadoChanged(CentroLinha? value)
    {
        if (EmConsulta) MostrarSelecionado();
        AtualizarBarra();
    }

    protected override void MostrarSelecionado()
    {
        if (Selecionado is { } s) (Codigo, Descricao, CodigoPai, Ativo, Sintetico) = (s.Codigo, s.Descricao, s.Superior, s.Centro.Ativo, !s.Centro.Analitico);
        else (Codigo, Descricao, CodigoPai, Ativo, Sintetico) = ("", "", "", true, false);
        Frota = FrotasOpcoes.FirstOrDefault(f => f.Id == (Selecionado?.Centro.FrotaId ?? 0)) ?? FrotasOpcoes.FirstOrDefault();
    }

    /// <summary>Novo centro: sugere como superior o centro selecionado.</summary>
    protected override void PrepararInclusao()
    {
        (Codigo, Descricao, CodigoPai, Ativo, Sintetico) = ("", "", Selecionado?.Codigo ?? "", true, false);
        Frota = FrotasOpcoes.FirstOrDefault();
    }

    protected override async Task<bool> GravarAsync(bool inclusao)
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            Domain.Comum.Resultado r;
            long? id = null;
            if (inclusao)
            {
                var inc = await app.PlanoContas.IncluirCentroAsync(empresa, Codigo, Descricao, CodigoPai, usuario, sintetico: Sintetico);
                r = inc;
                if (inc.Sucesso) id = inc.Valor;
            }
            else
            {
                if (Selecionado is null) { Erros.Add("Selecione um centro de custo."); return; }
                id = Selecionado.Centro.Id;
                r = await app.PlanoContas.AlterarCentroAsync(empresa, Selecionado.Centro.Id, Descricao, Ativo, usuario, analitico: !Sintetico);
            }
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            var escolhida = Frota?.Id is > 0 and var fid ? fid : (long?)null;
            if (id is { } centroId && escolhida != (inclusao ? null : Selecionado?.Centro.FrotaId))
            {
                var rf = await app.PlanoContas.DefinirFrotaDoCentroAsync(empresa, centroId, escolhida, usuario);
                if (!rf.Sucesso) { MostrarResultado(rf, ""); return; }
            }
            ok = true;
            Modo = ModoCadastro.Consulta;
            var codigo = Codigo.Trim();
            await CarregarAsync();
            Selecionado = Lista.FirstOrDefault(l => l.Centro.Id == id) ?? Lista.FirstOrDefault(l => l.Codigo == codigo);
            Mensagem = inclusao ? $"Centro de custo {codigo} incluído." : "Centro de custo alterado.";
        }, Permissao.CentroCustoManter);
        return ok;
    }

    protected override async Task<bool> ExcluirSelecionadoAsync()
    {
        var ok = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Selecionado is null) return;
            var r = await app.PlanoContas.ExcluirCentroAsync(empresa, Selecionado.Centro.Id, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            ok = true;
            Selecionado = null;
            await CarregarAsync();
            Mensagem = "Centro de custo excluído.";
        }, Permissao.CentroCustoManter);
        return ok;
    }
}
