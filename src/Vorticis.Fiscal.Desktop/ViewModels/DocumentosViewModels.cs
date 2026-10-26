using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Relatorios;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Relatorios;
using Vorticis.Fiscal.Sped;

namespace Vorticis.Fiscal.Desktop.ViewModels;

public sealed record ResultadoImportacaoLinha(string Arquivo, string Situacao, string Numero, string Operacao, string Valor, string Mensagem);

/// <summary>Repassa o andamento da importação à tela no máximo a cada 100 ms (milhares de arquivos não inundam a fila de eventos).</summary>
internal sealed class ProgressoLimitado(Action<ProgressoImportacao> aplicar) : IProgress<ProgressoImportacao>
{
    private readonly SynchronizationContext? _contexto = SynchronizationContext.Current;
    private long _ultimo;

    public void Report(ProgressoImportacao valor)
    {
        var agora = Environment.TickCount64;
        if (agora - Interlocked.Read(ref _ultimo) < 100 && valor.Processados < valor.Total) return;
        Interlocked.Exchange(ref _ultimo, agora);
        if (_contexto is null) aplicar(valor); else _contexto.Post(_ => aplicar(valor), null);
    }
}

/// <summary>
/// Documentos fiscais › Importação de XML: arquivos, vários arquivos, pastas (com subpastas) ou .zip (inclusive com subpastas) com NF-e, NFC-e e
/// NFS-e nacional. Roda em segundo plano (a tela continua livre), com barra de progresso, cancelamento e resumo final exportável. Também é usada
/// dentro de Lançamentos › Documentos fiscais › Entrada e Saída, que reage ao evento <see cref="Concluida"/>.
/// </summary>
public partial class ImportacaoXmlViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public ObservableCollection<ResultadoImportacaoLinha> Resultados { get; } = [];

    [ObservableProperty] private string _pasta = "";
    [ObservableProperty] private bool _incluirSubpastas = true;
    [ObservableProperty] private string _resumo = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PodeIniciar))] [NotifyCanExecuteChangedFor(nameof(CancelarCommand))] private bool _importando;
    [ObservableProperty] private double _percentual;
    [ObservableProperty] private bool _indeterminado;
    [ObservableProperty] private string _andamento = "";
    [ObservableProperty] private bool _temResumo;
    /// <summary>Fornecedor/cliente cadastrado a partir do XML: completar com a consulta pública do CNPJ (como no cadastro de clientes e fornecedores).</summary>
    [ObservableProperty] private bool _completarPeloCnpj = true;

    /// <summary>Resultado da última importação concluída (ou interrompida).</summary>
    public ResultadoImportacao? Ultimo { get; private set; }

    /// <summary>Disparado na tela, depois de cada importação, com o resultado (os dados já estão gravados).</summary>
    public event Func<ResultadoImportacao, Task>? Concluida;

    private CancellationTokenSource? _cancelamento;

    public bool PodeImportar => Contexto.Pode(Permissao.FiscalDocumentosImportar);
    public bool PodeIniciar => PodeImportar && !Importando;

    public void AtualizarPermissoes()
    {
        OnPropertyChanged(nameof(PodeImportar));
        OnPropertyChanged(nameof(PodeIniciar));
    }

    [RelayCommand]
    private Task ImportarPastaAsync()
    {
        if (string.IsNullOrWhiteSpace(Pasta) || !Directory.Exists(Pasta)) { Erros.Clear(); Erros.Add("Selecione uma pasta existente."); return Task.CompletedTask; }
        return ImportarPastaAsync(Pasta.Trim(), IncluirSubpastas);
    }

    /// <summary>Importa os .xml e .zip da pasta; com <paramref name="subpastas"/>, de todas as subpastas.</summary>
    public Task ImportarPastaAsync(string pasta, bool subpastas)
        => ExecutarLoteAsync((svc, empresa, usuario, progresso, ct) => svc.ImportarPastaAsync(empresa, pasta, subpastas, usuario, progresso, ct));

    /// <summary>Importa os arquivos escolhidos na tela (.xml e .zip).</summary>
    public Task ImportarArquivosAsync(IReadOnlyList<string> caminhos)
        => ExecutarLoteAsync((svc, empresa, usuario, progresso, ct) => svc.ImportarArquivosAsync(empresa, caminhos, usuario, progresso, ct));

    private bool PodeCancelar() => Importando;

    [RelayCommand(CanExecute = nameof(PodeCancelar))]
    private void Cancelar()
    {
        _cancelamento?.Cancel();
        Andamento = "Cancelando... as notas já gravadas serão mantidas.";
    }

    /// <summary>Resumo da última importação em CSV (para exportar), ou null se não houve importação.</summary>
    public string? ResumoCsv() => Ultimo?.ParaCsv();

    private Task ExecutarLoteAsync(Func<ServicoDocumentosFiscais, long, long, IProgress<ProgressoImportacao>, CancellationToken, Task<Resultado<ResultadoImportacao>>> importar)
        => ExecutarAsync(async (app, empresa, usuario) =>
        {
            if (Importando) { Erros.Add("Já existe uma importação em andamento."); return; }
            using var cancelamento = _cancelamento = new CancellationTokenSource();
            Importando = true;
            (Percentual, Indeterminado, Andamento, Resumo, TemResumo) = (0, true, "Preparando a importação...", "", false);
            Resultados.Clear();
            try
            {
                var progresso = new ProgressoLimitado(p =>
                {
                    if (!Importando) return;
                    (Indeterminado, Percentual) = (p.Total <= 0, p.Total > 0 ? 100.0 * p.Processados / p.Total : 0);
                    Andamento = $"{p.Processados} de {p.Total} - {p.Arquivo}";
                });
                var servico = app.Fiscal().Documentos;
                // Em segundo plano: leitura dos arquivos, XML e gravação não rodam na thread da tela.
                var r = await Task.Run(() => importar(servico, empresa, usuario, progresso, cancelamento.Token));
                MostrarResultadoImportacao(r);
                if (r.Sucesso && CompletarPeloCnpj) await CompletarCadastrosAsync(app, empresa, usuario, r.Valor);
                if (r.Sucesso) await ContabilizarOnlineAsync(app, empresa, usuario, r.Valor);
                if (r.Sucesso && Concluida is { } aviso)
                    foreach (var ouvinte in aviso.GetInvocationList().Cast<Func<ResultadoImportacao, Task>>()) await ouvinte(r.Valor);
            }
            finally
            {
                _cancelamento = null;
                (Importando, Indeterminado) = (false, false);
            }
        }, Permissao.FiscalDocumentosImportar);

    /// <summary>Completa pela consulta do CNPJ os fornecedores/clientes que a importação acabou de cadastrar com os dados do XML.</summary>
    public async Task CompletarCadastrosAsync(VorticisApp app, long empresa, long usuario, ResultadoImportacao r)
    {
        var novos = r.Arquivos.Select(a => a.ParticipanteNovo).OfType<string>().Distinct().ToList();
        if (novos.Count == 0) return;
        var completados = 0;
        var avisos = new List<string>();
        foreach (var doc in novos)
        {
            Andamento = $"Consultando o CNPJ {Domain.Cadastros.Cnpj.Formatar(Domain.Cadastros.Cnpj.SomenteDigitos(doc))} para completar o cadastro...";
            try
            {
                var c = await app.Fiscal().ParticipantesNota.CompletarPelaConsultaAsync(empresa, doc, usuario);
                if (c.Sucesso) completados++;
                else avisos.AddRange(c.Erros.Where(e => !e.StartsWith("CPF", StringComparison.Ordinal)));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or InvalidOperationException)
            {
                avisos.Add($"consulta do CNPJ indisponível ({ex.Message}).");
            }
        }
        Andamento = r.Interrompida ? "Importação cancelada." : "Importação concluída.";
        // a nota já está importada: falha na consulta é só aviso (o cadastro fica com os dados do XML e pode ser completado depois)
        if (completados > 0) Resumo += $" {completados} fornecedor(es)/cliente(s) novo(s) com o cadastro completado pela consulta do CNPJ.";
        if (avisos.Count > 0) Resumo += $" Cadastro não completado (ficou com os dados do XML): {string.Join(" ", avisos)}";
    }

    /// <summary>Com a contabilização on-line ligada nos Parâmetros, contabiliza as notas importadas (período das notas) e resume no painel.</summary>
    public async Task ContabilizarOnlineAsync(VorticisApp app, long empresa, long usuario, ResultadoImportacao r)
    {
        var datas = r.Arquivos.Where(a => a.Situacao == SituacaoImportacao.Importado && a.DataLivro is not null).Select(a => a.DataLivro!.Value).ToList();
        if (datas.Count == 0) return;
        var online = await app.Fiscal().Contabilizacao.ContabilizarSeOnlineAsync(empresa, datas.Min(), datas.Max(), null, usuario);
        if (online is not null) Resumo += TextoContabilizacaoOnline(online);
    }

    /// <summary>Texto do resultado da contabilização on-line (vazio quando ela está desligada).</summary>
    public static string TextoContabilizacaoOnline(Resultado<Contabilizacao.ResultadoContabilizacao>? r) => r switch
    {
        null => "",
        { Sucesso: false } => $" Contabilização on-line não feita: {string.Join(" ", r.Erros)}",
        { Valor.Registradas: > 0 } => $" {r.Valor.Registradas} nota(s) registrada(s) na Contabilização em lote (o lançamento é gerado lá).",
        _ => (r.Valor.Contabilizados > 0 ? $" Contabilização on-line: {r.Valor.Contabilizados} nota(s) contabilizada(s)." : "") +
             (r.Valor.Falhas.Count > 0 ? $" {r.Valor.Falhas.Count} nota(s) ficaram pendentes de contabilização (sem regra ou conta válida): " +
                                         string.Join("; ", r.Valor.Falhas.Take(5).Select(f => $"nota {f.Documento}: {string.Join(" ", f.Erros)}")) : ""),
    };

    private void MostrarResultadoImportacao(Resultado<ResultadoImportacao> r)
    {
        Resultados.Clear();
        Ultimo = r.Sucesso ? r.Valor : null;
        if (!r.Sucesso) { MostrarResultado(r, ""); Resumo = ""; TemResumo = false; Andamento = ""; return; }
        foreach (var a in r.Valor.Arquivos)
            Resultados.Add(new ResultadoImportacaoLinha(a.Arquivo, ResultadoImportacao.RotuloSituacao(a.Situacao), a.Numero ?? "", a.Operacao?.Rotulo() ?? "",
                a.Valor == 0 ? "" : Numeros.Moeda(a.Valor), a.Mensagem));
        Resumo = r.Valor.Resumo;
        TemResumo = true;
        Percentual = r.Valor.Interrompida ? Percentual : 100;
        Andamento = r.Valor.Interrompida ? "Importação cancelada." : "Importação concluída.";
        Mensagem = r.Valor.Interrompida ? "Importação cancelada: o que já foi importado foi mantido."
                 : r.Valor.Erros + r.Valor.OutrasEmpresas == 0 ? "Importação concluída." : "Importação concluída com pendências: veja a lista com o motivo de cada arquivo.";
    }
}

