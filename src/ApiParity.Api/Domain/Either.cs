namespace ApiParity.Api.Domain;

/// <summary>
/// A deliberately tiny result type: either a <typeparamref name="TLeft"/> error or a
/// <typeparamref name="TRight"/> value, never both.
/// <para>
/// This exists so the service layer can report expected failures without exceptions, and so the
/// three API styles each have something non-trivial to translate into an HTTP response. It is not
/// a functional-programming library and is not trying to be. If you want the real thing, reach
/// for LanguageExt or OneOf. Thirty lines here keeps the repository's single dependency story
/// simple and keeps the comparison about transport layers rather than about a library.
/// </para>
/// <para>
/// It is a class rather than a struct so an uninitialized <c>default</c> cannot exist. That costs
/// one small allocation per call, charged identically to all three styles, so it does not move the
/// benchmark comparison.
/// </para>
/// </summary>
public sealed class Either<TLeft, TRight>
{
    private readonly TLeft _left;
    private readonly TRight _right;

    private Either(TLeft left, TRight right, bool isRight)
    {
        _left = left;
        _right = right;
        IsRight = isRight;
    }

    public bool IsRight { get; }

    public bool IsLeft => !IsRight;

    public static implicit operator Either<TLeft, TRight>(TLeft left) => new(left, default!, isRight: false);

    public static implicit operator Either<TLeft, TRight>(TRight right) => new(default!, right, isRight: true);

    /// <summary>
    /// Collapses both sides into a single value. The parameters are capitalised so call sites read
    /// as <c>Match(Right: ..., Left: ...)</c>, which is the clearest way to show both branches are
    /// handled. The compiler will not let you forget one.
    /// </summary>
    public TResult Match<TResult>(Func<TRight, TResult> Right, Func<TLeft, TResult> Left) =>
        IsRight ? Right(_right) : Left(_left);
}
