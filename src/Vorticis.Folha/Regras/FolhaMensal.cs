using Vorticis.Domain.Comum;

namespace Vorticis.Folha.Regras;

/// <summary>Contexto de um cálculo: competência, tabelas vigentes, cadastro de rubricas e o que já foi tributado no mês.</summary>
public sealed record ContextoCalculo(DateOnly Competencia, TabelasLegais Tabelas, IReadOnlyDictionary<string, RubricaCalc> Rubricas,
                                     AcumuladoMes Acumulado, IReadOnlyList<DateOnly>? FeriadosExtras = null)
{
    public RubricaCalc Rubrica(string codigo) =>
        Rubricas.TryGetValue(codigo, out var r) ? r
        : RubricasPadrao.Catalogo.TryGetValue(codigo, out var p) ? p
        : throw new ArgumentException($"Rubrica {codigo} não cadastrada.");
}

/// <summary>Erro de dados do cálculo (evento sem valor, rubrica inexistente...) com mensagem para o usuário.</summary>
public sealed class ErroCalculoFolhaException(string mensagem) : Exception(mensagem);

/// <summary>
/// Cálculo da folha mensal de um funcionário, com memória por rubrica: salário proporcional, horas extras, adicionais, DSR,
/// faltas, salário-família, INSS progressivo, IRRF (melhor dedução + redução 2026) e FGTS. Regras puras, sem acesso a banco.
/// </summary>
public static class CalculadoraFolhaMensal
{
    public const decimal PericulosidadePercentual = 30m;
    public const decimal AdicionalNoturnoPercentual = 20m;