public sealed record DocumentoLinha(DocumentoFiscal Documento)
{
    public long Id => Documento.Id;
    public string Data => Documento.DataLivro.ToString("dd/MM/yyyy");
    public string Modelo => Documento.Modelo.Rotulo();
    public string Operacao => Documento.Operacao.Rotulo();
    public string Numero => Documento.Numero;
    public string Serie => Documento.Serie;
    public string Participante => Documento.NomeParticipante ?? "";
    public string Valor => Numeros.Moeda(Documento.Totais.Total);
    public string Icms => Numeros.MoedaOuVazio(Documento.Totais.Icms);
    public string Iss => Numeros.MoedaOuVazio(Documento.Totais.Iss);
    public string Situacao => Documento.Situacao.Rotulo();
    public string Contabilizado => Documento.Contabilizado ? "Sim" : "Não";
    public string Chave => Documento.Chave;
    public string Origem => Documento.Digitado ? "Digitada" : "XML";
}

public sealed record ItemDocumentoLinha(ItemFiscal Item)
{
    public int N => Item.Numero;
    public string Cfop => Item.Cfop;
    public string Ncm => Item.Ncm ?? "";
    public string Descricao => Item.Descricao ?? "";
    public string Valor => Numeros.Moeda(Item.ValorContabil);
    public string CstIcms => Item.CstIcms ?? "";
    public string Icms => Numeros.MoedaOuVazio(Item.Icms);
    public string IcmsSt => Numeros.MoedaOuVazio(Item.IcmsSt);
    public string Ipi => Numeros.MoedaOuVazio(Item.Ipi);
    public string Pis => Numeros.MoedaOuVazio(Item.Pis);
    public string Cofins => Numeros.MoedaOuVazio(Item.Cofins);
    public string Iss => Numeros.MoedaOuVazio(Item.Iss);
}

