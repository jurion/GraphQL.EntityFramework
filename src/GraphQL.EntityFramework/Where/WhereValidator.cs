static class WhereValidator
{
    public static void ValidateObject(Type propertyType, Comparison comparison)
    {
        if (comparison is
            Comparison.Contains or
            Comparison.StartsWith or
            Comparison.EndsWith or
            Comparison.Like)
        {
            throw new($"Cannot perform {comparison} on {propertyType.FullName}.");
        }
    }

    public static void ValidateSingleObject(Type propertyType, Comparison comparison)
    {
        ValidateObject(propertyType, comparison);
        if (comparison == Comparison.In)
        {
            throw new($"Cannot perform {comparison} on {propertyType.FullName}.");
        }
    }

    public static void ValidateString(Comparison comparison)
    {
        if (comparison is
            Comparison.GreaterThan or
            Comparison.GreaterThanOrEqual or
            Comparison.LessThanOrEqual or
            Comparison.LessThan)
        {
            throw new($"Cannot perform {comparison} on a String.");
        }
    }

    public static void ValidateSingleString(Comparison comparison)
    {
        ValidateString(comparison);
        if (comparison == Comparison.In)
        {
            throw new($"Cannot perform {comparison} on a single String.");
        }
    }

    public static void ValidateBetween(Type propertyType, Comparison comparison, string?[]? values)
    {
        ValidateObject(propertyType, comparison);
        if (values == null || values.Length != 2)
        {
            throw new($"Between comparison requires exactly 2 values, but {values?.Length ?? 0} were provided.");
        }
    }
}