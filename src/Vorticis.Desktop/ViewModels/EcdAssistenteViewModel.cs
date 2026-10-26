using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Sistema;
using Vorticis.Application.Sped;
using Vorticis.Domain.Cadastros;
using Vorticis.Domain.Seguranca;
using Vorticis.Domain.Sped;
using Vorticis.Infra.Sped;

namespace Vorticis.Desktop.ViewModels;

/// <summary>Item da lista de pré-requisitos (passo 2): resolvido ou com pendências, e a tela que o resolve.</summary>
public sealed record ItemPreRequisito(string Titulo, bool Ok, string Detalhe, string Tela, string TextoAtalho)
{
    public string Marca => Ok ? "✔" : "✖";
    public string Cor => Ok ? "#1B7F3B" : "#B3261E";
    public bool TemAtalho => !Ok && Tela.Length > 0;
}

/// <summary>Pendência mostrada na validação prévia (passo 3).</summary>
public sealed record LinhaPendencia(bool Bloqueante, string Mensagem)
{
    public string Marca => Bloqueante ? "Bloqueante" : "Aviso";
    public string Cor => Bloqueante ? "#B3261E" : "#9A6700";
}

public enum ModoAssinatura { Nenhum, ArquivoA1, RepositorioWindows, Cadastrado }

/// <summary>Um signatário do J930 e o certificado com que ele assina (passo 4).</summary>
public partial class SignatarioAssinatura(SignatarioEcd signatario) : ObservableObject
{
    public SignatarioEcd Signatario { get; } = signatario;
    public string Nome => Signatario.Nome;
    public string Qualificacao => $"{Signatario.Qualificacao} ({Signatario.CodigoQualificacao})";
    public string Documento => Signatario.CpfCnpj.Length == 14 ? Cnpj.Formatar(Signatario.CpfCnpj) : Cpf.Formatar(Signatario.CpfCnpj);
    public bool DocumentoValido => Signatario.CpfCnpj.Length == 14 ? Cnpj.Valido(Signatario.CpfCnpj) : Cpf.Valido(Signatario.CpfCnpj);

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PedeArquivo), nameof(PedeInstalado), nameof(PedeCadastrado), nameof(PedeSenha), nameof(ModoIndice))]
    private ModoAssinatura _modo = ModoAssinatura.Nenhum;
    [ObservableProperty] private string _arquivoPfx = "";
    [ObservableProperty] private string _senha = "";
    [ObservableProperty] private DadosCertificado? _instalado;
    [ObservableProperty] private CertificadoDigital? _cadastrado;

    public int ModoIndice { get => (int)Modo; set => Modo = (ModoAssinatura)value; }
    public bool PedeArquivo => Modo == ModoAssinatura.ArquivoA1;
    public bool PedeInstalado => Modo == ModoAssinatura.RepositorioWindows;
    public bool PedeCadastrado => Modo == ModoAssinatura.Cadastrado;
    public bool PedeSenha => Modo is ModoAssinatura.ArquivoA1 or ModoAssinatura.Cadastrado;
}

