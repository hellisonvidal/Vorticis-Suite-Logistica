using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Sistema;
using Vorticis.Application.Sped;
using Vorticis.Desktop.Comum.ViewModels;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Sped;
using Vorticis.Infra.Sped;

namespace Vorticis.Fiscal.Desktop.ViewModels;

/// <summary>Item da lista de pré-requisitos (passo 2): resolvido ou com pendências, e a tela que o resolve.</summary>
public sealed record ItemPreRequisitoEfd(string Titulo, bool Ok, string Detalhe, string Tela, string TextoAtalho)
{
    public string Marca => Ok ? "✔" : "✖";
    public string Cor => Ok ? "#1B7F3B" : "#B3261E";
    public bool TemAtalho => !Ok && Tela.Length > 0;
}

/// <summary>Pendência mostrada na validação prévia (passo 3).</summary>
public sealed record LinhaPendenciaEfd(bool Bloqueante, string Mensagem)
{
    public string Marca => Bloqueante ? "Bloqueante" : "Aviso";
    public string Cor => Bloqueante ? "#B3261E" : "#9A6700";
}

/// <summary>Linha do resumo da apuração (validação e arquivo gerado).</summary>
public sealed record LinhaResumoEfd(string Rotulo, string Valor);

/// <summary>
/// Base dos assistentes da EFD (Obrigações › SPED): cinco passos (empresa e período, pré-requisitos, validação prévia, assinatura, gerar e salvar)
/// e aba com o histórico dos arquivos gerados. Mesmo padrão dos assistentes da ECD e da ECF; as classes derivadas fornecem as opções de cada arquivo.
/// </summary>
public abstract partial class EfdAssistenteViewModel : ModuloViewModel
{
    public const int TotalPassos = 5;
    public const int AbaAssistente = 0, AbaGeradas = 1;
    public static readonly string[] Titulos = ["Empresa e período", "Pré-requisitos", "Validação prévia", "Assinatura", "Gerar e salvar"];
    public static IReadOnlyList<string> Meses { get; } = [.. CultureInfo.GetCultureInfo("pt-BR").DateTimeFormat.MonthNames.Take(12).Select(m => char.ToUpper(m[0]) + m[1..])];
    public static IReadOnlyList<string> ModosAssinatura { get; } =
        ["Não assinar agora (assino no PVA)", "Certificado A1 — arquivo .pfx", "Certificado A3 (token/cartão no Windows)", "Certificado cadastrado (ou padrão)"];

    protected EfdAssistenteViewModel(IContexto contexto) : base(contexto) { }

    public abstract TipoEfd Tipo { get; }
    public abstract string TituloTela { get; }
    public abstract string ExplicacaoPasso1 { get; }
    public abstract string InstrucoesPva { get; }
    public abstract string NomePasta { get; }
    public bool EhIcms => Tipo == TipoEfd.IcmsIpi;
    public bool EhContribuicoes => Tipo == TipoEfd.Contribuicoes;

    protected abstract Task<Resultado<PreviaEfd>> ValidarNoServicoAsync(VorticisApp app, long empresa, long usuario);
    protected abstract Task<Resultado<ArquivoEfd>> GerarNoServicoAsync(VorticisApp app, long empresa, long usuario, IReadOnlyList<CertificadoAssinante>? assinantes);
    protected virtual string? ValidarOpcoes() => null;
    protected virtual Task CarregarConfiguracaoAsync(VorticisApp app, long empresa, long usuario) => Task.CompletedTask;

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
    public string IndicadorPasso => string.Join("   ›   ", Titulos.Select((t, i) => i + 1 == Passo ? $"[{i + 1}. {t}]" : $"{i + 1}. {t}"));
    public string ExplicacaoPasso => Passo switch
    {
        1 => ExplicacaoPasso1,
        2 => "Confira se o que o arquivo exige já está pronto: cadastro da empresa, contador, notas fiscais, participantes e apuração. Para cada item pendente, use o atalho e volte aqui.",
        3 => "O Vorticis confere o arquivo antes de gerar, com as regras que o PVA aplicaria. Só é possível avançar sem pendências bloqueantes; avisos não impedem.",
        4 => "A assinatura é opcional: o fluxo oficial costuma assinar no próprio PVA, na transmissão. Se preferir assinar aqui, escolha o certificado (A1, A3 ou o cadastrado/padrão em Configurações › Certificados).",
        _ => "Gere o arquivo, confira o resumo e salve o .txt na pasta escolhida. Depois é só importar e validar no PVA.",
    };