/// <summary>Documentos fiscais › Consulta de documentos, com filtros, itens (CFOP, NCM, CST e tributos) e exclusão de importados.</summary>
public partial class DocumentosViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public static IReadOnlyList<string> Operacoes { get; } = ["Todas", "Entradas", "Saídas"];
    public static IReadOnlyList<string> Modelos { get; } = ["Todos", "NF-e (55)", "NFC-e (65)", "NFS-e"];
    public static IReadOnlyList<string> Situacoes { get; } = ["Todas", "Autorizadas", "Canceladas", "Denegadas"];
    public static IReadOnlyList<string> Contabilizacoes { get; } = ["Todas", "Contabilizadas", "Não contabilizadas"];

    public ObservableCollection<DocumentoLinha> Documentos { get; } = [];
    public ObservableCollection<ItemDocumentoLinha> Itens { get; } = [];

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] private int _operacao;
    [ObservableProperty] private int _modelo;
    [ObservableProperty] private int _situacao;
    [ObservableProperty] private int _contabilizacao;
    [ObservableProperty] private string _texto = "";
    [ObservableProperty] private string _cfop = "";
    [ObservableProperty] private string _resumo = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ExcluirCommand))] private DocumentoLinha? _selecionado;

    public bool PodeExcluir => Contexto.Pode(Permissao.FiscalDocumentosImportar);

    public void AtualizarPermissoes() => OnPropertyChanged(nameof(PodeExcluir));

    partial void OnSelecionadoChanged(DocumentoLinha? value) => _ = CarregarItensAsync(value);

    private async Task CarregarItensAsync(DocumentoLinha? doc)
    {
        Itens.Clear();
        if (doc is null || Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var r = await app.Fiscal().Documentos.ItensAsync(empresa, doc.Id, sessao.Id);
        if (r.Sucesso && ReferenceEquals(Selecionado, doc)) foreach (var i in r.Valor) Itens.Add(new ItemDocumentoLinha(i));
    }

    [RelayCommand]
    private Task ConsultarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var f = new FiltroDocumentos(
            Inicio is { } i ? DateOnly.FromDateTime(i) : null, Fim is { } fi ? DateOnly.FromDateTime(fi) : null,
            Operacao switch { 1 => TipoOperacao.Entrada, 2 => TipoOperacao.Saida, _ => null },
            Modelo switch { 1 => ModeloDocumento.NFe55, 2 => ModeloDocumento.NFCe65, 3 => ModeloDocumento.NFSe, _ => null },
            Situacao switch { 1 => SituacaoDocumento.Autorizada, 2 => SituacaoDocumento.Cancelada, 3 => SituacaoDocumento.Denegada, _ => null },
            Texto, Cfop, Contabilizacao switch { 1 => true, 2 => false, _ => null });
        var r = await app.Fiscal().Documentos.ConsultarAsync(empresa, f, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Documentos.Clear();
        Itens.Clear();
        foreach (var d in r.Valor) Documentos.Add(new DocumentoLinha(d));
        var ativos = r.Valor.Where(d => d.Situacao == SituacaoDocumento.Autorizada).ToList();
        Resumo = $"{r.Valor.Count} documento(s) · total (autorizados) {Numeros.Moeda(ativos.Sum(d => d.Totais.Total))} · ICMS {Numeros.Moeda(ativos.Sum(d => d.Totais.Icms))} · " +
                 $"ISS {Numeros.Moeda(ativos.Sum(d => d.Totais.Iss))}" + (r.Valor.Count >= f.Limite ? $" · limitado a {f.Limite} documentos: refine o filtro" : "");
        Mensagem = r.Valor.Count == 0 ? "Nenhum documento encontrado." : null;
    }, Permissao.FiscalDocumentosConsultar);

    [RelayCommand(CanExecute = nameof(TemSelecionado))]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionado is not { } s) return;
        var r = await ExclusaoNotas.ExcluirAsync(Contexto, app, empresa, usuario, s);
        if (r is null) return;
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Documentos.Remove(s);
        Itens.Clear();
        Mensagem = "Documento excluído.";
    }, Permissao.FiscalDocumentosImportar);

    private bool TemSelecionado() => Selecionado is not null;
}

