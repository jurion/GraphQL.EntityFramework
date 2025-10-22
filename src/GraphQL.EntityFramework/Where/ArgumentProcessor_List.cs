using GraphQL.EntityFramework.Interfaces;

namespace GraphQL.EntityFramework;

public static partial class ArgumentProcessor
{
    public static IEnumerable<TItem> ApplyGraphQlArguments<TItem>(
        this IEnumerable<TItem> items,
        bool hasId,
        IResolveFieldContext context,
        bool omitQueryArguments)
    {
        if (omitQueryArguments)
        {
            return items;
        }

        var alreadyOrdered = items is ICollection<TItem>;

        if (hasId)
        {
            if (ArgumentReader.TryReadIds(context, out var idValues))
            {
                var predicate = ExpressionBuilder<TItem>.BuildIdPredicate("Id", idValues);
                items = items.Where(predicate.Compile());
            }
        }

        if (ArgumentReader.TryReadWhere(context, out var wheres))
        {
            var predicate = ExpressionBuilder<TItem>.BuildPredicate(wheres,context.RequestServices?.GetService<ICustomExpressionBuilder<TItem>>(), context, context.RequestServices?.GetService<ITagsProcessor>());
            items = items.Where(predicate.Compile());
        }

        var (orderedItems, order) = Order(items, context);
        items = orderedItems;

        if (ArgumentReader.TryReadSkip(context, out var skip))
        {
            EnsureOrderForSkip(order|| alreadyOrdered, context);

            items = items.Skip(skip);
        }

        if (ArgumentReader.TryReadTake(context, out var take))
        {
            EnsureOrderForTake(order|| alreadyOrdered, context);

            items = items.Take(take);
        }

        return items;
    }

    static (IEnumerable<TItem> items, bool order) Order<TItem>(IEnumerable<TItem> queryable, IResolveFieldContext context)
    {
        var customSorting = context.RequestServices?.GetService<ICustomSorting<TItem>>();
        var orderBys = ArgumentReader
            .ReadOrderBy(context);
        if (orderBys.Count == 0)
        {
            return (queryable, false);
        }

        var orderBy = orderBys.First();
        if (!(customSorting?.ApplySort(queryable, orderBy, context, true, out var ordered) ?? false))
        {
            var propertyFunc = PropertyCache<TItem>.GetProperty(orderBy.Path)
                .Func;
            ordered = orderBy.Descending ? queryable.OrderByDescending(propertyFunc) : queryable.OrderBy(propertyFunc);
        }

        foreach (var subsequentOrderBy in orderBys.Skip(1))
        {
            if (customSorting?.ApplySort(ordered, subsequentOrderBy, context, false, out ordered) ?? false)
                continue;
            var subsequentPropertyFunc = PropertyCache<TItem>.GetProperty(subsequentOrderBy.Path)
                .Func;
            ordered = subsequentOrderBy.Descending ? ordered.ThenByDescending(subsequentPropertyFunc) : ordered.ThenBy(subsequentPropertyFunc);
        }

        return (ordered, true);
    }
}