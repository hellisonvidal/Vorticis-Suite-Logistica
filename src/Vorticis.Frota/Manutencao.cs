namespace Vorticis.Frota;

/// <summary>Peça ou serviço do catálogo de manutenção (valor de referência para orçar as ordens de serviço).</summary>
public sealed record ItemManutencao(long Id, int Codigo, string Tipo, string Descricao, string Unidade, decimal? ValorReferencia, bool Ativo = true);

/// <summary>Linha da ordem de serviço: peça ou serviço, do catálogo ou digitado.</summary>
public sealed record ItemOrdem(long Id, long? ItemId, string Tipo, string Descricao, decimal Quantidade, decimal ValorUnitario)
{
    public decimal Total => Math.Round(Quantidade * ValorUnitario, 2);
}

/// <summary>Decisão sobre uma ordem de serviço: aprovada (manual ou automática, até o limite) ou reprovada com motivo.</summary>
public sealed record Autorizacao(long Id, long OrdemId, int NumeroOrdem, string? Placa, DateTime Momento, string? Usuario, string Decisao, decimal Valor, bool Automatica,
                                 string? Observacao);

/// <summary>Nota fiscal de compra de peças ou serviços, de preferência ligada à ordem de serviço que atendeu.</summary>
public sealed record NotaCompra(long Id, long? OrdemId, string Fornecedor, string? Cnpj, string Numero, string Serie, DateOnly Emissao, string? Chave, decimal ValorPecas,
                                decimal ValorServicos, string? Observacao, int? NumeroOrdem = null, string? Placa = null)
{
    public decimal Total => ValorPecas + ValorServicos;
}

/// <summary>Ação sobre a ordem de serviço (botões da tela Ordens de serviço e da tela Autorização de serviços).</summary>
public enum AcaoOrdem { SolicitarAutorizacao, Aprovar, Reprovar, Concluir, Cancelar, Reabrir }

/// <summary>Regras da manutenção: fluxo da ordem de serviço, autorização e notas de compra.</summary>
public static partial class RegrasFrota
{
    public static readonly IReadOnlyList<string> TiposItem = ["PECA", "SERVICO"];
    public static readonly IReadOnlyList<string> SituacoesOrdem = ["ABERTA", "AGUARDANDO", "APROVADA", "REPROVADA", "CONCLUIDA", "CANCELADA"];

    public static string RotuloManutencao(string codigo) => codigo switch
    {
        "PECA" => "Peça", "SERVICO" => "Serviço", "AGUARDANDO" => "Aguardando autorização", "APROVADA" => "Autorizada", "REPROVADA" => "Reprovada",
        "CANCELADA" => "Cancelada", _ => Rotulo(codigo),
    };

    /// <summary>Itens e valores só mudam com a ordem aberta (ou reprovada, para refazer o orçamento).</summary>
    public static bool OrdemEditavel(string situacao) => situacao is "ABERTA" or "REPROVADA";

    /// <summary>Peças e serviços somados pelos itens.</summary>
    public static (decimal Pecas, decimal Servicos) Totais(IEnumerable<ItemOrdem> itens)
    {
        var lista = itens.ToList();
        return (lista.Where(i => i.Tipo == "PECA").Sum(i => i.Total), lista.Where(i => i.Tipo == "SERVICO").Sum(i => i.Total));
    }

    /// <summary>Até o limite da configuração a ordem é autorizada sozinha; acima dele, alguém com a função "Autorizar ordens" decide.</summary>
    public static bool ExigeAutorizacao(decimal total, decimal limite) => total > limite;

