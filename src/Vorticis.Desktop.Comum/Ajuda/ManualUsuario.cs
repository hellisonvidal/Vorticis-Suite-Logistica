using System.Reflection;
using System.Text;

namespace Vorticis.Desktop.Comum.Ajuda;

/// <summary>Seção do manual: o arquivo inteiro (<see cref="Ancora"/> vazia, nível 1) ou uma seção "##" dele.</summary>
public sealed record TopicoManual(string Arquivo, string Ancora, string Titulo, int Nivel, IReadOnlyList<Bloco> Blocos)
{
    public string Id => Ancora.Length == 0 ? Arquivo : $"{Arquivo}#{Ancora}";
    public string TextoBusca { get; } = MarkdownSimples.Simplificar(Titulo + " " + string.Join(' ', Blocos.Select(MarkdownSimples.TextoPlano)));
}

/// <summary>Arquivo do manual. <see cref="Area"/> é "Comum" ou o nome curto do módulo (Contábil, Fiscal, Folha).</summary>
public sealed record ArquivoManual(string Nome, string Titulo, string Area, IReadOnlyList<Bloco> Blocos, IReadOnlyList<TopicoManual> Topicos)
{
    public string Raiz => Path.GetFileNameWithoutExtension(Nome);
}

/// <summary>
/// Manual do usuário embutido (docs/manual/*.md). O arquivo mapa.txt liga a chave de cada tela do sistema ao tópico
/// (arquivo.md#seção) e é a base do F1; um teste garante que toda tela registrada tenha tópico.
/// </summary>
public static class ManualUsuario
{
    private const string Prefixo = "Manual/";

    /// <summary>Área (módulo) de cada arquivo; os demais são comuns a todos os módulos.</summary>
    private static readonly Dictionary<string, string> AreaPorArquivo = new()
    {
        ["03-contabil.md"] = "Contábil",
        ["04-auditoria.md"] = "Contábil",
        ["05-fiscal.md"] = "Fiscal",
    };

    private static readonly Lazy<IReadOnlyList<ArquivoManual>> _arquivos = new(Carregar);
    private static readonly Lazy<IReadOnlyDictionary<string, string>> _mapa = new(CarregarMapa);

    /// <summary>Todos os arquivos do manual, com o índice (index.md) primeiro.</summary>
    public static IReadOnlyList<ArquivoManual> Arquivos => _arquivos.Value;

    /// <summary>Chave da tela → "arquivo.md#seção".</summary>
    public static IReadOnlyDictionary<string, string> Mapa => _mapa.Value;

    public static ArquivoManual? Arquivo(string nome) => Arquivos.FirstOrDefault(a => a.Nome == nome);

    /// <summary>Tópico da tela (ou nulo se a tela não tem tópico ou ele não existe).</summary>
    public static TopicoManual? TopicoDaTela(string? chave) =>
        chave is null ? null
        : Mapa.TryGetValue(chave, out var destino) ? Resolver(destino, null)
        // no programa que junta módulos, a tela que cada módulo tem a sua vira "Modulo:chave" (ex.: os Parâmetros de cada um)
        : chave.IndexOf(':') is > 0 and var i ? TopicoDaTela(chave[(i + 1)..]) : null;

    /// <summary>Resolve "arquivo.md#seção", "#seção" (no arquivo de <paramref name="atual"/>) ou "arquivo.md".</summary>
    public static TopicoManual? Resolver(string destino, string? atual)
    {
        var (nome, ancora) = Separar(destino);
        if (nome.Length == 0) nome = atual ?? "";
        var arq = Arquivo(nome);
        if (arq is null) return null;
        if (ancora.Length == 0) return arq.Topicos[0];
        return arq.Topicos.FirstOrDefault(t => t.Ancora == ancora)
            ?? SecaoQualquer(arq, ancora);
    }

    /// <summary>Âncoras de títulos "###" não viram tópico próprio: abre a seção "##" que as contém.</summary>
    private static TopicoManual? SecaoQualquer(ArquivoManual arq, string ancora)
    {
        TopicoManual? atual = null;
        foreach (var b in arq.Blocos)
        {
            if (b is BlocoTitulo { Nivel: <= 2 } t) atual = arq.Topicos.FirstOrDefault(x => x.Ancora == t.Ancora && x.Nivel == t.Nivel);
            if (b is BlocoTitulo t3 && t3.Ancora == ancora) return atual;
        }
        return null;
    }

    public static (string Arquivo, string Ancora) Separar(string destino)
    {
        var i = destino.IndexOf('#');
        return i < 0 ? (destino, "") : (destino[..i], destino[(i + 1)..]);
    }

    /// <summary>Arquivos do módulo atual e comuns (ou todos).</summary>
    public static IEnumerable<ArquivoManual> Visiveis(string moduloNomeCurto, bool todos) => Visiveis([moduloNomeCurto], todos);

    /// <summary>Arquivos das áreas dadas (programa que junta vários módulos) e os comuns.</summary>
    public static IEnumerable<ArquivoManual> Visiveis(IReadOnlyList<string> areas, bool todos) =>
        Arquivos.Where(a => todos || a.Area == "Comum" || areas.Contains(a.Area));

