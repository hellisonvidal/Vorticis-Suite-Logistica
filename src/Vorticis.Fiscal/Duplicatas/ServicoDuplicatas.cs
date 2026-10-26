using Vorticis.Application.Conciliacao;
using Vorticis.Application.Contabil;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Fiscal.Duplicatas;

public interface IRepositorioDuplicatas
{
    Task<IReadOnlyList<Duplicata>> ListarAsync(long empresaId, FiltroDuplicatas filtro, CancellationToken ct);
    Task<Duplicata?> ObterAsync(long empresaId, long id, CancellationToken ct);
    Task<IReadOnlyList<BaixaDuplicata>> ListarBaixasAsync(long empresaId, IReadOnlyCollection<long> duplicataIds, CancellationToken ct);
    Task<BaixaDuplicata?> ObterBaixaAsync(long empresaId, long baixaId, CancellationToken ct);
    Task<long> GravarAsync(long empresaId, DadosDuplicata dados, long usuarioId, CancellationToken ct);
    Task ExcluirAsync(long empresaId, long id, CancellationToken ct);
    Task CancelarAsync(long empresaId, long id, CancellationToken ct);

    /// <summary>Grava a baixa (travando a duplicata); o saldo e a situação são recalculados pelo banco.</summary>
    Task<long> InserirBaixaAsync(long empresaId, NovaBaixa baixa, long? lancamentoId, long? partidaExtratoId, long usuarioId, CancellationToken ct);
    Task DefinirConciliacaoAsync(long baixaId, long conciliacaoId, CancellationToken ct);
    /// <summary>Liga (ou desliga, com null) o lançamento contábil da baixa — usado pela Contabilização em lote.</summary>
    Task DefinirLancamentoBaixaAsync(long empresaId, long baixaId, long? lancamentoId, CancellationToken ct);
    Task ExcluirBaixaAsync(long empresaId, long baixaId, CancellationToken ct);

    Task<ConfiguracaoDuplicatas> ObterConfiguracaoAsync(long empresaId, CancellationToken ct);
    Task GravarConfiguracaoAsync(long empresaId, ConfiguracaoDuplicatas configuracao, CancellationToken ct);
    Task<IReadOnlySet<string>> ContasAnaliticasAsync(long empresaId, IEnumerable<string> codigos, CancellationToken ct);
    Task<(long Id, string Codigo, string Nome)?> ObterParticipanteAsync(long empresaId, long participanteId, CancellationToken ct);

    /// <summary>Dos documentos informados, os que têm duplicata não cancelada.</summary>
    Task<IReadOnlySet<long>> DocumentosComDuplicataAsync(long empresaId, IEnumerable<long> documentoIds, CancellationToken ct);

    /// <summary>Dos documentos informados, os que têm alguma duplicata com baixa.</summary>
    Task<IReadOnlySet<long>> DocumentosComBaixaAsync(long empresaId, IEnumerable<long> documentoIds, CancellationToken ct);

    Task<IReadOnlyList<MovimentoExtrato>> ListarMovimentosExtratoAsync(long empresaId, string contaBanco, DateOnly inicio, DateOnly fim, CancellationToken ct);
    Task<MovimentoExtrato?> ObterMovimentoAsync(long empresaId, long partidaBancoId, CancellationToken ct);
    Task<long?> PartidaDaContaAsync(long lancamentoId, string contaCodigo, IndicadorDC dc, CancellationToken ct);
}

