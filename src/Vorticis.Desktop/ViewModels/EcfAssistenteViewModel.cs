using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Sistema;
using Vorticis.Application.Sped;
using Vorticis.Domain.Seguranca;
using Vorticis.Domain.Sped;

namespace Vorticis.Desktop.ViewModels;

/// <summary>
/// Auditoria › Gerar ECF (SPED): assistente em cinco passos (dados, pré-requisitos, validação, assinaturas, gerar e salvar) e aba com o
/// histórico das ECFs geradas. Mesmo padrão do assistente da ECD; cobre Lucro Real e Lucro Presumido trimestrais.
/// </summary>
public partial class EcfAssistenteViewModel : ModuloViewModel
{
    public const int TotalPassos = 5;
    public const int AbaAssistente = 0, AbaGeradas = 1;

    public static readonly string[] Titulos =
        ["Empresa e ano-calendário", "Pré-requisitos", "Validação prévia", "Assinaturas", "Gerar e salvar"];

    public static readonly string[] Explicacoes =
    [
        "Escolha a empresa (menu superior) e o ano-calendário. A forma de tributação (Lucro Real ou Presumido) vem do cadastro e das apurações trimestrais do módulo Fiscal; empresas do Simples Nacional não entregam ECF.",
        "Confira se o que a ECF exige já está pronto: apurações trimestrais, LALUR, vínculo ao plano referencial, exercício encerrado e cadastros. Para cada item pendente, use o atalho (quando houver) e volte aqui.",
        "O Vorticis confere o arquivo antes de gerar e compara os cálculos da ECF com as apurações do módulo Fiscal. Só é possível avançar sem pendências bloqueantes; avisos não impedem.",
        "Escolha o certificado digital de cada signatário (contador e responsável): A1 (arquivo .pfx), A3 (token ou cartão no Windows) ou o certificado padrão (definido em Configurações › Certificados), ou deixe para assinar depois no PVA.",
        "Gere o arquivo, confira o resumo e salve o .txt na pasta escolhida. Depois é só importar e validar no PVA da ECF.",
    ];

    public EcfAssistenteViewModel(IContexto contexto) : base(contexto)
    {
        Assinantes = new AssinantesSped(contexto, Erros);
    }

