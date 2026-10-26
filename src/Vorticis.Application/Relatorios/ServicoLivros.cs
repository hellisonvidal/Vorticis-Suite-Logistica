using Vorticis.Application.Cadastros;
using Vorticis.Application.Contabil;
using Vorticis.Application.Seguranca;
using Vorticis.Domain.Comum;
using Vorticis.Domain.Contabil;
using Vorticis.Domain.Seguranca;

namespace Vorticis.Application.Relatorios;

/// <summary>Dados dos termos do livro informados no momento da emissão.</summary>
/// <param name="Folhas">Total de folhas, só para os termos emitidos isoladamente (em branco = linha para preencher à mão).</param>
public sealed record DadosTermos(int NumeroLivro, string RepresentanteNome, string RepresentanteCpf, string ContadorNome, string ContadorCrc,
                                 string? DataArquivamentoAtos = null, int? Folhas = null);

/// <summary>O que entra no Livro Contábil Completo (a ordem no PDF é a da declaração) e quais assinaturas fecham o livro.</summary>
public sealed record OpcoesLivro(bool TermoAbertura = true, bool Diario = true, bool Razao = true, bool Balancete = true, bool Balanco = true,
                                 bool Dre = true, bool Dmpl = true, bool Dlpa = true, bool TermoEncerramento = true,
                                 OpcoesAssinatura? Assinaturas = null)
{
    public bool TemTermos => TermoAbertura || TermoEncerramento;
    public bool TemConteudo => Diario || Razao || Balancete || Balanco || Dre || Dmpl || Dlpa;

    /// <summary>Itens com conteúdo, por extenso (vai no texto dos termos).</summary>
    public string Descricao()
    {
        var itens = new List<string>();
        if (Diario) itens.Add("Diário");
        if (Razao) itens.Add("Razão");
        if (Balancete) itens.Add("Balancete de Verificação");
        if (Balanco) itens.Add("Balanço Patrimonial");
        if (Dre) itens.Add("DRE");
        if (Dmpl) itens.Add("DMPL");
        if (Dlpa) itens.Add("DLPA");
        return itens.Count switch { 0 => "", 1 => itens[0], _ => string.Join(", ", itens.Take(itens.Count - 1)) + " e " + itens[^1] };
    }
}

/// <summary>Qual dos termos emitir isoladamente.</summary>
public enum TipoTermo { Abertura, Encerramento }