    // ---------------------------------------------------------------- passo 1
    [ObservableProperty] private string _empresaNome = "";
    [ObservableProperty] private decimal? _ano = DateTime.Today.AddMonths(-1).Year;
    [ObservableProperty] private int _mes = DateTime.Today.AddMonths(-1).Month - 1;   // índice em Meses
    [ObservableProperty] private bool _retificadora;

    public bool PodeGerarEfd => Contexto.Pode(Permissao.FiscalSpedGerar);
    protected DateOnly Competencia => new((int)(Ano ?? DateTime.Today.Year), Mes + 1, 1);

    public virtual void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeGerarEfd));

    public async Task CarregarAsync()
    {
        OnPropertyChanged(nameof(PodeGerarEfd));
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var g = NovaGeracao();
        var detalhe = await app.Empresas.ObterAsync(empresa);
        if (!EhAtual(g)) return;
        EmpresaNome = detalhe?.RazaoSocial ?? Contexto.DescricaoEmpresa;
        Previa = null;
        try { await CarregarConfiguracaoAsync(app, empresa, sessao.Id); }
        catch (Exception ex) { Erros.Add(ex.Message); }
    }

    partial void OnAnoChanged(decimal? value) => Previa = null;
    partial void OnMesChanged(int value) => Previa = null;

    // ---------------------------------------------------------------- passos 2 e 3
    [ObservableProperty] private PreviaEfd? _previa;
    public ObservableCollection<ItemPreRequisitoEfd> PreRequisitos { get; } = [];
    public ObservableCollection<LinhaPendenciaEfd> Pendencias { get; } = [];
    public ObservableCollection<LinhaResumoEfd> Resumo { get; } = [];
    [ObservableProperty] private string _resumoValidacao = "";
    [ObservableProperty] private bool _podeAvancarValidacao;

    private static readonly (string Titulo, string[] Prefixos, string Tela, string Atalho)[] Grupos =
    [
        ("Cadastro da empresa (CNPJ, IE, endereço, município)", ["CNPJ", "UF", "MUNICIPIO", "SEM_IE", "SEM_ENDERECO", "PERFIL_", "ATIVIDADE_", "PERIODO", "SIMPLES"], TelasComuns.Empresas, "Abrir Cadastro de empresas"),
        ("Contador cadastrado (CPF, CRC, e-mail, telefone)", ["SEM_CONTADOR", "CONTADOR_"], TelasComuns.Contabilidade, "Abrir Contabilidade da empresa"),
        ("Documentos fiscais (chave, CFOP, itens, valores)", ["CHAVE_", "SEM_ITENS", "CFOP_", "VALOR_DOCUMENTO", "TOTAIS_NOTA", "ITEM_SEM_NCM", "NFSE_FORA", "SEM_MOVIMENTO", "DEVOLUCAO"], Telas.Documentos, "Abrir Consulta de documentos"),
        ("Participantes (clientes e fornecedores)", ["SEM_PARTICIPANTE", "PARTICIPANTE_"], TelasComuns.Clientes, "Abrir Clientes"),
        ("Apuração de PIS e COFINS do mês", ["SEM_APURACAO", "RECEITA_DIVERGE", "CREDITO_DIVERGE", "SALDO_CREDOR"], Telas.PisCofins, "Abrir Apuração de PIS e COFINS"),
        ("Códigos de receita e demais opções do arquivo", ["SEM_COD_REC", "ST_AVISO", "IPI_", "SALDO_NEGATIVO"], "", ""),
    ];

    [RelayCommand]
    public async Task ValidarAsync()
    {
        PreRequisitos.Clear();
        Pendencias.Clear();
        Resumo.Clear();
        PodeAvancarValidacao = false;
        await ExecutarAsync(async (app, empresa, usuario) =>
        {
            var r = await ValidarNoServicoAsync(app, empresa, usuario);
            if (!r.Sucesso) { MostrarResultado(r, ""); return; }
            Previa = r.Valor;
            var restantes = r.Valor.Pendencias.ToList();
            foreach (var (titulo, prefixos, tela, atalho) in Grupos)
            {
                var doGrupo = restantes.Where(p => prefixos.Any(x => p.Codigo.StartsWith(x, StringComparison.Ordinal))).ToList();
                restantes = restantes.Except(doGrupo).ToList();
                var bloq = doGrupo.Where(p => p.Bloqueante).ToList();
                PreRequisitos.Add(new ItemPreRequisitoEfd(titulo, bloq.Count == 0,
                    bloq.Count == 0 ? (doGrupo.Count > 0 ? $"{doGrupo.Count} aviso(s)" : "Em ordem") : $"{bloq.Count} pendência(s) bloqueante(s)", tela, atalho));
            }
            foreach (var p in r.Valor.Pendencias.OrderByDescending(p => p.Bloqueante)) Pendencias.Add(new LinhaPendenciaEfd(p.Bloqueante, p.Mensagem));
            foreach (var x in r.Valor.Resumo) Resumo.Add(new LinhaResumoEfd(x.Rotulo, x.Valor.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"))));
            var nb = r.Valor.Bloqueantes.Count();
            var na = r.Valor.Avisos.Count();
            PodeAvancarValidacao = r.Valor.PodeGerar;
            ResumoValidacao = r.Valor.PodeGerar
                ? $"Nenhuma pendência bloqueante. {na} aviso(s). Período {r.Valor.Inicio:dd/MM/yyyy} a {r.Valor.Fim:dd/MM/yyyy}, {r.Valor.Documentos} documento(s)."
                : $"{nb} pendência(s) bloqueante(s) e {na} aviso(s). Resolva as bloqueantes e valide de novo.";
        }, Permissao.FiscalSpedGerar);
    }

    [RelayCommand]
    private void AbrirAtalho(string? tela)
    {
        if (!string.IsNullOrEmpty(tela)) Contexto.AbrirTela(tela);
    }

    // ---------------------------------------------------------------- passo 4: assinatura (um certificado)
    public ObservableCollection<DadosCertificado> Instalados { get; } = [];
    public ObservableCollection<CertificadoDigital> Cadastrados { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PedeArquivo), nameof(PedeInstalado), nameof(PedeCadastrado), nameof(PedeSenha))] private int _modo;
    [ObservableProperty] private string _arquivoPfx = "";
    [ObservableProperty] private string _senha = "";
    [ObservableProperty] private DadosCertificado? _instalado;
    [ObservableProperty] private CertificadoDigital? _cadastrado;
    public bool PedeArquivo => Modo == 1;
    public bool PedeInstalado => Modo == 2;
    public bool PedeCadastrado => Modo == 3;
    public bool PedeSenha => Modo is 1 or 3;
    public bool RepositorioWindowsDisponivel => OperatingSystem.IsWindows();
    public string AvisoRepositorio => RepositorioWindowsDisponivel ? ""
        : "Certificados A3 / repositório do Windows só estão disponíveis no Windows. Use um arquivo A1 (.pfx), um certificado cadastrado ou assine no PVA.";
    private readonly List<CertificadoAssinante> _assinantes = [];

    [RelayCommand]
    public async Task CarregarCertificadosAsync()
    {
        Instalados.Clear();
        Cadastrados.Clear();
        try { foreach (var d in ServicoCertificados.ListarInstaladosNoWindows(ServicoCertificados.TipoA3)) Instalados.Add(d); }
        catch (Exception ex) { Erros.Add($"Não foi possível ler o repositório do Windows: {ex.Message}"); }
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var r = await app.Certificados.ListarParaAssinaturaAsync(sessao, empresa);
        if (r.Sucesso) foreach (var c in r.Valor) Cadastrados.Add(c);
        else foreach (var e in r.Erros) Erros.Add(e);
    }

    /// <summary>Abre o certificado escolhido (A1 com senha, A3 pelo Windows ou o cadastrado); sem escolha, o arquivo sai sem assinatura.</summary>
    private async Task<bool> PrepararAssinaturaAsync()
    {
        foreach (var c in _assinantes) c.Certificado.Dispose();
        _assinantes.Clear();
        if (Modo == 0) return true;
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return false;
        try
        {
            X509Certificate2? cert = null;
            switch (Modo)
            {
                case 1:
                    if (string.IsNullOrWhiteSpace(ArquivoPfx) || !File.Exists(ArquivoPfx)) { Erros.Add("Escolha o arquivo .pfx do certificado A1."); return false; }
                    cert = CertificadosSped.AbrirPfx(await File.ReadAllBytesAsync(ArquivoPfx), Senha);
                    break;
                case 2:
                    if (!RepositorioWindowsDisponivel) { Erros.Add(AvisoRepositorio); return false; }
                    if (Instalado is null) { Erros.Add("Escolha o certificado do repositório do Windows."); return false; }
                    cert = CertificadosSped.DoRepositorioWindows(Instalado.Impressao);
                    if (cert is null) { Erros.Add("Certificado não encontrado no Windows (token conectado?)."); return false; }
                    break;
                case 3:
                    if (Cadastrado is not { } cad) { Erros.Add("Escolha o certificado cadastrado."); return false; }
                    if (cad.Tipo == "A1")
                    {
                        var a1 = await app.Certificados.AbrirA1ParaAssinaturaAsync(sessao, empresa, cad.Id, Senha);
                        if (!a1.Sucesso) { foreach (var e in a1.Erros) Erros.Add(e); return false; }
                        cert = a1.Valor;
                    }
                    else
                    {
                        cert = OperatingSystem.IsWindows() ? CertificadosSped.DoRepositorioWindows(cad.Impressao) : null;
                        if (cert is null) { Erros.Add($"O certificado A3 \"{cad.Descricao}\" não está disponível neste computador."); return false; }
                    }
                    break;
            }
            if (cert is not null) _assinantes.Add(new CertificadoAssinante(cert, "900"));
            return true;
        }
        catch (Exception ex) { Erros.Add(ex.Message); return false; }
    }

    // ---------------------------------------------------------------- passo 5
    [ObservableProperty] private ArquivoEfd? _arquivo;
    [ObservableProperty] private string _resumoArquivo = "";
    [ObservableProperty] private string _pasta = "";
    [ObservableProperty] private string _caminhoSalvo = "";
    public bool Gerado => Arquivo is not null;
    public bool Salvo => CaminhoSalvo.Length > 0;
    public const string ObservacaoPva =
        "O PVA da EFD assina e transmite o arquivo na própria tela; se ele recusar a assinatura gravada aqui, gere sem assinatura (passo 4, \"Não assinar agora\") e assine no PVA.";

    partial void OnArquivoChanged(ArquivoEfd? value) => OnPropertyChanged(nameof(Gerado));
    partial void OnCaminhoSalvoChanged(string value) => OnPropertyChanged(nameof(Salvo));

    protected string PastaPadrao() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Vorticis Fiscal", NomePasta);

    [RelayCommand]
    public Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        Arquivo = null;
        CaminhoSalvo = "";
        if (!await PrepararAssinaturaAsync()) return;
        var r = await GerarNoServicoAsync(app, empresa, usuario, _assinantes.Count == 0 ? null : _assinantes.ToList());
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Arquivo = r.Valor;
        var a = r.Valor;
        var regs = string.Join(", ", a.Registros.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}: {k.Value}"));
        var assin = a.Assinaturas.Count == 0 ? "sem assinatura (assine no PVA)"
            : string.Join("; ", a.Assinaturas.Select(x => $"{x.Titular} ({x.CodigoQualificacao}){(x.Valida ? "" : " — INVÁLIDA")}"));
        ResumoArquivo = $"Arquivo: {a.NomeArquivo}\nLinhas: {a.Linhas}\nHash SHA-1: {a.HashSha1}\nRegistros: {regs}\nAssinaturas: {assin}" +
                        (a.Avisos.Count > 0 ? $"\nAvisos: {a.Avisos.Count}" : "");
        Mensagem = "Arquivo gerado. Escolha a pasta e salve.";
    }, Permissao.FiscalSpedGerar);

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

    [RelayCommand]
    private void Voltar()
    {
        if (Passo > 1) { Erros.Clear(); Mensagem = null; Passo--; }
    }

    [RelayCommand]
    private async Task AvancarAsync()
    {
        Erros.Clear();
        Mensagem = null;
        if (Contexto.EmpresaId is null) { Erros.Add("Selecione uma empresa."); return; }
        if (!PodeGerarEfd) { Erros.Add($"Seu perfil nesta empresa não permite: {Permissao.FiscalSpedGerar.Descricao()}."); return; }
        switch (Passo)
        {
            case 1:
                if (Ano is null or < 2010 or > 2100) { Erros.Add("Informe o ano."); return; }
                if (ValidarOpcoes() is { } erro) { Erros.Add(erro); return; }
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
                if (!await PrepararAssinaturaAsync()) return;
                if (Pasta.Length == 0) Pasta = PastaPadrao();
                Passo = 5;
                break;
        }
    }

    [RelayCommand]
    private void Reiniciar()
    {
        (Arquivo, CaminhoSalvo, ResumoArquivo) = (null, "", "");
        Erros.Clear();
        Mensagem = null;
        Passo = 1;
    }

    // ---------------------------------------------------------------- histórico
    public ObservableCollection<EfdRegistrada> Geradas { get; } = [];

    [RelayCommand]
    public async Task CarregarGeradasAsync()
    {
        if (Contexto.App is not { } app || Contexto.Sessao is not { } sessao || Contexto.EmpresaId is not { } empresa) return;
        var g = NovaGeracao();
        var r = await app.Fiscal().Efd.ListarGeradasAsync(empresa, Tipo, sessao.Id);
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
        Resumo.Clear();
        Geradas.Clear();
        Erros.Clear();
        Mensagem = null;
        Passo = 1;
    }
}