    /// <summary>Busca por palavras (sem acentos, qualquer ordem) no título e no texto das seções.</summary>
    public static IReadOnlyList<TopicoManual> Buscar(string termo, IEnumerable<ArquivoManual> arquivos)
    {
        var palavras = MarkdownSimples.Simplificar(termo).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (palavras.Length == 0) return [];
        return [.. arquivos.SelectMany(a => a.Topicos.Where(t => t.Nivel == 2))
            .Where(t => palavras.All(p => t.TextoBusca.Contains(p)))
            .OrderByDescending(t => palavras.All(p => MarkdownSimples.Simplificar(t.Titulo).Contains(p)))];
    }

    /// <summary>Página HTML única com os arquivos dados (botão "Abrir no navegador").</summary>
    public static string GerarHtml(IEnumerable<ArquivoManual> arquivos)
    {
        var lista = arquivos.ToList();
        var nomes = lista.Select(a => a.Nome).ToHashSet();
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><title>Manual do usuário Vorticis</title><style>")
          .Append("body{font-family:Segoe UI,Arial,sans-serif;max-width:900px;margin:24px auto;padding:0 16px;color:#1f2937;line-height:1.5}")
          .Append("h1{color:#1F3A5F;border-bottom:2px solid #1F3A5F;padding-bottom:4px;margin-top:48px}h2{color:#1F3A5F;margin-top:32px}")
          .Append("table{border-collapse:collapse;margin:8px 0}th,td{border:1px solid #c8d3e6;padding:4px 8px;text-align:left;vertical-align:top}th{background:#eef3fa}")
          .Append("code{background:#eef1f5;padding:0 3px;border-radius:3px}a{color:#1b5fbf}</style></head><body>\n");
        foreach (var a in lista)
        {
            string Destino(string d)
            {
                if (d.Contains("://")) return d;
                var (n, anc) = Separar(d);
                if (n.Length == 0) n = a.Nome;
                return nomes.Contains(n) ? "#" + IdHtml(n, anc) : d;
            }
            sb.Append($"<section id=\"{Path.GetFileNameWithoutExtension(a.Nome)}\">\n")
              .Append(MarkdownSimples.ParaHtml(a.Blocos, anc => IdHtml(a.Nome, anc), Destino))
              .Append("</section>\n");
        }
        return sb.Append("</body></html>").ToString();
    }

    /// <summary>Id HTML de uma seção: arquivo e âncora (a âncora vazia aponta o início do arquivo).</summary>
    public static string IdHtml(string arquivo, string ancora) =>
        ancora.Length == 0 ? Path.GetFileNameWithoutExtension(arquivo) : $"{Path.GetFileNameWithoutExtension(arquivo)}--{ancora}";

    // ------------------------------------------------------------------ carga dos recursos embutidos

    private static string? LerRecurso(string nome)
    {
        var asm = typeof(ManualUsuario).Assembly;
        using var s = asm.GetManifestResourceStream(Prefixo + nome);
        if (s is null) return null;
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static IReadOnlyList<ArquivoManual> Carregar()
    {
        var nomes = typeof(ManualUsuario).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefixo, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal))
            .Select(n => n[Prefixo.Length..])
            .OrderBy(n => n == "index.md" ? "" : n, StringComparer.Ordinal);
        var res = new List<ArquivoManual>();
        foreach (var nome in nomes)
        {
            var blocos = MarkdownSimples.Analisar(LerRecurso(nome) ?? "");
            var titulo = blocos.OfType<BlocoTitulo>().FirstOrDefault(t => t.Nivel == 1) is { } h ? MarkdownSimples.TextoPlano(h.Trechos) : nome;
            var topicos = new List<TopicoManual> { new(nome, "", titulo, 1, blocos) };
            for (var i = 0; i < blocos.Count; i++)
            {
                if (blocos[i] is not BlocoTitulo { Nivel: 2 } t) continue;
                var fim = i + 1;
                while (fim < blocos.Count && blocos[fim] is not BlocoTitulo { Nivel: <= 2 }) fim++;
                topicos.Add(new TopicoManual(nome, t.Ancora, MarkdownSimples.TextoPlano(t.Trechos), 2, [.. blocos.Skip(i).Take(fim - i)]));
            }
            res.Add(new ArquivoManual(nome, titulo, AreaPorArquivo.GetValueOrDefault(nome, "Comum"), blocos, topicos));
        }
        return res;
    }

    private static IReadOnlyDictionary<string, string> CarregarMapa()
    {
        var mapa = new Dictionary<string, string>();
        foreach (var linha in (LerRecurso("mapa.txt") ?? "").Split('\n'))
        {
            var l = linha.Trim();
            if (l.Length == 0 || l[0] == ';') continue;
            var i = l.IndexOf('=');
            if (i > 0) mapa[l[..i].Trim()] = l[(i + 1)..].Trim();
        }
        return mapa;
    }
}
