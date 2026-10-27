using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum.ViewModels;

namespace Vorticis.Desktop.Tests;

/// <summary>Tecla Delete: confirma e usa o botão Excluir da tela; a confirmação simples da tela não se repete.</summary>
public class TeclaDeleteTests
{
    /// <summary>Tela de exemplo: o Excluir pede a própria confirmação (e, se for o caso, outra com consequência diferente).</summary>
    private sealed class TelaFalsa(MainViewModelBase main, bool avisoExtra)
    {
        public int Excluidos { get; private set; }
        public List<string> Perguntas { get; } = [];

        public IAsyncRelayCommand ExcluirCommand => new AsyncRelayCommand(async () =>
        {
            if (!await main.ConfirmarAsync("Excluir veículo", "Excluir o veículo?")) return;
            if (avisoExtra && !await main.ConfirmarAsync("Desfazer contabilização e excluir", "A nota está contabilizada.")) return;
            Excluidos++;
        });
    }

    private static async Task Responder(MainViewModelBase main, bool sim, List<string> titulos)
    {
        for (var i = 0; i < 100 && main.Confirmacao is null; i++) await Task.Delay(10);
        var c = main.Confirmacao!;
        titulos.Add(c.Titulo);
        main.Confirmacao = null;
        c.Responder(sim);
    }

    [Fact]
    public async Task Delete_confirma_uma_vez_e_exclui()
    {
        var main = new Logistica.Desktop.MainViewModel(new Logistica.Desktop.ModuloFrotaLogistica());
        var tela = new TelaFalsa(main, avisoExtra: false);
        var titulos = new List<string>();
        var exclusao = main.ExcluirPelaTeclaAsync(tela);
        await Responder(main, true, titulos);
        Assert.True(await exclusao);
        Assert.Equal(1, tela.Excluidos);
        Assert.Equal(["Excluir registro"], titulos);   // a confirmação "Excluir veículo" da tela não se repete

        // cancelar a confirmação não exclui
        exclusao = main.ExcluirPelaTeclaAsync(tela);
        await Responder(main, false, titulos);
        await exclusao;
        Assert.Equal(1, tela.Excluidos);

        // fora da tecla Delete, a confirmação da tela volta a aparecer
        var direto = tela.ExcluirCommand.ExecuteAsync(null);
        await Responder(main, true, titulos);
        await direto;
        Assert.Equal("Excluir veículo", titulos[^1]);
    }

    [Fact]
    public async Task Aviso_de_outra_consequencia_continua_aparecendo_e_tela_sem_excluir_ignora()
    {
        var main = new Logistica.Desktop.MainViewModel(new Logistica.Desktop.ModuloFrotaLogistica());
        var tela = new TelaFalsa(main, avisoExtra: true);
        var titulos = new List<string>();
        var exclusao = main.ExcluirPelaTeclaAsync(tela);
        await Responder(main, true, titulos);
        await Responder(main, true, titulos);
        await exclusao;
        Assert.Equal(["Excluir registro", "Desfazer contabilização e excluir"], titulos);
        Assert.Equal(1, tela.Excluidos);
        Assert.False(await main.ExcluirPelaTeclaAsync(new object()));
        Assert.Null(main.Confirmacao);
    }
}