/// <summary>Obrigações › SPED › EFD ICMS/IPI (SPED Fiscal).</summary>
public sealed partial class EfdIcmsAssistenteViewModel(IContexto contexto) : EfdAssistenteViewModel(contexto)
{
    public static IReadOnlyList<string> Perfis { get; } = ["A — mais detalhado (C170 em todas as notas)", "B — C170 só nas entradas", "C — C170 só nas entradas"];
    public static IReadOnlyList<string> Atividades { get; } = ["Industrial ou equiparado a industrial (IPI)", "Outros (comércio, serviços...)"];

    public override TipoEfd Tipo => TipoEfd.IcmsIpi;
    public override string TituloTela => "Gerar EFD ICMS/IPI (SPED Fiscal)";
    public override string ExplicacaoPasso1 => "Escolha a empresa (menu superior), o mês de apuração e as opções do arquivo: perfil, atividade e os códigos de receita do ICMS na sua UF. Os documentos vêm do módulo Fiscal (entradas e saídas do mês, pela data de entrada/saída).";
    public override string InstrucoesPva => "No PVA da EFD ICMS/IPI: Arquivo › Importar EFD (ou Escrituração › Importar) › selecione o arquivo salvo › Validar › Assinar e Transmitir. Confira o resumo da apuração (E110) antes de transmitir.";
    public override string NomePasta => "EFD ICMS-IPI";

