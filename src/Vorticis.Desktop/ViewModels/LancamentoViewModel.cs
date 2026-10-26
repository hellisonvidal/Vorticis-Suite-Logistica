using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Application.Cadastros;
using Vorticis.Application.Contabil;
using Vorticis.Application.Sistema;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;
using Vorticis.Infra;

namespace Vorticis.Desktop.ViewModels;

/// <summary>
/// Uma linha da digitação. Com conta de débito e de crédito é um lançamento inteiro (partida dobrada); com só uma
/// das contas é uma partida simples, que se junta às demais linhas de uma conta só para formar um único lançamento.
/// Todos os campos são texto livre, editados direto pelo teclado.
/// </summary>
public partial class LinhaLancamentoViewModel : ObservableObject
{
    [ObservableProperty] private string _data = "";
    [ObservableProperty] private string _reduzido = "";
    [ObservableProperty] private string _conta = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ResumoContas))] private string _nomeConta = "";
    [ObservableProperty] private string _reduzidoCredito = "";
    [ObservableProperty] private string _contaCredito = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ResumoContas))] private string _nomeContaCredito = "";
    [ObservableProperty] private string _valor = "";
    [ObservableProperty] private string _documento = "";
    [ObservableProperty] private string _codigoHistorico = "";
    [ObservableProperty] private string _historico = "";
    [ObservableProperty] private string _centroCusto = "";
    [ObservableProperty] private string _participante = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ResumoContas))] private string _nomeParticipante = "";

    /// <summary>Criada pela seta para baixo e ainda não alterada pelo usuário (a seta para cima a cancela).</summary>
    public bool Nova { get; set; }
    public bool Editada { get; private set; }

    public LinhaLancamentoViewModel() => PropertyChanged += (_, e) =>
    {
        if (e.PropertyName is not (nameof(NomeConta) or nameof(NomeContaCredito) or nameof(NomeParticipante) or nameof(ResumoContas))) Editada = true;
    };

    /// <summary>Marca a linha como recém-criada depois de preencher as sugestões (que não contam como edição).</summary>
    public LinhaLancamentoViewModel ComoNova()
    {
        (Nova, Editada) = (true, false);
        return this;
    }

    /// <summary>Nomes das contas da linha (rodapé da tela).</summary>
    public string ResumoContas => (NomeContaCredito.Length == 0 ? NomeConta
        : $"Débito: {(NomeConta.Length > 0 ? NomeConta : "—")}   ·   Crédito: {NomeContaCredito}")
        + (NomeParticipante.Length > 0 ? $"   ·   Participante: {NomeParticipante}" : "");

    public decimal? ValorDecimal => Formato.LerValor(Valor);
    public bool TemDebito => Conta.Trim().Length > 0;
    public bool TemCredito => ContaCredito.Trim().Length > 0;
    /// <summary>Texto do histórico padrão escolhido (com variáveis {X}) e o último texto que o sistema preencheu a partir dele.</summary>
    public string? ModeloHistorico { get; set; }
    public string? HistoricoAutomatico { get; set; }
    public bool Vazia => Conta.Trim().Length == 0 && ContaCredito.Trim().Length == 0 && Valor.Trim().Length == 0;

    /// <summary>Valor com separador de milhar e duas casas (1.500,00), aplicado ao sair do campo.</summary>
    public void FormatarValor()
    {
        if (ValorDecimal is { } v) Valor = Formato.Moeda(v);
    }
}

/// <summary>Lançamento já efetuado (lista da tela de lançamento), inclusive os estornados.</summary>
public sealed record LancamentoEfetuadoLinha(long Id, long Numero, DateOnly DataLancamento, string Debito, string Credito, decimal Total,
                                             string Historico, string Documento, bool Estornado)
{
    public string Data => DataLancamento.ToString("dd/MM/yyyy");
    public string Valor => Formato.Moeda(Total);
    public string Situacao => Estornado ? "Estornado" : Historico.StartsWith("Estorno", StringComparison.OrdinalIgnoreCase) ? "Estorno" : "";
}

public sealed record ContaSelecionavel(string Codigo, string Descricao, int? Reduzido = null)
{
    public string Rotulo => $"{(Reduzido is { } r ? $"{r,5}  " : "")}{Codigo}  {Descricao}";
}

/// <summary>
/// Lançamentos › Lançamento contábil, pensado para o teclado: Enter/Tab avançam, seta para baixo na última linha cria
/// outra, seta para cima numa linha recém-criada e intocada a cancela, Ctrl+Enter salva. O tipo de lançamento é inferido:
/// uma linha com conta débito e conta crédito é um lançamento (partida dobrada); linhas com uma conta só (débito ou
/// crédito) são partidas simples que, somadas, precisam fechar (débitos = créditos) para formar um lançamento.
/// </summary>
public partial class LancamentoViewModel : ModuloViewModel, Comum.ViewModels.IAlteracoesPendentes
{
    private IReadOnlyList<ContaSelecionavel> _todasContas = [];
    private Dictionary<string, ContaSelecionavel> _porCodigo = [];
    private Dictionary<int, ContaSelecionavel> _porReduzido = [];
    private Dictionary<string, HistoricoPadrao> _historicos = [];
    private Dictionary<string, Participante> _participantes = [];
    private HashSet<string> _contasComParticipante = [];
    private bool _resolvendo;

    public LancamentoViewModel(IContexto contexto) : base(contexto)
    {
        Linhas.CollectionChanged += AoMudarLinhas;
        Limpar();
    }

