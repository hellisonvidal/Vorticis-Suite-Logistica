namespace Vorticis.Domain.Comum;

/// <summary>Resultado de uma operação de negócio: sucesso ou lista de erros legíveis pelo usuário.</summary>
public class Resultado
{
    protected Resultado(IReadOnlyList<string> erros) => Erros = erros;

    public IReadOnlyList<string> Erros { get; }
    public bool Sucesso => Erros.Count == 0;

    public static Resultado Ok() => new([]);
    public static Resultado Falha(params string[] erros) => new(erros);
    public static Resultado Falha(IEnumerable<string> erros) => new(erros.ToArray());
}

public sealed class Resultado<T> : Resultado
{
    private readonly T? _valor;

    private Resultado(T? valor, IReadOnlyList<string> erros) : base(erros) => _valor = valor;

    public T Valor => Sucesso ? _valor! : throw new InvalidOperationException("Resultado com falha não possui valor.");

    public static Resultado<T> Ok(T valor) => new(valor, []);
    public static new Resultado<T> Falha(params string[] erros) => new(default, erros);
    public static new Resultado<T> Falha(IEnumerable<string> erros) => new(default, erros.ToArray());
}

/// <summary>Violação de regra detectada pela última linha de defesa (banco de dados).</summary>
public sealed class ViolacaoRegraException(string mensagem, Exception? interna = null) : Exception(mensagem, interna);
