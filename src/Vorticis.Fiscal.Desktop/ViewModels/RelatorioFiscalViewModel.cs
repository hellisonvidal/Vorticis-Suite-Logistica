using System.Collections.ObjectModel;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Fiscal.Desktop.ViewModels;

/// <summary>Tabela já formatada para exibição na grade (texto pt-BR); os dados vêm de <see cref="TabelaRelatorio"/>, a mesma que é exportada.</summary>
public sealed class TabelaExibida(TabelaRelatorio origem)
{
    public string Titulo { get; } = origem.Titulo;
    public string? Rodape { get; } = origem.Rodape;
    public IReadOnlyList<ColunaRelatorio> Colunas { get; } = origem.Colunas;
    public IReadOnlyList<string[]> Linhas { get; } =
        origem.Linhas.Select(l => l.Valores.Select((v, i) => ExportacaoRelatorio.Texto(v, origem.Colunas[i].Tipo)).ToArray()).ToList();
    public IReadOnlyList<bool> Destaques { get; } = origem.Linhas.Select(l => l.Destaque).ToList();
}

/// <summary>
/// Base das telas fiscais que exibem um relatório: o resultado aparece nas grades da tela e é exportado (PDF, Excel, CSV) a partir das
/// mesmas tabelas. As exportações seguem a permissão de exportar relatórios do perfil.
/// </summary>
public abstract class RelatorioFiscalViewModel(IContexto contexto) : RelatorioViewModel(contexto)
{
    private IReadOnlyList<TabelaRelatorio> _tabelas = [];

    public ObservableCollection<TabelaExibida> Exibidas { get; } = [];

    protected override IReadOnlyList<TabelaRelatorio> Tabelas() => _tabelas;

    protected override bool Paisagem => true;

    /// <summary>Mostra as tabelas na tela e habilita a exportação; sem tabelas, limpa a tela.</summary>
    protected void Mostrar(IReadOnlyList<TabelaRelatorio> tabelas)
    {
        _tabelas = tabelas;
        Exibidas.Clear();
        foreach (var t in tabelas) Exibidas.Add(new TabelaExibida(t));
        TemDados = tabelas.Count > 0;
        AtualizarPermissoesExportacao();
    }

    protected void Mostrar(TabelaRelatorio tabela) => Mostrar([tabela]);

    protected void Limpar() => Mostrar([]);

    /// <summary>Atualiza botões que dependem do perfil (chamado pelo casco ao trocar usuário ou empresa).</summary>
    public virtual void AtualizarPermissoes()
    {
        AtualizarPermissoesExportacao();
        OnPropertyChanged(string.Empty);
    }

    public bool Pode(Permissao p) => Contexto.Pode(p);
}