    public static ResultadoFolha Calcular(FuncionarioCalc f, IReadOnlyList<EventoCalc> eventos, ContextoCalculo ctx)
    {
        var itens = new List<ItemCalculo>();
        var comp = new DateOnly(ctx.Competencia.Year, ctx.Competencia.Month, 1);
        var dias = Calendario.DiasTrabalhadosComerciais(comp, f.Admissao, f.Desligamento);
        if (dias == 0) return new ResultadoFolha([], 0, 0, 0, 0, 0, 0, 0, "", "");

        ItemCalculo Novo(string codigo, decimal referencia, decimal valor, string memoria)
        {
            var r = ctx.Rubrica(codigo);
            return new ItemCalculo(codigo, r.Descricao, r.Tipo, referencia, Dinheiro.Arredondar(valor), memoria, r.IncInss, r.IncIrrf, r.IncFgts, r.Variavel);
        }

        // Eventos agrupados por rubrica (vários lançamentos do mesmo evento se somam)
        var ev = eventos.GroupBy(e => e.RubricaCodigo).ToDictionary(g => g.Key,
            g => (Ref: g.Sum(e => e.Referencia), Valor: g.All(e => e.Valor is null) ? (decimal?)null : g.Sum(e => e.Valor ?? 0)));

        var salMes = f.SalarioMensal;
        var fracao = dias / 30m;

        // Salário
        var salario = Dinheiro.Arredondar(salMes * fracao);
        itens.Add(Novo(RubricasPadrao.Salario, f.Horista ? Math.Round(f.JornadaMensalHoras * fracao, 2) : dias, salario,
            f.Horista ? $"Horista: {Formato.Moeda(f.Salario)}/h x {Formato.Num(f.JornadaMensalHoras)}h x {dias}/30 dias"
                      : $"Salário {Formato.Moeda(salMes)} x {dias}/30 dias"));

        // Adicional de insalubridade (% do salário mínimo) ou de periculosidade (30% do salário): o maior, sem acumular (CLT, art. 193, § 2º)
        var insalubridade = f.InsalubridadePercentual * ctx.Tabelas.SalarioMinimo / 100m;
        var periculosidade = f.Periculosidade ? salMes * PericulosidadePercentual / 100m : 0;
        decimal adicionalMensal = 0;
        if (periculosidade > 0 && periculosidade >= insalubridade)
        {
            adicionalMensal = Dinheiro.Arredondar(periculosidade);
            itens.Add(Novo(RubricasPadrao.Periculosidade, PericulosidadePercentual, adicionalMensal * fracao,
                $"{Formato.Num(PericulosidadePercentual)}% de {Formato.Moeda(salMes)} = {Formato.Moeda(adicionalMensal)} x {dias}/30 dias"));
        }
        else if (insalubridade > 0)
        {
            adicionalMensal = Dinheiro.Arredondar(insalubridade);
            itens.Add(Novo(RubricasPadrao.Insalubridade, f.InsalubridadePercentual, adicionalMensal * fracao,
                $"{Formato.Num(f.InsalubridadePercentual)}% do salário mínimo {Formato.Moeda(ctx.Tabelas.SalarioMinimo)} = {Formato.Moeda(adicionalMensal)} x {dias}/30 dias"));
        }

        // Horas extras: valor-hora sobre salário + adicionais, com 50% ou 100%
        var jornada = f.JornadaMensalHoras <= 0 ? 220m : f.JornadaMensalHoras;
        var valorHoraExtraBase = (salMes + adicionalMensal) / jornada;
        foreach (var (codigo, fator) in new[] { (RubricasPadrao.HoraExtra50, 1.5m), (RubricasPadrao.HoraExtra100, 2.0m) })
        {
            if (!ev.TryGetValue(codigo, out var e) || e.Ref <= 0) continue;
            itens.Add(Novo(codigo, e.Ref, e.Ref * valorHoraExtraBase * fator,
                $"{Formato.Num(e.Ref)}h x {Formato.Moeda(Dinheiro.Arredondar(valorHoraExtraBase))}/h (salário + adicionais ÷ {Formato.Num(jornada)}h) x {Formato.Num(fator)}"));
        }

        // Adicional noturno: sobre o valor da hora do salário base
        if (ev.TryGetValue(RubricasPadrao.AdicionalNoturno, out var noturno) && noturno.Ref > 0)
            itens.Add(Novo(RubricasPadrao.AdicionalNoturno, noturno.Ref, noturno.Ref * f.ValorHora * AdicionalNoturnoPercentual / 100m,
                $"{Formato.Num(noturno.Ref)}h x {Formato.Moeda(Dinheiro.Arredondar(f.ValorHora))}/h x {Formato.Num(AdicionalNoturnoPercentual)}%"));

        // Valores informados: comissões, outros proventos e descontos manuais
        foreach (var codigo in new[] { RubricasPadrao.Comissoes, RubricasPadrao.OutrosProventos, RubricasPadrao.Adiantamento, RubricasPadrao.ValeTransporte,
                                       RubricasPadrao.PensaoAlimenticia, RubricasPadrao.OutrosDescontos })
        {
            if (!ev.TryGetValue(codigo, out var e)) continue;
            if (e.Valor is not { } v || v <= 0) throw new ErroCalculoFolhaException($"{f.Nome}: informe o valor da rubrica {codigo} – {ctx.Rubrica(codigo).Descricao}.");
            itens.Add(Novo(codigo, e.Ref, v, "Valor informado no lançamento de eventos"));
        }

        // Rubricas da empresa fora do padrão: valor informado (ou referência x valor, se só houver a referência não há como calcular)
        foreach (var (codigo, e) in ev.Where(x => !RubricasPadrao.EhPadrao(x.Key)))
        {
            var r = ctx.Rubrica(codigo);
            if (e.Valor is not { } v || v <= 0) throw new ErroCalculoFolhaException($"{f.Nome}: informe o valor da rubrica {codigo} – {r.Descricao}.");
            itens.Add(Novo(codigo, e.Ref, v, "Valor informado no lançamento de eventos"));
        }

        // DSR sobre horas extras, adicional noturno e comissões: base ÷ dias úteis x domingos e feriados
        var (uteis, descanso) = Calendario.DiasUteisEDescanso(comp.Year, comp.Month, ctx.FeriadosExtras);
        var baseDsr = itens.Where(i => i.Codigo is RubricasPadrao.HoraExtra50 or RubricasPadrao.HoraExtra100 or RubricasPadrao.AdicionalNoturno
                                                 or RubricasPadrao.Comissoes).Sum(i => i.Valor);
        if (baseDsr > 0 && uteis > 0)
            itens.Add(Novo(RubricasPadrao.Dsr, descanso, baseDsr / uteis * descanso,
                $"{Formato.Moeda(baseDsr)} ÷ {uteis} dias úteis x {descanso} domingos/feriados"));

        // Faltas e perda do DSR
        if (ev.TryGetValue(RubricasPadrao.Faltas, out var faltas) && faltas.Ref > 0)
        {
            itens.Add(Novo(RubricasPadrao.Faltas, faltas.Ref, faltas.Ref * salMes / 30m, $"{Formato.Num(faltas.Ref)} dia(s) x {Formato.Moeda(Dinheiro.Arredondar(salMes / 30m))}/dia"));
            var diasDsr = ev.TryGetValue(RubricasPadrao.DsrFaltas, out var manual) && manual.Ref > 0
                ? manual.Ref
                : Math.Min(faltas.Ref, descanso);
            var origem = manual.Ref > 0 ? "informado" : "1 DSR por falta injustificada (limitado aos DSR do mês); informe a rubrica 2004 para ajustar";
            if (diasDsr > 0)
                itens.Add(Novo(RubricasPadrao.DsrFaltas, diasDsr, diasDsr * salMes / 30m, $"{Formato.Num(diasDsr)} DSR x {Formato.Moeda(Dinheiro.Arredondar(salMes / 30m))}/dia ({origem})"));
        }
        else if (ev.TryGetValue(RubricasPadrao.DsrFaltas, out var soDsr) && soDsr.Ref > 0)
            itens.Add(Novo(RubricasPadrao.DsrFaltas, soDsr.Ref, soDsr.Ref * salMes / 30m, $"{Formato.Num(soDsr.Ref)} DSR x {Formato.Moeda(Dinheiro.Arredondar(salMes / 30m))}/dia (informado)"));

        // Salário-família: remuneração do mês até o limite, por dependente elegível
        var remuneracao = Soma(itens, i => i.IncInss == "11");
        var cotas = DependentesSalarioFamilia(f, comp);
        var sf = ctx.Tabelas.SalarioFamilia;
        decimal salarioFamilia = 0;
        if (cotas > 0 && remuneracao <= sf.LimiteRemuneracao)
        {
            salarioFamilia = Dinheiro.Arredondar(cotas * sf.Cota * fracao);
            itens.Add(Novo(RubricasPadrao.SalarioFamilia, cotas, salarioFamilia,
                $"{cotas} cota(s) x {Formato.Moeda(sf.Cota)} (remuneração {Formato.Moeda(remuneracao)} até {Formato.Moeda(sf.LimiteRemuneracao)})"
                + (dias < 30 ? $" x {dias}/30 dias" : "")));
        }

        return Fechar(f, itens, ctx, Novo);
    }

