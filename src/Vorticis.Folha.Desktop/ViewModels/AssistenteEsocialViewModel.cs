using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Sistema;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Servicos;

namespace Vorticis.Folha.Desktop.ViewModels;

/// <summary>Etapa do assistente na tela: ícone e cor da situação.</summary>
public sealed class EtapaEsocialLinha(EtapaEsocial e)
{
    private static readonly IBrush Verde = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#15803D"));
    private static readonly IBrush Amarelo = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#B45309"));
    private static readonly IBrush Vermelho = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#B91C1C"));

    public EtapaEsocial E { get; } = e;
    public string Titulo => E.Titulo;
    public string Icone => E.Situacao switch { SituacaoEtapaEsocial.Pronta => "✔", SituacaoEtapaEsocial.Atencao => "!", _ => "✖" };
    public string TextoSituacao => E.Situacao switch { SituacaoEtapaEsocial.Pronta => "Pronta", SituacaoEtapaEsocial.Atencao => "Atenção", _ => "Pendente" };
    public IBrush Cor => E.Situacao switch { SituacaoEtapaEsocial.Pronta => Verde, SituacaoEtapaEsocial.Atencao => Amarelo, _ => Vermelho };
}

/// <summary>
/// Obrigações › eSocial › Assistente de configuração: confere, etapa por etapa, o que falta para gerar e transmitir os eventos
/// da competência (empregador, responsável, ambiente, rubricas, lotações, trabalhadores, folha e certificado digital) e leva à
/// tela onde cada pendência se corrige. No fim, gera os eventos e abre a transmissão.
/// </summary>
public partial class AssistenteEsocialViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<EtapaEsocialLinha> Etapas { get; } = [];

    [ObservableProperty] private DateTime? _competencia = FolhaExtensoes.Mes(contexto.PeriodoTrabalho);
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TemSelecionada), nameof(Pendencias), nameof(PodeCorrigir), nameof(TextoCorrigir), nameof(EhUltima))]
    private EtapaEsocialLinha? _selecionada;
    [ObservableProperty] private string _resumo = "Clique em Verificar para conferir a empresa etapa por etapa.";

    public bool TemSelecionada => Selecionada is not null;
    public IReadOnlyList<string> Pendencias => Selecionada?.E.Pendencias ?? [];
    public bool PodeCorrigir => Selecionada?.E.Destino is { } d && d != DestinoEtapaEsocial.Nenhum;
    public string TextoCorrigir => Selecionada?.E.TextoDestino ?? "";
    public bool EhUltima => Selecionada is not null && Etapas.IndexOf(Selecionada) == Etapas.Count - 1;
    public bool PodeUsar => Contexto.Pode(Permissao.FolhaESocial);
    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeUsar));

    partial void OnCompetenciaChanged(DateTime? value) => _ = CarregarAsync();

    public Task CarregarAsync() => VerificarAsync();

    [RelayCommand]
    private async Task VerificarAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var etapas = (await app.Folha().AssistenteEsocial.DiagnosticarAsync(empresa, FolhaExtensoes.InicioMes(Competencia), sessao.Id)).ToList();
        if (etapas.Count > 1)
        {
            var cfg = await app.Folha().Cadastros.ObterConfiguracaoAsync(empresa, sessao.Id);
            etapas.Insert(etapas.Count - 1, await EtapaCertificadoAsync(app, sessao, empresa, cfg.TransmissorEsocial));
        }
        if (!EhAtual(g)) return;
        var indice = Selecionada is null ? -1 : Etapas.IndexOf(Selecionada);
        Etapas.Clear();
        foreach (var e in etapas) Etapas.Add(new EtapaEsocialLinha(e));
        Selecionada = indice >= 0 && indice < Etapas.Count ? Etapas[indice] : Etapas.FirstOrDefault(e => e.E.Situacao != SituacaoEtapaEsocial.Pronta) ?? Etapas.FirstOrDefault();
        var prontas = Etapas.Count(e => e.E.Situacao == SituacaoEtapaEsocial.Pronta);
        var pendentes = Etapas.Count(e => e.E.Situacao == SituacaoEtapaEsocial.Pendente);
        Resumo = pendentes == 0
            ? $"{prontas} de {Etapas.Count} etapas prontas{(prontas < Etapas.Count ? " (confira os avisos)" : "")}. Pode gerar e transmitir os eventos de {Competencia:MM/yyyy}."
            : $"{prontas} de {Etapas.Count} etapas prontas; {pendentes} com pendência. Corrija na ordem e clique em Verificar de novo.";
    }

    /// <summary>Etapa 8: certificado digital padrão de quem transmite (administrador; Configurações › Certificados digitais).</summary>
    private static async Task<EtapaEsocial> EtapaCertificadoAsync(Vorticis.Infra.VorticisApp app, SessaoUsuario sessao, long empresa, string transmissor)
    {
        const string titulo = "8. Certificado digital";
        const string explicacao = "Os eventos são assinados com certificado ICP-Brasil (A1 ou A3) de quem transmite: e-CNPJ do empregador, ou e-CPF/e-CNPJ do procurador ou do escritório contábil.";
        var papel = transmissor switch { "PROCURADOR" => PapelAssinatura.Procurador, "CONTADOR" => PapelAssinatura.Contador, _ => PapelAssinatura.Responsavel };
        if (!sessao.Administrador)
            return new EtapaEsocial(titulo, explicacao, SituacaoEtapaEsocial.Atencao, ["Só o administrador confere e usa os certificados digitais."], DestinoEtapaEsocial.Nenhum, null);
        var r = await app.Certificados.ObterPadraoAsync(sessao, papel, empresa);
        var nome = papel switch { PapelAssinatura.Procurador => "do procurador", PapelAssinatura.Contador => "do contador", _ => "do empregador (responsável)" };
        if (!r.Sucesso) return new EtapaEsocial(titulo, explicacao, SituacaoEtapaEsocial.Pendente, r.Erros, DestinoEtapaEsocial.Certificados, "Abrir Certificados digitais");
        if (r.Valor is not { } c)
            return new EtapaEsocial(titulo, explicacao, SituacaoEtapaEsocial.Pendente, [$"Não há certificado padrão {nome}. Cadastre o certificado e marque-o como padrão."],
                                    DestinoEtapaEsocial.Certificados, "Abrir Certificados digitais");
        var vencido = c.ValidoAte < DateTimeOffset.Now;
        return new EtapaEsocial(titulo, explicacao, vencido ? SituacaoEtapaEsocial.Pendente : SituacaoEtapaEsocial.Pronta,
                                [vencido ? $"O certificado padrão {nome} ({c.Descricao}) está vencido." : $"Certificado padrão {nome}: {c.Descricao} (válido até {c.ValidoAte:dd/MM/yyyy})."],
                                DestinoEtapaEsocial.Certificados, "Abrir Certificados digitais");
    }

    [RelayCommand]
    private void Anterior()
    {
        if (Selecionada is not null && Etapas.IndexOf(Selecionada) is > 0 and var i) Selecionada = Etapas[i - 1];
    }

    [RelayCommand]
    private void Proxima()
    {
        if (Selecionada is not null && Etapas.IndexOf(Selecionada) is var i && i < Etapas.Count - 1) Selecionada = Etapas[i + 1];
    }

    /// <summary>Abre a tela onde a pendência da etapa se corrige.</summary>
    [RelayCommand]
    private void Corrigir()
    {
        var tela = Selecionada?.E.Destino switch
        {
            DestinoEtapaEsocial.Empresas => TelasComuns.Empresas,
            DestinoEtapaEsocial.ParametrosFolha => FolhaTelas.Parametros,
            DestinoEtapaEsocial.ParametrosModulo => TelasComuns.Parametros,
            DestinoEtapaEsocial.Rubricas => FolhaTelas.Rubricas,
            DestinoEtapaEsocial.Departamentos => FolhaTelas.Departamentos,
            DestinoEtapaEsocial.Funcionarios => FolhaTelas.Funcionarios,
            DestinoEtapaEsocial.FolhaMensal => FolhaTelas.FolhaMensal,
            DestinoEtapaEsocial.Certificados => TelasComuns.Certificados,
            DestinoEtapaEsocial.Geracao => FolhaTelas.ESocial,
            DestinoEtapaEsocial.Transmissao => FolhaTelas.Transmissao,
            _ => null,
        };
        if (tela is not null) Contexto.AbrirTela(tela);
    }

    /// <summary>Última etapa: gera todos os eventos da competência (os já enviados não são regerados).</summary>
    [RelayCommand]
    private Task GerarEventosAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var r = await app.Folha().ESocial.GerarAsync(empresa, FolhaExtensoes.InicioMes(Competencia), [.. EventosEsocial.Todos.Select(t => t.Tipo)], usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        foreach (var a in r.Valor.Avisos) Erros.Add(a);
        await VerificarAsync();
        Mensagem = $"{r.Valor.Gerados} evento(s) gerado(s)" + (r.Valor.Ignorados > 0 ? $"; {r.Valor.Ignorados} já enviado(s) mantido(s)" : "") +
                   ". Agora use Abrir Transmissão para assinar e enviar (comece pela produção restrita).";
    }, Permissao.FolhaESocial);

    [RelayCommand]
    private void AbrirTransmissao() => Contexto.AbrirTela(FolhaTelas.Transmissao);
}