    [ObservableProperty] private int _perfil;
    [ObservableProperty] private int _atividade = 1;
    [ObservableProperty] private string _codigoReceita = "";
    [ObservableProperty] private string _codigoReceitaSt = "";
    [ObservableProperty] private decimal? _diaVencimento = 10;
    [ObservableProperty] private string _saldoCredorAnterior = "";
    [ObservableProperty] private bool _inventarioZerado;

    public OpcoesEfdIcms Opcoes() => new((char)('A' + Perfil), (char)('0' + Atividade), Retificadora, null,
        string.IsNullOrWhiteSpace(CodigoReceita) ? null : CodigoReceita.Trim(), string.IsNullOrWhiteSpace(CodigoReceitaSt) ? null : CodigoReceitaSt.Trim(),
        (int)(DiaVencimento ?? 10), Saldo(), InventarioZerado);

    private decimal Saldo() => decimal.TryParse(SaldoCredorAnterior.Replace("R$", "").Trim(), NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out var v) ? v : 0m;

    protected override string? ValidarOpcoes()
    {
        if (!string.IsNullOrWhiteSpace(SaldoCredorAnterior) && !decimal.TryParse(SaldoCredorAnterior.Replace("R$", "").Trim(), NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out var s))
            return "Saldo credor anterior inválido.";
        else if (Saldo() < 0) return "O saldo credor anterior não pode ser negativo.";
        return DiaVencimento is < 1 or > 28 ? "O dia de vencimento do ICMS deve estar entre 1 e 28." : null;
    }