/// <summary>Base dos livros de Entradas e de Saídas.</summary>
public abstract partial class LivroFiscalViewModel(IContexto contexto) : RelatorioFiscalViewModel(contexto)
{
    private (DateOnly Ini, DateOnly Fim) _periodo;

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;

    protected abstract TipoOperacao Operacao { get; }
    public abstract string Titulo { get; }
    protected override string TituloRelatorio => Titulo;
    protected override string Parametros => $"Período: {_periodo.Ini:dd/MM/yyyy} a {_periodo.Fim:dd/MM/yyyy}";
    protected override string NomeArquivo => $"{(Operacao == TipoOperacao.Entrada ? "livro_entradas" : "livro_saidas")}_{_periodo.Ini:yyyyMMdd}_{_periodo.Fim:yyyyMMdd}";

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Data(Inicio, new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1));
        var fim = Data(Fim, ini);
        var r = await app.Fiscal().Documentos.LivroAsync(empresa, Operacao, ini, fim, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); Limpar(); return; }
        _periodo = (ini, fim);
        var itens = await app.Fiscal().Documentos.ResumoPorCfopAsync(empresa, Operacao, ini, fim, usuario);
        Mostrar(itens.Sucesso ? [TabelasFiscais.Livro(Operacao, r.Valor), TabelasFiscais.ResumoCfop(itens.Valor)] : [TabelasFiscais.Livro(Operacao, r.Valor)]);
        Mensagem = r.Valor.Count == 0 ? "Nenhum documento no período." : "Escolha o formato em Exportar: PDF, Excel (.xlsx) ou CSV.";
    }, Permissao.FiscalRelatorios);
}