    /// <summary>Situação depois da ação, ou o motivo de não poder (null na situação = ação recusada).</summary>
    public static (string? Nova, string? Erro) Transicao(string atual, AcaoOrdem acao) => (acao, atual) switch
    {
        (AcaoOrdem.SolicitarAutorizacao, "ABERTA" or "REPROVADA") => ("AGUARDANDO", null),
        (AcaoOrdem.SolicitarAutorizacao, _) => (null, "Só uma ordem aberta (ou reprovada) pode ser enviada para autorização."),
        (AcaoOrdem.Aprovar, "AGUARDANDO") => ("APROVADA", null),
        (AcaoOrdem.Reprovar, "AGUARDANDO") => ("REPROVADA", null),
        (AcaoOrdem.Aprovar or AcaoOrdem.Reprovar, _) => (null, "Só uma ordem aguardando autorização pode ser aprovada ou reprovada."),
        (AcaoOrdem.Concluir, "APROVADA") => ("CONCLUIDA", null),
        (AcaoOrdem.Concluir, "ABERTA" or "AGUARDANDO" or "REPROVADA") => (null, "A ordem precisa estar autorizada para ser concluída."),
        (AcaoOrdem.Cancelar, "CONCLUIDA" or "CANCELADA") => (null, "Ordem concluída ou cancelada não pode ser cancelada."),
        (AcaoOrdem.Cancelar, _) => ("CANCELADA", null),
        (AcaoOrdem.Reabrir, "AGUARDANDO" or "APROVADA" or "REPROVADA" or "CANCELADA") => ("ABERTA", null),
        (AcaoOrdem.Reabrir, "ABERTA") => (null, "A ordem já está aberta."),
        _ => (null, "Ordem concluída não pode ser alterada."),
    };

    public static IReadOnlyList<string> Validar(ItemOrdem i)
    {
        var erros = new List<string>();
        if (!TiposItem.Contains(i.Tipo)) erros.Add("Escolha se o item é peça ou serviço.");
        if (string.IsNullOrWhiteSpace(i.Descricao)) erros.Add("Descreva a peça ou o serviço.");
        if (i.Quantidade <= 0) erros.Add($"Quantidade de \"{i.Descricao}\" deve ser maior que zero.");
        if (i.ValorUnitario < 0) erros.Add($"Valor de \"{i.Descricao}\" não pode ser negativo.");
        return erros;
    }

    public static IReadOnlyList<string> Validar(NotaCompra n)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(n.Fornecedor)) erros.Add("Informe o fornecedor.");
        if (string.IsNullOrWhiteSpace(n.Numero)) erros.Add("Informe o número da nota.");
        if (n.Cnpj is { Length: > 0 } c && !Vorticis.Domain.Cadastros.Cnpj.Valido(c) && !Vorticis.Domain.Cadastros.Cpf.Valido(c)) erros.Add("CNPJ/CPF do fornecedor inválido.");
        if (n.ValorPecas < 0 || n.ValorServicos < 0) erros.Add("Valores não podem ser negativos.");
        else if (n.Total <= 0) erros.Add("Informe o valor das peças e/ou dos serviços.");
        if (n.Chave is { Length: > 0 } k)
        {
            if (!ChaveValida(k)) erros.Add("Chave de acesso inválida (44 dígitos; confira o dígito verificador).");
            else if (n.Cnpj is { Length: 14 } cnpj && k[6..20] != cnpj) erros.Add("A chave de acesso não é do CNPJ do fornecedor informado.");
        }
        return erros;
    }

    /// <summary>Chave de acesso da NF-e/NFS-e nacional: 44 dígitos, o último confere os 43 anteriores (módulo 11, pesos 2 a 9).</summary>
    public static bool ChaveValida(string? chave)
    {
        if (chave is not { Length: 44 } || !chave.All(char.IsAsciiDigit)) return false;
        int soma = 0, peso = 2;
        for (var i = 42; i >= 0; i--)
        {
            soma += (chave[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }
        var dv = 11 - soma % 11;
        return (dv >= 10 ? 0 : dv) == chave[43] - '0';
    }

    /// <summary>Dias em que a ordem ficou (ou está) aberta: da abertura até a conclusão, ou até hoje.</summary>
    public static int DiasEmAberto(OrdemServico o, DateOnly hoje) => (o.Conclusao ?? (o.Situacao == "CANCELADA" ? o.Data : hoje)).DayNumber - o.Data.DayNumber;
}
