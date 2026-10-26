using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Sistema;

namespace Vorticis.Desktop.Comum.ViewModels;

public sealed record CertificadoLinha(CertificadoDigital C, DateTimeOffset Agora)
{
    public string Tipo => C.Tipo;
    public string Descricao => C.Descricao;
    public string Titular => C.Titular;
    public string Documento => C.Documento switch
    {
        { Length: 14 } d => Domain.Cadastros.Cnpj.Formatar(d),
        { Length: 11 } d => Domain.Cadastros.Cpf.Formatar(d),
        var d => d ?? "",
    };
    public string Validade => $"{C.ValidoDe.ToLocalTime():dd/MM/yyyy} a {C.ValidoAte.ToLocalTime():dd/MM/yyyy}";
    public string Situacao => C.Situacao(Agora);
    public bool Alerta => C.DiasParaVencer(Agora) <= 30;
    public string Emissor => C.Emissor;
    public string Padrao => string.Join(" e ", new[] { C.PadraoContador ? "Contador" : null, C.PadraoResponsavel ? "Responsável" : null, C.PadraoProcurador ? "Procurador" : null }.OfType<string>());
}

/// <summary>
/// Configurações › Certificados digitais (administrador): repositório dos certificados A1 (.pfx importado) e A3
/// (token/cartão, registrado a partir do Windows) usados na comunicação com órgãos públicos.
/// </summary>
public partial class CertificadosViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<CertificadoLinha> Lista { get; } = [];
    /// <summary>Instalados no Windows com chave de dispositivo (token/cartão): só A3, para registrar.</summary>
    public ObservableCollection<DadosCertificado> Instalados { get; } = [];
    /// <summary>Instalados no Windows com chave exportável (software): podem ser importados para o Vorticis como A1.</summary>
    public ObservableCollection<DadosCertificado> InstaladosA1 { get; } = [];
    [ObservableProperty] private DadosCertificado? _instaladoA1Selecionado;
    [ObservableProperty] private string _senhaProtecao = "";
    [ObservableProperty] private string _senhaPadrao = "";
    public bool RepositorioWindowsDisponivel => OperatingSystem.IsWindows();
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ExcluirCommand), nameof(PadraoContadorCommand), nameof(PadraoResponsavelCommand), nameof(PadraoProcuradorCommand), nameof(RetirarPadraoCommand))] private CertificadoLinha? _selecionado;
    [ObservableProperty] private DadosCertificado? _instaladoSelecionado;

    // Importação de A1
    [ObservableProperty] private string _arquivo = "";
    [ObservableProperty] private string _senha = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _conferencia = "";
    [ObservableProperty] private string _avisoVencimento = "";

    public bool PodeAlterar => Contexto.Sessao?.Administrador == true;

    public async Task CarregarAsync()
    {
        OnPropertyChanged(nameof(PodeAlterar));
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var r = await app.Certificados.ListarAsync(sessao);
        if (!EhAtual(g)) return;
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Lista.Clear();
        foreach (var c in r.Valor) Lista.Add(new CertificadoLinha(c, app.Certificados.Agora));
        var alertas = Lista.Where(l => l.Alerta).Select(l => $"{l.Descricao}: {l.Situacao.ToLowerInvariant()}").ToList();
        AvisoVencimento = alertas.Count == 0 ? "" : "Atenção — " + string.Join("; ", alertas) + ".";
    }

    partial void OnArquivoChanged(string value) => Conferencia = "";

    /// <summary>Confere o .pfx com a senha (titular, CNPJ/CPF e validade) sem gravar.</summary>
    [RelayCommand]
    private void Conferir()
    {
        Erros.Clear();
        Mensagem = null;
        if (Ler() is { } d)
            Conferencia = $"{d.Titular}{(d.Documento is { } doc ? $" — {Formatar(doc)}" : "")} · válido até {d.ValidoAte.ToLocalTime():dd/MM/yyyy} · emissor {d.Emissor}";
    }

    private DadosCertificado? Ler()
    {
        if (string.IsNullOrWhiteSpace(Arquivo) || !File.Exists(Arquivo)) { Erros.Add("Escolha o arquivo do certificado A1 (.pfx ou .p12)."); return null; }
        var r = ServicoCertificados.LerPfx(File.ReadAllBytes(Arquivo), Senha);
        if (!r.Sucesso) { MostrarResultado(r, ""); return null; }
        return r.Valor;
    }

    [RelayCommand]
    private Task ImportarAsync() => ExecutarAdminAsync(async (app, sessao) =>
    {
        if (string.IsNullOrWhiteSpace(Arquivo) || !File.Exists(Arquivo)) { Erros.Add("Escolha o arquivo do certificado A1 (.pfx ou .p12)."); return; }
        var r = await app.Certificados.ImportarA1Async(sessao, await File.ReadAllBytesAsync(Arquivo), Senha, Descricao, Contexto.EmpresaId);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        (Arquivo, Senha, Descricao, Conferencia) = ("", "", "", "");
        await CarregarAsync();
        Mensagem = "Certificado A1 importado. A senha não é gravada: ela será pedida quando o certificado for usado.";
    });

    /// <summary>Lê os certificados instalados no Windows: A3 (token/cartão) para registrar e A1 (software, chave exportável) para importar.</summary>
    [RelayCommand]
    private void LerInstalados()
    {
        Erros.Clear();
        Instalados.Clear();
        InstaladosA1.Clear();
        if (!OperatingSystem.IsWindows()) { Erros.Add(ServicoCertificados.AvisoSomenteWindows); return; }
        try
        {
            foreach (var d in ServicoCertificados.ListarInstaladosNoWindows(ServicoCertificados.TipoA3)) Instalados.Add(d);
            foreach (var d in ServicoCertificados.ListarInstaladosNoWindows(ServicoCertificados.TipoA1)) InstaladosA1.Add(d);
            Mensagem = Instalados.Count + InstaladosA1.Count == 0
                ? "Nenhum certificado com chave privada no repositório do Windows. Conecte o token/cartão e tente de novo."
                : $"{Instalados.Count} certificado(s) A3 (token/cartão) e {InstaladosA1.Count} A1 (software) encontrado(s).";
        }
        catch (Exception ex) { Erros.Add($"Não foi possível ler o repositório do Windows: {ex.Message}"); }
    }

    /// <summary>Importa para o Vorticis, como A1, um certificado instalado no Windows com chave exportável (pede a senha que protegerá o arquivo).</summary>
    [RelayCommand]
    private Task ImportarInstaladoAsync() => ExecutarAdminAsync(async (app, sessao) =>
    {
        if (InstaladoA1Selecionado is not { } d) { Erros.Add("Escolha um certificado A1 instalado no Windows."); return; }
        var pfx = ServicoCertificados.ExportarInstaladoComoPfx(d.Impressao, SenhaProtecao);
        if (!pfx.Sucesso) { MostrarResultado(pfx, ""); return; }
        var r = await app.Certificados.ImportarA1Async(sessao, pfx.Valor, SenhaProtecao, Descricao, Contexto.EmpresaId);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        (SenhaProtecao, Descricao) = ("", "");
        await CarregarAsync();
        Mensagem = "Certificado importado como A1. A senha de proteção não é gravada: ela será pedida quando o certificado for usado (ou defina-o como padrão para guardá-la neste computador).";
    });

    private Task DefinirPadraoAsync(PapelAssinatura papel) => ExecutarAdminAsync(async (app, sessao) =>
    {
        if (Selecionado is not { } s) { Erros.Add("Escolha um certificado na lista."); return; }
        if (s.Tipo == "A1")
        {
            if (string.IsNullOrEmpty(SenhaPadrao)) { Erros.Add("Informe a senha do certificado A1 para defini-lo como padrão: ela é conferida e guardada, protegida, só neste computador."); return; }
            var aberto = await app.Certificados.AbrirA1Async(sessao, s.C.Id, SenhaPadrao);
            if (!aberto.Sucesso) { MostrarResultado(aberto, ""); return; }
            aberto.Valor.Dispose();
        }
        var r = await app.Certificados.DefinirPadraoAsync(sessao, s.C.Id, papel, true);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var aviso = "";
        if (s.Tipo == "A1")
        {
            aviso = SenhasCertificados.Guardar(s.C.Id, SenhaPadrao)
                ? " A senha ficou guardada, protegida pelo Windows, só neste computador."
                : " Neste sistema a senha não pode ser guardada: o programa a pedirá a cada uso.";
            SenhaPadrao = "";
        }
        await CarregarAsync();
        Mensagem = $"\"{s.Descricao}\" é agora o certificado padrão {(papel switch { PapelAssinatura.Contador => "do contador", PapelAssinatura.Procurador => "do procurador", _ => "do responsável" })}.{aviso}";
    });

    [RelayCommand(CanExecute = nameof(TemSelecionado))] private Task PadraoContadorAsync() => DefinirPadraoAsync(PapelAssinatura.Contador);

    [RelayCommand(CanExecute = nameof(TemSelecionado))] private Task PadraoResponsavelAsync() => DefinirPadraoAsync(PapelAssinatura.Responsavel);

    [RelayCommand(CanExecute = nameof(TemSelecionado))] private Task PadraoProcuradorAsync() => DefinirPadraoAsync(PapelAssinatura.Procurador);

    [RelayCommand(CanExecute = nameof(TemSelecionado))]
    private Task RetirarPadraoAsync() => ExecutarAdminAsync(async (app, sessao) =>
    {
        if (Selecionado is not { } s) return;
        foreach (var papel in new[] { PapelAssinatura.Contador, PapelAssinatura.Responsavel, PapelAssinatura.Procurador })
            await app.Certificados.DefinirPadraoAsync(sessao, s.C.Id, papel, false);
        SenhasCertificados.Esquecer(s.C.Id);
        await CarregarAsync();
        Mensagem = $"\"{s.Descricao}\" deixou de ser certificado padrão.";
    });

    [RelayCommand]
    private Task RegistrarA3Async() => ExecutarAdminAsync(async (app, sessao) =>
    {
        if (InstaladoSelecionado is not { } d) { Erros.Add("Escolha um certificado instalado."); return; }
        var r = await app.Certificados.RegistrarA3Async(sessao, d, Descricao, Contexto.EmpresaId);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Descricao = "";
        await CarregarAsync();
        Mensagem = "Certificado A3 registrado.";
    });

    private bool TemSelecionado() => Selecionado is not null;

    [RelayCommand(CanExecute = nameof(TemSelecionado))]
    private Task ExcluirAsync() => ExecutarAdminAsync(async (app, sessao) =>
    {
        if (Selecionado is not { } s) return;
        if (!await Contexto.ConfirmarAsync("Excluir certificado", $"Excluir o certificado \"{s.Descricao}\" do repositório?" +
                (s.Tipo == "A1" ? "\n\nO arquivo .pfx guardado será apagado; tenha uma cópia se for usá-lo de novo." : ""), "Excluir", "Cancelar"))
            return;
        var r = await app.Certificados.ExcluirAsync(sessao, s.C.Id);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        SenhasCertificados.Esquecer(s.C.Id);
        await CarregarAsync();
        Mensagem = $"Certificado \"{s.Descricao}\" excluído.";
    });

    private async Task ExecutarAdminAsync(Func<Infra.VorticisApp, Domain.Seguranca.SessaoUsuario, Task> acao)
    {
        Erros.Clear();
        Mensagem = null;
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao) return;
        if (!sessao.Administrador) { Erros.Add("Somente o administrador mantém os certificados digitais."); return; }
        try
        {
            Ocupado = true;
            await acao(app, sessao);
        }
        catch (Exception ex) { Erros.Add(ex.Message); }
        finally { Ocupado = false; }
    }

    private static string Formatar(string doc) => doc.Length == 14 ? Domain.Cadastros.Cnpj.Formatar(doc) : Domain.Cadastros.Cpf.Formatar(doc);
}