/// <summary>
/// Duplicatas (contas a pagar e a receber) originadas das notas fiscais ou lançadas à mão: manutenção, baixa total ou parcial com lançamento contábil
/// (pelo <see cref="ServicoLancamento"/>), estorno da baixa e conciliação com o extrato bancário (OFX já importado no contábil). Toda operação verifica a
/// permissão do usuário na empresa antes de qualquer outra regra.
/// </summary>
public sealed class ServicoDuplicatas(IRepositorioDuplicatas repositorio, ServicoLancamento lancamentos, ServicoConciliacao conciliacao, Autorizador autorizador,
                                      ServicoPendencias? pendencias = null)
{
    // ------------------------------------------------------------------ consultas

    public async Task<Resultado<IReadOnlyList<Duplicata>>> ConsultarAsync(long empresaId, FiltroDuplicatas filtro, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<Duplicata>>(usuarioId, empresaId, Permissao.FiscalDocumentosConsultar, ct) is { } negado) return negado;
        return Resultado<IReadOnlyList<Duplicata>>.Ok(await repositorio.ListarAsync(empresaId, filtro, ct));
    }

    /// <summary>Mesma consulta para os relatórios (exige a função de relatórios fiscais).</summary>
    public async Task<Resultado<IReadOnlyList<Duplicata>>> RelatorioAsync(long empresaId, FiltroDuplicatas filtro, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<Duplicata>>(usuarioId, empresaId, Permissao.FiscalRelatorios, ct) is { } negado) return negado;
        return Resultado<IReadOnlyList<Duplicata>>.Ok(await repositorio.ListarAsync(empresaId, filtro, ct));
    }

    public async Task<Resultado<IReadOnlyList<BaixaDuplicata>>> BaixasAsync(long empresaId, IReadOnlyCollection<long> duplicataIds, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<BaixaDuplicata>>(usuarioId, empresaId, Permissao.FiscalDocumentosConsultar, ct) is { } negado) return negado;
        return Resultado<IReadOnlyList<BaixaDuplicata>>.Ok(duplicataIds.Count == 0 ? [] : await repositorio.ListarBaixasAsync(empresaId, duplicataIds, ct));
    }

    // ------------------------------------------------------------------ configuração

    public async Task<Resultado<ConfiguracaoDuplicatas>> ObterConfiguracaoAsync(long empresaId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ConfiguracaoDuplicatas>(usuarioId, empresaId, Permissao.FiscalDocumentosConsultar, ct) is { } negado) return negado;
        return Resultado<ConfiguracaoDuplicatas>.Ok(await repositorio.ObterConfiguracaoAsync(empresaId, ct));
    }

    public async Task<Resultado> GravarConfiguracaoAsync(long empresaId, ConfiguracaoDuplicatas c, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalDuplicatasManter, ct) is { } negado) return Resultado.Falha(negado);
        var erros = new List<string>();
        if (c.AVistaDias is < 0 or > 365) erros.Add("Dias do vencimento à vista: informe de 0 a 365.");
        foreach (var (texto, rotulo) in new[] { (c.HistoricoPagamento, "pagamento"), (c.HistoricoRecebimento, "recebimento") })
        {
            if (string.IsNullOrWhiteSpace(texto)) erros.Add($"Informe o histórico do {rotulo}.");
            else if (texto.Length > 200) erros.Add($"O histórico do {rotulo} pode ter até 200 caracteres.");
            else if (VariaveisHistorico.Desconhecidas(texto) is { Count: > 0 } desc)
                erros.Add($"Variável desconhecida no histórico do {rotulo}: {string.Join(", ", desc.Select(d => "{" + d + "}"))}.");
        }
        string? Limpa(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
        var normalizada = c with
        {
            ContaFornecedores = Limpa(c.ContaFornecedores), ContaClientes = Limpa(c.ContaClientes), ContaJurosPagos = Limpa(c.ContaJurosPagos),
            ContaMultaPaga = Limpa(c.ContaMultaPaga), ContaDescontoObtido = Limpa(c.ContaDescontoObtido), ContaJurosRecebidos = Limpa(c.ContaJurosRecebidos),
            ContaMultaRecebida = Limpa(c.ContaMultaRecebida), ContaDescontoConcedido = Limpa(c.ContaDescontoConcedido),
            HistoricoPagamento = c.HistoricoPagamento?.Trim() ?? "", HistoricoRecebimento = c.HistoricoRecebimento?.Trim() ?? "",
        };
        var contas = new[]
        {
            (normalizada.ContaFornecedores, "fornecedores"), (normalizada.ContaClientes, "clientes"), (normalizada.ContaJurosPagos, "juros pagos"),
            (normalizada.ContaMultaPaga, "multa paga"), (normalizada.ContaDescontoObtido, "desconto obtido"), (normalizada.ContaJurosRecebidos, "juros recebidos"),
            (normalizada.ContaMultaRecebida, "multa recebida"), (normalizada.ContaDescontoConcedido, "desconto concedido"),
        };
        var codigos = contas.Select(x => x.Item1).OfType<string>().Distinct().ToList();
        var validas = codigos.Count == 0 ? new HashSet<string>() : await repositorio.ContasAnaliticasAsync(empresaId, codigos, ct);
        foreach (var (codigo, rotulo) in contas)
            if (codigo is not null && !validas.Contains(codigo))
                erros.Add($"Conta de {rotulo} \"{codigo}\" não encontrada entre as analíticas ativas (informe o código completo da conta).");
        if (erros.Count > 0) return Resultado.Falha(erros);
        try { await repositorio.GravarConfiguracaoAsync(empresaId, normalizada, ct); return Resultado.Ok(); }
        catch (ViolacaoRegraException ex) { return Resultado.Falha(ex.Message); }
    }

    // ------------------------------------------------------------------ manutenção

    /// <summary>Inclui (Id = 0) ou altera uma duplicata lançada à mão (ou ajusta vencimento, valor e observação de uma originada de nota).</summary>
    public async Task<Resultado<long>> GravarAsync(long empresaId, DadosDuplicata d, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FiscalDuplicatasManter, ct) is { } negado) return negado;
        var erros = new List<string>();
        var numero = (d.NumeroDocumento ?? "").Trim();
        var parcela = string.IsNullOrWhiteSpace(d.Parcela) ? "001" : d.Parcela.Trim();
        if (numero.Length == 0) erros.Add("Informe o número do documento.");
        else if (numero.Length > 30) erros.Add("O número do documento pode ter até 30 caracteres.");
        if (parcela.Length > 20) erros.Add("O número da parcela pode ter até 20 caracteres.");
        if (d.Valor <= 0) erros.Add("O valor da duplicata deve ser maior que zero.");
        if (d.Emissao == default) erros.Add("Informe a data de emissão.");
        if (d.Vencimento == default) erros.Add("Informe a data de vencimento.");
        else if (d.Emissao != default && d.Vencimento < d.Emissao) erros.Add("O vencimento não pode ser anterior à emissão.");
        if (d.Observacao is { Length: > 500 }) erros.Add("A observação pode ter até 500 caracteres.");
        if (d.ParticipanteId == 0 || await repositorio.ObterParticipanteAsync(empresaId, d.ParticipanteId, ct) is null)
            erros.Add($"Informe o {(d.Tipo == TipoDuplicata.Pagar ? "fornecedor" : "cliente")} (participante cadastrado).");
        if (d.Id != 0)
        {
            var atual = await repositorio.ObterAsync(empresaId, d.Id, ct);
            if (atual is null) erros.Add("Duplicata não encontrada.");
            else if (atual.Situacao is SituacaoDuplicata.Cancelada or SituacaoDuplicata.Liquidada)
                erros.Add($"Duplicata {atual.Situacao.Rotulo().ToLowerInvariant()}: não pode ser alterada.");
        }
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        try { return Resultado<long>.Ok(await repositorio.GravarAsync(empresaId, d with { NumeroDocumento = numero, Parcela = parcela }, usuarioId, ct)); }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }

    /// <summary>Cancela a duplicata em aberto (sem baixa). A nota cancelada cancela as suas duplicatas sozinha.</summary>
    public async Task<Resultado> CancelarAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalDuplicatasManter, ct) is { } negado) return Resultado.Falha(negado);
        if (await repositorio.ObterAsync(empresaId, id, ct) is not { } d) return Resultado.Falha("Duplicata não encontrada.");
        if (d.Situacao == SituacaoDuplicata.Cancelada) return Resultado.Falha("A duplicata já está cancelada.");
        if (d.Situacao != SituacaoDuplicata.Aberta) return Resultado.Falha("Só se cancela duplicata em aberto, sem baixas: estorne as baixas antes.");
        try { await repositorio.CancelarAsync(empresaId, id, ct); return Resultado.Ok(); }
        catch (ViolacaoRegraException ex) { return Resultado.Falha(ex.Message); }
    }

    /// <summary>Exclui duplicata lançada à mão e sem baixas (a originada de nota é cancelada ou some com a nota).</summary>
    public async Task<Resultado> ExcluirAsync(long empresaId, long id, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalDuplicatasManter, ct) is { } negado) return Resultado.Falha(negado);
        if (await repositorio.ObterAsync(empresaId, id, ct) is not { } d) return Resultado.Falha("Duplicata não encontrada.");
        if (d.Origem != "MANUAL") return Resultado.Falha("Duplicata originada de nota fiscal: cancele-a ou altere a nota.");
        if (d.Situacao is SituacaoDuplicata.Parcial or SituacaoDuplicata.Liquidada) return Resultado.Falha("A duplicata tem baixas: estorne-as antes de excluir.");
        await repositorio.ExcluirAsync(empresaId, id, ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ baixa

    /// <summary>
    /// Lançamento da baixa. Pagamento: débito no fornecedor (principal), juros e multa; crédito no desconto obtido e no disponível (líquido). Recebimento: débito
    /// no disponível (líquido) e no desconto concedido; crédito no cliente (principal), juros e multa recebidos. Documento = nota/parcela; histórico pelo
    /// modelo da configuração. Devolve os erros (conta não configurada) em vez do lançamento.
    /// </summary>
    public static (LancamentoProposto? Lancamento, IReadOnlyList<string> Erros) MontarLancamento(long empresaId, Duplicata d, NovaBaixa b, ConfiguracaoDuplicatas cfg,
                                                                                               string disponivel)
    {
        var erros = new List<string>();
        var pagar = d.Tipo == TipoDuplicata.Pagar;
        string? Exigir(string? conta, string rotulo)
        {
            if (string.IsNullOrWhiteSpace(conta)) erros.Add($"Informe a conta de {rotulo} em Cadastros › Parâmetros (Parâmetros do Fiscal).");
            return conta;
        }
        var contaPart = Exigir(cfg.ContaParticipante(d.Tipo), pagar ? "fornecedores" : "clientes");
        var contaJuros = b.Juros > 0 ? Exigir(pagar ? cfg.ContaJurosPagos : cfg.ContaJurosRecebidos, pagar ? "juros pagos" : "juros recebidos") : null;
        var contaMulta = b.Multa > 0 ? Exigir(pagar ? cfg.ContaMultaPaga : cfg.ContaMultaRecebida, pagar ? "multa paga" : "multa recebida") : null;
        var contaDesc = b.Desconto > 0 ? Exigir(pagar ? cfg.ContaDescontoObtido : cfg.ContaDescontoConcedido, pagar ? "desconto obtido" : "desconto concedido") : null;
        if (erros.Count > 0) return (null, erros);

        var liquido = b.Principal + b.Juros + b.Multa - b.Desconto;
        var hist = VariaveisHistorico.Aplicar(pagar ? cfg.HistoricoPagamento : cfg.HistoricoRecebimento,
            new ContextoHistorico(Documento: d.Titulo, Participante: d.ParticipanteNome, Data: b.Data, Valor: b.Principal)).Texto;
        var doc = d.Titulo;
        var principal = IndicadorDC.D;
        var partidas = new List<PartidaProposta>();
        void Add(string conta, IndicadorDC dc, decimal valor, string? participante = null)
        {
            if (valor > 0) partidas.Add(new PartidaProposta(conta, dc, valor, hist, CodigoParticipante: participante, NumeroArquivo: doc));
        }
        if (pagar)
        {
            Add(contaPart!, principal, b.Principal, d.ParticipanteCodigo);
            Add(contaJuros ?? "", IndicadorDC.D, b.Juros);
            Add(contaMulta ?? "", IndicadorDC.D, b.Multa);
            Add(contaDesc ?? "", IndicadorDC.C, b.Desconto);
            Add(disponivel, IndicadorDC.C, liquido);
        }
        else
        {
            Add(disponivel, IndicadorDC.D, liquido);
            Add(contaDesc ?? "", IndicadorDC.D, b.Desconto);
            Add(contaPart!, IndicadorDC.C, b.Principal, d.ParticipanteCodigo);
            Add(contaJuros ?? "", IndicadorDC.C, b.Juros);
            Add(contaMulta ?? "", IndicadorDC.C, b.Multa);
        }
        return (new LancamentoProposto(empresaId, b.Data, OrigemLancamento.Manual, partidas), []);
    }

    private static IReadOnlyList<string> Validar(Duplicata d, NovaBaixa b)
    {
        var erros = new List<string>();
        if (d.Situacao == SituacaoDuplicata.Cancelada) erros.Add($"Duplicata {d.Titulo} cancelada: não pode ser baixada.");
        else if (d.Situacao == SituacaoDuplicata.Liquidada) erros.Add($"Duplicata {d.Titulo} já está liquidada.");
        if (b.Data == default) erros.Add("Informe a data da baixa.");
        if (b.Principal <= 0) erros.Add("O valor principal da baixa deve ser maior que zero.");
        else if (b.Principal > d.Saldo) erros.Add($"O valor da baixa ({Formato.Moeda(b.Principal)}) é maior que o saldo da duplicata ({Formato.Moeda(d.Saldo)}).");
        if (b.Juros < 0 || b.Multa < 0 || b.Desconto < 0) erros.Add("Juros, multa e desconto não podem ser negativos.");
        if (b.Principal + b.Juros + b.Multa - b.Desconto <= 0) erros.Add("O valor líquido da baixa (principal + juros + multa - desconto) deve ser maior que zero.");
        if (string.IsNullOrWhiteSpace(b.ContaDisponivel)) erros.Add($"Informe a conta do banco ou caixa {(d.Tipo == TipoDuplicata.Pagar ? "de onde saiu" : "onde entrou")} o dinheiro.");
        return erros;
    }

    /// <summary>Baixa (total ou parcial) uma duplicata e gera o lançamento contábil. Devolve o id da baixa.</summary>
    public async Task<Resultado<long>> BaixarAsync(long empresaId, NovaBaixa b, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FiscalDuplicatasBaixar, ct) is { } negado) return negado;
        if (await repositorio.ObterAsync(empresaId, b.DuplicataId, ct) is not { } d) return Resultado<long>.Falha("Duplicata não encontrada.");
        b = b with { ContaDisponivel = (b.ContaDisponivel ?? "").Trim() };
        var erros = Validar(d, b);
        if (erros.Count > 0) return Resultado<long>.Falha(erros);
        var cfg = await repositorio.ObterConfiguracaoAsync(empresaId, ct);
        var (proposta, errosLanc) = MontarLancamento(empresaId, d, b, cfg, b.ContaDisponivel);
        if (proposta is null) return Resultado<long>.Falha(errosLanc);
        if (pendencias is not null && await pendencias.ModoDaEmpresaAsync(empresaId, ct) == ModoContabilizacao.Lote)
        {
            // Contabilização em lote: a baixa é gravada sem lançamento e fica pendente na Contabilização em lote.
            var idBaixa = await repositorio.InserirBaixaAsync(empresaId, b, null, null, usuarioId, ct);
            await pendencias.RegistrarAsync(empresaId, new NovaPendencia(OrigensPendencia.Duplicatas, TipoBaixa, idBaixa.ToString(), b.Data,
                $"{(d.Tipo == TipoDuplicata.Pagar ? "Pagamento" : "Recebimento")} da duplicata {d.Titulo}{(string.IsNullOrWhiteSpace(d.ParticipanteNome) ? "" : " - " + d.ParticipanteNome)}",
                b.Principal + b.Juros + b.Multa - b.Desconto), usuarioId, ct);
            return Resultado<long>.Ok(idBaixa);
        }
        var lanc = await lancamentos.EfetivarAsync(proposta, usuarioId, ct);
        if (!lanc.Sucesso) return Resultado<long>.Falha(lanc.Erros);
        try
        {
            return Resultado<long>.Ok(await repositorio.InserirBaixaAsync(empresaId, b, lanc.Valor.Id, null, usuarioId, ct));
        }
        catch (ViolacaoRegraException ex)
        {
            await lancamentos.EstornarAsync(empresaId, lanc.Valor.Id, b.Data, $"Baixa não registrada da duplicata {d.Titulo}", usuarioId, ct);
            return Resultado<long>.Falha(ex.Message);
        }
    }

    // ------------------------------------------------------------------ contabilização em lote das baixas

    public const string TipoBaixa = "BAIXA";

    private async Task<(BaixaDuplicata? Baixa, Duplicata? Duplicata, LancamentoProposto? Proposta, IReadOnlyList<string> Erros)> PropostaDaBaixaAsync(
        long empresaId, PendenciaContabil p, CancellationToken ct)
    {
        if (!long.TryParse(p.Referencia, out var id) || await repositorio.ObterBaixaAsync(empresaId, id, ct) is not { } baixa) return (null, null, null, ["Baixa não encontrada."]);
        var d = await repositorio.ObterAsync(empresaId, baixa.DuplicataId, ct);
        if (d is null) return (baixa, null, null, ["Duplicata não encontrada."]);
        var cfg = await repositorio.ObterConfiguracaoAsync(empresaId, ct);
        var nova = new NovaBaixa(d.Id, baixa.Data, baixa.Principal, baixa.Juros, baixa.Multa, baixa.Desconto, baixa.ContaDisponivel ?? "");
        var (proposta, erros) = MontarLancamento(empresaId, d, nova, cfg, nova.ContaDisponivel);
        return (baixa, d, proposta, erros);
    }

    /// <summary>Processador da origem DUPLICATAS (baixas pendentes da Contabilização em lote): as mesmas regras da baixa on-line.</summary>
    public IProcessadorPendencia ProcessadorPendencias => new ProcessadorBaixas(this);

    private async Task<IReadOnlyList<string>> PreviaPendenciaAsync(long empresaId, PendenciaContabil p, CancellationToken ct)
    {
        var (_, _, proposta, erros) = await PropostaDaBaixaAsync(empresaId, p, ct);
        return proposta is null ? erros : [.. proposta.Partidas.Select(x => $"{x.DC} {x.CodigoConta}  {Formato.Moeda(x.Valor)}")];
    }

    private async Task<ResultadoProcessamento> ContabilizarPendenciaAsync(long empresaId, PendenciaContabil p, long usuarioId, CancellationToken ct)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalDuplicatasBaixar, ct) is { } negado) return ResultadoProcessamento.Falha(negado);
        var (baixa, _, proposta, erros) = await PropostaDaBaixaAsync(empresaId, p, ct);
        if (baixa is null) return ResultadoProcessamento.Cancelada("a baixa foi estornada.");
        if (baixa.LancamentoId is { } ja) return ResultadoProcessamento.Ok(ja);
        if (proposta is null) return ResultadoProcessamento.Falha(erros);
        var lanc = await lancamentos.EfetivarAsync(proposta, usuarioId, ct);
        if (!lanc.Sucesso) return ResultadoProcessamento.Falha(lanc.Erros);
        await repositorio.DefinirLancamentoBaixaAsync(empresaId, baixa.Id, lanc.Valor.Id, ct);
        return ResultadoProcessamento.Ok(lanc.Valor.Id);
    }

    private async Task<Resultado> DesfazerPendenciaAsync(long empresaId, PendenciaContabil p, long usuarioId, CancellationToken ct)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalDuplicatasBaixar, ct) is { } negado) return Resultado.Falha(negado);
        if (!long.TryParse(p.Referencia, out var id) || await repositorio.ObterBaixaAsync(empresaId, id, ct) is not { LancamentoId: { } lanc } baixa) return Resultado.Ok();
        var e = await lancamentos.EstornarAsync(empresaId, lanc, baixa.Data, "Desfazer lote de contabilização - baixa de duplicata", usuarioId, ct);
        if (!e.Sucesso) return Resultado.Falha(e.Erros);
        await repositorio.DefinirLancamentoBaixaAsync(empresaId, baixa.Id, null, ct);
        return Resultado.Ok();
    }

    private sealed class ProcessadorBaixas(ServicoDuplicatas s) : IProcessadorPendencia
    {
        public string Origem => OrigensPendencia.Duplicatas;
        public Task<IReadOnlyList<string>> PreviaAsync(long empresaId, PendenciaContabil p, long usuarioId, CancellationToken ct) => s.PreviaPendenciaAsync(empresaId, p, ct);
        public Task<ResultadoProcessamento> ContabilizarAsync(long empresaId, PendenciaContabil p, long usuarioId, CancellationToken ct) => s.ContabilizarPendenciaAsync(empresaId, p, usuarioId, ct);
        public Task<Resultado> DesfazerAsync(long empresaId, PendenciaContabil p, long usuarioId, CancellationToken ct) => s.DesfazerPendenciaAsync(empresaId, p, usuarioId, ct);
    }

    /// <summary>Baixa o saldo inteiro de cada duplicata selecionada, na mesma data e conta; uma com pendência não impede as outras.</summary>
    public async Task<Resultado<ResultadoBaixaLote>> BaixarLoteAsync(long empresaId, IReadOnlyCollection<long> ids, DateOnly data, string contaDisponivel, long usuarioId,
                                                                    CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ResultadoBaixaLote>(usuarioId, empresaId, Permissao.FiscalDuplicatasBaixar, ct) is { } negado) return negado;
        if (ids.Count == 0) return Resultado<ResultadoBaixaLote>.Falha("Selecione ao menos uma duplicata.");
        var falhas = new List<FalhaDuplicata>();
        var baixadas = 0;
        var total = 0m;
        foreach (var id in ids)
        {
            var d = await repositorio.ObterAsync(empresaId, id, ct);
            if (d is null) { falhas.Add(new FalhaDuplicata($"#{id}", ["Duplicata não encontrada."])); continue; }
            var r = await BaixarAsync(empresaId, new NovaBaixa(id, data, d.Saldo, 0, 0, 0, contaDisponivel), usuarioId, ct);
            if (r.Sucesso) { baixadas++; total += d.Saldo; }
            else falhas.Add(new FalhaDuplicata(d.Titulo, r.Erros));
        }
        return Resultado<ResultadoBaixaLote>.Ok(new ResultadoBaixaLote(baixadas, total, falhas));
    }

    /// <summary>
    /// Estorna a baixa: desfaz a conciliação com o extrato (se houver), estorna o lançamento contábil (na data da baixa, ou em <paramref name="dataEstorno"/>)
    /// e devolve o valor ao saldo da duplicata. Se o estorno contábil falhar (ex.: período fechado), a baixa é mantida.
    /// </summary>
    public async Task<Resultado> EstornarBaixaAsync(long empresaId, long baixaId, DateOnly? dataEstorno, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FiscalDuplicatasBaixar, ct) is { } negado) return Resultado.Falha(negado);
        if (await repositorio.ObterBaixaAsync(empresaId, baixaId, ct) is not { } b) return Resultado.Falha("Baixa não encontrada.");
        var titulo = (await repositorio.ObterAsync(empresaId, b.DuplicataId, ct))?.Titulo ?? "";
        if (b.ConciliacaoId is { } conc)
        {
            var d = await conciliacao.DesfazerAsync(empresaId, conc, usuarioId, ct);
            if (!d.Sucesso) return d;
        }
        if (b.LancamentoId is { } lanc)
        {
            var e = await lancamentos.EstornarAsync(empresaId, lanc, dataEstorno ?? b.Data, $"Estorno da baixa da duplicata {titulo}", usuarioId, ct);
            if (!e.Sucesso) return Resultado.Falha(e.Erros);
        }
        await repositorio.ExcluirBaixaAsync(empresaId, baixaId, ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ conciliação com o extrato

    /// <summary>
    /// Sugere a baixa de duplicatas em aberto a partir dos movimentos do extrato (OFX importado em Lançamentos › Importação) na conta do banco: valor igual ao
    /// saldo, data próxima do vencimento (até <paramref name="toleranciaDias"/>) e/ou documento citado.
    /// </summary>
    public async Task<Resultado<IReadOnlyList<SugestaoConciliacao>>> SugerirConciliacaoAsync(long empresaId, string contaBanco, DateOnly inicio, DateOnly fim, int toleranciaDias,
                                                                                            long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<SugestaoConciliacao>>(usuarioId, empresaId, Permissao.FiscalDuplicatasBaixar, ct) is { } negado) return negado;
        if (string.IsNullOrWhiteSpace(contaBanco)) return Resultado<IReadOnlyList<SugestaoConciliacao>>.Falha("Informe a conta contábil do banco do extrato.");
        if (fim < inicio) return Resultado<IReadOnlyList<SugestaoConciliacao>>.Falha("Data final anterior à inicial.");
        if (toleranciaDias is < 0 or > 60) return Resultado<IReadOnlyList<SugestaoConciliacao>>.Falha("Tolerância de dias: informe de 0 a 60.");
        var movimentos = await repositorio.ListarMovimentosExtratoAsync(empresaId, contaBanco, inicio, fim, ct);
        var abertas = await repositorio.ListarAsync(empresaId, new FiltroDuplicatas(SomenteAbertas: true, Limite: 20000), ct);
        return Resultado<IReadOnlyList<SugestaoConciliacao>>.Ok(ConciliadorDuplicatas.Sugerir(abertas, movimentos, toleranciaDias));
    }

    /// <summary>
    /// Baixa a duplicata pelo valor total do saldo, casando-a com o movimento do extrato (a partida bancária fica registrada na baixa e não aparece mais
    /// nas sugestões). Se a contrapartida do movimento já é a conta de fornecedores/clientes, o lançamento do extrato basta; senão a baixa lança contra a
    /// contrapartida do extrato (conta transitória) e as duas partidas são conciliadas em Lançamentos › Conciliações.
    /// </summary>
    public async Task<Resultado<long>> ConciliarComExtratoAsync(long empresaId, long duplicataId, long partidaBancoId, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<long>(usuarioId, empresaId, Permissao.FiscalDuplicatasBaixar, ct) is { } negado) return negado;
        if (await repositorio.ObterAsync(empresaId, duplicataId, ct) is not { } d) return Resultado<long>.Falha("Duplicata não encontrada.");
        if (await repositorio.ObterMovimentoAsync(empresaId, partidaBancoId, ct) is not { } m)
            return Resultado<long>.Falha("Movimento do extrato não encontrado ou já conciliado com outra duplicata.");
        var erros = new List<string>();
        if (!d.EmAberto) erros.Add($"Duplicata {d.Titulo} {d.Situacao.Rotulo().ToLowerInvariant()}: não pode ser baixada.");
        if (m.Tipo != d.Tipo) erros.Add(d.Tipo == TipoDuplicata.Pagar ? "Duplicata a pagar só casa com saída do banco." : "Duplicata a receber só casa com entrada no banco.");
        if (m.Valor != d.Saldo) erros.Add($"O valor do movimento ({Formato.Moeda(m.Valor)}) é diferente do saldo da duplicata ({Formato.Moeda(d.Saldo)}).");
        if (m.ContraConciliada) erros.Add("A contrapartida do movimento do extrato já está conciliada.");
        if (erros.Count > 0) return Resultado<long>.Falha(erros);

        var cfg = await repositorio.ObterConfiguracaoAsync(empresaId, ct);
        var contaPart = cfg.ContaParticipante(d.Tipo);
        if (string.IsNullOrWhiteSpace(contaPart))
            return Resultado<long>.Falha($"Informe a conta de {(d.Tipo == TipoDuplicata.Pagar ? "fornecedores" : "clientes")} em Cadastros › Parâmetros (Parâmetros do Fiscal).");
        var baixa = new NovaBaixa(duplicataId, m.Data, d.Saldo, 0, 0, 0, m.ContaBanco);

        try
        {
            if (m.ContaContra == contaPart)   // o lançamento do extrato já baixa o fornecedor/cliente
                return Resultado<long>.Ok(await repositorio.InserirBaixaAsync(empresaId, baixa, null, m.PartidaBancoId, usuarioId, ct));

            var (proposta, errosLanc) = MontarLancamento(empresaId, d, baixa, cfg, m.ContaContra);
            if (proposta is null) return Resultado<long>.Falha(errosLanc);
            var lanc = await lancamentos.EfetivarAsync(proposta, usuarioId, ct);
            if (!lanc.Sucesso) return Resultado<long>.Falha(lanc.Erros);

            async Task<Resultado<long>> Desfazer(long? baixaId, IReadOnlyList<string> motivo)
            {
                if (baixaId is { } id) await repositorio.ExcluirBaixaAsync(empresaId, id, ct);
                await lancamentos.EstornarAsync(empresaId, lanc.Valor.Id, m.Data, $"Conciliação não concluída da duplicata {d.Titulo}", usuarioId, ct);
                return Resultado<long>.Falha(motivo);
            }

            long baixaId;
            try { baixaId = await repositorio.InserirBaixaAsync(empresaId, baixa, lanc.Valor.Id, m.PartidaBancoId, usuarioId, ct); }
            catch (ViolacaoRegraException ex) { return await Desfazer(null, [ex.Message]); }

            var dcBaixa = d.Tipo == TipoDuplicata.Pagar ? IndicadorDC.C : IndicadorDC.D;
            if (await repositorio.PartidaDaContaAsync(lanc.Valor.Id, m.ContaContra, dcBaixa, ct) is not { } partidaBaixa)
                return await Desfazer(baixaId, ["Partida da baixa não encontrada para conciliar."]);
            var c = await conciliacao.ConciliarManualAsync(empresaId, m.ContaContra, [m.PartidaContraId, partidaBaixa], usuarioId, ct);
            if (!c.Sucesso) return await Desfazer(baixaId, c.Erros);
            await repositorio.DefinirConciliacaoAsync(baixaId, c.Valor, ct);
            return Resultado<long>.Ok(baixaId);
        }
        catch (ViolacaoRegraException ex) { return Resultado<long>.Falha(ex.Message); }
    }
}
