using GraphQL.EntityFramework.Interfaces;

namespace GraphQL.EntityFramework;

public static partial class ExpressionBuilder<T>
{
    /// <summary>
    /// Build a predicate for a supplied list of where's (Grouped or not)
    /// </summary>
    public static Expression<Func<T, bool>> BuildPredicate(IReadOnlyCollection<WhereExpression> wheres, ICustomExpressionBuilder<T>? customExpressionBuilder = null, IResolveFieldContext? context = null, ITagsProcessor? tagsProcessor = null)
    {
        var param = PropertyCache<T>.SourceParameter;
        var expressionBody = MakePredicateBody(wheres, customExpressionBuilder, param, context, tagsProcessor);
        return Expression.Lambda<Func<T, bool>>(expressionBody, param);
    }

    static Expression MakePredicateBody(IReadOnlyCollection<WhereExpression> wheres, ICustomExpressionBuilder<T>? customExpressionBuilder, ParameterExpression parameterExpression, IResolveFieldContext? resolveFieldContext, ITagsProcessor? tagsProcessor)
    {
        Expression? mainExpression = null;
        var previousWhere = new WhereExpression();

        // Iterate over wheres
        foreach (var where in wheres)
        {
            tagsProcessor?.ProcessTags(where);
            Expression nextExpression;
            Expression? customExpression = null;
            if (customExpressionBuilder != null)
                customExpression = customExpressionBuilder.GetExpression(where, parameterExpression, resolveFieldContext);
            if (customExpression == null)
            {
                // If there are grouped expressions
                if (where.GroupedExpressions?.Length > 0)
                {
                    // Recurse with new set of expression
                    nextExpression = MakePredicateBody(where.GroupedExpressions, customExpressionBuilder, parameterExpression, resolveFieldContext, tagsProcessor);

                    // If the whole group is to be negated
                    if (where.Negate)
                    {
                        // Negate it
                        nextExpression = NegateExpression(nextExpression);
                    }
                }
                // Otherwise handle single expressions
                else
                {
                    // Get the predicate body for the single expression
                    nextExpression = MakePredicateBody(where.Path, where.Comparison, where.Value, where.Negate);
                }
            }
            else
            {
                nextExpression = customExpression;
            }

            // If this is the first where processed
            if (mainExpression is null)
            {
                // Assign to main expression
                mainExpression = nextExpression;
            }
            else
            {
                // Otherwise combine expression by specified connector or default (AND) if not provided
                mainExpression = CombineExpressions(previousWhere.Connector, mainExpression, nextExpression);
            }

            // Save the previous where so the connector can be retrieved
            previousWhere = where;
        }

        return mainExpression ?? Expression.Constant(false);
    }

    /// <summary>
    /// Create a single predicate for the single set of supplied conditional arguments
    /// </summary>
    public static Expression<Func<T, bool>> BuildPredicate(string path, Comparison comparison, string?[]? values, bool negate = false)
    {
        var expressionBody = MakePredicateBody(path, comparison, values, negate);
        var param = PropertyCache<T>.SourceParameter;

        return Expression.Lambda<Func<T, bool>>(expressionBody, param);
    }