/// <summary>
/// Auditoria › Gerar ECD (SPED Contábil): assistente em cinco passos (dados, pré-requisitos, validação, assinaturas, gerar e salvar)
/// e aba com o histórico das ECDs geradas.
/// </summary>
public partial class EcdAssistenteViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public const int TotalPassos = 5;
    public const int AbaAssistente = 0, AbaGeradas = 1;

    public static readonly string[] Titulos =
        ["Empresa e exercício", "Pré-requisitos", "Validação prévia", "Assinaturas", "Gerar e salvar"];

    public static readonly string[] Explicacoes =
    [
        "Escolha a empresa (menu superior) e o exercício e confira os dados que irão para a ECD: NIRE, porte, número do livro e opções de geração.",
        "Confira se o que a ECD exige já está pronto. Para cada item pendente, use o atalho para abrir a tela que o resolve e volte aqui.",
        "O Vorticis confere o arquivo antes de gerar, com as mesmas regras do PVA. Só é possível avançar sem pendências bloqueantes; avisos não impedem.",
        "Escolha o certificado digital de cada signatário do livro (contador e responsável): A1 (arquivo .pfx), A3 (token ou cartão no Windows) ou o certificado padrão (já escolhido quando definido em Configurações › Certificados), ou deixe para assinar depois no PVA.",
        "Gere o arquivo, confira o resumo e salve o .txt na pasta escolhida. Depois é só importar e validar no PVA.",
    ];

    public const string OpcaoDmpl = "DMPL — mutações do patrimônio líquido";
    public const string OpcaoDlpa = "DLPA — lucros ou prejuízos acumulados";

    [ObservableProperty] private int _aba = AbaAssistente;

    // ---------------------------------------------------------------- navegação
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoPasso1), nameof(NoPasso2), nameof(NoPasso3), nameof(NoPasso4), nameof(NoPasso5),
        nameof(TituloPasso), nameof(ExplicacaoPasso), nameof(IndicadorPasso), nameof(PodeVoltar), nameof(NoUltimoPasso))]
    private int _passo = 1;

    public bool NoPasso1 => Passo == 1;
    public bool NoPasso2 => Passo == 2;
    public bool NoPasso3 => Passo == 3;
    public bool NoPasso4 => Passo == 4;
    public bool NoPasso5 => Passo == 5;
    public bool NoUltimoPasso => Passo == TotalPassos;
    public bool PodeVoltar => Passo > 1;
    public string TituloPasso => $"Passo {Passo} de {TotalPassos} — {Titulos[Passo - 1]}";
    public string ExplicacaoPasso => Explicacoes[Passo - 1];
    public string IndicadorPasso => string.Join("   ›   ", Titulos.Select((t, i) => i + 1 == Passo ? $"[{i + 1}. {t}]" : $"{i + 1}. {t}"));

    // ---------------------------------------------------------------- passo 1
    [ObservableProperty] private decimal? _ano = DateTime.Today.Year - 1;
    [ObservableProperty] private string _empresaNome = "";
    [ObservableProperty] private string _nire = "";
    [ObservableProperty] private bool _grandePorte;
    [ObservableProperty] private string _natureza = "DIÁRIO GERAL";
    [ObservableProperty] private decimal? _numeroLivro;
    [ObservableProperty] private string _numeroLivroSugerido = "";
    [ObservableProperty] private decimal? _nivelAglutinacao = 3;
    [ObservableProperty] private string _mutacoes = OpcaoDmpl;
    [ObservableProperty] private bool _historicosPadronizados = true;
    [ObservableProperty] private bool _responsavelPessoaJuridica;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AvisoProcuracao), nameof(TemAvisoProcuracao))] private bool _assinarComoProcurador;

    /// <summary>Lembrete do passo final: a transmissão por procurador exige a procuração eletrônica no e-CAC.</summary>
    public string AvisoProcuracao => AssinarComoProcurador
        ? "Assinatura como procurador: para transmitir, a procuração eletrônica precisa estar cadastrada no e-CAC (Procurações), com poderes para a ECD, e o certificado usado deve ser o do procurador."
        : "";
    public bool TemAvisoProcuracao => AssinarComoProcurador;
    [ObservableProperty] private string _certidaoCrc = "";
    public string[] OpcoesMutacoes { get; } = [OpcaoDmpl, OpcaoDlpa];

    public bool PodeGerarEcd => Contexto.Pode(Permissao.SpedEcdGerar);

    private int AnoInt => (int)(Ano ?? DateTime.Today.Year);

    public OpcoesEcd Opcoes() => new(
        NumeroOrdem: NumeroLivro is { } n ? (int)n : null,
        NaturezaLivro: string.IsNullOrWhiteSpace(Natureza) ? "DIÁRIO GERAL" : Natureza.Trim(),
        NivelAglutinacao: (short)(NivelAglutinacao ?? 3),
        Mutacoes: Mutacoes == OpcaoDlpa ? TipoMutacoesEcd.Dlpa : TipoMutacoesEcd.Dmpl,
        IncluirHistoricosPadronizados: HistoricosPadronizados,
        ResponsavelPessoaJuridica: ResponsavelPessoaJuridica,
        AssinarComoProcurador: AssinarComoProcurador,
        NumeroCertidaoCrc: string.IsNullOrWhiteSpace(CertidaoCrc) ? null : CertidaoCrc.Trim());

    /// <summary>Carrega os dados da empresa ativa para conferência (abrir a tela e trocar de empresa).</summary>
    public async Task CarregarAsync()
    {
        OnPropertyChanged(nameof(PodeGerarEcd));
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var detalhe = await app.Empresas.ObterAsync(empresa);
        var cfg = await app.PlanoContas.ObterConfiguracaoAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        EmpresaNome = detalhe?.RazaoSocial ?? Contexto.DescricaoEmpresa;
        Nire = detalhe?.Nire ?? "";
        GrandePorte = cfg.GrandePorte;
        Previa = null;
        await SugerirNumeroAsync();
    }

    /// <summary>Número do livro sugerido pela API (o da última ECD do ano ou o seguinte ao do ano anterior).</summary>
    private async Task SugerirNumeroAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var r = await app.Ecd.ValidarAsync(empresa, AnoInt, new OpcoesEcd(), sessao.Id);
        NumeroLivroSugerido = r.Sucesso ? r.Valor.NumeroOrdem.ToString() : "";
        if (r.Sucesso) NumeroLivro ??= r.Valor.NumeroOrdem;
    }

    partial void OnAnoChanged(decimal? value)
    {
        Previa = null;
        NumeroLivro = null;
        if (Passo == 1 && Contexto.EmpresaId is not null) _ = SugerirNumeroAsync();
    }

    // ---------------------------------------------------------------- passos 2 e 3
    [ObservableProperty] private PreviaEcd? _previa;
    public ObservableCollection<ItemPreRequisito> PreRequisitos { get; } = [];
    public ObservableCollection<LinhaPendencia> Pendencias { get; } = [];
    [ObservableProperty] private string _resumoValidacao = "";
    [ObservableProperty] private bool _podeAvancarValidacao;

    private static readonly (string Titulo, string[] Prefixos, string Tela, string Atalho)[] Grupos =
    [
        ("Exercício encerrado (apuração do resultado)", ["NAO_ENCERRADO", "RESULTADO_NAO_ZERADO", "RESULTADO_ANTERIOR_ABERTO", "ENCERRAMENTO"], Telas.Encerramento, "Abrir Encerramento do exercício"),
        ("Plano de contas vinculado ao plano referencial da RFB", ["SEM_REFERENCIAL", "SEM_PLANO_REFERENCIAL"], Telas.MapeamentoReferencial, "Abrir Vínculo ao plano referencial"),
        ("Plano de contas (níveis e naturezas)", ["NIVEL_", "NATUREZA_SUPERIOR", "CONTA_SEM_SUPERIOR"], Telas.PlanoContas, "Abrir Plano de contas"),
        ("Cadastro da empresa (CNPJ, NIRE, município)", ["CNPJ_", "NIRE_", "MUNICIPIO_", "UF_", "NOME_", "PERIODO_"], Telas.Empresas, "Abrir Cadastro de empresas"),
        ("Contador cadastrado (CPF, CRC, e-mail, telefone)", ["SEM_CONTADOR", "CONTADOR_"], Telas.Contabilidade, "Abrir Contabilidade da empresa"),
        ("Responsável pela assinatura (sócio)", ["SEM_RESPONSAVEL", "RESPONSAVEIS", "SIGNATARIO_"], Telas.Socios, "Abrir Sócios"),
        ("Procurador com procuração vigente (assinatura como procurador)", ["PROCURADOR_", "PROCURACAO_"], Telas.Contabilidade, "Abrir Contadores"),
        ("Lançamentos e saldos consistentes", ["ABERTURA_", "BALANCETE_", "BALANCO_", "LANCAMENTO_", "HISTORICO_", "EXTEMPORANEO_", "SALDO_", "SEM_LANCAMENTOS", "SEM_BALANCO", "SEM_DRE", "RASCUNHO", "LACUNA"], Telas.DiferencasSaldos, "Abrir Diferenças de saldos"),
    ];

    /// <summary>Roda a validação prévia e monta o checklist e a lista de pendências.</summary>
    [RelayCommand]
    public async Task ValidarAsync()
    {
        PreRequisitos.Clear();
        Pendencias.Clear();
        PodeAvancarValidacao = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            var r = await app.Ecd.ValidarAsync(empresa, AnoInt, Opcoes(), usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            Previa = r.Valor;
            var restantes = r.Valor.Pendencias.ToList();
            foreach (var (titulo, prefixos, tela, atalho) in Grupos)
            {
                var doGrupo = restantes.Where(p => prefixos.Any(x => p.Codigo.StartsWith(x, StringComparison.Ordinal))).ToList();
                restantes = restantes.Except(doGrupo).ToList();
                var bloq = doGrupo.Where(p => p.Bloqueante).ToList();
                PreRequisitos.Add(new ItemPreRequisito(titulo, bloq.Count == 0,
                    bloq.Count == 0 ? (doGrupo.Count > 0 ? $"{doGrupo.Count} aviso(s)" : "Em ordem") : $"{bloq.Count} pendência(s) bloqueante(s)", tela, atalho));
            }
            foreach (var p in r.Valor.Pendencias.OrderByDescending(p => p.Bloqueante)) Pendencias.Add(new LinhaPendencia(p.Bloqueante, p.Mensagem));
            var nb = r.Valor.Bloqueantes.Count();
            var na = r.Valor.Avisos.Count();
            PodeAvancarValidacao = r.Valor.PodeGerar;
            ResumoValidacao = r.Valor.PodeGerar
                ? $"Nenhuma pendência bloqueante. {na} aviso(s). Período {r.Valor.Inicio:dd/MM/yyyy} a {r.Valor.Fim:dd/MM/yyyy}, livro nº {r.Valor.NumeroOrdem}, {r.Valor.Lancamentos} lançamento(s)."
                : $"{nb} pendência(s) bloqueante(s) e {na} aviso(s). Resolva as bloqueantes e valide de novo.";
            Signatarios.Clear();
            foreach (var s in r.Valor.Signatarios) Signatarios.Add(new SignatarioAssinatura(s));
        }, Permissao.SpedEcdGerar);
    }

    [RelayCommand]
    private void AbrirAtalho(string? tela)
    {
        if (tela is not null) Contexto.AbrirTela(tela);
    }

    // ---------------------------------------------------------------- passo 4
    private AssinantesSped? _assinantesSped;
    public AssinantesSped Assinantes => _assinantesSped ??= new AssinantesSped(Contexto, Erros);
    public ObservableCollection<SignatarioAssinatura> Signatarios => Assinantes.Signatarios;
    public ObservableCollection<DadosCertificado> Instalados => Assinantes.Instalados;
    public ObservableCollection<CertificadoDigital> Cadastrados => Assinantes.Cadastrados;
    public bool RepositorioWindowsDisponivel => AssinantesSped.RepositorioWindowsDisponivel;
    public string AvisoRepositorio => AssinantesSped.AvisoRepositorio;

    /// <summary>Lê os certificados do usuário no Windows e os cadastrados em Configurações › Certificados.</summary>
    [RelayCommand]
    public async Task CarregarCertificadosAsync()
    {
        await Assinantes.CarregarCertificadosAsync();
        OnPropertyChanged(nameof(RepositorioWindowsDisponivel));
        OnPropertyChanged(nameof(AvisoRepositorio));
    }

    private Task<bool> PrepararAssinantesAsync() => Assinantes.PrepararAsync();

    // ---------------------------------------------------------------- passo 5
    [ObservableProperty] private ArquivoEcd? _arquivo;
    [ObservableProperty] private string _resumoArquivo = "";
    [ObservableProperty] private string _pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Vorticis Contabil", "ECD");
    [ObservableProperty] private string _caminhoSalvo = "";
    public bool Gerado => Arquivo is not null;
    public bool Salvo => CaminhoSalvo.Length > 0;
    public const string InstrucoesPva =
        "No PVA do SPED Contábil: Escrituração › Importar › selecione o arquivo salvo › Validar › Transmitir.";
    public const string ObservacaoPva =
        "Se o PVA recusar a assinatura externa, gere o arquivo sem assinatura (volte ao passo 4 e escolha \"Não assinar agora\") e assine no próprio PVA.";

    partial void OnArquivoChanged(ArquivoEcd? value) => OnPropertyChanged(nameof(Gerado));
    partial void OnCaminhoSalvoChanged(string value) => OnPropertyChanged(nameof(Salvo));

    [RelayCommand]
    public Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        Arquivo = null;
        CaminhoSalvo = "";
        if (!await PrepararAssinantesAsync()) return;
        var r = await app.Ecd.GerarAsync(empresa, AnoInt, Opcoes(), usuario, Assinantes.Assinantes.Count == 0 ? null : Assinantes.Assinantes.ToList());
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Arquivo = r.Valor;
        var a = r.Valor;
        var regs = string.Join(", ", a.Registros.OrderBy(k => k.Key).Select(k => $"{k.Key}: {k.Value}"));
        var assin = a.Assinaturas.Count == 0
            ? "sem assinatura (assine no PVA)"
            : string.Join("; ", a.Assinaturas.Select(x => $"{x.Titular} ({x.CodigoQualificacao}){(x.Valida ? "" : " — INVÁLIDA")}"));
        ResumoArquivo = $"Arquivo: {a.NomeArquivo}\nLinhas: {a.Linhas}\nHash SHA-1: {a.HashSha1}\nRegistros: {regs}\nAssinaturas: {assin}" +
                        (a.Avisos.Count > 0 ? $"\nAvisos: {a.Avisos.Count}" : "");
        Mensagem = "ECD gerada. Escolha a pasta e salve o arquivo.";
    }, Permissao.SpedEcdGerar);

    [RelayCommand]
    public void Salvar()
    {
        Erros.Clear();
        if (Arquivo is not { } a) { Erros.Add("Gere o arquivo antes de salvar."); return; }
        if (string.IsNullOrWhiteSpace(Pasta)) { Erros.Add("Escolha a pasta de destino."); return; }
        try
        {
            Directory.CreateDirectory(Pasta);
            var caminho = Path.Combine(Pasta, a.NomeArquivo);
            File.WriteAllBytes(caminho, a.Conteudo);
            CaminhoSalvo = caminho;
            Mensagem = $"Arquivo salvo em {caminho}.";
        }
        catch (Exception ex) { Erros.Add($"Não foi possível salvar: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- navegação
    [RelayCommand]
    private void Voltar()
    {
        if (Passo > 1) { Erros.Clear(); Mensagem = null; Passo--; }
    }

    /// <summary>Avança validando o passo atual: 1→2 e 2→3 rodam a validação; 3→4 exige não haver bloqueantes; 4→5 confere os certificados.</summary>
    [RelayCommand]
    private async Task AvancarAsync()
    {
        Erros.Clear();
        Mensagem = null;
        if (Contexto.EmpresaId is null) { Erros.Add("Selecione uma empresa."); return; }
        if (!PodeGerarEcd) { Erros.Add($"Seu perfil nesta empresa não permite: {Permissao.SpedEcdGerar.Descricao()}."); return; }
        switch (Passo)
        {
            case 1:
                if (Ano is null or < 2000 or > 2100) { Erros.Add("Informe o exercício (ano)."); return; }
                if (NivelAglutinacao is null or < 2) { Erros.Add("O nível de aglutinação mínimo é 2."); return; }
                await ValidarAsync();
                if (Erros.Count > 0) return;
                Passo = 2;
                break;
            case 2:
                await ValidarAsync();
                if (Erros.Count > 0) return;
                Passo = 3;
                break;
            case 3:
                await ValidarAsync();
                if (Erros.Count > 0) return;
                if (!PodeAvancarValidacao) { Erros.Add("Há pendências bloqueantes: resolva-as (veja os atalhos no passo 2) e valide de novo."); return; }
                await CarregarCertificadosAsync();
                Passo = 4;
                break;
            case 4:
                if (!await PrepararAssinantesAsync()) return;
                Passo = 5;
                break;
        }
    }

    /// <summary>Volta ao primeiro passo para gerar outra ECD.</summary>
    [RelayCommand]
    private void Reiniciar()
    {
        (Arquivo, CaminhoSalvo, ResumoArquivo) = (null, "", "");
        Erros.Clear();
        Mensagem = null;
        Passo = 1;
    }

    // ---------------------------------------------------------------- histórico
    public ObservableCollection<EcdRegistrada> Geradas { get; } = [];

    [RelayCommand]
    public async Task CarregarGeradasAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var r = await app.Ecd.ListarGeradasAsync(empresa, sessao.Id);
        if (!EhAtual(g)) return;
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Geradas.Clear();
        foreach (var e in r.Valor) Geradas.Add(e);
    }

    partial void OnAbaChanged(int value)
    {
        if (value == AbaGeradas) _ = CarregarGeradasAsync();
    }

    /// <summary>Ao trocar de empresa o assistente recomeça.</summary>
    public void Limpar()
    {
        (Arquivo, CaminhoSalvo, ResumoArquivo, Previa) = (null, "", "", null);
        PreRequisitos.Clear();
        Pendencias.Clear();
        Signatarios.Clear();
        Geradas.Clear();
        Erros.Clear();
        Mensagem = null;
        NumeroLivro = null;
        Passo = 1;
    }
}
