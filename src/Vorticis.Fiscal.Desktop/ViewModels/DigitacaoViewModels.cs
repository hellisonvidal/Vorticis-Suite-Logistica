using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vorticis.Desktop.Comum;
using Vorticis.Application.Cadastros;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Fiscal.Documentos;
using Vorticis.Fiscal.Duplicatas;
using Vorticis.Fiscal.Produtos;
using Vorticis.Infra;
using Vorticis.Infra.Financeiro;

namespace Vorticis.Fiscal.Desktop.ViewModels;

/// <summary>Busca de participante e de produto por texto digitado (código, nome/descrição, CPF/CNPJ ou código de barras).</summary>
internal static class Resolvedores
{
    public static async Task<(long Id, string Codigo, string Nome)?> ParticipanteAsync(VorticisApp app, long empresa, long usuario, string texto)
    {
        texto = texto.Trim();
        var lista = await app.Participantes.ListarAsync(empresa, usuario);
        var p = lista.FirstOrDefault(x => x.Codigo == texto) ?? lista.FirstOrDefault(x => string.Equals(x.Nome, texto, StringComparison.CurrentCultureIgnoreCase))
                ?? lista.FirstOrDefault(x => x.CpfCnpj is { Length: > 0 } && x.CpfCnpj == new string(texto.Where(char.IsAsciiDigit).ToArray()));
        return p is null ? null : (p.Id, p.Codigo, p.Nome);
    }

    public static async Task<Produto?> ProdutoAsync(VorticisApp app, long empresa, long usuario, string texto, bool apenasAtivos)
    {
        texto = texto.Trim();
        var r = await app.Fiscal().Produtos.ListarAsync(empresa, apenasAtivos, texto, usuario);
        if (!r.Sucesso) return null;
        return r.Valor.FirstOrDefault(x => x.Codigo.ToString() == texto || x.Gtin == texto || string.Equals(x.Descricao, texto, StringComparison.CurrentCultureIgnoreCase))
               ?? (r.Valor.Count == 1 ? r.Valor[0] : null);
    }
}

/// <summary>Forma de pagamento oferecida na digitação da nota (null = sem forma: parcelas manuais ou à vista pelos Parâmetros).</summary>
public sealed record OpcaoFormaPagamento(FormaPagamento? Forma, string Texto);

/// <summary>Parcela (duplicata) da nota na tela de digitação.</summary>
public sealed record ParcelaLinha(string Numero, DateOnly Vencimento, decimal Valor, string Situacao = "")
{
    public string VencimentoTexto => Vencimento.ToString("dd/MM/yyyy");
    public string ValorTexto => Numeros.Moeda(Valor);
}

public sealed record CentroOpcao(long? Id, string Texto, string? Codigo = null)
{
    public override string ToString() => Texto;
}

public sealed record ItemNotaLinha(ItemFiscal Item, string? ProdutoVinculado = null)
{
    public int N => Item.Numero;
    public string Produto => Item.CodProduto ?? "";
    public string Descricao => Item.Descricao ?? "";
    /// <summary>CFOP do XML/da digitação (o original, que não muda nas notas importadas).</summary>
    public string Cfop => Item.CfopOriginal ?? Item.Cfop;
    /// <summary>CFOP de escrituração (ex.: de entrada), quando diferente do original.</summary>
    public string CfopEscrituracao => Item.CfopOriginal is { } o && o != Item.Cfop ? Item.Cfop : "";
    public string Vinculo => ProdutoVinculado ?? "";
    public string Quantidade => Item.Quantidade.ToString("0.####", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
    public string Unitario => Numeros.Moeda(Item.ValorUnitario);
    public string Total => Numeros.Moeda(Item.ValorContabil);
    public string Icms => Numeros.MoedaOuVazio(Item.Icms);
    public string Ipi => Numeros.MoedaOuVazio(Item.Ipi);
    public string Pis => Numeros.MoedaOuVazio(Item.Pis);
    public string Cofins => Numeros.MoedaOuVazio(Item.Cofins);
    public string Iss => Numeros.MoedaOuVazio(Item.Iss);
    public string CentroCusto => Item.CentroCustoCodigo ?? "";
}

/// <summary>Campos de digitação de um item (texto, como digitado); converte de e para <see cref="ItemFiscal"/>.</summary>
public partial class ItemForm : ObservableObject
{
    [ObservableProperty] private string _produto = "";
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _cfop = "";
    [ObservableProperty] private string _ncm = "";
    [ObservableProperty] private string _cest = "";
    [ObservableProperty] private string _unidade = "UN";
    [ObservableProperty] private string _origem = "0";
    [ObservableProperty] private string _quantidade = "1";
    [ObservableProperty] private string _unitario = "";
    [ObservableProperty] private string _desconto = "";
    [ObservableProperty] private string _frete = "";
    [ObservableProperty] private string _seguro = "";
    [ObservableProperty] private string _outras = "";
    [ObservableProperty] private string _cstIcms = "";
    [ObservableProperty] private string _bcIcms = "";
    [ObservableProperty] private string _aliqIcms = "";
    [ObservableProperty] private string _icms = "";
    [ObservableProperty] private string _bcIcmsSt = "";
    [ObservableProperty] private string _icmsSt = "";
    [ObservableProperty] private string _cstIpi = "";
    [ObservableProperty] private string _bcIpi = "";
    [ObservableProperty] private string _aliqIpi = "";
    [ObservableProperty] private string _ipi = "";
    [ObservableProperty] private string _cstPis = "";
    [ObservableProperty] private string _bcPis = "";
    [ObservableProperty] private string _aliqPis = "";
    [ObservableProperty] private string _pis = "";
    [ObservableProperty] private string _cstCofins = "";
    [ObservableProperty] private string _bcCofins = "";
    [ObservableProperty] private string _aliqCofins = "";
    [ObservableProperty] private string _cofins = "";
    [ObservableProperty] private string _bcIss = "";
    [ObservableProperty] private string _aliqIss = "";
    [ObservableProperty] private string _iss = "";

    public long? ProdutoId { get; set; }
    public string? Gtin { get; set; }