    protected override async Task CarregarConfiguracaoAsync(VorticisApp app, long empresa, long usuario)
    {
        var r = await app.Fiscal().Efd.ConfiguracaoAsync(empresa, usuario);
        if (!r.Sucesso) return;
        var c = r.Valor;
        (Perfil, Atividade, CodigoReceita, CodigoReceitaSt, DiaVencimento) = (c.IcmsPerfil - 'A', c.IcmsIndicadorAtividade - '0', c.IcmsCodigoReceita ?? "", c.IcmsCodigoReceitaSt ?? "", c.IcmsDiaVencimento);
    }

    [RelayCommand]
    public Task SalvarConfiguracaoAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var atual = (await app.Fiscal().Efd.ConfiguracaoAsync(empresa, usuario)).Valor ?? new ConfiguracaoEfd();
        var o = Opcoes();
        var r = await app.Fiscal().Efd.SalvarConfiguracaoAsync(empresa, atual with { IcmsPerfil = o.Perfil, IcmsIndicadorAtividade = o.IndicadorAtividade, IcmsCodigoReceita = o.CodigoReceitaIcms,
            IcmsCodigoReceitaSt = o.CodigoReceitaIcmsSt, IcmsDiaVencimento = o.DiaVencimento }, usuario);
        MostrarResultado(r, "Opções salvas como padrão desta empresa.");
    }, Permissao.FiscalSpedGerar);

    protected override Task<Resultado<PreviaEfd>> ValidarNoServicoAsync(VorticisApp app, long empresa, long usuario) =>
        app.Fiscal().Efd.ValidarIcmsAsync(empresa, Competencia, Opcoes(), usuario);

    protected override Task<Resultado<ArquivoEfd>> GerarNoServicoAsync(VorticisApp app, long empresa, long usuario, IReadOnlyList<CertificadoAssinante>? assinantes) =>
        app.Fiscal().Efd.GerarIcmsAsync(empresa, Competencia, Opcoes(), usuario, assinantes);
}