    public AssinantesSped Assinantes { get; }
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
    [ObservableProperty] private bool _retificadora;
    [ObservableProperty] private string _numeroRecibo = "";
    [ObservableProperty] private bool _informarEcd;
    [ObservableProperty] private bool _responsavelPessoaJuridica;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AvisoProcuracao), nameof(TemAvisoProcuracao))] private bool _assinarComoProcurador;

    /// <summary>Lembrete do passo final: a transmissão por procurador exige a procuração eletrônica no e-CAC.</summary>
    public string AvisoProcuracao => AssinarComoProcurador
        ? "Assinatura como procurador: para transmitir, a procuração eletrônica precisa estar cadastrada no e-CAC (Procurações), com poderes para a ECF, e o certificado usado deve ser o do procurador."
        : "";
    public bool TemAvisoProcuracao => AssinarComoProcurador;

    public bool PodeGerarEcf => Contexto.Pode(Permissao.SpedEcfGerar);
    private int AnoInt => (int)(Ano ?? DateTime.Today.Year - 1);

    public OpcoesEcf Opcoes() => new(
        Retificadora: Retificadora,
        NumeroRecibo: string.IsNullOrWhiteSpace(NumeroRecibo) ? null : NumeroRecibo.Trim(),
        InformarEcd: InformarEcd,
        ResponsavelPessoaJuridica: ResponsavelPessoaJuridica,
        AssinarComoProcurador: AssinarComoProcurador);

    /// <summary>Carrega os dados da empresa ativa para conferência (abrir a tela e trocar de empresa).</summary>
    public async Task CarregarAsync()
    {
        OnPropertyChanged(nameof(PodeGerarEcf));
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var detalhe = await app.Empresas.ObterAsync(empresa);
        if (!EhAtual(g)) return;
        EmpresaNome = detalhe?.RazaoSocial ?? Contexto.DescricaoEmpresa;
        Previa = null;
    }

    partial void OnAnoChanged(decimal? value) => Previa = null;

    // ---------------------------------------------------------------- passos 2 e 3
    [ObservableProperty] private PreviaEcf? _previa;
    public ObservableCollection<ItemPreRequisito> PreRequisitos { get; } = [];
    public ObservableCollection<LinhaPendencia> Pendencias { get; } = [];
    [ObservableProperty] private string _resumoValidacao = "";
    [ObservableProperty] private bool _podeAvancarValidacao;

    private static readonly (string Titulo, string[] Prefixos, string Tela, string Atalho)[] Grupos =
    [
        ("Apurações trimestrais do IRPJ e da CSLL (módulo Fiscal › Lucro Presumido/Real)", ["SEM_APURACAO", "APURACAO_", "REGIME_MISTO", "RECEITA_SEM", "PERCENTUAL_", "DIVERGENCIA"], "", ""),
        ("LALUR/LACS: códigos da tabela dinâmica e Parte B (módulo Fiscal)", ["LALUR_", "COMPENSACAO_", "PARTE_B", "DRE_SEM"], "", ""),
        ("Exercício encerrado e balanço", ["BALANCO_", "SEM_LANCAMENTOS", "RASCUNHO", "PERIODOS_"], Telas.Encerramento, "Abrir Encerramento do exercício"),
        ("Plano de contas vinculado ao plano referencial da RFB", ["SEM_REFERENCIAL", "SEM_PLANO_REFERENCIAL", "REFERENCIAL_INEXISTENTE"], Telas.MapeamentoReferencial, "Abrir Vínculo ao plano referencial"),
        ("Cadastro da empresa (CNPJ, natureza jurídica, CNAE, endereço, e-mail)", ["CNPJ_", "NOME_", "UF_", "MUNICIPIO_", "NATUREZA_", "CNAE", "ENDERECO", "EMAIL_EMPRESA", "RECIBO_"], Telas.Empresas, "Abrir Cadastro de empresas"),
        ("Contador cadastrado (CPF, CRC, e-mail, telefone)", ["SEM_CONTADOR", "CONTADOR_"], Telas.Contabilidade, "Abrir Contabilidade da empresa"),
        ("Responsável pela assinatura e sócios (Y600)", ["SEM_RESPONSAVEL", "RESPONSAVEIS", "SIGNATARIO_", "SEM_SOCIOS", "SOCIO"], Telas.Socios, "Abrir Sócios"),
        ("Procurador com procuração vigente (assinatura como procurador)", ["PROCURADOR_", "PROCURACAO_"], Telas.Contabilidade, "Abrir Contadores"),
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
            var r = await app.Ecf.ValidarAsync(empresa, AnoInt, Opcoes(), usuario);
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
                ? $"{r.Valor.NomeForma}: nenhuma pendência bloqueante. {na} aviso(s). Período {r.Valor.Inicio:dd/MM/yyyy} a {r.Valor.Fim:dd/MM/yyyy}, {r.Valor.Periodos} trimestre(s), {r.Valor.Lancamentos} lançamento(s)."
                : $"{r.Valor.NomeForma}: {nb} pendência(s) bloqueante(s) e {na} aviso(s). Resolva as bloqueantes e valide de novo.";
            Assinantes.Signatarios.Clear();
            foreach (var s in r.Valor.Signatarios) Assinantes.Signatarios.Add(new SignatarioAssinatura(s));
        }, Permissao.SpedEcfGerar);
    }

    [RelayCommand]
    private void AbrirAtalho(string? tela)
    {
        if (!string.IsNullOrEmpty(tela)) Contexto.AbrirTela(tela);
    }

    // ---------------------------------------------------------------- passo 4
    public ObservableCollection<SignatarioAssinatura> Signatarios => Assinantes.Signatarios;
    public ObservableCollection<DadosCertificado> Instalados => Assinantes.Instalados;
    public ObservableCollection<CertificadoDigital> Cadastrados => Assinantes.Cadastrados;
    public bool RepositorioWindowsDisponivel => AssinantesSped.RepositorioWindowsDisponivel;
    public string AvisoRepositorio => AssinantesSped.AvisoRepositorio;

    [RelayCommand]
    public async Task CarregarCertificadosAsync()
    {
        await Assinantes.CarregarCertificadosAsync();
        OnPropertyChanged(nameof(RepositorioWindowsDisponivel));
        OnPropertyChanged(nameof(AvisoRepositorio));
    }

    // ---------------------------------------------------------------- passo 5
    [ObservableProperty] private ArquivoEcf? _arquivo;
    [ObservableProperty] private string _resumoArquivo = "";
    [ObservableProperty] private string _pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Vorticis Contabil", "ECF");
    [ObservableProperty] private string _caminhoSalvo = "";
    public bool Gerado => Arquivo is not null;
    public bool Salvo => CaminhoSalvo.Length > 0;
    public const string InstrucoesPva =
        "No PVA da ECF: Escrituração › Importar › selecione o arquivo salvo › Validar › Transmitir. Se a ECD foi entregue, use Escrituração › Recuperar ECD antes de validar.";
    public const string ObservacaoPva =
        "Se o PVA recusar a assinatura externa, gere o arquivo sem assinatura (volte ao passo 4 e escolha \"Não assinar agora\") e assine no próprio PVA.";

    partial void OnArquivoChanged(ArquivoEcf? value) => OnPropertyChanged(nameof(Gerado));
    partial void OnCaminhoSalvoChanged(string value) => OnPropertyChanged(nameof(Salvo));

    [RelayCommand]
    public Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        Arquivo = null;
        CaminhoSalvo = "";
        if (!await Assinantes.PrepararAsync()) return;
        var r = await app.Ecf.GerarAsync(empresa, AnoInt, Opcoes(), usuario, Assinantes.Assinantes.Count == 0 ? null : Assinantes.Assinantes.ToList());
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Arquivo = r.Valor;
        var a = r.Valor;
        var regs = string.Join(", ", a.Registros.OrderBy(k => k.Key).Select(k => $"{k.Key}: {k.Value}"));
        var assin = a.Assinaturas.Count == 0
            ? "sem assinatura (assine no PVA)"
            : string.Join("; ", a.Assinaturas.Select(x => $"{x.Titular} ({x.CodigoQualificacao}){(x.Valida ? "" : " — INVÁLIDA")}"));
        ResumoArquivo = $"Arquivo: {a.NomeArquivo}\nLinhas: {a.Linhas}\nHash SHA-1: {a.HashSha1}\nRegistros: {regs}\nAssinaturas: {assin}" +
                        (a.Avisos.Count > 0 ? $"\nAvisos: {a.Avisos.Count}" : "");
        Mensagem = "ECF gerada. Escolha a pasta e salve o arquivo.";
    }, Permissao.SpedEcfGerar);

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
        if (!PodeGerarEcf) { Erros.Add($"Seu perfil nesta empresa não permite: {Permissao.SpedEcfGerar.Descricao()}."); return; }
        switch (Passo)
        {
            case 1:
                if (Ano is null or < 2014 or > 2100) { Erros.Add("Informe o ano-calendário."); return; }
                if (Retificadora && string.IsNullOrWhiteSpace(NumeroRecibo)) { Erros.Add("ECF retificadora exige o número do recibo da ECF anterior."); return; }
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
                if (!PodeAvancarValidacao) { Erros.Add("Há pendências bloqueantes: resolva-as (veja o passo 2) e valide de novo."); return; }
                await CarregarCertificadosAsync();
                Passo = 4;
                break;
            case 4:
                if (!await Assinantes.PrepararAsync()) return;
                Passo = 5;
                break;
        }
    }

    /// <summary>Volta ao primeiro passo para gerar outra ECF.</summary>
    [RelayCommand]
    private void Reiniciar()
    {
        (Arquivo, CaminhoSalvo, ResumoArquivo) = (null, "", "");
        Erros.Clear();
        Mensagem = null;
        Passo = 1;
    }

    // ---------------------------------------------------------------- histórico
    public ObservableCollection<EcfRegistrada> Geradas { get; } = [];

    [RelayCommand]
    public async Task CarregarGeradasAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var r = await app.Ecf.ListarGeradasAsync(empresa, sessao.Id);
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
        Assinantes.Signatarios.Clear();
        Geradas.Clear();
        Erros.Clear();
        Mensagem = null;
        Passo = 1;
    }
}
