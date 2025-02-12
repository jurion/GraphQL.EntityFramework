﻿namespace GraphQL.EntityFramework;

public class EfInterfaceGraphType<TDbContext, TSource> :
    InterfaceGraphType<TSource>
    where TDbContext : DbContext
{
    public IEfGraphQLService<TDbContext> GraphQlService { get; }

    public EfInterfaceGraphType(IEfGraphQLService<TDbContext> graphQlService) =>
        GraphQlService = graphQlService;

    public ConnectionBuilder<TSource> AddNavigationConnectionField<TReturn>(
        string name,
        Type? graphType = null,
        IEnumerable<string>? includeNames = null)
        where TReturn : class =>
        GraphQlService.AddNavigationConnectionField<TSource, TReturn>(this, name, null, graphType, includeNames);

    public FieldBuilder<TSource, TReturn> AddNavigationField<TReturn>(
        string name,
        Type? graphType = null,
        IEnumerable<string>? includeNames = null)
        where TReturn : class =>
        GraphQlService.AddNavigationField<TSource, TReturn>(this, name, null, graphType, includeNames);

    public FieldBuilder<TSource, TReturn> AddNavigationListField<TReturn>(
        string name,
        Type? graphType = null,
        IEnumerable<string>? includeNames = null)
        where TReturn : class =>
        GraphQlService.AddNavigationListField<TSource, TReturn>(this, name, null, graphType, includeNames);

    public ConnectionBuilder<TSource> AddQueryConnectionField<TReturn>(
        string name,
        Type? graphType = null)
        where TReturn : class =>
        GraphQlService.AddQueryConnectionField<TSource, TReturn>(this, name, null, graphType);

    public FieldBuilder<object, TReturn> AddQueryField<TReturn>(
        string name,
        Type? graphType = null)
        where TReturn : class =>
        GraphQlService.AddQueryField<TReturn>(this, name, null, graphType);

    public TDbContext ResolveDbContext(IResolveFieldContext<TSource> context) =>
        GraphQlService.ResolveDbContext(context);

    public TDbContext ResolveDbContext(IResolveFieldContext context) =>
        GraphQlService.ResolveDbContext(context);
}