/// <summary>Obrigações › SPED › EFD-Contribuições (PIS/Pasep e COFINS).</summary>
public sealed partial class EfdContribuicoesAssistenteViewModel(IContexto contexto) : EfdAssistenteViewModel(contexto)
{
    public static IReadOnlyList<string> Naturezas { get; } = ["00 — Pessoa jurídica em geral", "01 — Sociedade cooperativa", "02 — Entidade sujeita ao PIS/Pasep exclusivamente com base na folha de salários"];
    public static IReadOnlyList<string> Atividades { get; } = ["Industrial", "Prestador de serviços", "Comércio", "Instituição financeira", "Imobiliária", "Outros"];
    private static readonly char[] CodigosAtividade = ['0', '1', '2', '3', '4', '9'];

    public override TipoEfd Tipo => TipoEfd.Contribuicoes;
    public override string TituloTela => "Gerar EFD-Contribuições (PIS/Pasep e COFINS)";
    public override string ExplicacaoPasso1 => "Escolha a empresa (menu superior), o mês e as opções do arquivo. O bloco M segue a apuração de PIS e COFINS salva em Apuração › PIS e COFINS (cumulativa ou não cumulativa); os blocos A e C vêm das NFS-e e das NF-e do mês.";
    public override string InstrucoesPva => "No PVA da EFD-Contribuições: Escrituração › Importar › selecione o arquivo salvo › Validar › Assinar e Transmitir. Compare o M200/M600 com a apuração antes de transmitir.";
    public override string NomePasta => "EFD Contribuicoes";

