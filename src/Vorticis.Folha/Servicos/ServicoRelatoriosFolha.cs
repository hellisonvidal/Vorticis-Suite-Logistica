using Vorticis.Application.Relatorios;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Seguranca;
using Vorticis.Folha.Cadastros;
using Vorticis.Folha.Regras;

namespace Vorticis.Folha.Servicos;

/// <summary>
/// Relatórios da folha: holerite (recibo de pagamento), recibo de férias e de 13º, termo de rescisão simplificado (PDF),
/// resumo da folha, encargos e provisões (tabelas exportáveis para PDF, Excel e CSV pela tela).
/// </summary>
public sealed class ServicoRelatoriosFolha(IRepositorioCadastrosFolha cadastros, IRepositorioMovimentoFolha movimento, IRepositorioContabilizacaoFolha contabilizacao,
                                           IGeradorPdf gerador, Autorizador autorizador)
{
    private async Task<string?> NegarAsync(long empresaId, long usuarioId, CancellationToken ct)
        => await autorizador.VerificarAsync(usuarioId, empresaId, Permissao.FolhaRelatorios, ct);

    private async Task<IReadOnlyList<string>> CabecalhoAsync(long empresaId, string linha2, CancellationToken ct)
    {
        var e = await cadastros.ObterEmpresaAsync(empresaId, ct);
        return [e is null ? "" : $"{e.RazaoSocial} – CNPJ {ServicoLivros.FormatarCnpj(e.Cnpj)}", linha2];
    }

    private static string Competencia(DateOnly c) => c.ToString("MM/yyyy");

    // ------------------------------------------------------------------ recibos (holerite, férias, 13º, rescisão)

    private static IReadOnlyList<Coluna> ColunasRecibo { get; } =
    [
        new("Cód.", 1.4), new("Descrição", 7.2), new("Referência", 2.2, Alinhamento.Direita), new("Proventos", 3.0, Alinhamento.Direita), new("Descontos", 3.0, Alinhamento.Direita),
    ];

    private static string Ref(ItemGravado i) => i.Referencia == 0 ? "" : i.Referencia.ToString("0.##", Formato.PtBr);

    private static IEnumerable<Bloco> BlocosRecibo(CalculoGravado c, Funcionario f, string titulo, string? observacao = null)
    {
        yield return new Bloco(Texto: $"{titulo}\nFuncionário: {c.Matricula} – {c.FuncionarioNome}   CPF {Domain.Cadastros.Cpf.Formatar(c.Cpf)}\n"
            + $"Cargo: {f.CargoNome ?? "—"}   Departamento: {f.DepartamentoNome ?? "—"}   Admissão: {f.Admissao:dd/MM/yyyy}"
            + $"{(f.Desligamento is { } d ? $"   Desligamento: {d:dd/MM/yyyy}" : "")}\n"
            + $"Competência: {Competencia(c.Competencia)}{(c.DataPagamento is { } p ? $"   Pagamento: {p:dd/MM/yyyy}" : "")}   {c.Tipo.Descricao()}");
        var linhas = c.Itens.Where(i => i.Tipo is 'P' or 'D').Select(i => new LinhaTabela(
            [i.Codigo, i.Descricao, Ref(i), i.Tipo == 'P' ? Formato.Moeda(i.Valor) : "", i.Tipo == 'D' ? Formato.Moeda(i.Valor) : ""])).ToList();
        linhas.Add(new LinhaTabela(["", "Totais", "", Formato.Moeda(c.TotalProventos), Formato.Moeda(c.TotalDescontos)], Negrito: true));
        linhas.Add(new LinhaTabela(["", "Líquido a receber", "", "", Formato.Moeda(c.Liquido)], Negrito: true));
        yield return new Bloco(Colunas: ColunasRecibo, Linhas: linhas);
        yield return new Bloco(Texto: $"Base INSS {Formato.Moeda(c.BaseInss)}   Base IRRF {Formato.Moeda(c.BaseIrrf)}   Base FGTS {Formato.Moeda(c.BaseFgts)}   FGTS do mês {Formato.Moeda(c.Fgts)}"
            + (c.SalarioFamilia > 0 ? $"   Salário-família {Formato.Moeda(c.SalarioFamilia)}" : "")
            + (f.Banco is null ? "" : $"\nCrédito em conta: banco {f.Banco} ag. {f.Agencia} conta {f.Conta}"));
        if (observacao is not null) yield return new Bloco(Texto: observacao);
    }

    private async Task<(CalculoGravado, Funcionario)?> CarregarAsync(long empresaId, long calculoId, CancellationToken ct)
    {
        var c = await movimento.ObterCalculoAsync(empresaId, calculoId, ct);
        if (c is null) return null;
        var f = await cadastros.ObterFuncionarioAsync(empresaId, c.FuncionarioId, ct);
        return f is null ? null : (c, f);
    }

    private static string TituloRecibo(TipoFolha t) => t switch
    {
        TipoFolha.Mensal => "RECIBO DE PAGAMENTO DE SALÁRIO",
        TipoFolha.Ferias => "RECIBO DE FÉRIAS",
        TipoFolha.Decimo1 => "RECIBO DE 13º SALÁRIO – 1ª PARCELA",
        TipoFolha.Decimo2 => "RECIBO DE 13º SALÁRIO – 2ª PARCELA",
        _ => "TERMO DE RESCISÃO DO CONTRATO DE TRABALHO (SIMPLIFICADO)",
    };

    private const string TextoRecebi = "Recebi o valor líquido discriminado neste recibo, dando plena quitação das parcelas nele constantes.";

    /// <summary>Holerite/recibo de um cálculo (mensal, férias ou 13º) em PDF.</summary>
    public async Task<Resultado<ArquivoGerado>> ReciboAsync(long empresaId, long calculoId, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(empresaId, usuarioId, ct) is { } negado) return Resultado<ArquivoGerado>.Falha(negado);
        var par = await CarregarAsync(empresaId, calculoId, ct);
        if (par is null) return Resultado<ArquivoGerado>.Falha("Cálculo não encontrado.");
        var (c, f) = par.Value;
        if (c.Tipo == TipoFolha.Rescisao) return await TrctAsync(empresaId, c, f, ct);
        var doc = new DocumentoRelatorio(TituloRecibo(c.Tipo), await CabecalhoAsync(empresaId, $"Competência {Competencia(c.Competencia)}", ct),
            BlocosRecibo(c, f, TituloRecibo(c.Tipo), c.Tipo == TipoFolha.Ferias ? await ObservacaoFeriasAsync(empresaId, c, ct) : null).Append(new Bloco(Texto: TextoRecebi)).ToList(),
            Assinaturas: [$"{c.FuncionarioNome}\nCPF {Domain.Cadastros.Cpf.Formatar(c.Cpf)}"]);
        return Resultado<ArquivoGerado>.Ok(gerador.Gerar(doc));
    }

    private async Task<string?> ObservacaoFeriasAsync(long empresaId, CalculoGravado c, CancellationToken ct)
    {
        if (c.OrigemId is not { } id || await movimento.ObterFeriasAsync(empresaId, id, ct) is not { } f) return null;
        return $"Período aquisitivo: {f.AquisitivoInicio:dd/MM/yyyy} a {f.AquisitivoFim:dd/MM/yyyy}. Gozo: {f.GozoInicio:dd/MM/yyyy} a {f.GozoInicio.AddDays(f.DiasGozo - 1):dd/MM/yyyy} ({f.DiasGozo} dias)"
            + (f.AbonoDias > 0 ? $" e abono pecuniário de {f.AbonoDias} dias" : "") + ". Retorno ao trabalho em " + f.GozoInicio.AddDays(f.DiasGozo).ToString("dd/MM/yyyy") + ".";
    }

    private async Task<Resultado<ArquivoGerado>> TrctAsync(long empresaId, CalculoGravado c, Funcionario f, CancellationToken ct)
    {
        var r = c.OrigemId is { } id ? await movimento.ObterRescisaoAsync(empresaId, id, ct) : null;
        var multa = c.Itens.Where(i => i.Codigo == RubricasPadrao.MultaFgts).Sum(i => i.Valor);
        var info = r is null ? "" : $"Motivo: {r.Motivo.Descricao()}\nData do desligamento: {r.Desligamento:dd/MM/yyyy}   Aviso prévio: {r.Aviso.ToString().ToLowerInvariant()}"
                                    + $"{(r.DiasAviso > 0 ? $" ({r.DiasAviso} dias)" : "")}   Pagamento até: {r.DataPagamento:dd/MM/yyyy}";
        var blocos = BlocosRecibo(c, f, TituloRecibo(c.Tipo), info).ToList();
        if (multa > 0)
            blocos.Add(new Bloco(Texto: $"Multa rescisória do FGTS: {Formato.Moeda(multa)} (depositada na conta vinculada, fora do valor líquido acima)."));
        blocos.Add(new Bloco(Texto: "Documento simplificado para conferência e recibo das verbas; não substitui o Termo de Rescisão (TRCT) e o Termo de Quitação oficiais, " +
                                    "nem a homologação/assistência quando exigida. " + TextoRecebi));
        var doc = new DocumentoRelatorio(TituloRecibo(c.Tipo), await CabecalhoAsync(empresaId, $"Rescisão – {Competencia(c.Competencia)}", ct), blocos,
            Assinaturas: ["Empregador", $"{c.FuncionarioNome}\nCPF {Domain.Cadastros.Cpf.Formatar(c.Cpf)}"]);
        return Resultado<ArquivoGerado>.Ok(gerador.Gerar(doc));
    }

    /// <summary>Holerites de toda a competência (um por página) em um só PDF.</summary>
    /// <summary>Holerites da competência: de todos os funcionários ou só de um (<paramref name="funcionarioId"/>).</summary>
    public async Task<Resultado<ArquivoGerado>> HoleritesAsync(long empresaId, DateOnly competencia, TipoFolha tipo, long usuarioId, CancellationToken ct = default,
                                                               long? funcionarioId = null)
    {
        if (await NegarAsync(empresaId, usuarioId, ct) is { } negado) return Resultado<ArquivoGerado>.Falha(negado);
        competencia = new DateOnly(competencia.Year, competencia.Month, 1);
        var calculos = (await movimento.ListarCalculosAsync(empresaId, competencia, tipo, true, ct))
            .Where(c => funcionarioId is null || c.FuncionarioId == funcionarioId).ToList();
        if (calculos.Count == 0)
            return Resultado<ArquivoGerado>.Falha(funcionarioId is null
                ? $"Não há {tipo.Descricao().ToLowerInvariant()} calculada em {Competencia(competencia)}."
                : $"O funcionário escolhido não tem {tipo.Descricao().ToLowerInvariant()} calculada em {Competencia(competencia)}.");
        var funcionarios = (await cadastros.ListarFuncionariosAsync(empresaId, ct)).ToDictionary(x => x.Id);
        var blocos = new List<Bloco>();
        var primeiro = true;
        foreach (var c in calculos)
        {
            var bs = BlocosRecibo(c, funcionarios[c.FuncionarioId], TituloRecibo(c.Tipo)).Append(new Bloco(Texto: TextoRecebi + "\n\n_______________________________________\n" + c.FuncionarioNome)).ToList();
            blocos.Add(bs[0] with { NovaPagina = !primeiro });
            blocos.AddRange(bs.Skip(1));
            primeiro = false;
        }
        return Resultado<ArquivoGerado>.Ok(gerador.Gerar(new DocumentoRelatorio($"Holerites – {Competencia(competencia)}",
            await CabecalhoAsync(empresaId, $"Competência {Competencia(competencia)}", ct), blocos)));
    }

    // ------------------------------------------------------------------ tabelas

    private static readonly TipoColuna V = TipoColuna.Valor;

    public async Task<Resultado<IReadOnlyList<TabelaRelatorio>>> ResumoFolhaAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(empresaId, usuarioId, ct) is { } negado) return Resultado<IReadOnlyList<TabelaRelatorio>>.Falha(negado);
        competencia = new DateOnly(competencia.Year, competencia.Month, 1);
        var calculos = await movimento.ListarCalculosAsync(empresaId, competencia, null, true, ct);
        var porFunc = calculos.GroupBy(c => (c.FuncionarioId, c.Matricula, c.FuncionarioNome)).OrderBy(g => g.Key.FuncionarioNome);
        var linhas = new List<LinhaRelatorio>();
        foreach (var g in porFunc)
            linhas.Add(new LinhaRelatorio([g.Key.Matricula, g.Key.FuncionarioNome, g.Sum(c => c.TotalProventos), g.Sum(c => c.TotalDescontos), g.Sum(c => c.Liquido),
                                           g.Sum(c => c.Inss), g.Sum(c => c.Irrf), g.Sum(c => c.Fgts)]));
        linhas.Add(new LinhaRelatorio(["", "TOTAL", calculos.Sum(c => c.TotalProventos), calculos.Sum(c => c.TotalDescontos), calculos.Sum(c => c.Liquido),
                                       calculos.Sum(c => c.Inss), calculos.Sum(c => c.Irrf), calculos.Sum(c => c.Fgts)], Destaque: true));
        var porFuncionario = new TabelaRelatorio("Resumo por funcionário",
            [new("Matrícula", TipoColuna.Texto, 1.8), new("Funcionário"), new("Proventos", V), new("Descontos", V), new("Líquido", V), new("INSS", V), new("IRRF", V), new("FGTS", V)],
            linhas);

        var rubricas = calculos.SelectMany(c => c.Itens).Where(i => i.Tipo is 'P' or 'D').GroupBy(i => (i.Codigo, i.Descricao, i.Tipo)).OrderBy(g => g.Key.Tipo == 'D').ThenBy(g => g.Key.Codigo)
            .Select(g => new LinhaRelatorio([g.Key.Codigo, g.Key.Descricao, g.Key.Tipo == 'P' ? "Provento" : "Desconto", g.Sum(i => i.Valor)])).ToList();
        var porRubrica = new TabelaRelatorio("Totais por rubrica", [new("Código", TipoColuna.Texto, 1.8), new("Rubrica"), new("Tipo", TipoColuna.Texto, 2.5), new("Valor", V)], rubricas);
        return Resultado<IReadOnlyList<TabelaRelatorio>>.Ok([porFuncionario, porRubrica]);
    }

    public async Task<Resultado<TabelaRelatorio>> EncargosAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(empresaId, usuarioId, ct) is { } negado) return Resultado<TabelaRelatorio>.Falha(negado);
        competencia = new DateOnly(competencia.Year, competencia.Month, 1);
        var calculos = await movimento.ListarCalculosAsync(empresaId, competencia, null, false, ct);
        var linhas = calculos.GroupBy(c => (c.Matricula, c.FuncionarioNome)).OrderBy(g => g.Key.FuncionarioNome).Select(g => new LinhaRelatorio(
            [g.Key.Matricula, g.Key.FuncionarioNome, g.Sum(c => c.BaseInss), g.Sum(c => c.EncInssPatronal), g.Sum(c => c.EncRat), g.Sum(c => c.EncTerceiros), g.Sum(c => c.Fgts),
             g.Sum(c => c.EncargosPrevidenciarios + c.Fgts)])).ToList();
        linhas.Add(new LinhaRelatorio(["", "TOTAL", calculos.Sum(c => c.BaseInss), calculos.Sum(c => c.EncInssPatronal), calculos.Sum(c => c.EncRat), calculos.Sum(c => c.EncTerceiros),
                                       calculos.Sum(c => c.Fgts), calculos.Sum(c => c.EncargosPrevidenciarios + c.Fgts)], Destaque: true));
        var sf = calculos.Sum(c => c.SalarioFamilia);
        var rodape = $"INSS retido dos empregados: {Formato.Moeda(calculos.Sum(c => c.Inss))} · IRRF retido: {Formato.Moeda(calculos.Sum(c => c.Irrf))}"
                     + (sf > 0 ? $" · Salário-família a compensar na GPS/DCTFWeb: {Formato.Moeda(sf)}" : "");
        return Resultado<TabelaRelatorio>.Ok(new TabelaRelatorio("Encargos patronais",
            [new("Matrícula", TipoColuna.Texto, 1.8), new("Funcionário"), new("Base INSS", V), new("INSS patronal", V), new("RAT x FAP", V), new("Terceiros", V), new("FGTS", V), new("Total", V)],
            linhas, rodape));
    }

    public async Task<Resultado<TabelaRelatorio>> ProvisoesAsync(long empresaId, DateOnly competencia, long usuarioId, CancellationToken ct = default)
    {
        if (await NegarAsync(empresaId, usuarioId, ct) is { } negado) return Resultado<TabelaRelatorio>.Falha(negado);
        var provisoes = await contabilizacao.ListarProvisoesAsync(empresaId, new DateOnly(competencia.Year, competencia.Month, 1), ct);
        var linhas = provisoes.Select(p => new LinhaRelatorio([p.Matricula, p.FuncionarioNome, p.Base, p.Ferias, p.EncFeriasInss + p.EncFeriasFgts, p.Decimo,
                                                              p.EncDecimoInss + p.EncDecimoFgts, p.Total])).ToList();
        linhas.Add(new LinhaRelatorio(["", "TOTAL", provisoes.Sum(p => p.Base), provisoes.Sum(p => p.Ferias), provisoes.Sum(p => p.EncFeriasInss + p.EncFeriasFgts),
                                       provisoes.Sum(p => p.Decimo), provisoes.Sum(p => p.EncDecimoInss + p.EncDecimoFgts), provisoes.Sum(p => p.Total)], Destaque: true));
        return Resultado<TabelaRelatorio>.Ok(new TabelaRelatorio("Provisões de férias e 13º salário",
            [new("Matrícula", TipoColuna.Texto, 1.8), new("Funcionário"), new("Base", V), new("Férias + 1/3", V), new("Encargos férias", V), new("13º salário", V), new("Encargos 13º", V), new("Total", V)],
            linhas));
    }
}