/// <summary>
/// Livros e relatórios em PDF. O Livro Diário e o Livro Contábil Completo seguem a ITG 2000: escrituração em ordem
/// cronológica, folhas numeradas sequencialmente e termos de abertura e de encerramento com identificação da entidade e
/// assinaturas do representante legal e do contador. Livro em papel ou PDF não substitui a ECD para quem é obrigado a ela.
/// </summary>
public sealed class ServicoLivros(ServicoRelatorios relatorios, ServicoDemonstracoes demonstracoes, IConsultasContabeis consultas,
                                  IRepositorioEmpresa empresas, Autorizador autorizador, IGeradorPdf gerador, ServicoAssinaturas assinaturas)
{
    public const string NomeDiario = "LIVRO DIÁRIO";
    public const string NomeLivroContabil = "LIVRO CONTÁBIL";

    private static List<string> ValidarTermos(DadosTermos termos)
    {
        var erros = new List<string>();
        if (termos.NumeroLivro < 1) erros.Add("Informe o número de ordem do livro.");
        if (string.IsNullOrWhiteSpace(termos.RepresentanteNome)) erros.Add("Informe o representante legal (assina os termos).");
        if (string.IsNullOrWhiteSpace(termos.ContadorNome) || string.IsNullOrWhiteSpace(termos.ContadorCrc)) erros.Add("Informe o contador e o CRC.");
        return erros;
    }

    private static string[] AssinaturasTermos(DadosTermos termos) =>
    [
        $"{termos.RepresentanteNome.Trim()}\nRepresentante legal{(string.IsNullOrWhiteSpace(termos.RepresentanteCpf) ? "" : " — CPF " + termos.RepresentanteCpf.Trim())}",
        $"{termos.ContadorNome.Trim()}\nContador — CRC {termos.ContadorCrc.Trim()}",
    ];

    /// <summary>
    /// Termo de abertura ou de encerramento (ITG 2000, itens 8 a 12). <c>{folhas}</c> é substituído pelo total de folhas do
    /// livro na montagem do PDF (ou pelo <see cref="TermoIsoladoAsync"/>, nos termos emitidos à parte).
    /// </summary>
    private static Termo MontarTermo(TipoTermo tipo, EmpresaDetalhe e, DadosTermos termos, string nomeLivro, string complemento, DateOnly inicio, DateOnly fim)
    {
        var periodo = $"{inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}";
        var identificacao = IdentificacaoEmpresa(e, termos.DataArquivamentoAtos);
        var local = Local(e);
        return tipo == TipoTermo.Abertura
            ? new Termo("TERMO DE ABERTURA",
                $"Contém este livro {{folhas}} folhas, numeradas tipograficamente de 1 a {{folhas}}, e servirá de {nomeLivro} Nº {termos.NumeroLivro}{complemento} " +
                $"para o registro das operações do período de {periodo} da entidade:\n\n{identificacao}\n\n{local}, {inicio:dd/MM/yyyy}.", AssinaturasTermos(termos))
            : new Termo("TERMO DE ENCERRAMENTO",
                $"Contém este livro {{folhas}} folhas, numeradas tipograficamente de 1 a {{folhas}}, e serviu de {nomeLivro} Nº {termos.NumeroLivro}{complemento} " +
                $"para o registro das operações do período de {periodo} da entidade:\n\n{identificacao}\n\n{local}, {fim:dd/MM/yyyy}.", AssinaturasTermos(termos));
    }

    public async Task<Resultado<ArquivoGerado>> DiarioAsync(long empresaId, DateOnly inicio, DateOnly fim, DadosTermos termos, long usuarioId,
                                                           CancellationToken ct = default)
    {
        var erros = ValidarTermos(termos);
        if (erros.Count > 0) return Resultado<ArquivoGerado>.Falha(erros);

        var blocos = await BlocosDiarioAsync(empresaId, inicio, fim, usuarioId, ct);
        if (!blocos.Sucesso) return Resultado<ArquivoGerado>.Falha(blocos.Erros);
        var e = await empresas.ObterAsync(empresaId, ct);
        if (e is null) return Resultado<ArquivoGerado>.Falha("Empresa não encontrada.");

        return Resultado<ArquivoGerado>.Ok(gerador.Gerar(new DocumentoRelatorio(
            $"{NomeDiario} Nº {termos.NumeroLivro}",
            [$"{e.RazaoSocial} — CNPJ {FormatarCnpj(e.Cnpj)}", $"Período: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}"],
            blocos.Valor,
            Paisagem: true,
            TermoAbertura: MontarTermo(TipoTermo.Abertura, e, termos, NomeDiario, "", inicio, fim),
            TermoEncerramento: MontarTermo(TipoTermo.Encerramento, e, termos, NomeDiario, "", inicio, fim),
            RotuloPagina: "Folha")));
    }

    /// <summary>
    /// Termo de abertura ou de encerramento emitido isoladamente (o texto é o mesmo do Diário). O número de folhas vem
    /// de <see cref="DadosTermos.Folhas"/>; sem ele, o termo sai com uma linha para preencher à mão.
    /// </summary>
    public async Task<Resultado<ArquivoGerado>> TermoIsoladoAsync(long empresaId, DateOnly inicio, DateOnly fim, DadosTermos termos, TipoTermo tipo,
                                                                 bool livroContabil, long usuarioId, CancellationToken ct = default)
    {
        if (await autorizador.NegarAsync<ArquivoGerado>(usuarioId, empresaId, Permissao.RelatorioDiario, ct) is { } negado) return negado;
        var erros = ValidarTermos(termos);
        if (fim < inicio) erros.Add("Data final anterior à inicial.");
        if (termos.Folhas is < 1) erros.Add("O número de folhas deve ser maior que zero (ou deixe em branco).");
        if (erros.Count > 0) return Resultado<ArquivoGerado>.Falha(erros);
        var e = await empresas.ObterAsync(empresaId, ct);
        if (e is null) return Resultado<ArquivoGerado>.Falha("Empresa não encontrada.");

        var nome = livroContabil ? NomeLivroContabil : NomeDiario;
        var termo = MontarTermo(tipo, e, termos, nome, "", inicio, fim);
        termo = termo with { Texto = termo.Texto.Replace("{folhas}", termos.Folhas?.ToString() ?? "______") };
        return Resultado<ArquivoGerado>.Ok(gerador.Gerar(new DocumentoRelatorio(
            $"{nome} Nº {termos.NumeroLivro}",
            [$"{e.RazaoSocial} — CNPJ {FormatarCnpj(e.Cnpj)}", $"Período: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}"], [],
            Paisagem: false,
            TermoAbertura: tipo == TipoTermo.Abertura ? termo : null,
            TermoEncerramento: tipo == TipoTermo.Encerramento ? termo : null,
            RotuloPagina: "Folha")));
    }

    /// <summary>
    /// Livro Contábil Completo: termo de abertura, Diário, Razão, Balancete, Balanço, DRE, DMPL, DLPA e termo de encerramento,
    /// só o que o usuário marcar, num único PDF com folhas numeradas, mais o bloco de assinaturas opcional ao final do conteúdo.
    /// </summary>
    public async Task<Resultado<ArquivoGerado>> LivroAsync(long empresaId, DateOnly inicio, DateOnly fim, DadosTermos termos, OpcoesLivro opcoes,
                                                          long usuarioId, CancellationToken ct = default)
    {
        var erros = new List<string>();
        if (termos.NumeroLivro < 1) erros.Add("Informe o número do livro.");
        if (fim < inicio) erros.Add("Data final anterior à inicial.");
        if (!opcoes.TemConteudo && !opcoes.TemTermos) erros.Add("Marque ao menos um item para compor o livro.");
        if (opcoes.TemTermos) erros.AddRange(ValidarTermos(termos with { NumeroLivro = 1 }));   // o número já foi conferido acima
        if (erros.Count > 0) return Resultado<ArquivoGerado>.Falha(erros);
        var e = await empresas.ObterAsync(empresaId, ct);
        if (e is null) return Resultado<ArquivoGerado>.Falha("Empresa não encontrada.");

        var blocos = new List<Bloco>();
        // Cada parte do livro começa em página nova (menos a primeira).
        void Parte(IReadOnlyList<Bloco> partes)
        {
            if (partes.Count == 0) return;
            var inicial = blocos.Count > 0;
            blocos.AddRange(partes.Select((b, i) => i == 0 && inicial ? b with { NovaPagina = true } : b));
        }

        if (opcoes.Diario)
        {
            var r = await BlocosDiarioAsync(empresaId, inicio, fim, usuarioId, ct);
            if (!r.Sucesso) return Resultado<ArquivoGerado>.Falha(r.Erros);
            Parte([new Bloco("LIVRO DIÁRIO"), .. r.Valor]);
        }
        if (opcoes.Razao)
        {
            var r = await BlocosRazaoAsync(empresaId, inicio, fim, usuarioId, ct);
            if (!r.Sucesso) return Resultado<ArquivoGerado>.Falha(r.Erros);
            Parte([new Bloco("LIVRO RAZÃO"), .. r.Valor]);
        }
        if (opcoes.Balancete)
        {
            var r = await BlocosBalanceteAsync(empresaId, inicio, fim, usuarioId, ct);
            if (!r.Sucesso) return Resultado<ArquivoGerado>.Falha(r.Erros);
            Parte(r.Valor);
        }
        if (opcoes.Balanco)
        {
            var bp = await demonstracoes.BalancoAsync(empresaId, fim, 3, usuarioId, ct);
            if (!bp.Sucesso) return Resultado<ArquivoGerado>.Falha(bp.Erros);
            Parte([new Bloco($"BALANÇO PATRIMONIAL EM {fim:dd/MM/yyyy} — ATIVO", Colunas: ColunasDemonstracao, Linhas: bp.Valor.Ativo.Select(LinhaDemo).ToArray()),
                   new Bloco("PASSIVO E PATRIMÔNIO LÍQUIDO", Colunas: ColunasDemonstracao, Linhas: bp.Valor.PassivoPl.Select(LinhaDemo).ToArray())]);
        }
        if (opcoes.Dre)
        {
            var dre = await demonstracoes.DreAsync(empresaId, inicio, fim, 3, usuarioId, ct);
            if (!dre.Sucesso) return Resultado<ArquivoGerado>.Falha(dre.Erros);
            Parte([new Bloco($"DEMONSTRAÇÃO DO RESULTADO DO PERÍODO DE {inicio:dd/MM/yyyy} A {fim:dd/MM/yyyy}", Colunas: ColunasDemonstracao,
                             Linhas: dre.Valor.Linhas.Select(LinhaDemo).ToArray())]);
        }
        if (opcoes.Dmpl || opcoes.Dlpa)
        {
            var mut = await demonstracoes.MutacoesAsync(empresaId, inicio, fim, usuarioId, ct);
            if (!mut.Sucesso) return Resultado<ArquivoGerado>.Falha(mut.Erros);
            if (opcoes.Dmpl) Parte(BlocosDe(TabelasMutacoes.Dmpl(mut.Valor)));
            if (opcoes.Dlpa) Parte(BlocosDe(TabelasMutacoes.Dlpa(mut.Valor)));
        }

        IReadOnlyList<string>? blocoAssinaturas = null;
        if (opcoes.Assinaturas is { Alguma: true } sel)
        {
            var a = await assinaturas.MontarAsync(empresaId, sel, ct);
            if (!a.Sucesso) return Resultado<ArquivoGerado>.Falha(a.Erros);
            blocoAssinaturas = a.Valor;
        }

        var complemento = opcoes.TemConteudo ? $" ({opcoes.Descricao()})" : "";
        return Resultado<ArquivoGerado>.Ok(gerador.Gerar(new DocumentoRelatorio(
            $"{NomeLivroContabil} Nº {termos.NumeroLivro}",
            [$"{e.RazaoSocial} — CNPJ {FormatarCnpj(e.Cnpj)}", $"Período: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}"],
            blocos,
            Paisagem: true,
            TermoAbertura: opcoes.TermoAbertura ? MontarTermo(TipoTermo.Abertura, e, termos, NomeLivroContabil, complemento, inicio, fim) : null,
            TermoEncerramento: opcoes.TermoEncerramento ? MontarTermo(TipoTermo.Encerramento, e, termos, NomeLivroContabil, complemento, inicio, fim) : null,
            RotuloPagina: "Folha",
            Assinaturas: blocoAssinaturas)));
    }

    /// <summary>Tabela da tela de relatório como bloco do PDF (com título), na largura da página paisagem.</summary>
    private static IReadOnlyList<Bloco> BlocosDe(TabelaRelatorio t)
    {
        var blocos = ExportacaoRelatorio.ParaPdf("", [], [t], paisagem: true).Blocos.ToList();
        blocos[0] = blocos[0] with { Titulo = t.Titulo };
        return blocos;
    }

    private static readonly Coluna[] ColunasDemonstracao = [new("Descrição", 11.0), new("Conta", 2.5), new("R$", 3.5, Alinhamento.Direita)];

    private static LinhaTabela LinhaDemo(LinhaDemonstracao l) => new([l.Descricao, l.Codigo, Formato.Moeda(l.Valor)],
        Negrito: l.Tipo != TipoLinhaDemonstracao.Conta, Recuo: l.Tipo == TipoLinhaDemonstracao.Total ? 0 : l.Nivel - 1);

    private async Task<Resultado<IReadOnlyList<Bloco>>> BlocosDiarioAsync(long empresaId, DateOnly inicio, DateOnly fim, long usuarioId, CancellationToken ct)
    {
        var diario = await relatorios.DiarioAsync(empresaId, inicio, fim, usuarioId, ct);
        if (!diario.Sucesso) return Resultado<IReadOnlyList<Bloco>>.Falha(diario.Erros);

        var linhas = new List<LinhaTabela>();
        foreach (var g in diario.Valor.GroupBy(l => (l.Numero, l.Data)))
            foreach (var l in g)
                linhas.Add(new LinhaTabela([
                    l.Sequencia == g.First().Sequencia ? l.Data.ToString("dd/MM/yyyy") : "",
                    l.Sequencia == g.First().Sequencia ? l.Numero.ToString() : "",
                    $"{l.CodigoConta} {l.DescricaoConta}",
                    l.DC == IndicadorDC.D ? Formato.Moeda(l.Valor) : "",
                    l.DC == IndicadorDC.C ? Formato.Moeda(l.Valor) : "",
                    l.Historico + (l.Status == StatusLancamento.Estornado ? " [ESTORNADO]" : "")]));
        var totalD = diario.Valor.Where(l => l.DC == IndicadorDC.D).Sum(l => l.Valor);
        var totalC = diario.Valor.Where(l => l.DC == IndicadorDC.C).Sum(l => l.Valor);
        linhas.Add(new LinhaTabela(["", "", "TOTAIS DO PERÍODO", Formato.Moeda(totalD), Formato.Moeda(totalC), ""], Negrito: true));
        return Resultado<IReadOnlyList<Bloco>>.Ok([new Bloco(Colunas: [new("Data", 1.9), new("Nº", 1.2, Alinhamento.Direita), new("Conta", 6.2),
            new("Débito", 2.6, Alinhamento.Direita), new("Crédito", 2.6, Alinhamento.Direita), new("Histórico", 12.2)], Linhas: linhas)]);
    }

    /// <summary>Livro Razão de todas as contas analíticas com saldo ou movimento no período.</summary>
    public async Task<Resultado<ArquivoGerado>> RazaoAsync(long empresaId, DateOnly inicio, DateOnly fim, long usuarioId, CancellationToken ct = default)
    {
        var blocos = await BlocosRazaoAsync(empresaId, inicio, fim, usuarioId, ct);
        if (!blocos.Sucesso) return Resultado<ArquivoGerado>.Falha(blocos.Erros);
        var e = await empresas.ObterAsync(empresaId, ct);
        if (e is null) return Resultado<ArquivoGerado>.Falha("Empresa não encontrada.");
        return Resultado<ArquivoGerado>.Ok(gerador.Gerar(new DocumentoRelatorio("LIVRO RAZÃO",
            [$"{e.RazaoSocial} — CNPJ {FormatarCnpj(e.Cnpj)}", $"Período: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}"], blocos.Valor, Paisagem: true)));
    }

    private async Task<Resultado<IReadOnlyList<Bloco>>> BlocosRazaoAsync(long empresaId, DateOnly inicio, DateOnly fim, long usuarioId, CancellationToken ct)
    {
        if (await autorizador.NegarAsync<IReadOnlyList<Bloco>>(usuarioId, empresaId, Permissao.RelatorioRazao, ct) is { } negado) return negado;
        var plano = await consultas.ObterPlanoAsync(empresaId, ct);
        var movimentos = (await consultas.ObterMovimentosAsync(empresaId, inicio, fim, ct)).ToDictionary(m => m.ContaId);
        var contas = plano.Where(c => c.Analitica && movimentos.TryGetValue(c.Id, out var m) && (m.SaldoAnterior != 0 || m.Debitos != 0 || m.Creditos != 0))
                          .OrderBy(c => c.Codigo, StringComparer.Ordinal).ToArray();

        var blocos = new List<Bloco>();
        foreach (var c in contas)
        {
            var r = await relatorios.RazaoAsync(empresaId, c.Codigo, inicio, fim, usuarioId, ct);
            if (!r.Sucesso) return Resultado<IReadOnlyList<Bloco>>.Falha(r.Erros);
            var linhas = new List<LinhaTabela> { new(["", "", "Saldo anterior", "", "", LinhaBalancete.Formatar(r.Valor.SaldoAnterior)], Negrito: true) };
            linhas.AddRange(r.Valor.Linhas.Select(l => new LinhaTabela([
                l.Data.ToString("dd/MM/yyyy"), l.Numero.ToString(), l.Historico + (l.Estornado ? " [ESTORNADO]" : ""),
                l.DC == IndicadorDC.D ? Formato.Moeda(l.Valor) : "", l.DC == IndicadorDC.C ? Formato.Moeda(l.Valor) : "",
                LinhaBalancete.Formatar(l.SaldoAcumulado)])));
            linhas.Add(new LinhaTabela(["", "", "Saldo final", "", "", LinhaBalancete.Formatar(r.Valor.SaldoFinal)], Negrito: true));
            blocos.Add(new Bloco($"{c.Codigo} — {c.Descricao}", Colunas: ColunasRazao, Linhas: linhas));
        }
        if (blocos.Count == 0) blocos.Add(new Bloco(Texto: "Nenhuma conta com saldo ou movimento no período."));
        return Resultado<IReadOnlyList<Bloco>>.Ok(blocos);
    }

    private static readonly Coluna[] ColunasRazao =
        [new("Data", 2.0), new("Nº", 1.3, Alinhamento.Direita), new("Histórico", 12.5), new("Débito", 2.8, Alinhamento.Direita),
         new("Crédito", 2.8, Alinhamento.Direita), new("Saldo", 3.2, Alinhamento.Direita)];

    public async Task<Resultado<ArquivoGerado>> BalanceteAsync(long empresaId, DateOnly inicio, DateOnly fim, long usuarioId, CancellationToken ct = default)
    {
        var blocos = await BlocosBalanceteAsync(empresaId, inicio, fim, usuarioId, ct);
        if (!blocos.Sucesso) return Resultado<ArquivoGerado>.Falha(blocos.Erros);
        var e = await empresas.ObterAsync(empresaId, ct);
        if (e is null) return Resultado<ArquivoGerado>.Falha("Empresa não encontrada.");
        return Resultado<ArquivoGerado>.Ok(gerador.Gerar(new DocumentoRelatorio("BALANCETE DE VERIFICAÇÃO",
            [$"{e.RazaoSocial} — CNPJ {FormatarCnpj(e.Cnpj)}", $"Período: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}"], blocos.Valor, Paisagem: true)));
    }

    private async Task<Resultado<IReadOnlyList<Bloco>>> BlocosBalanceteAsync(long empresaId, DateOnly inicio, DateOnly fim, long usuarioId, CancellationToken ct)
    {
        var bal = await relatorios.BalanceteAsync(empresaId, inicio, fim, usuarioId, ct: ct);
        if (!bal.Sucesso) return Resultado<IReadOnlyList<Bloco>>.Falha(bal.Erros);
        var linhas = bal.Valor.Select(l => new LinhaTabela([l.Codigo, l.Descricao, LinhaBalancete.Formatar(l.SaldoAnterior),
            l.Debitos == 0 ? "" : Formato.Moeda(l.Debitos), l.Creditos == 0 ? "" : Formato.Moeda(l.Creditos), LinhaBalancete.Formatar(l.SaldoFinal)],
            Negrito: !l.Analitica, Recuo: l.Nivel - 1)).ToList();
        var analiticas = bal.Valor.Where(l => l.Analitica).ToArray();
        linhas.Add(new LinhaTabela(["", "TOTAIS (contas analíticas)", "", Formato.Moeda(analiticas.Sum(l => l.Debitos)),
                                    Formato.Moeda(analiticas.Sum(l => l.Creditos)), ""], Negrito: true));
        return Resultado<IReadOnlyList<Bloco>>.Ok([new Bloco("BALANCETE DE VERIFICAÇÃO", Colunas: [new("Conta", 2.8), new("Descrição", 7.0),
            new("Saldo anterior", 3.3, Alinhamento.Direita), new("Débitos", 3.0, Alinhamento.Direita), new("Créditos", 3.0, Alinhamento.Direita),
            new("Saldo final", 3.3, Alinhamento.Direita)], Linhas: linhas)]);
    }

    /// <summary>Balanço Patrimonial na data final e DRE do período, num único PDF, com assinaturas.</summary>
    public async Task<Resultado<ArquivoGerado>> DemonstracoesAsync(long empresaId, DateOnly inicio, DateOnly fim, short nivel, string? contador,
                                                                  string? representante, long usuarioId, CancellationToken ct = default)
    {
        var bp = await demonstracoes.BalancoAsync(empresaId, fim, nivel, usuarioId, ct);
        if (!bp.Sucesso) return Resultado<ArquivoGerado>.Falha(bp.Erros);
        var dre = await demonstracoes.DreAsync(empresaId, inicio, fim, nivel, usuarioId, ct);
        if (!dre.Sucesso) return Resultado<ArquivoGerado>.Falha(dre.Erros);
        var e = await empresas.ObterAsync(empresaId, ct);
        if (e is null) return Resultado<ArquivoGerado>.Falha("Empresa não encontrada.");

        var assinaturasTexto = string.Join("\n\n\n", new[] { representante, contador }.Where(s => !string.IsNullOrWhiteSpace(s))
                                                   .Select(s => "_______________________________________\n" + s));

        return Resultado<ArquivoGerado>.Ok(gerador.Gerar(new DocumentoRelatorio("DEMONSTRAÇÕES CONTÁBEIS",
            [$"{e.RazaoSocial} — CNPJ {FormatarCnpj(e.Cnpj)}"],
            [
                new Bloco($"BALANÇO PATRIMONIAL EM {fim:dd/MM/yyyy} — ATIVO", Colunas: ColunasDemonstracao, Linhas: bp.Valor.Ativo.Select(LinhaDemo).ToArray()),
                new Bloco("PASSIVO E PATRIMÔNIO LÍQUIDO", Colunas: ColunasDemonstracao, Linhas: bp.Valor.PassivoPl.Select(LinhaDemo).ToArray()),
                new Bloco($"DEMONSTRAÇÃO DO RESULTADO DO PERÍODO DE {inicio:dd/MM/yyyy} A {fim:dd/MM/yyyy}", Colunas: ColunasDemonstracao,
                          Linhas: dre.Valor.Linhas.Select(LinhaDemo).ToArray(), NovaPagina: true),
                new Bloco(Texto: assinaturasTexto.Length == 0 ? null : "\n\n" + assinaturasTexto),
            ])));
    }

    private static string IdentificacaoEmpresa(EmpresaDetalhe e, string? dataArquivamento)
    {
        var linhas = new List<string> { e.RazaoSocial };
        linhas.Add($"CNPJ: {FormatarCnpj(e.Cnpj)}" + (string.IsNullOrWhiteSpace(e.Nire) ? "" :
            $" — NIRE: {e.Nire}" + (string.IsNullOrWhiteSpace(dataArquivamento) ? "" : $", atos constitutivos arquivados em {dataArquivamento}")));
        var endereco = string.Join(", ", new[] { e.Logradouro, e.Numero, e.Complemento, e.Bairro }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var cidade = $"{(string.IsNullOrWhiteSpace(e.Municipio) ? $"município IBGE {e.CodMunicipioIbge}" : e.Municipio)}/{e.Uf}" +
                     (string.IsNullOrWhiteSpace(e.Cep) ? "" : $" — CEP {e.Cep}");
        linhas.Add(endereco.Length > 0 ? $"Endereço: {endereco} — {cidade}" : $"Sede: {cidade}");
        return string.Join("\n", linhas);
    }

    private static string Local(EmpresaDetalhe e) => $"{(string.IsNullOrWhiteSpace(e.Municipio) ? e.Uf : e.Municipio + "/" + e.Uf)}";

    public static string FormatarCnpj(string c) => c.Length == 14 ? $"{c[..2]}.{c[2..5]}.{c[5..8]}/{c[8..12]}-{c[12..]}" : c;
}
