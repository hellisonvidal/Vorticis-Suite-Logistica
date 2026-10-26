using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Obrigacoes;
using Vorticis.Domain.Comum;

namespace Vorticis.Desktop.Comum.ViewModels;

/// <summary>Linha do quadro "Próximas obrigações" da tela inicial.</summary>
public sealed record ProximaObrigacao(string Vencimento, string Nome, string Situacao, Avalonia.Media.IBrush Cor);

/// <summary>Tela de descanso: exibida após o login e quando nenhuma função está aberta.</summary>
public partial class InicioViewModel(MainViewModelBase main) : ModuloViewModel(main)
{
    /// <summary>Nome curto do módulo ("Contábil"), em maiúsculas ao fundo da tela inicial.</summary>
    public string NomeCurto => main.Modulo.NomeCurto.ToUpper(Formato.PtBr);

    /// <summary>Cor de destaque do módulo (nome ao fundo da tela inicial).</summary>
    public Avalonia.Media.IBrush PincelDestaque => main.PincelDestaque;

    public string Versao => $"versão {SobreViewModel.Versao}";

    [ObservableProperty] private string? _dica;

    /// <summary>Atrasadas e a vencer nos próximos 15 dias da empresa ativa (não inclui as cumpridas).</summary>
    public ObservableCollection<ProximaObrigacao> Proximas { get; } = [];

    [ObservableProperty] private bool _temProximas;

    private int _geracaoProximas;

    public void Atualizar()
    {
        Dica = main.EmpresasPermitidas.Count > 0 ? null
             : main.Sessao?.Administrador == true
                 ? "Nenhuma empresa cadastrada ainda. Quando quiser começar, use Cadastros › Empresas."
                 : "Nenhuma empresa liberada para o seu usuário. Procure o administrador do sistema.";
        _ = AtualizarProximasAsync();
    }

    public async Task AtualizarProximasAsync()
    {
        var g = Interlocked.Increment(ref _geracaoProximas);
        if (!main.Modulo.UsaCalendarioObrigacoes || !main.Logado || main.App is not { } app || main.Sessao is not { Implantacao: false } sessao || main.EmpresaId is not { } empresa)
        {
            Proximas.Clear();
            TemProximas = false;
            return;
        }
        try
        {
            var hoje = DateOnly.FromDateTime(DateTime.Today);
            var r = await app.Obrigacoes.ProximasAsync(sessao.Id, empresa, hoje, modulos: ObrigacoesViewModel.ModuloDe(main) is { } mo ? [mo] : null);
            if (g != Volatile.Read(ref _geracaoProximas)) return;
            Proximas.Clear();
            if (r.Sucesso)
                foreach (var i in r.Valor)
                {
                    var l = new LinhaObrigacao(i);
                    Proximas.Add(new ProximaObrigacao($"{i.Ocorrencia.Vencimento:dd/MM}", $"{l.Nome} ({l.Competencia})", l.Situacao, l.Texto));
                }
            TemProximas = Proximas.Count > 0;
        }
        catch
        {
            Proximas.Clear();
            TemProximas = false;
        }
    }

    [RelayCommand] private void AbrirCalendario() => main.AbrirTela(TelasComuns.Obrigacoes);
}