public sealed class LivroEntradasViewModel(IContexto contexto) : LivroFiscalViewModel(contexto)
{
    protected override TipoOperacao Operacao => TipoOperacao.Entrada;
    public override string Titulo => "Livro de Registro de Entradas";
}

public sealed class LivroSaidasViewModel(IContexto contexto) : LivroFiscalViewModel(contexto)
{
    protected override TipoOperacao Operacao => TipoOperacao.Saida;
    public override string Titulo => "Livro de Registro de Saídas";
}

/// <summary>Base dos livros de apuração do ICMS e do IPI: um mês por vez.</summary>
public abstract partial class LivroApuracaoViewModel(IContexto contexto) : RelatorioFiscalViewModel(contexto)
{
    private DateOnly _competencia;

    public static IReadOnlyList<string> Meses { get; } =
        ["Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho", "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"];

    [ObservableProperty] private int _mes = DateTime.Today.AddMonths(-1).Month - 1;
    [ObservableProperty] private string _ano = DateTime.Today.AddMonths(-1).Year.ToString();
    [ObservableProperty] private string _saldoCredorAnterior = "";

    public abstract string Titulo { get; }
    public abstract bool MostrarSaldo { get; }
    protected abstract string Prefixo { get; }
    protected abstract TabelaRelatorio Montar(LivroApuracao livro);
    protected override string TituloRelatorio => Titulo;
    protected override string Parametros => $"Período: {_competencia:MM/yyyy}";
    protected override string NomeArquivo => $"{Prefixo}_{_competencia:yyyyMM}";

    private void Falhar(string erro) { Erros.Clear(); Erros.Add(erro); Limpar(); }

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (!int.TryParse(Ano, out var ano) || ano is < 1990 or > 2100 || Mes is < 0 or > 11) { Falhar("Informe um mês e um ano válidos."); return; }
        decimal saldo = 0;
        if (MostrarSaldo && !string.IsNullOrWhiteSpace(SaldoCredorAnterior) &&
            !decimal.TryParse(SaldoCredorAnterior.Replace("R$", "").Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), out saldo))
        { Falhar("Saldo credor do período anterior inválido."); return; }
        var comp = new DateOnly(ano, Mes + 1, 1);
        var r = await app.Fiscal().Efd.LivroApuracaoAsync(empresa, comp, saldo, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); Limpar(); return; }
        _competencia = comp;
        Mostrar(Montar(r.Valor));
        Mensagem = "Escolha o formato em Exportar: PDF, Excel (.xlsx) ou CSV.";
    }, Permissao.FiscalRelatorios);
}

