namespace GraphQL.EntityFramework.Interfaces;
/// <summary>
///     This interface is called for each WhereExpression.
///     This allows client to modify Where clause before it's processed and converted into Expression 
/// </summary>
public interface ITagsProcessor
{
    /// <summary>
    ///     Allows client to modify Where before it's converted into c# Expression
    /// </summary>
    /// <param name="whereClause"></param>
    void ProcessTags(WhereExpression whereClause);
}