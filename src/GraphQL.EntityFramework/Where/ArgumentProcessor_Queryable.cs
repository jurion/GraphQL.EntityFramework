using GraphQL.EntityFramework.Interfaces;

namespace GraphQL.EntityFramework;

public static partial class ArgumentProcessor
{
    public static IQueryable<TItem> ApplyGraphQlArguments<TItem>(
        this IQueryable<TItem> queryable,
        IResolveFieldContext context,
        List<string>? keyNames,
        bool applyOrder,
        bool omitQueryArguments)
        where TItem : class
    {
        if (omitQueryArguments)
        {
            return queryable;
        }

        if (keyNames is not null)
        {
            if (ArgumentReader.TryReadIds(context, out var idValues))
            {
                var keyName = GetKeyName(keyNames);
                var predicate = ExpressionBuilder<TItem>.BuildIdPredicate(keyName, idValues);
                queryable = queryable.Where(predicate);
            }
        }
        var customWhereService = context.RequestServices?.GetService<ICustomExpressionBuilder<TItem>>();
        var tagService = context.RequestServices?.GetService<ITagsProcessor>();
        if (ArgumentReader.TryReadWhere(context, out var wheres))
        {
            var predicate = ExpressionBuilder<TItem>.BuildPredicate(wheres, customWhereService, context, tagService);
            queryable = queryable.Where(predicate);
        }

        if (!applyOrder)
            return queryable;

        var (orderedItems, order) = Order(queryable, context);
        queryable = orderedItems;

        if (ArgumentReader.TryReadSkip(context, out var skip))
        {
            EnsureOrderForSkip(order, context);

            queryable = queryable.Skip(skip);
        }

        if (ArgumentReader.TryReadTake(context, out var take))
        {
            EnsureOrderForTake(order, context);

            queryable = queryable.Take(take);
        }

        return queryable;
    }

    static (IQueryable<TItem> items, bool order) Order<TItem>(IQueryable<TItem> queryable, IResolveFieldContext context)
    {
        var orderBys = ArgumentReader.ReadOrderBy(context);
        if (orderBys.Count == 0)
        {
            return (queryable, false);
        }
        var customSorting = context.RequestServices?.GetService<ICustomSorting<TItem>>();
        var orderBy = orderBys.First();
        if (!(customSorting?.ApplySort(queryable, orderBy, context, true, out var ordered) ?? false))
        {
            var property = PropertyCache<TItem>.GetProperty(orderBy.Path)
                .Lambda;
            ordered = orderBy.Descending ? queryable.OrderByDescending(property) : queryable.OrderBy(property);
        }

        foreach (var subsequentOrderBy in orderBys.Skip(1))
        {
            if (customSorting?.ApplySort(ordered, orderBy, context, false, out ordered) ?? false)
                continue;
            var subsequentPropertyFunc = PropertyCache<TItem>.GetProperty(subsequentOrderBy.Path).Lambda;
            ordered = subsequentOrderBy.Descending ? ordered.ThenByDescending(subsequentPropertyFunc) : ordered.ThenBy(subsequentPropertyFunc);
        }

        return (ordered, true);
    }
}