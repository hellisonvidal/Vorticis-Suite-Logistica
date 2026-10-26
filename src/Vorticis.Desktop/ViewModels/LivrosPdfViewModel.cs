using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Desktop.ViewModels;

/// <summary>
/// Base das funções que emitem livro em PDF (Livro Contábil Completo e Termos): período, número do livro, NIRE (do cadastro
/// da empresa), representante legal e contador que assinam os termos — preenchidos dos cadastros de Sócios e Contadores.
/// </summary>
public abstract partial class LivroBaseViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime? _fim = new DateTime(DateTime.Today.Year, 12, 31);
    [ObservableProperty] private decimal? _numeroLivro = 1;
    [ObservableProperty] private string _representante = "";
    [ObservableProperty] private string _representanteCpf = "";
    [ObservableProperty] private string _contador = "";
    [ObservableProperty] private string _crc = "";
    [ObservableProperty] private string _dataArquivamento = "";
    [ObservableProperty] private string? _ultimoArquivo;

    /// <summary>NIRE do cadastro da empresa (somente leitura na tela).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SemNire), nameof(AvisoNire))] private string _nire = "";

    public bool SemNire => string.IsNullOrWhiteSpace(Nire);
    public string AvisoNire => SemNire ? "NIRE não cadastrado para esta empresa: o termo sairá sem NIRE. Informe-o em Cadastros › Empresas." : "";

    /// <summary>Preenche NIRE, representante legal (Sócios) e responsável técnico (Contadores) da empresa ativa.</summary>
    public async Task PreencherResponsaveisAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var rep = await app.Responsaveis.RepresentanteLegalAsync(empresa);
        var cont = await app.Responsaveis.SignatarioEcdEcfAsync(empresa);
        var dados = await app.Empresas.ObterAsync(empresa);
        if (!EhAtual(g)) return;
        Nire = dados?.Nire?.Trim() ?? "";
        (Representante, RepresentanteCpf) = rep is null ? ("", "") : (rep.Nome, rep.CpfCnpj is { Length: 11 } c ? Domain.Cadastros.Cpf.Formatar(c) : rep.CpfCnpj ?? "");
        (Contador, Crc) = cont is null ? ("", "") : (cont.Nome, cont.CrcCompleto);
    }

    protected (DateOnly Ini, DateOnly Fim) Periodo()
    {
        var ini = Data(Inicio, DateOnly.FromDateTime(DateTime.Today));
        return (ini, Data(Fim, ini));
    }

    protected DadosTermos Termos(int? folhas = null) =>
        new((int)(NumeroLivro ?? 1), Representante, RepresentanteCpf, Contador, Crc,
            string.IsNullOrWhiteSpace(DataArquivamento) ? null : DataArquivamento.Trim(), folhas);

    /// <summary>Assina digitalmente o PDF (PAdES/PKCS#7) com o certificado padrão do contador, ou do responsável.</summary>
    [ObservableProperty] private bool _assinarPdfDigitalmente;

    /// <summary>Abre o certificado padrão para o PDF que será gerado; nulo (com erros na tela) se a assinatura foi pedida e não é possível.</summary>
    protected async Task<AssinaturaDigitalPdf?> AssinaturaDigitalAsync(string motivo)
    {
        if (!AssinarPdfDigitalmente) return null;
        var a = await CertificadosPadrao.ParaPdfAsync(Contexto, motivo);
        if (!a.Sucesso) { foreach (var e in a.Erros) Erros.Add(e); return null; }
        return a.Valor;
    }

    protected void Concluir(Resultado<ArquivoGerado> r, string nome)
    {
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        UltimoArquivo = ArquivosGerados.Salvar(nome, r.Valor.Conteudo);
        Mensagem = $"{r.Valor.Paginas} página(s) gerada(s): {UltimoArquivo}";
    }

    protected string Nome(string tipo) { var (i, f) = Periodo(); return $"{tipo}_{i:yyyyMMdd}_{f:yyyyMMdd}.pdf"; }
}

/// <summary>Relatórios › Contábeis › Livro Contábil Completo: o usuário escolhe o que entra no livro e o número do livro.</summary>
public partial class LivrosPdfViewModel(IContexto contexto) : LivroBaseViewModel(contexto)
{
    [ObservableProperty] private bool _incluirTermoAbertura = true;
    [ObservableProperty] private bool _incluirDiario = true;
    [ObservableProperty] private bool _incluirRazao = true;
    [ObservableProperty] private bool _incluirBalancete = true;
    [ObservableProperty] private bool _incluirBalanco = true;
    [ObservableProperty] private bool _incluirDre = true;
    [ObservableProperty] private bool _incluirDmpl = true;
    [ObservableProperty] private bool _incluirDlpa = true;
    [ObservableProperty] private bool _incluirTermoEncerramento = true;
    [ObservableProperty] private bool _assinaturaResponsavel = true;
    [ObservableProperty] private bool _assinaturaContador = true;

    public bool PodeGerar => Contexto.Pode(Permissao.RelatorioDiario) || Contexto.Pode(Permissao.RelatorioRazao)
                             || Contexto.Pode(Permissao.RelatorioBalancete) || Contexto.Pode(Permissao.RelatorioDemonstracoes);

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeGerar));

    public OpcoesLivro Opcoes() => new(IncluirTermoAbertura, IncluirDiario, IncluirRazao, IncluirBalancete, IncluirBalanco, IncluirDre,
                                       IncluirDmpl, IncluirDlpa, IncluirTermoEncerramento,
                                       new OpcoesAssinatura(AssinaturaResponsavel, AssinaturaContador));

    [RelayCommand]
    private Task GerarLivroAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var (ini, fim) = Periodo();
        var digital = await AssinaturaDigitalAsync("Livro contábil");
        if (AssinarPdfDigitalmente && digital is null) return;
        using var escopo = ContextoAssinaturaPdf.Usar(digital);
        Concluir(await app.Livros.LivroAsync(empresa, ini, fim, Termos(), Opcoes(), usuario), Nome($"livro_contabil_{NumeroLivro}"));
    });
}

/// <summary>Relatórios › Contábeis › Termos: Termo de Abertura e Termo de Encerramento do livro, gerados isoladamente.</summary>
public partial class TermosViewModel(IContexto contexto) : LivroBaseViewModel(contexto)
{
    public static IReadOnlyList<string> Livros { get; } = ["Livro Diário", "Livro Contábil Completo"];

    [ObservableProperty] private string _livro = Livros[0];
    /// <summary>Total de folhas do livro; vazio deixa a linha para preencher à mão.</summary>
    [ObservableProperty] private decimal? _folhas;

    public bool PodeGerar => Contexto.Pode(Permissao.RelatorioDiario);

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeGerar));

    private Task GerarAsync(TipoTermo tipo) => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var (ini, fim) = Periodo();
        var contabil = Livro == Livros[1];
        var digital = await AssinaturaDigitalAsync("Termo do livro contábil");
        if (AssinarPdfDigitalmente && digital is null) return;
        using var escopo = ContextoAssinaturaPdf.Usar(digital);
        Concluir(await app.Livros.TermoIsoladoAsync(empresa, ini, fim, Termos(Folhas is { } f ? (int)f : null), tipo, contabil, usuario),
                 Nome($"termo_{(tipo == TipoTermo.Abertura ? "abertura" : "encerramento")}_{NumeroLivro}"));
    }, Permissao.RelatorioDiario);

    [RelayCommand] private Task GerarAberturaAsync() => GerarAsync(TipoTermo.Abertura);

    [RelayCommand] private Task GerarEncerramentoAsync() => GerarAsync(TipoTermo.Encerramento);
}