    public ObservableCollection<LinhaLancamentoViewModel> Linhas { get; } = [];
    public ObservableCollection<ContaSelecionavel> Contas { get; } = [];
    public IReadOnlyList<ContaSelecionavel> TodasContas => _todasContas;

    [ObservableProperty] private DateTime? _data = DateTime.Today;
    [ObservableProperty] private LinhaLancamentoViewModel? _linhaSelecionada;
    [ObservableProperty] private ContaSelecionavel? _contaSelecionada;
    [ObservableProperty] private string _filtroConta = "";
    [ObservableProperty] private string _totalDebito = "0,00";
    [ObservableProperty] private string _totalCredito = "0,00";
    [ObservableProperty] private string _diferenca = "0,00";
    [ObservableProperty] private bool _balanceado;
    /// <summary>Há algo digitado para salvar (o salvamento confere se fecha e, se faltar a contrapartida, pergunta).</summary>
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SalvarCommand))] private bool _temDados;
    /// <summary>Janela pop-up de digitação aberta (a tela mostra só a grade dos lançamentos efetuados).</summary>
    [ObservableProperty] private bool _digitacaoAberta;
    public bool TemAlteracoesPendentes => DigitacaoAberta && TemDados;

    /// <summary>"Novo lançamento": digitação em branco no pop-up.</summary>
    [RelayCommand]
    private void NovoLancamento()
    {
        Erros.Clear();
        Mensagem = "";
        if (!Contexto.Pode(Permissao.LancamentoIncluir)) { Erros.Add("Sem permissão para incluir lançamentos."); return; }
        Limpar();
        DigitacaoAberta = true;
    }

    /// <summary>Botão Editar da grade: abre o selecionado no pop-up para alteração.</summary>
    [RelayCommand]
    private async Task EditarSelecionadoAsync()
    {
        Erros.Clear(); Mensagem = "";
        if (EfetuadoSelecionado is not { } e) { Erros.Add("Selecione um lançamento na grade para editar."); return; }
        await EditarAsync(e);
    }

    /// <summary>Abre o selecionado só para consulta.</summary>
    [RelayCommand]
    private async Task ConsultarSelecionadoAsync()
    {
        Erros.Clear(); Mensagem = "";
        if (EfetuadoSelecionado is not { } e) { Erros.Add("Selecione um lançamento na grade para consultar."); return; }
        await EditarAsync(e, somenteLeitura: true);
    }

    /// <summary>Botão Excluir: confirma e remove o lançamento (as regras de período, ECD e origem são do serviço).</summary>
    [RelayCommand]
    private Task ExcluirSelecionadoAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (EfetuadoSelecionado is not { } e) { Erros.Add("Selecione um lançamento na grade para excluir."); return; }
        var par = e.Estornado || e.Situacao == "Estorno"
            ? "\n\nEle faz par com um estorno: o lançamento original e o estorno serão excluídos juntos." : "";
        if (!await Contexto.ConfirmarAsync("Excluir lançamento",
                $"Deseja realmente excluir o lançamento nº {e.Numero} de {e.Data}, valor R$ {e.Valor}?{par}\n\nA exclusão fica registrada na trilha de auditoria.",
                "Excluir", "Cancelar")) return;
        var r = await app.Lancamentos.ExcluirAsync(empresa, e.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        if (Editando?.Id == e.Id || r.Valor.Any(x => x.Id == Editando?.Id)) CancelarDigitacao();
        await CarregarEfetuadosAsync();
        Mensagem = r.Valor.Count == 1 ? $"Lançamento nº {e.Numero} excluído."
            : $"Lançamentos nº {string.Join(" e ", r.Valor.Select(x => x.Numero).OrderBy(n => n))} (par de estorno) excluídos.";
    }, Permissao.LancamentoEstornar);

    /// <summary>Fecha o pop-up descartando a digitação (ou a alteração) em andamento.</summary>
    [RelayCommand]
    public void CancelarDigitacao()
    {
        Limpar();
        DigitacaoAberta = false;
    }

    /// <summary>Plano com participantes: a coluna Part. aparece e as contas dos participantes exigem o código.</summary>
    [ObservableProperty] private bool _usaParticipantes;

    // Lançamentos efetuados (inclusive estornados) e o estorno do selecionado
    public ObservableCollection<LancamentoEfetuadoLinha> Efetuados { get; } = [];
    [ObservableProperty] private DateTime? _listaInicio = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime? _listaFim = new DateTime(DateTime.Today.Year, 12, 31);
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(EstornarCommand))] private LancamentoEfetuadoLinha? _efetuadoSelecionado;
    [ObservableProperty] private DateTime? _dataEstorno;
    [ObservableProperty] private string _motivoEstorno = "";
    /// <summary>Configuração da empresa "Usar estorno de lançamentos" (ligada por padrão).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PodeEstornar), nameof(TextoEdicao))] private bool _usaEstorno = true;
    public bool PodeEstornar => UsaEstorno && Contexto.Pode(Permissao.LancamentoEstornar);
    public bool PodeExcluir => Contexto.Pode(Permissao.LancamentoEstornar);

    // Alteração de um lançamento efetuado (duplo clique na lista)
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EmEdicao), nameof(TextoEdicao), nameof(TituloDigitacao))] private LancamentoEfetuadoLinha? _editando;
    public bool EmEdicao => Editando is not null;
    /// <summary>Lançamento aberto só para consulta (pop-up sem digitação; só se pode estornar ou fechar).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PodeDigitar), nameof(TituloDigitacao), nameof(TextoEdicao))] [NotifyCanExecuteChangedFor(nameof(SalvarCommand))] private bool _somenteLeitura;
    public bool PodeDigitar => !SomenteLeitura;
    /// <summary>Cabeçalho da digitação: lançamento novo ou o número do existente.</summary>
    public string TituloDigitacao => Editando is { } e
        ? (SomenteLeitura ? $"Lançamento nº {e.Numero} — consulta" : $"Lançamento nº {e.Numero} — alteração")
        : "Novo lançamento";
    public string TextoEdicao => Editando is { } e
        ? SomenteLeitura ? $"Lançamento nº {e.Numero} de {e.Data} aberto somente para consulta."
        : UsaEstorno ? $"Alterando o lançamento nº {e.Numero} de {e.Data}. Ao salvar, o original é estornado e o corrigido é gravado. Limpar cancela a alteração."
        : $"Alterando o lançamento nº {e.Numero} de {e.Data}. Ao salvar, o lançamento é regravado com o mesmo número. Limpar cancela a alteração."
        : "";

    /// <summary>Pede o foco num campo (tag) de uma linha: a tela atende depois que a linha existir.</summary>
    public event Action<int, string>? FocoSolicitado;

    /// <summary>Troca de empresa: limpa a digitação e recarrega contas e históricos.</summary>
    public async Task AoTrocarEmpresaAsync()
    {
        await CarregarAsync();
        Limpar();
    }

    /// <summary>Ao abrir a tela: contas analíticas (com reduzido), históricos padrão e participantes.</summary>
    public async Task CarregarAsync()
    {
        var geracao = NovaGeracao();
        IReadOnlyList<ContaDetalhe> plano = [];
        IReadOnlyList<HistoricoPadrao> historicos = [];
        IReadOnlyList<Participante> participantes = [];
        var usa = false;
        var usaEstorno = true;
        if (Contexto.App is { } app)
        {
            if (Contexto.EmpresaId is { } empresa && Contexto.Sessao is { } sessao)
            {
                usaEstorno = await app.Lancamentos.UsaEstornoAsync(empresa, sessao.Id);
                plano = await app.PlanoContas.ListarAsync(empresa, sessao.Id);
                historicos = await app.Historicos.ListarAsync(empresa, sessao.Id);
                usa = await app.Participantes.UsaParticipantesAsync(empresa);
                if (usa) participantes = await app.Participantes.ListarAsync(empresa, sessao.Id);
            }
        }
        if (!EhAtual(geracao)) return;
        _todasContas = plano.Where(c => c.Analitica && c.Ativa).Select(c => new ContaSelecionavel(c.Codigo, c.Descricao, c.CodigoReduzido)).ToArray();
        _porCodigo = _todasContas.ToDictionary(c => c.Codigo);
        _porReduzido = _todasContas.Where(c => c.Reduzido is not null).ToDictionary(c => c.Reduzido!.Value);
        _historicos = historicos.ToDictionary(h => h.Codigo);
        UsaParticipantes = usa;
        UsaEstorno = usaEstorno;
        _participantes = participantes.Where(p => p.Ativo).ToDictionary(p => p.Codigo);
        _contasComParticipante = plano.Where(c => c.ExigeParticipante).Select(c => c.Codigo)
                                      .Concat(participantes.Where(p => p.ControleConta && p.CodigoConta is not null).Select(p => p.CodigoConta!))
                                      .ToHashSet();
        OnPropertyChanged(nameof(TodasContas));
        if (EmEdicao) { Filtrar(); OnPropertyChanged(nameof(PodeEstornar)); await CarregarEfetuadosAsync(); return; }
        if (Linhas.All(l => l.Vazia)) Limpar();
        Filtrar();
        OnPropertyChanged(nameof(PodeEstornar));
        await CarregarEfetuadosAsync();
    }

    /// <summary>Lista os lançamentos do período (os estornados continuam na lista, marcados).</summary>
    [RelayCommand]
    public async Task CarregarEfetuadosAsync()
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var ini = Data(ListaInicio, new DateOnly(DateTime.Today.Year, 1, 1));
        var r = await app.Relatorios.DiarioAsync(empresa, ini, Data(ListaFim, ini.AddYears(1).AddDays(-1)), sessao.Id);
        if (!r.Sucesso) return;
        var id = EfetuadoSelecionado?.Id;
        Efetuados.Clear();
        foreach (var g in r.Valor.GroupBy(l => l.LancamentoId).OrderByDescending(g => g.First().Data).ThenByDescending(g => g.First().Numero))
        {
            var deb = g.Where(l => l.DC == IndicadorDC.D).ToList();
            var cred = g.Where(l => l.DC == IndicadorDC.C).ToList();
            static string Contas(List<LinhaDiario> ls) =>
                ls.Count == 0 ? "" : $"{ls[0].CodigoConta} {ls[0].DescricaoConta}" + (ls.Count > 1 ? $" (+{ls.Count - 1})" : "");
            var f = g.First();
            Efetuados.Add(new LancamentoEfetuadoLinha(g.Key, f.Numero, f.Data, Contas(deb), Contas(cred), deb.Sum(l => l.Valor), f.Historico,
                g.Select(l => l.Documento).FirstOrDefault(d => !string.IsNullOrEmpty(d)) ?? "", f.Status == StatusLancamento.Estornado));
        }
        EfetuadoSelecionado = Efetuados.FirstOrDefault(e => e.Id == id);
    }

    partial void OnEfetuadoSelecionadoChanged(LancamentoEfetuadoLinha? value)
    {
        if (value is not null) DataEstorno = value.DataLancamento.ToDateTime(TimeOnly.MinValue);
    }

    private bool PodeEstornarSelecionado() => EfetuadoSelecionado is { Estornado: false };

    /// <summary>Estorna o lançamento selecionado na lista (gera o lançamento inverso; o original fica marcado como estornado).</summary>
    [RelayCommand(CanExecute = nameof(PodeEstornarSelecionado))]
    private Task EstornarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (EfetuadoSelecionado is not { } sel) return;
        if (string.IsNullOrWhiteSpace(MotivoEstorno)) { Erros.Add("Informe o motivo do estorno."); return; }
        if (!await Contexto.ConfirmarAsync("Estornar lançamento",
                $"Estornar o lançamento nº {sel.Numero} de {sel.Data} ({sel.Valor})?\n\nSerá gerado o lançamento inverso; o original continua na lista, marcado como estornado.",
                "Estornar", "Cancelar")) return;
        var r = await app.Lancamentos.EstornarAsync(empresa, sel.Id, Data(DataEstorno, sel.DataLancamento), MotivoEstorno.Trim(), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        MotivoEstorno = "";
        if (DigitacaoAberta) CancelarDigitacao();
        await CarregarEfetuadosAsync();
        Mensagem = $"Lançamento nº {sel.Numero} estornado pelo lançamento nº {r.Valor.Numero}.";
    }, Permissao.LancamentoEstornar);

    /// <summary>
    /// Duplo clique num lançamento efetuado: carrega as partidas na digitação para alterar. Débito e crédito únicos
    /// cabem numa linha; os demais abrem com uma conta por linha (partidas simples).
    /// </summary>
    public Task EditarAsync(LancamentoEfetuadoLinha efetuado, bool somenteLeitura = false) => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (efetuado.Estornado && !somenteLeitura) { Erros.Add($"O lançamento nº {efetuado.Numero} está estornado e não pode ser alterado."); return; }
        if (!somenteLeitura && !Contexto.Pode(Permissao.LancamentoEstornar)) { Erros.Add("Alterar um lançamento exige permissão para estornar."); return; }
        var r = await app.Lancamentos.ObterAsync(empresa, efetuado.Id, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var l = r.Valor;
        if (l.Origem != OrigemLancamento.Manual && !somenteLeitura)
        {
            Erros.Add($"O lançamento nº {efetuado.Numero} não foi digitado manualmente e não pode ser alterado aqui{(UsaEstorno ? ": corrija-o por estorno" : "; desfaça-o no módulo de origem")}.");
            return;
        }
        var ps = l.Partidas;
        var umaLinha = ps.Count == 2 && ps[0].DC == IndicadorDC.D && ps[1].DC == IndicadorDC.C && ps[0].Valor == ps[1].Valor
                       && ps[0].Historico == ps[1].Historico && ps[0].CodigoCentroCusto == ps[1].CodigoCentroCusto
                       && ps[0].NumeroArquivo == ps[1].NumeroArquivo;
        Editando = efetuado;
        SomenteLeitura = somenteLeitura;
        EfetuadoSelecionado = efetuado;
        Data = l.Data.ToDateTime(TimeOnly.MinValue);
        Linhas.Clear();
        var data = l.Data.ToString("dd/MM/yyyy");
        if (umaLinha)
            Linhas.Add(new LinhaLancamentoViewModel
            {
                Data = data, Conta = ps[0].CodigoConta, ContaCredito = ps[1].CodigoConta, Valor = Formato.Moeda(ps[0].Valor),
                Documento = ps[0].NumeroArquivo ?? "", Historico = ps[0].Historico, CentroCusto = ps[0].CodigoCentroCusto ?? "",
                Participante = ps[0].CodigoParticipante ?? ps[1].CodigoParticipante ?? "",
            });
        else
            foreach (var p in ps)
                Linhas.Add(new LinhaLancamentoViewModel
                {
                    Data = data, Conta = p.DC == IndicadorDC.D ? p.CodigoConta : "", ContaCredito = p.DC == IndicadorDC.C ? p.CodigoConta : "",
                    Valor = Formato.Moeda(p.Valor),
                    Documento = p.NumeroArquivo ?? "", Historico = p.Historico, CentroCusto = p.CodigoCentroCusto ?? "",
                    Participante = p.CodigoParticipante ?? "",
                });
        foreach (var linha in Linhas)                       // reduzidos e nomes das contas
        {
            ConfirmarConta(linha, credito: false);
            ConfirmarConta(linha, credito: true);
            if (linha.Participante.Length > 0 && _participantes.TryGetValue(linha.Participante, out var pe)) linha.NomeParticipante = pe.Nome;
        }
        LinhaSelecionada = Linhas[0];
        DigitacaoAberta = true;
    }, Permissao.LancamentoIncluir);

    /// <summary>Enter no último campo: salva quando o lançamento já fecha; senão (false) segue para a próxima linha.</summary>
    public bool SalvarPeloEnter()
    {
        if (!Balanceado || !SalvarCommand.CanExecute(null)) return false;
        SalvarCommand.Execute(null);
        return true;
    }

    /// <summary>Grava a digitação: inclusão normal ou, em alteração, estorno do original + lançamento corrigido.</summary>
    private async Task<bool> GravarAsync(VorticisApp app, long empresa, long usuario, IReadOnlyList<LancamentoProposto> propostas, IReadOnlyList<int> linhasOrigem)
    {
        if (Editando is { } ed)
        {
            if (propostas.Count != 1) { Erros.Add("Na alteração, o lançamento deve continuar sendo um só (uma linha)."); return false; }
            var s = await app.Lancamentos.SubstituirAsync(ed.Id, propostas[0], usuario);
            if (!s.Sucesso) { foreach (var e in s.Erros) Erros.Add(e); return false; }
            Mensagem = s.Valor.Count == 1 ? $"Lançamento nº {ed.Numero} alterado."
                : $"Lançamento nº {ed.Numero} alterado: estornado pelo nº {s.Valor[0].Numero} e regravado como nº {s.Valor[1].Numero}.";
            return true;
        }
        var r = await app.Lancamentos.EfetivarLoteAsync(propostas, null, usuario);
        if (!r.Sucesso)
        {
            // "Lançamento 2: ..." do serviço vira "Linha N: ..." da tela (N = linha que originou aquele lançamento).
            foreach (var e in r.Erros)
            {
                var m = System.Text.RegularExpressions.Regex.Match(e, @"^Lançamento (\d+): (.*)$");
                Erros.Add(m.Success && int.Parse(m.Groups[1].Value) is var k && k >= 1 && k <= linhasOrigem.Count ? $"Linha {linhasOrigem[k - 1]}: {m.Groups[2].Value}" : e);
            }
            return false;
        }
        Mensagem = r.Valor.Count == 1 ? $"Lançamento nº {r.Valor[0].Numero} salvo."
                                      : $"{r.Valor.Count} lançamentos salvos (nº {r.Valor.Min(x => x.Numero)} a {r.Valor.Max(x => x.Numero)}).";
        return true;
    }

    partial void OnFiltroContaChanged(string value) => Filtrar();

    private void Filtrar()
    {
        Contas.Clear();
        foreach (var c in _todasContas.Where(c => Combina(c, FiltroConta))) Contas.Add(c);
    }

    /// <summary>Busca de conta: reduzido exato, início do código ou parte do nome.</summary>
    public static bool Combina(ContaSelecionavel c, string? texto)
    {
        var f = (texto ?? "").Trim();
        return f.Length == 0 || c.Codigo.StartsWith(f, StringComparison.Ordinal) || c.Reduzido?.ToString() == f
               || c.Descricao.Contains(f, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Clique numa conta da lista: vai para a linha selecionada (débito, se vazio; senão crédito).</summary>
    partial void OnContaSelecionadaChanged(ContaSelecionavel? value)
    {
        if (value is null || LinhaSelecionada is not { } l) return;
        if (l.Conta.Trim().Length > 0 && l.ContaCredito.Trim().Length == 0) l.ContaCredito = value.Codigo;
        else l.Conta = value.Codigo;
    }

    private LinhaLancamentoViewModel NovaLinha()
    {
        var anterior = Linhas.LastOrDefault();
        var nova = new LinhaLancamentoViewModel { Data = anterior?.Data ?? (Data ?? DateTime.Today).ToString("dd/MM/yyyy") };
        // Depois de uma partida simples (uma conta só), repete histórico e documento e sugere o valor que fecha o lançamento.
        if (anterior is { } ant && !(ant.TemDebito && ant.TemCredito))
        {
            (nova.Historico, nova.CodigoHistorico, nova.Documento) = (ant.Historico, ant.CodigoHistorico, ant.Documento);
            (nova.ModeloHistorico, nova.HistoricoAutomatico) = (ant.ModeloHistorico, ant.HistoricoAutomatico);
        }
        var dif = SomaDebitos() - SomaCreditos();
        if (dif != 0) nova.Valor = Formato.Moeda(Math.Abs(dif));
        return nova;
    }

    /// <summary>Seta para baixo na última linha: cria a próxima.</summary>
    public LinhaLancamentoViewModel AdicionarLinha()
    {
        var nova = NovaLinha().ComoNova();
        Linhas.Add(nova);
        LinhaSelecionada = nova;
        return nova;
    }

    /// <summary>Seta para cima numa linha recém-criada e não alterada: desfaz a criação. Retorna se cancelou.</summary>
    public bool CancelarLinhaNova(LinhaLancamentoViewModel linha)
    {
        if (!linha.Nova || linha.Editada || Linhas.Count <= 1 || !ReferenceEquals(linha, Linhas[^1])) return false;
        Linhas.Remove(linha);
        LinhaSelecionada = Linhas[^1];
        return true;
    }

    [RelayCommand]
    private void Limpar()
    {
        Editando = null;
        SomenteLeitura = false;
        Linhas.Clear();
        Linhas.Add(new LinhaLancamentoViewModel { Data = (Data ?? DateTime.Today).ToString("dd/MM/yyyy") });
        LinhaSelecionada = Linhas[0];
    }

    /// <summary>Botão "Nova partida" (o mesmo que a seta para baixo na última partida): acrescenta uma partida e põe o foco na data.</summary>
    [RelayCommand]
    private void NovaPartida()
    {
        var nova = AdicionarLinha();
        FocoSolicitado?.Invoke(Linhas.IndexOf(nova), "data");
    }

    /// <summary>Exclui a linha atual (Ctrl+Delete); a última linha é apenas esvaziada.</summary>
    [RelayCommand]
    private void ExcluirLinha()
    {
        if (LinhaSelecionada is not { } l) return;
        if (Linhas.Count <= 1)
        {
            var i = Linhas.IndexOf(l);
            Linhas[i] = new LinhaLancamentoViewModel { Data = l.Data };
            LinhaSelecionada = Linhas[i];
            return;
        }
        var pos = Linhas.IndexOf(l);
        Linhas.Remove(l);
        LinhaSelecionada = Linhas[Math.Min(pos, Linhas.Count - 1)];
    }

    private bool PodeSalvar() => TemDados && !SomenteLeitura;

    [RelayCommand(CanExecute = nameof(PodeSalvar))]
    private Task SalvarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var preenchidas = Linhas.Where(l => !l.Vazia).ToList();
        var propostas = new List<LancamentoProposto>();
        var linhasOrigem = new List<int>();          // nº da linha da tela que originou cada proposta
        var simples = new List<LinhaLancamentoViewModel>();
        foreach (var l in preenchidas)
        {
            var n = Linhas.IndexOf(l) + 1;
            var data = Formato.LerData(CompletarData(l.Data));
            var ok = true;
            if (data is null) { Erros.Add($"Linha {n}: data inválida."); ok = false; }
            if (l.ValorDecimal is not > 0) { Erros.Add($"Linha {n}: informe o valor."); ok = false; }
            if (!l.TemDebito && !l.TemCredito) { Erros.Add($"Linha {n}: informe a conta de débito e/ou a de crédito."); ok = false; }
            foreach (var v in HistoricoPendente(l.Historico))
            {
                Erros.Add($"Linha {n}: complete no histórico a variável {{{v}}}.");
                ok = false;
            }
            if (!ok) continue;
            if (!(l.TemDebito && l.TemCredito)) { simples.Add(l); continue; }
            var cc = string.IsNullOrWhiteSpace(l.CentroCusto) ? null : l.CentroCusto.Trim();
            var (partD, partC) = (Part(l, l.Conta), Part(l, l.ContaCredito));
            if (l.Participante.Trim().Length > 0 && partD is null && partC is null)
            {
                Erros.Add($"Linha {n}: participante informado, mas nenhuma das contas controla participantes.");
                continue;
            }
            propostas.Add(new LancamentoProposto(empresa, data!.Value, OrigemLancamento.Manual,
            [
                new PartidaProposta(l.Conta.Trim(), IndicadorDC.D, l.ValorDecimal!.Value, l.Historico.Trim(), cc, partD, Doc(l)),
                new PartidaProposta(l.ContaCredito.Trim(), IndicadorDC.C, l.ValorDecimal.Value, l.Historico.Trim(), cc, partC, Doc(l)),
            ]));
            linhasOrigem.Add(n);
        }
        if (Erros.Count > 0) return;
        if (preenchidas.Count == 0) { Erros.Add("Digite ao menos um lançamento."); return; }

        // Linhas de uma conta só (partidas simples): juntas formam um único lançamento, que precisa fechar.
        if (simples.Count > 0)
        {
            var datas = simples.Select(l => Formato.LerData(CompletarData(l.Data))!.Value).Distinct().ToList();
            if (datas.Count > 1)
            {
                Erros.Add("As linhas com uma conta só formam um único lançamento e devem ter a mesma data.");
                return;
            }
            var (d, c) = (simples.Where(l => l.TemDebito).Sum(l => l.ValorDecimal!.Value), simples.Where(l => l.TemCredito).Sum(l => l.ValorDecimal!.Value));
            if (d != c)
            {
                if (d == 0 || c == 0) await PerguntarContrapartidaAsync(somenteDebito: c == 0, d + c);
                else Erros.Add($"Débitos ({Formato.Moeda(d)}) e créditos ({Formato.Moeda(c)}) não fecham: diferença de {Formato.Moeda(Math.Abs(d - c))}.");
                return;
            }
            propostas.Add(new LancamentoProposto(empresa, datas[0], OrigemLancamento.Manual,
                simples.Select(l => new PartidaProposta(l.TemDebito ? l.Conta.Trim() : l.ContaCredito.Trim(), l.TemDebito ? IndicadorDC.D : IndicadorDC.C,
                    l.ValorDecimal!.Value, l.Historico.Trim(), string.IsNullOrWhiteSpace(l.CentroCusto) ? null : l.CentroCusto.Trim(),
                    PartSimples(l), Doc(l))).ToArray()));
            linhasOrigem.Add(Linhas.IndexOf(simples[0]) + 1);
        }
        if (!await GravarAsync(app, empresa, usuario, propostas, linhasOrigem)) return;
        var ultimaData = preenchidas[^1].Data;
        Limpar();
        Linhas[0].Data = ultimaData;
        DigitacaoAberta = false;                            // fecha o pop-up; a grade se atualiza e recebe o foco
        await CarregarEfetuadosAsync();
    }, Permissao.LancamentoIncluir);

    /// <summary>
    /// Só há débito (ou só crédito): avisa que falta a contrapartida e, se o usuário quiser, cria a linha complementar já
    /// com o valor que fecha. Nada é gravado desbalanceado.
    /// </summary>
    private async Task PerguntarContrapartidaAsync(bool somenteDebito, decimal valor)
    {
        var (tem, falta) = somenteDebito ? ("débito", "crédito") : ("crédito", "débito");
        if (!await Contexto.ConfirmarAsync("Lançamento incompleto",
                $"O lançamento tem apenas {tem} ({Formato.Moeda(valor)}): falta o {falta} complementar. " +
                $"Um lançamento só é salvo com débitos e créditos iguais.\n\nIncluir agora a linha do {falta}?",
                $"Incluir {falta}", "Voltar")) return;
        var nova = AdicionarLinha();
        FocoSolicitado?.Invoke(Linhas.IndexOf(nova), somenteDebito ? "contacredito" : "conta");
    }

    /// <summary>Participante da partida: só nas contas que controlam participantes (plano com participantes).</summary>
    private string? Part(LinhaLancamentoViewModel l, string conta) =>
        l.Participante.Trim() is { Length: > 0 } p && _contasComParticipante.Contains(conta.Trim()) ? p : null;

    /// <summary>Partida simples: a linha é uma conta só; o participante digitado vai na partida.</summary>
    private static string? PartSimples(LinhaLancamentoViewModel l) => l.Participante.Trim() is { Length: > 0 } p ? p : null;

    /// <summary>Nº do documento (NF, recibo, boleto...) — base da conciliação automática.</summary>
    private static string? Doc(LinhaLancamentoViewModel l) => string.IsNullOrWhiteSpace(l.Documento) ? null : l.Documento.Trim();

    /// <summary>Data digitada completada e no formato dd/mm/aaaa (ou como foi digitada, se inválida).</summary>
    public string DataCompleta(string texto) =>
        Formato.LerData(CompletarData(texto)) is { } d ? d.ToString("dd/MM/yyyy") : texto;

    /// <summary>"10/03" ou "10" usam o mês/ano da data padrão.</summary>
    private string CompletarData(string texto)
    {
        var t = texto.Trim();
        var padrao = Data ?? DateTime.Today;
        var partes = t.Split('/');
        return partes.Length switch
        {
            1 when int.TryParse(t, out var d) && d is >= 1 and <= 31 => $"{d:00}/{padrao:MM/yyyy}",
            2 when int.TryParse(partes[0], out var d) && int.TryParse(partes[1], out var m) => $"{d:00}/{m:00}/{padrao:yyyy}",
            _ => t,
        };
    }

    private decimal SomaDebitos() => Linhas.Where(l => l.TemDebito).Sum(l => l.ValorDecimal ?? 0);
    private decimal SomaCreditos() => Linhas.Where(l => l.TemCredito).Sum(l => l.ValorDecimal ?? 0);

    private void Recalcular()
    {
        var d = SomaDebitos();
        var c = SomaCreditos();
        TotalDebito = Formato.Moeda(d);
        TotalCredito = Formato.Moeda(c);
        Diferenca = Formato.Moeda(d - c);
        Balanceado = d == c && d > 0;
        TemDados = Linhas.Any(l => !l.Vazia);
    }

    private void AoMudarLinhas(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (LinhaLancamentoViewModel l in e.NewItems ?? Array.Empty<LinhaLancamentoViewModel>()) l.PropertyChanged += AoMudarLinha;
        foreach (LinhaLancamentoViewModel l in e.OldItems ?? Array.Empty<LinhaLancamentoViewModel>()) l.PropertyChanged -= AoMudarLinha;
        Recalcular();
    }

    /// <summary>Liga reduzido ↔ conta ↔ nome e o código do histórico ao texto fixo.</summary>
    private void AoMudarLinha(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is LinhaLancamentoViewModel l && !_resolvendo)
        {
            _resolvendo = true;
            try
            {
                switch (e.PropertyName)
                {
                    case nameof(LinhaLancamentoViewModel.Reduzido):
                        if (PorReduzido(l.Reduzido) is { } cr) (l.Conta, l.NomeConta) = (cr.Codigo, cr.Descricao);
                        else if (l.Reduzido.Trim().Length == 0) { } else l.NomeConta = "reduzido não encontrado";
                        break;
                    case nameof(LinhaLancamentoViewModel.Conta):
                        (l.Reduzido, l.NomeConta) = _porCodigo.TryGetValue(l.Conta.Trim(), out var cd)
                            ? (cd.Reduzido?.ToString() ?? "", cd.Descricao) : ("", "");
                        break;
                    case nameof(LinhaLancamentoViewModel.ReduzidoCredito):
                        if (PorReduzido(l.ReduzidoCredito) is { } cc) (l.ContaCredito, l.NomeContaCredito) = (cc.Codigo, cc.Descricao);
                        else if (l.ReduzidoCredito.Trim().Length > 0) l.NomeContaCredito = "reduzido não encontrado";
                        break;
                    case nameof(LinhaLancamentoViewModel.ContaCredito):
                        (l.ReduzidoCredito, l.NomeContaCredito) = _porCodigo.TryGetValue(l.ContaCredito.Trim(), out var cc2)
                            ? (cc2.Reduzido?.ToString() ?? "", cc2.Descricao) : ("", "");
                        break;
                    case nameof(LinhaLancamentoViewModel.Participante):
                        l.NomeParticipante = l.Participante.Trim() is { Length: > 0 } cp
                            ? _participantes.TryGetValue(cp, out var pa) ? pa.Nome : "participante não encontrado" : "";
                        break;
                    case nameof(LinhaLancamentoViewModel.CodigoHistorico):
                        if (_historicos.TryGetValue(l.CodigoHistorico.Trim(), out var h)) EscolherHistorico(l, h);
                        break;
                    case nameof(LinhaLancamentoViewModel.Documento) or nameof(LinhaLancamentoViewModel.Valor) or nameof(LinhaLancamentoViewModel.Data):
                        ReaplicarHistorico(l);
                        break;
                }
                if (e.PropertyName is nameof(LinhaLancamentoViewModel.Conta) or nameof(LinhaLancamentoViewModel.ContaCredito)
                    or nameof(LinhaLancamentoViewModel.Reduzido) or nameof(LinhaLancamentoViewModel.ReduzidoCredito)
                    or nameof(LinhaLancamentoViewModel.Participante)) ReaplicarHistorico(l);
            }
            finally { _resolvendo = false; }
        }
        Recalcular();
    }

    private ContaSelecionavel? PorReduzido(string texto) =>
        int.TryParse(texto.Trim(), out var n) && _porReduzido.TryGetValue(n, out var c) ? c : null;

    /// <summary>
    /// Ao sair do campo da conta: aceita o código, o reduzido ou o nome digitado (quando identifica uma única conta)
    /// e troca pelo código; senão avisa que a conta não foi encontrada.
    /// </summary>
    public void ConfirmarConta(LinhaLancamentoViewModel l, bool credito)
    {
        var (codigo, reduzido, nome) = Resolver(credito ? l.ContaCredito : l.Conta);
        _resolvendo = true;
        try
        {
            if (credito) (l.ContaCredito, l.ReduzidoCredito, l.NomeContaCredito) = (codigo, reduzido, nome);
            else (l.Conta, l.Reduzido, l.NomeConta) = (codigo, reduzido, nome);
        }
        finally { _resolvendo = false; }
        ReaplicarHistorico(l);
        Recalcular();
    }

    // ------------------------------------------------------------------ variáveis do histórico padrão

    /// <summary>Dados da linha que preenchem as variáveis do histórico ({DOCUMENTO}, {VALOR}, {CONTA}...).</summary>
    private ContextoHistorico ContextoDe(LinhaLancamentoViewModel l)
    {
        var texto = l.Data.Trim().Length > 0 ? l.Data : (Data ?? DateTime.Today).ToString("dd/MM/yyyy");
        var conta = _porCodigo.TryGetValue(l.Conta.Trim(), out var cd) ? cd.Descricao
                  : _porCodigo.TryGetValue(l.ContaCredito.Trim(), out var cc) ? cc.Descricao : null;
        var part = _participantes.TryGetValue(l.Participante.Trim(), out var p) ? p.Nome : null;
        return new ContextoHistorico(l.Documento, part, Formato.LerData(CompletarData(texto)), conta, l.ValorDecimal);
    }

    /// <summary>Histórico padrão escolhido: substitui as variáveis pelos dados da linha; as que faltam ficam {entre chaves} para completar.</summary>
    private void EscolherHistorico(LinhaLancamentoViewModel l, HistoricoPadrao h)
    {
        if (!VariaveisHistorico.TemVariaveis(h.Descricao))
        {
            (l.ModeloHistorico, l.HistoricoAutomatico) = (null, null);
            l.Historico = h.Descricao.TrimEnd() + " ";
            return;
        }
        l.ModeloHistorico = h.Descricao;
        l.HistoricoAutomatico = null;
        ReaplicarHistorico(l, forcar: true);
    }

    /// <summary>
    /// Refaz o texto do histórico quando os dados da linha mudam (documento, valor, conta...), desde que o usuário
    /// não tenha mexido no texto preenchido pelo sistema.
    /// </summary>
    private void ReaplicarHistorico(LinhaLancamentoViewModel l, bool forcar = false)
    {
        if (l.ModeloHistorico is not { Length: > 0 } modelo) return;
        if (!forcar && l.Historico != l.HistoricoAutomatico) return;
        var texto = VariaveisHistorico.Aplicar(modelo, ContextoDe(l)).Texto;
        l.HistoricoAutomatico = texto;
        if (l.Historico != texto)
        {
            var antes = _resolvendo;
            _resolvendo = true;
            try { l.Historico = texto; }
            finally { _resolvendo = antes; }
        }
    }

    /// <summary>Variáveis conhecidas que ainda estão sem preencher no texto do histórico.</summary>
    private static IEnumerable<string> HistoricoPendente(string historico) =>
        VariaveisHistorico.Usadas(historico).Where(n => VariaveisHistorico.Todas.Any(v => v.Nome == n));

    /// <summary>Código digitado, reduzido ou nome (único ou parte única) → (código, reduzido, nome da conta).</summary>
    private (string Codigo, string Reduzido, string Nome) Resolver(string texto)
    {
        var t = texto.Trim();
        if (t.Length == 0) return ("", "", "");
        if (_porCodigo.TryGetValue(t, out var c)) return (c.Codigo, c.Reduzido?.ToString() ?? "", c.Descricao);
        if (PorReduzido(t) is { } r) return (r.Codigo, r.Reduzido?.ToString() ?? "", r.Descricao);
        var porNome = _todasContas.Where(x => string.Equals(x.Descricao, t, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (porNome.Count != 1) porNome = _todasContas.Where(x => x.Descricao.Contains(t, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (porNome.Count == 1) return (porNome[0].Codigo, porNome[0].Reduzido?.ToString() ?? "", porNome[0].Descricao);
        return (texto, "", _todasContas.Count == 0 ? "" : "conta não encontrada");
    }
}