public sealed class LivroApuracaoIcmsViewModel(IContexto contexto) : LivroApuracaoViewModel(contexto)
{
    public override string Titulo => "Livro de Apuração do ICMS";
    public override bool MostrarSaldo => true;
    protected override string Prefixo => "livro_apuracao_icms";
    protected override TabelaRelatorio Montar(LivroApuracao livro) => TabelasFiscais.LivroApuracaoIcms(livro);
}

public sealed class LivroApuracaoIpiViewModel(IContexto contexto) : LivroApuracaoViewModel(contexto)
{
    public override string Titulo => "Livro de Apuração do IPI";
    public override bool MostrarSaldo => false;
    protected override string Prefixo => "livro_apuracao_ipi";
    protected override TabelaRelatorio Montar(LivroApuracao livro) => TabelasFiscais.LivroApuracaoIpi(livro);
}

/// <summary>Relatórios › Resumo por CFOP (valor contábil e tributos por CFOP, entradas e saídas).</summary>
public partial class ResumoCfopViewModel(IContexto contexto) : RelatorioFiscalViewModel(contexto)
{
    private (DateOnly Ini, DateOnly Fim) _periodo;

    public static IReadOnlyList<string> Operacoes { get; } = ["Entradas e saídas", "Somente entradas", "Somente saídas"];

    [ObservableProperty] private DateTime? _inicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _fim = DateTime.Today;
    [ObservableProperty] private int _operacao;

    protected override string TituloRelatorio => "Resumo por CFOP";
    protected override string Parametros => $"Período: {_periodo.Ini:dd/MM/yyyy} a {_periodo.Fim:dd/MM/yyyy} · {Operacoes[Operacao]}";
    protected override string NomeArquivo => $"resumo_cfop_{_periodo.Ini:yyyyMMdd}_{_periodo.Fim:yyyyMMdd}";

    [RelayCommand]
    private Task GerarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var ini = Data(Inicio, new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1));
        var fim = Data(Fim, ini);
        var r = await app.Fiscal().Documentos.ResumoPorCfopAsync(empresa, Operacao switch { 1 => TipoOperacao.Entrada, 2 => TipoOperacao.Saida, _ => null }, ini, fim, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); Limpar(); return; }
        _periodo = (ini, fim);
        Mostrar(TabelasFiscais.ResumoCfop(r.Valor));
        Mensagem = r.Valor.Count == 0 ? "Nenhum documento no período." : "Escolha o formato em Exportar: PDF, Excel (.xlsx) ou CSV.";
    }, Permissao.FiscalRelatorios);
}

/// <summary>Exclusão de notas (digitadas ou importadas) com confirmação; nota contabilizada pede "Desfazer contabilização e excluir".</summary>
internal static class ExclusaoNotas
{
    /// <summary>Null = o usuário desistiu na confirmação.</summary>
    public static async Task<Resultado<int>?> ExcluirAsync(IContexto contexto, VorticisApp app, long empresa, long usuario, DocumentoLinha nota)
    {
        var contabilizada = nota.Documento.Contabilizado;
        var origem = nota.Documento.Digitado ? "digitada" : "importada de XML (o XML original também será removido)";
        var confirmou = contabilizada
            ? await contexto.ConfirmarAsync("Desfazer contabilização e excluir",
                $"A nota {nota.Modelo} {nota.Numero} está contabilizada. O lançamento contábil será estornado e, em seguida, a nota ({origem}) será excluída. Continuar?",
                "Desfazer contabilização e excluir", "Cancelar")
            : await contexto.ConfirmarAsync("Excluir nota", $"Excluir a nota {nota.Modelo} {nota.Numero}, {origem}?", "Excluir", "Cancelar");
        return confirmou ? await app.Fiscal().Contabilizacao.ExcluirDocumentosAsync(empresa, [nota.Id], contabilizada, usuario) : null;
    }
}
