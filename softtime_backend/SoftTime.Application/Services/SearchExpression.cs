using System.Linq.Expressions;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Services;

/// <summary>
/// Construit des prédicats LINQ/EF Core à partir d'un terme de recherche.
/// Tous les mots doivent être présents (ET), chacun pouvant correspondre à l'un des champs (OU).
/// </summary>
public static class SearchExpression
{
    public static Expression<Func<T, bool>>? For<T>(
        string? search,
        params Expression<Func<T, string?>>[] fields)
    {
        var terms = SearchText.RawTerms(search);
        if (terms.Count == 0 || fields.Length == 0) return null;

        var parameter = fields[0].Parameters[0];
        Expression? allTerms = null;

        foreach (var term in terms)
        {
            Expression? anyField = null;
            foreach (var field in fields)
            {
                var body = ReplaceParameter(field.Body, field.Parameters[0], parameter);
                var notNull = Expression.NotEqual(body, Expression.Constant(null, typeof(string)));
                var lowerBody = Expression.Call(body, nameof(string.ToLower), Type.EmptyTypes);
                var contains = Expression.Call(
                    lowerBody,
                    nameof(string.Contains),
                    Type.EmptyTypes,
                    Expression.Constant(term.ToLowerInvariant(), typeof(string)));
                var match = Expression.AndAlso(notNull, contains);
                anyField = anyField is null ? match : Expression.OrElse(anyField, match);
            }

            allTerms = allTerms is null ? anyField : Expression.AndAlso(allTerms, anyField!);
        }

        return Expression.Lambda<Func<T, bool>>(allTerms!, parameter);
    }

    /// <summary>Combine deux prédicats avec un ET, en alignant leurs paramètres respectifs.</summary>
    public static Expression<Func<T, bool>> AndAlso<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        var parameter = left.Parameters[0];
        var rightBody = ReplaceParameter(right.Body, right.Parameters[0], parameter);
        return Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(left.Body, rightBody!),
            parameter);
    }

    /// <summary>Combine deux prédicats avec un OU, en alignant leurs paramètres respectifs.</summary>
    public static Expression<Func<T, bool>> OrElse<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        var parameter = left.Parameters[0];
        var rightBody = ReplaceParameter(right.Body, right.Parameters[0], parameter);
        return Expression.Lambda<Func<T, bool>>(
            Expression.OrElse(left.Body, rightBody!),
            parameter);
    }

    private static Expression ReplaceParameter(
        Expression expression,
        ParameterExpression source,
        ParameterExpression target)
        => new ParameterReplacer(source, target).Visit(expression);

    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _source;
        private readonly ParameterExpression _target;

        public ParameterReplacer(ParameterExpression source, ParameterExpression target)
        {
            _source = source;
            _target = target;
        }

        protected override Expression VisitParameter(ParameterExpression node)
            => node == _source ? _target : base.VisitParameter(node);
    }
}