    [ObservableProperty] private int _natureza;
    [ObservableProperty] private int _atividade = 2;
    [ObservableProperty] private string _numeroRecibo = "";

    public OpcoesEfdContribuicoes Opcoes() => new(Retificadora, string.IsNullOrWhiteSpace(NumeroRecibo) ? null : NumeroRecibo.Trim(), $"{Natureza:00}", CodigosAtividade[Atividade]);

    protected override string? ValidarOpcoes() =>
        Retificadora && string.IsNullOrWhiteSpace(NumeroRecibo) ? "A escrituração retificadora exige o número do recibo da escrituração anterior." : null;

    protected override async Task CarregarConfiguracaoAsync(VorticisApp app, long empresa, long usuario)
    {
        var r = await app.Fiscal().Efd.ConfiguracaoAsync(empresa, usuario);
        if (!r.Sucesso) return;
        (Natureza, Atividade) = (int.Parse(r.Valor.ContribNaturezaPj), Math.Max(0, Array.IndexOf(CodigosAtividade, r.Valor.ContribIndicadorAtividade)));
    }

    [RelayCommand]
    public Task SalvarConfiguracaoAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var atual = (await app.Fiscal().Efd.ConfiguracaoAsync(empresa, usuario)).Valor ?? new ConfiguracaoEfd();
        var o = Opcoes();
        var r = await app.Fiscal().Efd.SalvarConfiguracaoAsync(empresa, atual with { ContribNaturezaPj = o.IndicadorNaturezaPj, ContribIndicadorAtividade = o.IndicadorAtividade }, usuario);
        MostrarResultado(r, "Opções salvas como padrão desta empresa.");
    }, Permissao.FiscalSpedGerar);

    protected override Task<Resultado<PreviaEfd>> ValidarNoServicoAsync(VorticisApp app, long empresa, long usuario) =>
        app.Fiscal().Efd.ValidarContribuicoesAsync(empresa, Competencia, Opcoes(), usuario);

    protected override Task<Resultado<ArquivoEfd>> GerarNoServicoAsync(VorticisApp app, long empresa, long usuario, IReadOnlyList<CertificadoAssinante>? assinantes) =>
        app.Fiscal().Efd.GerarContribuicoesAsync(empresa, Competencia, Opcoes(), usuario, assinantes);
}