    /// <summary>Completa o cálculo: bases, INSS, IRRF, FGTS e itens informativos. Compartilhado com férias, 13º e rescisão.</summary>
    internal static ResultadoFolha Fechar(FuncionarioCalc f, List<ItemCalculo> itens, ContextoCalculo ctx,
                                          Func<string, decimal, decimal, string, ItemCalculo> novo)
    {
        var baseInss = Soma(itens, i => i.IncInss == "11");
        var rendimento = Soma(itens, i => i.IncIrrf == "01");
        var baseFgts = Soma(itens, i => i.IncFgts is "11" or "12");
        var pensao = itens.Where(i => i.IncIrrf == "31").Sum(i => i.Valor);

        var acum = ctx.Acumulado;
        decimal inss = 0;
        var memInss = "Sem base de cálculo do INSS.";
        if (!f.Estagiario && baseInss + acum.BaseInss > 0)
        {
            var total = CalculoInss.Calcular(baseInss + acum.BaseInss, ctx.Tabelas.Inss, out memInss);
            inss = Math.Max(0, total - acum.Inss);
            if (acum.Inss > 0) memInss += $"; já retido no mês (férias) {Formato.Moeda(acum.Inss)}; a reter {Formato.Moeda(inss)}";
        }

        decimal irrf = 0;
        var memIrrf = "Sem rendimento tributável.";
        if (rendimento + acum.Rendimento > 0)
        {
            var r = CalculoIrrf.Calcular(rendimento + acum.Rendimento, inss + acum.Inss, f.DependentesIrrf, pensao, ctx.Tabelas.Irrf);
            irrf = Math.Max(0, r.Imposto - acum.IrrfRetido);
            memIrrf = r.Memoria + (acum.IrrfRetido > 0 ? $"; já retido no mês (férias) {Formato.Moeda(acum.IrrfRetido)}; a reter {Formato.Moeda(irrf)}" : "");
        }

        var aliqFgts = CalculoEncargos.AliquotaFgts(f, ctx.Tabelas.Fgts);
        var fgts = Dinheiro.Arredondar(baseFgts * aliqFgts / 100m);

        if (inss > 0) itens.Add(novo(RubricasPadrao.Inss, 0, inss, memInss));
        if (irrf > 0) itens.Add(novo(RubricasPadrao.Irrf, 0, irrf, memIrrf));
        itens.Add(novo(RubricasPadrao.BaseInss, 0, baseInss, "Proventos com incidência de INSS menos descontos que reduzem a base"));
        itens.Add(novo(RubricasPadrao.BaseIrrf, 0, rendimento, "Rendimentos tributáveis (antes das deduções)"));
        itens.Add(novo(RubricasPadrao.BaseFgts, 0, baseFgts, "Proventos com incidência de FGTS"));
        itens.Add(novo(RubricasPadrao.ValorFgts, aliqFgts, fgts, $"{Formato.Moeda(baseFgts)} x {Formato.Num(aliqFgts)}%"));

        var salarioFamilia = itens.Where(i => i.Codigo == RubricasPadrao.SalarioFamilia).Sum(i => i.Valor);
        return new ResultadoFolha(itens, baseInss, rendimento, baseFgts, inss, irrf, fgts, salarioFamilia, memInss, memIrrf);
    }

    /// <summary>Soma com sinal: proventos somam e descontos subtraem da base (faltas reduzem as bases).</summary>
    internal static decimal Soma(IEnumerable<ItemCalculo> itens, Func<ItemCalculo, bool> filtro)
        => itens.Where(i => i.Tipo != 'I' && filtro(i)).Sum(i => i.Tipo == 'P' ? i.Valor : -i.Valor);

    /// <summary>
    /// Cotas de salário-família: dependentes marcados, menores de 14 anos (direito até o mês do 14º aniversário) ou incapazes.
    /// </summary>
    public static int DependentesSalarioFamilia(FuncionarioCalc f, DateOnly competencia)
        => f.Dependentes.Count(d => d.DepSalarioFamilia && (d.Incapaz || CompetenciaAniversario(d.Nascimento, 14) >= new DateOnly(competencia.Year, competencia.Month, 1)));

    private static DateOnly CompetenciaAniversario(DateOnly nascimento, int anos)
    {
        var aniv = nascimento.AddYears(anos);
        return new DateOnly(aniv.Year, aniv.Month, 1);
    }
}