    private static decimal D(string? t) => Numeros.LerValor(t) ?? 0m;
    private static string T(decimal v) => v == 0 ? "" : v.ToString("0.####", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
    private static string? N(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();

    public void Limpar()
    {
        Produto = Descricao = Cfop = Ncm = Cest = CstIcms = BcIcms = AliqIcms = Icms = BcIcmsSt = IcmsSt = CstIpi = BcIpi = AliqIpi = Ipi = "";
        CstPis = BcPis = AliqPis = Pis = CstCofins = BcCofins = AliqCofins = Cofins = BcIss = AliqIss = Iss = Unitario = Desconto = Frete = Seguro = Outras = "";
        (Unidade, Origem, Quantidade, ProdutoId, Gtin) = ("UN", "0", "1", null, null);
    }

    /// <summary>Preenche os campos com os padrões do produto para a operação (CFOP, CST, alíquotas, NCM, unidade).</summary>
    public void AplicarProduto(Produto p, TipoOperacao operacao)
    {
        (ProdutoId, Gtin, Produto, Descricao, Unidade, Ncm, Cest, Origem) = (p.Id, p.Gtin, p.Codigo.ToString(), p.Descricao, p.Unidade, p.Ncm ?? "", p.Cest ?? "", p.Origem);
        Cfop = (operacao == TipoOperacao.Entrada ? p.CfopEntrada : p.CfopSaida) ?? Cfop;
        CstIcms = (operacao == TipoOperacao.Entrada ? p.CstIcmsEntrada : p.CstIcmsSaida) ?? CstIcms;
        (AliqIcms, AliqIpi, AliqPis, AliqCofins) = (T(p.AliqIcms), T(p.AliqIpi), T(p.AliqPis), T(p.AliqCofins));
    }

    public void Carregar(ItemFiscal i)
    {
        (ProdutoId, Gtin) = (i.ProdutoId, i.Gtin);
        (Produto, Descricao, Cfop, Ncm, Cest, Unidade, Origem) = (i.CodProduto ?? "", i.Descricao ?? "", i.Cfop, i.Ncm ?? "", i.Cest ?? "", i.Unidade ?? "", i.Origem ?? "0");
        (Quantidade, Unitario, Desconto, Frete, Seguro, Outras) = (T(i.Quantidade), T(i.ValorUnitario), T(i.Desconto), T(i.Frete), T(i.Seguro), T(i.Outras));
        (CstIcms, BcIcms, AliqIcms, Icms, BcIcmsSt, IcmsSt) = (i.CstIcms ?? "", T(i.BcIcms), T(i.AliqIcms), T(i.Icms), T(i.BcIcmsSt), T(i.IcmsSt));
        (CstIpi, BcIpi, AliqIpi, Ipi) = (i.CstIpi ?? "", T(i.BcIpi), T(i.AliqIpi), T(i.Ipi));
        (CstPis, BcPis, AliqPis, Pis) = (i.CstPis ?? "", T(i.BcPis), T(i.AliqPis), T(i.Pis));
        (CstCofins, BcCofins, AliqCofins, Cofins) = (i.CstCofins ?? "", T(i.BcCofins), T(i.AliqCofins), T(i.Cofins));
        (BcIss, AliqIss, Iss) = (T(i.BcIss), T(i.AliqIss), T(i.Iss));
    }

    /// <summary>Base padrão dos tributos: produtos - desconto + frete + seguro + outras despesas.</summary>
    public decimal BaseItem() => Dinheiro(D(Quantidade) * D(Unitario)) - D(Desconto) + D(Frete) + D(Seguro) + D(Outras);

    private static decimal Dinheiro(decimal v) => Vorticis.Domain.Comum.Dinheiro.Arredondar(v);

    /// <summary>Calcula base (se vazia) e valor de cada tributo com alíquota informada e valor vazio: valor = base x alíquota / 100.</summary>
    public void CalcularTributos()
    {
        var @base = BaseItem();
        void C(ref string bc, string aliq, ref string valor)
        {
            if (D(aliq) <= 0 || D(valor) != 0) return;
            if (D(bc) == 0) bc = T(@base);
            valor = T(Dinheiro(D(bc) * D(aliq) / 100m));
        }
        var (bcIcms, icms) = (BcIcms, Icms); C(ref bcIcms, AliqIcms, ref icms); (BcIcms, Icms) = (bcIcms, icms);
        var (bcIpi, ipi) = (BcIpi, Ipi); C(ref bcIpi, AliqIpi, ref ipi); (BcIpi, Ipi) = (bcIpi, ipi);
        var (bcPis, pis) = (BcPis, Pis); C(ref bcPis, AliqPis, ref pis); (BcPis, Pis) = (bcPis, pis);
        var (bcCof, cof) = (BcCofins, Cofins); C(ref bcCof, AliqCofins, ref cof); (BcCofins, Cofins) = (bcCof, cof);
        var (bcIss, iss) = (BcIss, Iss); C(ref bcIss, AliqIss, ref iss); (BcIss, Iss) = (bcIss, iss);
    }

    public ItemFiscal Montar(long? centroId, string? centroCodigo) => new(
        0, N(Produto), N(Descricao), N(Ncm), N(Cest), Cfop.Trim(), N(Unidade), D(Quantidade), D(Unitario), 0, D(Desconto), D(Frete), D(Seguro), D(Outras), 0,
        N(CstIcms), N(Origem), D(BcIcms), D(AliqIcms), D(Icms), D(BcIcmsSt), D(IcmsSt), N(CstIpi), D(BcIpi), D(AliqIpi), D(Ipi),
        N(CstPis), D(BcPis), D(AliqPis), D(Pis), N(CstCofins), D(BcCofins), D(AliqCofins), D(Cofins), D(BcIss), D(AliqIss), D(Iss),
        Gtin, ProdutoId, centroId, centroCodigo);
}

/// <summary>
/// Lançamentos › Documentos fiscais › Entrada / Saída: digitação manual de nota fiscal (cabeçalho e itens), gravada nas mesmas tabelas da
/// importação de XML. Notas contabilizadas ficam somente para consulta.
/// </summary>
public abstract partial class DigitacaoDocumentoViewModel(IContexto contexto) : ModuloViewModel(contexto)
{
    public static IReadOnlyList<string> Modelos { get; } = ["NF-e (55)", "NFC-e (65)", "NFS-e"];
    public static IReadOnlyList<ModeloDocumento> ModelosValores { get; } = [ModeloDocumento.NFe55, ModeloDocumento.NFCe65, ModeloDocumento.NFSe];

    public abstract TipoOperacao Operacao { get; }
    public string Titulo => $"Nota fiscal de {Operacao.Rotulo().ToLowerInvariant()}";
    public string RotuloParticipante => Operacao == TipoOperacao.Entrada ? "Fornecedor" : "Cliente";
    public string RotuloData => Operacao == TipoOperacao.Entrada ? "Data de entrada" : "Data de saída";

    public ObservableCollection<DocumentoLinha> Lista { get; } = [];
    public ObservableCollection<ItemNotaLinha> Itens { get; } = [];
    public ObservableCollection<ParcelaLinha> Parcelas { get; } = [];
    public ObservableCollection<CentroOpcao> Centros { get; } = [new CentroOpcao(null, "(sem centro de custo)")];
    public ItemForm Item { get; } = new();

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ExcluirCommand))] [NotifyCanExecuteChangedFor(nameof(EditarCommand))] private DocumentoLinha? _selecionada;
    [ObservableProperty] private DateTime? _filtroInicio = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _filtroFim = DateTime.Today;
    [ObservableProperty] private int _modelo;
    [ObservableProperty] private string _serie = "1";
    [ObservableProperty] private string _numero = "";
    [ObservableProperty] private string _chave = "";
    [ObservableProperty] private DateTime? _emissao = DateTime.Today;
    [ObservableProperty] private DateTime? _dataLivro = DateTime.Today;
    [ObservableProperty] private string _natureza = "";
    [ObservableProperty] private string _participante = "";
    [ObservableProperty] private string _totalInformado = "";
    [ObservableProperty] private CentroOpcao? _centroDocumento;
    [ObservableProperty] private CentroOpcao? _centroItem;
    [ObservableProperty] private ItemNotaLinha? _itemSelecionado;
    [ObservableProperty] private string _totais = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PodeEditar))] private bool _contabilizado;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PodeEditar))] private bool _usaCentroCusto;
    [ObservableProperty] private string _situacaoDocumento = "Nova nota";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PodeEditar))] [NotifyPropertyChangedFor(nameof(PodeEditarEscrituracao))] private bool _importada;
    [ObservableProperty] private string _observacoes = "";
    [ObservableProperty] private string _contaDebito = "";
    [ObservableProperty] private string _contaCredito = "";
    [ObservableProperty] private string _escProduto = "";
    [ObservableProperty] private string _parcelasQuantidade = "1";
    [ObservableProperty] private DateTime? _parcelasPrimeiroVencimento = DateTime.Today.AddDays(30);
    [ObservableProperty] private string _parcelasIntervalo = "30";

    /// <summary>Formas de pagamento ativas (Cadastros › Formas de pagamento): escolher uma gera as duplicatas pelos prazos dela.</summary>
    public ObservableCollection<OpcaoFormaPagamento> FormasPagamento { get; } = [new(null, "(sem forma de pagamento)")];
    [ObservableProperty] private OpcaoFormaPagamento? _formaPagamento;
    private bool _suprimirForma;

    partial void OnFormaPagamentoChanged(OpcaoFormaPagamento? value)
    {
        if (_suprimirForma || value?.Forma is not { } f || !PodeEditar) return;
        AplicarForma(f);
    }

    /// <summary>Gera as parcelas pela forma de pagamento: total da nota dividido nos prazos, contados da emissão.</summary>
    private void AplicarForma(FormaPagamento f)
    {
        Erros.Clear();
        var total = CalcularTotais().Total;
        if (total <= 0) { Mensagem = $"Forma \"{f.Descricao}\" escolhida: as parcelas serão geradas ao incluir os itens (use Gerar pela forma)."; return; }
        Parcelas.Clear();
        foreach (var p in f.GerarParcelas(total, Data(Emissao, DateOnly.FromDateTime(DateTime.Today)))) Parcelas.Add(new ParcelaLinha(p.Numero, p.Vencimento, p.Valor));
        Mensagem = $"{Parcelas.Count} parcela(s) gerada(s) pela forma \"{f.Descricao}\" ({f.CondicaoTexto}).";
    }

    [RelayCommand]
    private void GerarPelaForma()
    {
        if (FormaPagamento?.Forma is { } f) AplicarForma(f);
        else Erros.Add("Escolha a forma de pagamento.");
    }

    private void SelecionarForma(long? id)
    {
        _suprimirForma = true;
        FormaPagamento = FormasPagamento.FirstOrDefault(o => o.Forma?.Id == id && id is not null) ?? FormasPagamento[0];
        _suprimirForma = false;
    }

    // Fornecedor/cliente: busca automática pelo CPF/CNPJ digitado (cadastrados da empresa e, para CNPJ, a base pública).
    [ObservableProperty] private string _participanteInfo = "";
    [ObservableProperty] private bool _temNovoParticipante;
    [ObservableProperty] private string _novoNome = "";
    [ObservableProperty] private string _novoUf = "";
    [ObservableProperty] private string _novoMunicipio = "";
    [ObservableProperty] private string _novoIe = "";

    private long _documentoId;
    private long _participanteId;
    private Participante? _novoParticipante;
    private bool _suprimirBusca;
    private int _buscaGeracao;
    private IReadOnlyDictionary<long, string> _produtosPorId = new Dictionary<long, string>();
    private ImportacaoXmlViewModel? _importador;

    /// <summary>Importar XML na própria tela de lançamentos (arquivos, pasta ou .zip); as notas importadas aparecem na lista.</summary>
    public ImportacaoXmlViewModel Importador => _importador ??= CriarImportador();

    private ImportacaoXmlViewModel CriarImportador()
    {
        var imp = new ImportacaoXmlViewModel(Contexto);
        imp.Concluida += AposImportacaoAsync;
        return imp;
    }
    private TotaisDocumento _retencoes = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0);

    public bool PodeManter => Contexto.Pode(Permissao.FiscalDocumentosImportar);
    /// <summary>Dados da nota (cabeçalho, itens, valores): só nas notas digitadas e não contabilizadas. Nas importadas, o XML original é imutável.</summary>
    public bool PodeEditar => PodeManter && !Contabilizado && !Importada;
    /// <summary>Escrituração (CFOP de entrada, centro de custo, produto vinculado, contas, observações): também nas importadas, se não contabilizadas.</summary>
    public bool PodeEditarEscrituracao => PodeManter && !Contabilizado;
    public bool PodeEditarDigitada => PodeEditar;

    /// <summary>Por que os campos estão bloqueados (aviso no topo); vazio quando a nota pode ser digitada.</summary>
    public string MotivoSomenteLeitura => !PodeManter
        ? "Seu perfil nesta empresa só permite consultar notas. Para digitar ou importar, o administrador precisa liberar a permissão " +
          "\"Importar XML e digitar notas fiscais\" no seu perfil (Configurações › Usuários › Perfis)."
        : Contabilizado ? "Esta nota está contabilizada: para alterar, desfaça a contabilização. Para lançar outra nota, clique em \"Nova nota\"."
        : "";
    public bool MostraMotivoSomenteLeitura => MotivoSomenteLeitura.Length > 0 && !Importada;

    // Importação individual pela chave de acesso (SEFAZ, Ambiente Nacional, com o certificado da empresa)
    [ObservableProperty] private string _chaveImportacao = "";
    [ObservableProperty] private bool _podeManifestar;
    public bool MostraGravarNota => !Importada;
    public string AvisoImportada => "Nota importada de XML: os dados do XML original (valores, chave de acesso, participante e itens) não podem ser alterados. " +
                                    "Você pode editar a escrituração: CFOP de entrada, centro de custo, produto vinculado, contas e observações.";
    public bool MostraCentro => UsaCentroCusto;

    partial void OnUsaCentroCustoChanged(bool value) => OnPropertyChanged(nameof(MostraCentro));

    private void AvisarSomenteLeitura()
    {
        OnPropertyChanged(nameof(MotivoSomenteLeitura));
        OnPropertyChanged(nameof(MostraMotivoSomenteLeitura));
    }

    public void AtualizarPermissoes()
    {
        AvisarSomenteLeitura();
        OnPropertyChanged(nameof(PodeManter));
        OnPropertyChanged(nameof(PodeEditar));
        OnPropertyChanged(nameof(PodeEditarEscrituracao));
        OnPropertyChanged(nameof(PodeEditarDigitada));
        Importador.AtualizarPermissoes();
    }

    partial void OnImportadaChanged(bool value) { OnPropertyChanged(nameof(MostraGravarNota)); AvisarSomenteLeitura(); }

    partial void OnContabilizadoChanged(bool value) { OnPropertyChanged(nameof(PodeEditarEscrituracao)); AvisarSomenteLeitura(); }

    partial void OnSelecionadaChanged(DocumentoLinha? value)
    {
        if (value is not null) _ = CarregarDocumentoAsync(value.Id);
        EditarCommand.NotifyCanExecuteChanged();
    }

    partial void OnParticipanteChanged(string value)
    {
        if (_suprimirBusca) return;
        _participanteId = 0;
        (_novoParticipante, TemNovoParticipante, ParticipanteInfo) = (null, false, "");
        // CNPJ completo (14 dígitos): busca sozinha ao digitar. CPF e demais textos: ao sair do campo ou em "Buscar".
        if (ServicoParticipanteNota.DocumentoDigitado(value) is { Length: 14 }) _ = BuscarParticipanteAsync();
    }

    partial void OnItemSelecionadoChanged(ItemNotaLinha? value)
    {
        if (value is null) return;
        Item.Carregar(value.Item);
        CentroItem = Centros.FirstOrDefault(c => c.Id == value.Item.CentroCustoId) ?? Centros[0];
        EscProduto = value.ProdutoVinculado?.Split(" - ")[0] ?? "";
    }

    /// <summary>Carga ao abrir: configuração (centros de custo), centros e notas digitadas do período.</summary>
    public Task CarregarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var cfg = await app.Fiscal().Digitacao.ObterConfiguracaoAsync(empresa, usuario);
        UsaCentroCusto = cfg.Sucesso && cfg.Valor.CentroCustoAtivo;
        Centros.Clear();
        Centros.Add(new CentroOpcao(null, "(sem centro de custo)"));
        if (UsaCentroCusto)
            foreach (var c in (await app.PlanoContas.ListarCentrosAsync(empresa, usuario)).Where(c => c.Ativo && c.Analitico))
                Centros.Add(new CentroOpcao(c.Id, $"{c.Codigo} - {c.Descricao}", c.Codigo));
        CentroDocumento = CentroItem = Centros[0];
        var formas = await app.CadastrosFinanceiros().FormasAsync(empresa, usuario);
        var formaAtual = FormaPagamento?.Forma?.Id;
        _suprimirForma = true;
        FormasPagamento.Clear();
        FormasPagamento.Add(new OpcaoFormaPagamento(null, "(sem forma de pagamento)"));
        foreach (var f in formas.Where(f => f.Ativa)) FormasPagamento.Add(new OpcaoFormaPagamento(f, $"{f.Codigo} – {f.Descricao} ({f.CondicaoTexto})"));
        _suprimirForma = false;
        SelecionarForma(formaAtual);
        var produtos = await app.Fiscal().Produtos.ListarAsync(empresa, false, null, usuario);
        _produtosPorId = produtos.Sucesso ? produtos.Valor.ToDictionary(p => p.Id, p => $"{p.Codigo} - {p.Descricao}") : new Dictionary<long, string>();
        await ListarAsync(app, empresa, usuario);
    });

    private async Task ListarAsync(VorticisApp app, long empresa, long usuario)
    {
        var f = new FiltroDocumentos(Data(FiltroInicio, new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1)), Data(FiltroFim, DateOnly.FromDateTime(DateTime.Today)), Operacao);
        var r = await app.Fiscal().Documentos.ConsultarAsync(empresa, f, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Lista.Clear();
        foreach (var d in r.Valor) Lista.Add(new DocumentoLinha(d));
    }

    [RelayCommand]
    private Task ConsultarAsync() => ExecutarAsync(ListarAsync, Permissao.FiscalDocumentosConsultar);

    // Selecionar uma nota e clicar em Editar disparam duas cargas quase juntas; em fila, a última a começar é a última a terminar e as mensagens
    // que o comando coloca depois dela não são apagadas por uma carga atrasada.
    private readonly SemaphoreSlim _travaCarga = new(1, 1);

    private async Task CarregarDocumentoAsync(long id)
    {
        await _travaCarga.WaitAsync();
        try { await CarregarDocumentoInternoAsync(id); }
        finally { _travaCarga.Release(); }
    }

    private async Task CarregarDocumentoInternoAsync(long id)
    {
        if (Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var r = await app.Fiscal().Digitacao.ObterAsync(empresa, id, sessao.Id);
        // a seleção mudou durante a consulta (ex.: "Novo" logo depois de gravar): não sobrescreve a tela
        if (Selecionada?.Id != id) return;
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var d = r.Valor;
        _documentoId = d.Id;
        _participanteId = d.ParticipanteId;
        _retencoes = d.Totais;
        (Modelo, Serie, Numero, Chave, Emissao, DataLivro, Natureza) =
            (Math.Max(0, ModelosValores.ToList().IndexOf(d.Modelo)), d.Serie, d.Numero, d.Chave ?? "", d.Emissao.ToDateTime(TimeOnly.MinValue), d.DataLivro.ToDateTime(TimeOnly.MinValue), d.Natureza ?? "");
        var lista = await app.Participantes.ListarAsync(empresa, sessao.Id);
        if (Selecionada?.Id != id) return;
        _suprimirBusca = true;
        Participante = lista.FirstOrDefault(p => p.Id == d.ParticipanteId)?.Nome ?? "";
        _suprimirBusca = false;
        (_novoParticipante, TemNovoParticipante, ParticipanteInfo) = (null, false, "");
        (Observacoes, ContaDebito, ContaCredito, EscProduto) = (d.Observacoes ?? "", d.ContaDebito ?? "", d.ContaCredito ?? "", "");
        Importada = d.Importado;
        CentroDocumento = Centros.FirstOrDefault(c => c.Id == d.CentroCustoId) ?? Centros[0];
        Itens.Clear();
        foreach (var i in d.Itens) Itens.Add(Linha(i));
        TotalInformado = "";
        var dups = await app.Fiscal().Duplicatas.ConsultarAsync(empresa, new FiltroDuplicatas(DocumentoId: id), sessao.Id);
        if (Selecionada?.Id != id) return;
        Parcelas.Clear();
        if (dups.Sucesso)
            foreach (var p in dups.Valor.Where(p => p.Situacao != SituacaoDuplicata.Cancelada)) Parcelas.Add(new ParcelaLinha(p.Parcela, p.Vencimento, p.Valor, p.Situacao.Rotulo()));
        SelecionarForma(d.FormaPagamentoId);
        Contabilizado = d.Contabilizado;
        SituacaoDocumento = d.Contabilizado ? "Nota contabilizada: somente consulta (use \"Desfazer contabilização e excluir\" para removê-la, ou desfaça a contabilização para alterar)"
            : d.Importado ? $"Nota importada de XML {d.Numero}: só a escrituração pode ser alterada" : $"Alterando a nota {d.Numero}";
        AtualizarTotais();
        Erros.Clear();
        Mensagem = null;
    }

    /// <summary>Gera N parcelas iguais a partir do total da nota, com o intervalo em dias informado (a última absorve a diferença do arredondamento).</summary>
    [RelayCommand]
    private void GerarParcelas()
    {
        Erros.Clear();
        Mensagem = null;
        if (!int.TryParse(ParcelasQuantidade.Trim(), out var n) || n is < 1 or > 120) { Erros.Add("Informe a quantidade de parcelas (de 1 a 120)."); return; }
        if (!int.TryParse(ParcelasIntervalo.Trim(), out var dias) || dias is < 0 or > 365) { Erros.Add("Informe o intervalo entre as parcelas em dias (de 0 a 365)."); return; }
        var total = CalcularTotais().Total;
        if (total <= 0) { Erros.Add("Inclua os itens da nota antes de gerar as parcelas."); return; }
        var primeiro = Data(ParcelasPrimeiroVencimento, Data(Emissao, DateOnly.FromDateTime(DateTime.Today)));
        Parcelas.Clear();
        foreach (var p in GeradorParcelas.Gerar(total, n, primeiro, dias)) Parcelas.Add(new ParcelaLinha(p.Numero, p.Vencimento, p.Valor));
    }

    [RelayCommand]
    private void LimparParcelas() => Parcelas.Clear();

    private ItemNotaLinha Linha(ItemFiscal i) => new(i, i.ProdutoId is { } pid && _produtosPorId.TryGetValue(pid, out var nome) ? nome : null);

    [RelayCommand]
    private void Novo()
    {
        Selecionada = null;
        (_documentoId, _participanteId) = (0, 0);
        _suprimirBusca = true;
        _retencoes = _retencoes with { IssRetido = false, RetPis = 0, RetCofins = 0, RetCsll = 0, Irrf = 0, RetPrev = 0 };
        (Modelo, Serie, Numero, Chave, Emissao, DataLivro, Natureza, Participante, TotalInformado) = (0, "1", "", "", DateTime.Today, DateTime.Today, "", "", "");
        _suprimirBusca = false;
        (_novoParticipante, TemNovoParticipante, ParticipanteInfo) = (null, false, "");
        (Observacoes, ContaDebito, ContaCredito, EscProduto, Importada) = ("", "", "", "", false);
        CentroDocumento = CentroItem = Centros[0];
        Itens.Clear();
        Item.Limpar();
        Parcelas.Clear();
        (ParcelasQuantidade, ParcelasPrimeiroVencimento, ParcelasIntervalo) = ("1", DateTime.Today.AddDays(30), "30");
        SelecionarForma(null);
        Contabilizado = false;
        SituacaoDocumento = "Nova nota";
        AtualizarTotais();
        Erros.Clear();
        Mensagem = null;
    }

    private TotaisDocumento CalcularTotais() => CalculadoraNota.Totalizar(Itens.Select(i => i.Item), _retencoes);

    private void AtualizarTotais()
    {
        OnPropertyChanged(nameof(TemItensImobilizado));
        var t = CalcularTotais();
        Totais = $"Produtos {Numeros.Moeda(t.Produtos)} · Desconto {Numeros.Moeda(t.Desconto)} · Frete {Numeros.Moeda(t.Frete)} · IPI {Numeros.Moeda(t.Ipi)} · ICMS {Numeros.Moeda(t.Icms)} · " +
                 $"ICMS-ST {Numeros.Moeda(t.IcmsSt)} · PIS {Numeros.Moeda(t.Pis)} · COFINS {Numeros.Moeda(t.Cofins)} · ISS {Numeros.Moeda(t.Iss)} · TOTAL DA NOTA {Numeros.Moeda(t.Total)}";
    }

    // ------------------------------------------------------------------ itens

    [RelayCommand]
    private Task BuscarProdutoAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (string.IsNullOrWhiteSpace(Item.Produto)) { Erros.Add("Informe o código, a descrição ou o código de barras do produto."); return; }
        var p = await Resolvedores.ProdutoAsync(app, empresa, usuario, Item.Produto, true);
        if (p is null) { Erros.Add($"Produto \"{Item.Produto.Trim()}\" não encontrado ou ambíguo (cadastre em Cadastros › Produtos)."); return; }
        Item.AplicarProduto(p, Operacao);
    });

    [RelayCommand]
    private void CalcularTributos() => Item.CalcularTributos();

    private ItemFiscal? MontarItem()
    {
        var item = Item.Montar(CentroItem?.Id, CentroItem?.Codigo);
        return item;
    }

    [RelayCommand]
    private async Task AdicionarItemAsync() => await ConfirmarItemAsync(substituir: false);

    [RelayCommand]
    private async Task AtualizarItemAsync() => await ConfirmarItemAsync(substituir: true);

    private Task ConfirmarItemAsync(bool substituir) => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (substituir && ItemSelecionado is null) { Erros.Add("Selecione o item a atualizar."); return; }
        if (Item.ProdutoId is null && !string.IsNullOrWhiteSpace(Item.Produto))
        {
            var p = await Resolvedores.ProdutoAsync(app, empresa, usuario, Item.Produto, true);
            if (p is null) { Erros.Add($"Produto \"{Item.Produto.Trim()}\" não encontrado ou ambíguo."); return; }
            var antes = (Item.Descricao, Item.Cfop, Item.Unidade);
            Item.AplicarProduto(p, Operacao);
            if (!string.IsNullOrWhiteSpace(antes.Descricao)) Item.Descricao = antes.Descricao;
            if (!string.IsNullOrWhiteSpace(antes.Cfop)) Item.Cfop = antes.Cfop;
        }
        var novo = CalculadoraNota.Completar(MontarItem()!);
        if (novo.Quantidade <= 0 || string.IsNullOrWhiteSpace(novo.Descricao) || string.IsNullOrWhiteSpace(novo.Cfop))
        {
            Erros.Add("Informe produto/descrição, CFOP e quantidade do item.");
            return;
        }
        if (substituir)
        {
            var pos = Itens.IndexOf(ItemSelecionado!);
            Itens[pos] = new ItemNotaLinha(novo with { Numero = pos + 1 });
        }
        else Itens.Add(new ItemNotaLinha(novo with { Numero = Itens.Count + 1 }));
        Item.Limpar();
        ItemSelecionado = null;
        AtualizarTotais();
    });

    [RelayCommand]
    private void RemoverItem()
    {
        if (ItemSelecionado is not { } s) { Erros.Add("Selecione o item a remover."); return; }
        Itens.Remove(s);
        for (var i = 0; i < Itens.Count; i++) Itens[i] = new ItemNotaLinha(Itens[i].Item with { Numero = i + 1 });
        Item.Limpar();
        AtualizarTotais();
    }

    // ------------------------------------------------------------------ gravação

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Importada) { Erros.Add("Nota importada de XML: use \"Gravar escrituração\" (os dados do XML não podem ser alterados)."); return; }
        if (string.IsNullOrWhiteSpace(Participante)) { Erros.Add($"Informe o {RotuloParticipante.ToLowerInvariant()}."); return; }
        var part = await ResolverParticipanteAsync(app, empresa, usuario);
        if (part is null) return;
        _participanteId = part.Value.Id;
        var notaNova = _documentoId == 0;
        var totais = CalcularTotais();
        if (Numeros.LerValor(TotalInformado) is { } informado) totais = totais with { Total = informado };   // total conferido com a soma dos itens
        var doc = new DocumentoDigitado(_documentoId, Operacao, ModelosValores[Math.Clamp(Modelo, 0, 2)], Serie, Numero, Chave, Data(Emissao, default), Data(DataLivro, default),
            Natureza, _participanteId, CentroDocumento?.Id, totais, Itens.Select(i => i.Item).ToList(),
            Observacoes: Texto(Observacoes), ContaDebito: Texto(ContaDebito), ContaCredito: Texto(ContaCredito),
            Parcelas: Parcelas.Select(p => new ParcelaDuplicata(p.Numero, p.Vencimento, p.Valor)).ToList(), FormaPagamentoId: FormaPagamento?.Forma?.Id);   // vazio: nota nova gera à vista (Parâmetros); nota existente fica sem duplicatas
        var r = await app.Fiscal().Digitacao.GravarAsync(empresa, doc, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        AmpliarPeriodo([Data(DataLivro, default).ToDateTime(TimeOnly.MinValue)]);   // a nota gravada sempre aparece na lista
        await ListarAsync(app, empresa, usuario);
        _documentoId = r.Valor;
        // contabilização on-line (Cadastros › Parâmetros): a nota já sai contabilizada, se houver regra para ela
        var dia = Data(DataLivro, default);
        var online = await app.Fiscal().Contabilizacao.ContabilizarSeOnlineAsync(empresa, dia, dia, new HashSet<long> { r.Valor }, usuario);
        if (online is { Sucesso: true, Valor.Contabilizados: > 0 }) await ListarAsync(app, empresa, usuario);
        Selecionada = Lista.FirstOrDefault(d => d.Id == r.Valor);
        Mensagem = "Nota gravada." + ImportacaoXmlViewModel.TextoContabilizacaoOnline(online);
        if (notaNova && CfopImobilizado.Itens(Operacao, doc.Itens).Count > 0
            && await Contexto.ConfirmarAsync("Ativo imobilizado",
                   "A nota tem itens com CFOP de ativo imobilizado. Cadastrar agora os bens no imobilizado do Contábil (já preenchidos com a nota, o fornecedor, a data e o valor)?",
                   "Cadastrar os bens", "Agora não"))
            AbrirBensDoImobilizado(doc, part.Value.Codigo, part.Value.Nome);
    }, Permissao.FiscalDocumentosImportar);

    /// <summary>Há itens de imobilizado na nota de entrada em edição (habilita "Cadastrar bens do imobilizado").</summary>
    public bool TemItensImobilizado => CfopImobilizado.Itens(Operacao, Itens.Select(i => i.Item)).Count > 0;

    private void AbrirBensDoImobilizado(DocumentoDigitado doc, string? codigoFornecedor, string fornecedor)
    {
        if (Contexto is not Vorticis.Desktop.Comum.ViewModels.MainViewModelBase m || !m.ConheceTela(Telas.BensDaNota)) return;
        PendenciaImobilizado.Entregar(Contexto, new PendenciaImobilizado($"{doc.Numero}", codigoFornecedor, fornecedor, doc.Emissao, doc.DataLivro,
            CfopImobilizado.Itens(doc.Operacao, doc.Itens)));
        Contexto.AbrirTela(Telas.BensDaNota);
    }

    [RelayCommand]
    private Task CadastrarBensImobilizadoAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var itens = CfopImobilizado.Itens(Operacao, Itens.Select(i => i.Item));
        if (itens.Count == 0) { Erros.Add("A nota não tem itens com CFOP de ativo imobilizado (1.551, 2.551, 3.551, 1.406 ou 2.406)."); return; }
        var doc = new DocumentoDigitado(_documentoId, Operacao, ModelosValores[Math.Clamp(Modelo, 0, 2)], Serie, Numero, Chave, Data(Emissao, default), Data(DataLivro, default),
            Natureza, _participanteId, null, CalcularTotais(), itens);
        var fornecedor = _participanteId == 0 ? null : (await app.Participantes.ListarAsync(empresa, usuario)).FirstOrDefault(p => p.Id == _participanteId);
        AbrirBensDoImobilizado(doc, fornecedor?.Codigo, fornecedor?.Nome ?? Participante);
    });

    private static string? Texto(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();

    /// <summary>
    /// Participante da nota: o já cadastrado (código, nome ou CPF/CNPJ) ou, se o CPF/CNPJ digitado não existe, o cadastro novo — com os dados
    /// da base pública do CNPJ já conferidos na tela — criado aqui, depois da confirmação do usuário. Null = não resolveu (erro já exibido).
    /// </summary>
    private async Task<(long Id, string Codigo, string Nome)?> ResolverParticipanteAsync(VorticisApp app, long empresa, long usuario)
    {
        var part = await Resolvedores.ParticipanteAsync(app, empresa, usuario, Participante);
        if (part is not null) return part;
        var servico = app.Fiscal().ParticipantesNota;
        var doc = ServicoParticipanteNota.DocumentoDigitado(Participante);
        if (doc is null)
        {
            Erros.Add($"Participante \"{Participante.Trim()}\" não encontrado (informe o código, o nome ou o CPF/CNPJ).");
            return null;
        }
        Participante? novo = _novoParticipante is { } pendente && pendente.CpfCnpj == doc ? pendente : null;
        if (novo is null)
        {
            var busca = await servico.BuscarAsync(empresa, Participante, Operacao, usuario);
            if (!busca.Sucesso) { MostrarResultado(busca, ""); return null; }
            if (busca.Valor.Cadastrado is { } c) return (c.Id, c.Codigo, c.Nome);
            if (busca.Valor.Novo is null) { Erros.Add("Participante não encontrado."); return null; }
            AplicarNovoParticipante(busca.Valor);
            novo = busca.Valor.Novo;
        }
        novo = novo with { Nome = NovoNome.Trim(), Uf = Texto(NovoUf)?.ToUpperInvariant(), Municipio = Texto(NovoMunicipio), InscricaoEstadual = Texto(NovoIe) };
        if (string.IsNullOrWhiteSpace(novo.Nome))
        {
            TemNovoParticipante = true;
            Erros.Add($"Informe o nome / razão social do novo {RotuloParticipante.ToLowerInvariant()}.");
            return null;
        }
        var rotulo = RotuloParticipante.ToLowerInvariant();
        if (!await Contexto.ConfirmarAsync($"Cadastrar {rotulo}",
                $"O {(doc.Length == 14 ? "CNPJ" : "CPF")} {(doc.Length == 14 ? Vorticis.Domain.Cadastros.Cnpj.Formatar(doc) : Vorticis.Domain.Cadastros.Cpf.Formatar(doc))} não está cadastrado.\n" +
                $"Cadastrar \"{novo.Nome}\" como {rotulo} e gravar a nota?", "Cadastrar e gravar", "Cancelar"))
            return null;
        var cad = await servico.CadastrarAsync(empresa, novo, usuario);
        if (!cad.Sucesso) { MostrarResultado(cad, ""); return null; }
        (_novoParticipante, TemNovoParticipante) = (null, false);
        ParticipanteInfo = $"{RotuloParticipante} cadastrado: {cad.Valor.Codigo} - {cad.Valor.Nome}";
        return (cad.Valor.Id, cad.Valor.Codigo, cad.Valor.Nome);
    }

    /// <summary>Busca o participante pelo CPF/CNPJ digitado: nos cadastrados da empresa e, se não existir, na base pública do CNPJ.</summary>
    [RelayCommand]
    private async Task BuscarParticipanteAsync()
    {
        if (!PodeEditar || Contexto.App is not { } app || Contexto.EmpresaId is not { } empresa || Contexto.Sessao is not { } sessao) return;
        var texto = Participante;
        if (ServicoParticipanteNota.DocumentoDigitado(texto) is not { } doc) return;
        if (_participanteId != 0 || _novoParticipante?.CpfCnpj == doc) return;   // já resolvido: não refaz a consulta nem apaga a edição
        var geracao = ++_buscaGeracao;
        try
        {
            ParticipanteInfo = "Buscando...";
            var r = await app.Fiscal().ParticipantesNota.BuscarAsync(empresa, texto, Operacao, sessao.Id);
            if (geracao != _buscaGeracao || texto != Participante) return;   // o usuário continuou digitando
            Erros.Clear();
            if (!r.Sucesso) { ParticipanteInfo = ""; foreach (var e in r.Erros) Erros.Add(e); return; }
            if (r.Valor.Cadastrado is { } c)
            {
                (_participanteId, _novoParticipante, TemNovoParticipante) = (c.Id, null, false);
                ParticipanteInfo = $"{RotuloParticipante} cadastrado: {c.Codigo} - {c.Nome}" + (string.IsNullOrWhiteSpace(c.Uf) ? "" : $" ({c.Municipio} / {c.Uf})");
            }
            else if (r.Valor.Novo is not null) AplicarNovoParticipante(r.Valor);
            else ParticipanteInfo = "";
        }
        catch (Exception ex) { ParticipanteInfo = ""; Erros.Add(ex.Message); }
    }

    private void AplicarNovoParticipante(ParticipanteResolvido r)
    {
        var n = r.Novo!;
        _novoParticipante = n;
        (NovoNome, NovoUf, NovoMunicipio, NovoIe) = (n.Nome, n.Uf ?? "", n.Municipio ?? "", n.InscricaoEstadual ?? "");
        TemNovoParticipante = true;
        ParticipanteInfo = $"{RotuloParticipante} não cadastrado: será cadastrado ao gravar a nota (confirmação).".Trim() +
                           (r.Fonte is null ? "" : $" Dados da base pública ({r.Fonte}).") + (string.IsNullOrWhiteSpace(r.Aviso) ? "" : " " + r.Aviso);
    }

    // ------------------------------------------------------------------ escrituração (notas importadas e digitadas)

    /// <summary>Aplica ao item selecionado o CFOP de escrituração, o produto vinculado e o centro de custo informados (os dados do XML não mudam).</summary>
    [RelayCommand]
    private Task AplicarEscrituracaoItemAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (ItemSelecionado is not { } sel) { Erros.Add("Selecione o item da nota."); return; }
        long? produtoId = sel.Item.ProdutoId;
        string? produtoNome = sel.ProdutoVinculado;
        if (string.IsNullOrWhiteSpace(EscProduto)) (produtoId, produtoNome) = (null, null);
        else
        {
            var p = await Resolvedores.ProdutoAsync(app, empresa, usuario, EscProduto, true);
            if (p is null) { Erros.Add($"Produto \"{EscProduto.Trim()}\" não encontrado ou ambíguo (cadastre em Cadastros › Produtos)."); return; }
            (produtoId, produtoNome) = (p.Id, $"{p.Codigo} - {p.Descricao}");
        }
        var cfop = string.IsNullOrWhiteSpace(Item.Cfop) ? sel.Cfop : Item.Cfop.Trim().ToUpperInvariant();
        var novo = sel.Item with { Cfop = cfop, ProdutoId = produtoId, CentroCustoId = CentroItem?.Id, CentroCustoCodigo = CentroItem?.Codigo };
        var pos = Itens.IndexOf(sel);
        Itens[pos] = new ItemNotaLinha(novo, produtoNome);
        ItemSelecionado = Itens[pos];
        Mensagem = $"Item {novo.Numero} atualizado na tela. Clique em \"Gravar escrituração\" para salvar.";
    });

    /// <summary>Grava somente a escrituração da nota importada: CFOP, produto e centro de custo dos itens, centro de custo da nota, contas e observações.</summary>
    [RelayCommand]
    private Task SalvarEscrituracaoAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (_documentoId == 0) { Erros.Add("Selecione uma nota na lista."); return; }
        var esc = new EscrituracaoDocumento(_documentoId, Texto(Observacoes), CentroDocumento?.Id, Texto(ContaDebito), Texto(ContaCredito),
            Itens.Select(i => new EscrituracaoItem(i.N, i.Item.Cfop, i.Item.ProdutoId, i.Item.CentroCustoId)).ToList());
        var r = await app.Fiscal().Digitacao.GravarEscrituracaoAsync(empresa, esc, usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        var id = _documentoId;
        await ListarAsync(app, empresa, usuario);
        await CarregarDocumentoAsync(id);
        Selecionada = Lista.FirstOrDefault(d => d.Id == id);
        Mensagem = "Escrituração gravada.";
    }, Permissao.FiscalDocumentosImportar);

    // ------------------------------------------------------------------ importação de XML na tela

    /// <summary>Depois de importar: ajusta o período da lista para incluir as notas importadas desta operação e as seleciona.</summary>
    private async Task AposImportacaoAsync(ResultadoImportacao r)
    {
        var minhas = r.Arquivos.Where(a => a.Situacao == SituacaoImportacao.Importado && a.Operacao == Operacao).ToList();
        var outras = r.Arquivos.Count(a => a.Situacao == SituacaoImportacao.Importado && a.Operacao != Operacao);
        var datas = minhas.Where(a => a.DataLivro is not null).Select(a => a.DataLivro!.Value.ToDateTime(TimeOnly.MinValue)).ToList();
        AmpliarPeriodo(datas);
        await ExecutarAsync(ListarAsync, Permissao.FiscalDocumentosConsultar);
        if (minhas.Count > 0) Selecionada = Lista.FirstOrDefault(d => d.Chave == minhas[0].Chave);
        Mensagem = $"{minhas.Count} nota(s) de {Operacao.Rotulo().ToLowerInvariant()} importada(s) e listada(s) abaixo." +
                   (outras > 0 ? $" {outras} nota(s) da outra operação (entrada/saída) também foram importadas e aparecem na tela correspondente." : "");
    }

    private void AmpliarPeriodo(IReadOnlyCollection<DateTime> datas)
    {
        if (datas.Count == 0) return;
        if (FiltroInicio is null || datas.Min() < FiltroInicio) FiltroInicio = datas.Min();
        if (FiltroFim is null || datas.Max() > FiltroFim) FiltroFim = datas.Max();
    }

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    private Task EditarAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is not { } s) return;
        await CarregarDocumentoAsync(s.Id);   // recarrega do banco, descartando alterações não gravadas
        if (Contabilizado) Mensagem = "Nota contabilizada: para alterar, desfaça a contabilização (ou use \"Desfazer contabilização e excluir\").";
        else if (Importada) Mensagem = "Nota importada: edite a escrituração abaixo (os dados do XML são somente leitura) e clique em \"Gravar escrituração\".";
        else Mensagem = "Altere os dados da nota e clique em \"Gravar nota\".";
    }, Permissao.FiscalDocumentosConsultar);

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    private Task ExcluirAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        if (Selecionada is not { } s) return;
        var r = await ExclusaoNotas.ExcluirAsync(Contexto, app, empresa, usuario, s);   // contabilizada: "Desfazer contabilização e excluir"
        if (r is null) return;
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        Lista.Remove(s);
        Novo();
        Mensagem = "Nota excluída.";
    }, Permissao.FiscalDocumentosImportar);

    private bool TemSelecionada() => Selecionada is not null;

    /// <summary>Busca a NF-e na SEFAZ pela chave de acesso (como no Portal da NF-e) e importa o XML completo.</summary>
    [RelayCommand]
    private Task ImportarChaveAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        PodeManifestar = false;
        if (Sefaz.ServicoImportacaoChave.Validar(ChaveImportacao) is { } erro) { Erros.Add(erro); return; }
        var cert = await CertificadosPadrao.AbrirParaEsocialAsync(Contexto, Vorticis.Application.Sistema.PapelAssinatura.Responsavel);
        if (!cert.Sucesso) { MostrarResultado(cert, ""); return; }
        var r = await app.Fiscal().ImportacaoChave.ImportarAsync(empresa, ChaveImportacao, Sefaz.ClienteNfe.Criar(cert.Valor), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        if (r.Valor.Arquivo is { Situacao: SituacaoImportacao.Importado } a)
        {
            if (Importador.CompletarPeloCnpj) await Importador.CompletarCadastrosAsync(app, empresa, usuario, new ResultadoImportacao([a]));   // fornecedor novo: completa pela Receita
            await Importador.ContabilizarOnlineAsync(app, empresa, usuario, new ResultadoImportacao([a]));
            await AposImportacaoAsync(new ResultadoImportacao([a]));
            ChaveImportacao = "";
        }
        PodeManifestar = r.Valor.Situacao == Sefaz.SituacaoChave.SomenteResumo;
        if (r.Valor.Situacao == Sefaz.SituacaoChave.Importada) Mensagem = r.Valor.Mensagem;
        else Erros.Add(r.Valor.Mensagem);
    }, Permissao.FiscalDocumentosImportar);

    /// <summary>Registra a "Ciência da operação" (manifestação do destinatário), que libera o XML completo na SEFAZ.</summary>
    [RelayCommand]
    private Task ManifestarCienciaAsync() => ExecutarAsync(async (app, empresa, usuario) =>
    {
        var cert = await CertificadosPadrao.AbrirParaEsocialAsync(Contexto, Vorticis.Application.Sistema.PapelAssinatura.Responsavel);
        if (!cert.Sucesso) { MostrarResultado(cert, ""); return; }
        var r = await app.Fiscal().ImportacaoChave.ManifestarCienciaAsync(empresa, ChaveImportacao, Sefaz.ClienteNfe.Criar(cert.Valor), usuario);
        if (!r.Sucesso) { MostrarResultado(r, ""); return; }
        PodeManifestar = false;
        Mensagem = "Ciência da operação registrada. Aguarde alguns minutos e clique de novo em \"Importar pela chave\".";
    }, Permissao.FiscalDocumentosImportar);
}

public sealed class NotaEntradaViewModel(IContexto contexto) : DigitacaoDocumentoViewModel(contexto)
{
    public override TipoOperacao Operacao => TipoOperacao.Entrada;
}

public sealed class NotaSaidaViewModel(IContexto contexto) : DigitacaoDocumentoViewModel(contexto)
{
    public override TipoOperacao Operacao => TipoOperacao.Saida;
}