    static Expression MakePredicateBody(string path, Comparison comparison, string?[]? values, bool negate)
    {
        try
        {
            Expression expressionBody;

            // If path includes list property access
            if (HasListPropertyInPath(path))
            {
                // Handle a list path
                expressionBody = ProcessList(path, comparison, values!);
            }
            // Otherwise linear property access
            else
            {
                // Just get expression
                expressionBody = GetExpression(path, comparison, values);
            }

            // If the expression should be negated
            if (negate)
            {
                expressionBody = NegateExpression(expressionBody);
            }

            return expressionBody;
        }
        catch (Exception exception)
        {
            throw new($"Failed to build expression. Path: {path}, Comparison: {comparison}, Negate: {negate}. Inner exception: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Create a single predicate for the single set of supplied conditional arguments
    /// </summary>
    public static Expression<Func<T, bool>> BuildIdPredicate(string path, string[] values)
    {
        var expressionBody = MakeIdPredicateBody(path, values);
        var param = PropertyCache<T>.SourceParameter;

        return Expression.Lambda<Func<T, bool>>(expressionBody, param);
    }

    static Expression MakeIdPredicateBody(string path, string[] values)
    {
        try
        {
            return GetExpression(path, Comparison.In, values);
        }
        catch (Exception exception)
        {
            throw new($"Failed to build expression. Path: {path} ", exception);
        }
    }

    static Expression ProcessList(string path, Comparison comparison, string?[]? values)
    {
        // Get the path pertaining to individual list items
        var listPath = ListPropertyRegex().Match(path).Groups[1].Value;
        // Remove the part of the path that leads into list item properties
        path = ListPropertyRegex().Replace(path, "");

        // Get the property on the current object up to the list member
        var property = PropertyCache<T>.GetProperty(path);

        // Get the list item type details
        var listItemType = property.PropertyType.GetGenericArguments().Single();

        // Generate the predicate for the list item type
        var genericType = typeof(ExpressionBuilder<>)
            .MakeGenericType(listItemType);
        var buildPredicate = genericType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SingleOrDefault(_ => _.Name == "BuildPredicate" &&
                                  _.GetParameters().Length == 4 &&
                                  _.GetParameters()[0].ParameterType == typeof(string) &&
                                  _.GetParameters()[1].ParameterType == typeof(Comparison) &&
                                  _.GetParameters()[2].ParameterType.IsArray &&
                                  _.GetParameters()[2].ParameterType.GetElementType() == typeof(string) &&
                                  _.GetParameters()[3].ParameterType == typeof(bool));
        if (buildPredicate == null)
        {
            throw new($"Could not find BuildPredicate method on {genericType.FullName}");
        }

        Expression subPredicate;
        try
        {
            // Ensure values array is properly passed - create a new array to avoid any potential issues
            var valuesArray = values ?? throw new($"Values cannot be null for Between comparison on list path {path}");
            if (valuesArray.Length != 2 && comparison == Comparison.Between)
            {
                throw new($"Between comparison requires exactly 2 values, but {valuesArray.Length} were provided for list path {path}.");
            }
            
            subPredicate = (Expression)buildPredicate
                .Invoke(
                    new(),
                    new object[]
                    {
                        listPath,
                        comparison,
                        valuesArray,
                        false
                    })!;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
        {
            // Unwrap TargetInvocationException to get the actual exception
            throw new($"Failed to build expression for list item. Path: {listPath}, Comparison: {comparison}, ListItemType: {listItemType.FullName}, ValuesCount: {values?.Length ?? 0}. {ex.InnerException.Message}", ex.InnerException);
        }

        // Generate a method info for the Any Enumerable Static Method
        var anyInfo = typeof(Enumerable)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .First(_ => _.Name == "Any" &&
                        _.GetParameters().Length == 2)
            .MakeGenericMethod(listItemType);

        // Create Any Expression Call
        return Expression.Call(anyInfo, property.Left, subPredicate);
    }

    static Expression GetExpression(string path, Comparison comparison, string?[]? values)
    {
        var property = PropertyCache<T>.GetProperty(path);
        Expression expression;

        if (property.PropertyType == typeof(string))
        {
            switch (comparison)
            {
                case Comparison.NotIn:
                    WhereValidator.ValidateString(comparison);
                    // Ensure expression is negated
                    expression = NegateExpression(MakeStringListInComparison(values!, property));
                    break;
                case Comparison.In:
                    WhereValidator.ValidateString(comparison);
                    expression = MakeStringListInComparison(values!, property);
                    break;

                default:
                    WhereValidator.ValidateSingleString(comparison);
                    var value = values?.Single();
                    expression = MakeSingleStringComparison(comparison, value, property);
                    break;
            }
        }
        else
        {
            switch (comparison)
            {
                case Comparison.NotIn:
                    WhereValidator.ValidateObject(property.PropertyType, comparison);
                    expression = NegateExpression(MakeObjectListInComparision(values!, property));
                    break;
                case Comparison.In:
                    WhereValidator.ValidateObject(property.PropertyType, comparison);
                    expression = MakeObjectListInComparision(values!, property);
                    break;
                case Comparison.Between:
                    WhereValidator.ValidateBetween(property.PropertyType, comparison, values);
                    expression = MakeBetweenComparison(values!, property);
                    break;

                default:
                    WhereValidator.ValidateSingleObject(property.PropertyType, comparison);
                    var value = values?.Single();
                    var valueObject = TypeConverter.ConvertStringToType(value, property.PropertyType);
                    expression = MakeSingleObjectComparison(comparison, valueObject, property);
                    break;
            }
        }

        return expression;
    }

    static MethodCallExpression MakeObjectListInComparision(string[] values, Property<T> property)
    {
        // Attempt to convert the string values to the object type
        var objects = TypeConverter.ConvertStringsToList(values, property.Info);
        // Make the object values a constant expression
        var constant = Expression.Constant(objects);
        // Build and return the expression body
        return Expression.Call(constant, property.SafeListContains, property.Left);
    }

    static MethodCallExpression MakeStringListInComparison(string[] values, Property<T> property)
    {
        var equalsBody = Expression.Call(null, ReflectionCache.StringEqual, ExpressionCache.StringParam, property.Left);

        // Make lambda for comparing each string value against property value
        var itemEvaluate = Expression.Lambda<Func<string, bool>>(equalsBody, ExpressionCache.StringParam);

        // Build Expression body to check if any string values match the property value
        return Expression.Call(null, ReflectionCache.StringAny, Expression.Constant(values), itemEvaluate);
    }

    static Expression MakeSingleStringComparison(Comparison comparison, string? value, Property<T> property)
    {
        var left = property.Left;

        var valueConstant = Expression.Constant(value, typeof(string));
        var nullCheck = Expression.NotEqual(left, ExpressionCache.Null);

        switch (comparison)
        {
            case Comparison.Equal:
                return Expression.Call(ReflectionCache.StringEqual, left, valueConstant);
            case Comparison.NotEqual:
                return Expression.Not(Expression.Call(ReflectionCache.StringEqual, left, valueConstant));
            case Comparison.Like:
                return Expression.Call(null, ReflectionCache.StringLike, ExpressionCache.EfFunction, left, valueConstant);
            case Comparison.StartsWith:
                var startsWithExpression = Expression.Call(left, ReflectionCache.StringStartsWith, valueConstant);
                return Expression.AndAlso(nullCheck, startsWithExpression);
            case Comparison.EndsWith:
                var endsWithExpression = Expression.Call(left, ReflectionCache.StringEndsWith, valueConstant);
                return Expression.AndAlso(nullCheck, endsWithExpression);
            case Comparison.Contains:
                var indexOfExpression = Expression.Call(left, ReflectionCache.StringIndexOf, valueConstant);
                var notEqualExpression = Expression.NotEqual(indexOfExpression, ExpressionCache.NegativeOne);
                return Expression.AndAlso(nullCheck, notEqualExpression);
        }

        throw new($"Invalid comparison operator '{comparison}'.");
    }

    static Expression MakeSingleObjectComparison(Comparison comparison, object? value, Property<T> property)
    {
        var left = property.Left;
        var constant = Expression.Constant(value, left.Type);

        return comparison switch
        {
            Comparison.Equal => Expression.MakeBinary(ExpressionType.Equal, left, constant),
            Comparison.NotEqual => Expression.MakeBinary(ExpressionType.NotEqual, left, constant),
            Comparison.GreaterThan => Expression.MakeBinary(ExpressionType.GreaterThan, left, constant),
            Comparison.GreaterThanOrEqual => Expression.MakeBinary(ExpressionType.GreaterThanOrEqual, left, constant),
            Comparison.LessThan => Expression.MakeBinary(ExpressionType.LessThan, left, constant),
            Comparison.LessThanOrEqual => Expression.MakeBinary(ExpressionType.LessThanOrEqual, left, constant),
            _ => throw new($"Invalid comparison operator '{comparison}'.")
        };
    }

    static Expression MakeBetweenComparison(string[] values, Property<T> property)
    {
        if (values.Length != 2)
        {
            throw new($"Between comparison requires exactly 2 values, but {values.Length} were provided.");
        }

        var left = property.Left;
        var minValue = TypeConverter.ConvertStringToType(values[0], property.PropertyType);
        var maxValue = TypeConverter.ConvertStringToType(values[1], property.PropertyType);

        // If min and max are equal, use equality comparison instead of range
        if (AreValuesEqual(minValue, maxValue))
        {
            var constant = Expression.Constant(minValue, left.Type);
            return Expression.MakeBinary(ExpressionType.Equal, left, constant);
        }

        var minConstant = Expression.Constant(minValue, left.Type);
        var maxConstant = Expression.Constant(maxValue, left.Type);

        var greaterThanOrEqual = Expression.MakeBinary(ExpressionType.GreaterThanOrEqual, left, minConstant);
        var lessThanOrEqual = Expression.MakeBinary(ExpressionType.LessThanOrEqual, left, maxConstant);

        return Expression.AndAlso(greaterThanOrEqual, lessThanOrEqual);
    }

    static bool AreValuesEqual(object? minValue, object? maxValue)
    {
        if (ReferenceEquals(minValue, maxValue))
            return true;
        if (minValue == null || maxValue == null)
            return false;

        // Use Equals which handles value types correctly (including boxing/unboxing)
        return Equals(minValue, maxValue);
    }

    static bool HasListPropertyInPath(string path) =>
        path.Contains('[');

    static Expression CombineExpressions(Connector connector, Expression expr1, Expression expr2) =>
        connector switch
        {
            Connector.And => Expression.AndAlso(expr1, expr2),
            Connector.Or => Expression.OrElse(expr1, expr2),
            _ => throw new($"Invalid connector operator '{connector}'.")
        };

    static Expression NegateExpression(Expression expression) =>
        Expression.Not(expression);

    [GeneratedRegex(@"\[(.*)\]")]
    private static partial Regex ListPropertyRegex();
}