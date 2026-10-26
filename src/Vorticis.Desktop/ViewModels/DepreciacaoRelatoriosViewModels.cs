using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Imobilizado;
using Vorticis.Application.Relatorios;

namespace Vorticis.Desktop.ViewModels;

public sealed record OpcaoGrupoBem(long? Id, string Texto);

/// <summary>
/// Base de Relatórios › Depreciação: bens da empresa ativa na posição da última depreciação lançada, filtrados por grupo,
/// com ou sem os bens baixados. Reaproveita a grade dos relatórios gerenciais.
/// </summary>
public abstract partial class RelatorioDepreciacaoViewModel(IContexto contexto) : GerencialBaseViewModel(contexto)
{
    public ObservableCollection<OpcaoGrupoBem> Grupos { get; } = [new(null, "Todos os grupos")];

    [ObservableProperty] private OpcaoGrupoBem? _grupo;
    [ObservableProperty] private bool _incluirBaixados;

    public abstract string Titulo { get; }
    public virtual bool FiltraGrupo => true;
    protected override string TituloRelatorio => Titulo;
    protected override string NomeArquivo => Titulo.ToLowerInvariant().Replace(' ', '_').Replace("ç", "c").Replace("ã", "a") + SufixoArquivo;

    protected abstract TabelaRelatorio Montar(IReadOnlyList<BemImobilizado> bens, IReadOnlyList<GrupoBem> grupos, long? grupoId, bool incluirBaixados);

    public async Task CarregarGruposAsync()
    {
        if (Contexto.App is not { } app) return;
        var selecionado = Grupo?.Id;
        var lista = await app.Imobilizado.ListarGruposAsync();
        Grupos.Clear();
        Grupos.Add(new(null, "Todos os grupos"));
        foreach (var g in lista.OrderBy(g => g.Codigo, StringComparer.Ordinal)) Grupos.Add(new(g.Id, $"{g.Codigo} — {g.Descricao}"));
        Grupo = Grupos.FirstOrDefault(g => g.Id == selecionado) ?? Grupos[0];
    }

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var bens = await app.Imobilizado.ListarBensAsync(empresa, usuario);
        var grupos = await app.Imobilizado.ListarGruposAsync();
        var grupoId = FiltraGrupo ? Grupo?.Id : null;
        Exibir(Montar(bens, grupos, grupoId, IncluirBaixados),
               $"Posição: última depreciação lançada{(grupoId is not null ? $" | Grupo: {Grupo!.Texto}" : "")}{(IncluirBaixados ? " | Com bens baixados" : "")}");
        Resumo = bens.Count == 0 ? "Nenhum bem cadastrado (Imobilizado › Cadastro de bens)." : "";
    });
}

/// <summary>Relatórios › Depreciação › Relação de bens.</summary>
public sealed class RelacaoBensViewModel(IContexto contexto) : RelatorioDepreciacaoViewModel(contexto)
{
    public override string Titulo => TabelasImobilizado.TituloRelacao;
    protected override TabelaRelatorio Montar(IReadOnlyList<BemImobilizado> b, IReadOnlyList<GrupoBem> g, long? id, bool baixados) => TabelasImobilizado.RelacaoBens(b, g, id, baixados);
}

/// <summary>Relatórios › Depreciação › Valor depreciado.</summary>
public sealed class ValorDepreciadoViewModel(IContexto contexto) : RelatorioDepreciacaoViewModel(contexto)
{
    public override string Titulo => TabelasImobilizado.TituloDepreciado;
    protected override TabelaRelatorio Montar(IReadOnlyList<BemImobilizado> b, IReadOnlyList<GrupoBem> g, long? id, bool baixados) => TabelasImobilizado.ValorDepreciado(b, g, id, baixados);
}

/// <summary>Relatórios › Depreciação › Valor residual.</summary>
public sealed class ValorResidualViewModel(IContexto contexto) : RelatorioDepreciacaoViewModel(contexto)
{
    public override string Titulo => TabelasImobilizado.TituloResidual;
    protected override TabelaRelatorio Montar(IReadOnlyList<BemImobilizado> b, IReadOnlyList<GrupoBem> g, long? id, bool baixados) => TabelasImobilizado.ValorResidual(b, g, id, baixados);
}

/// <summary>Relatórios › Depreciação › Resumo por grupo.</summary>
public sealed class DepreciacaoPorGrupoViewModel(IContexto contexto) : RelatorioDepreciacaoViewModel(contexto)
{
    public override string Titulo => TabelasImobilizado.TituloPorGrupo;
    public override bool FiltraGrupo => false;
    protected override TabelaRelatorio Montar(IReadOnlyList<BemImobilizado> b, IReadOnlyList<GrupoBem> g, long? id, bool baixados) => TabelasImobilizado.PorGrupo(b, g, baixados);
}
