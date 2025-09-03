namespace GraphQL.EntityFramework.Interfaces;
public interface ICustomExpressionBuilder<TItem>
{
    public Expression? GetExpression(WhereExpression where, ParameterExpression parameterExpression, IResolveFieldContext? resolveFieldContext);
}