using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Holds the error catalogue of the entity reads.</summary>
public static class EntityErrors
{
    /// <summary>Creates the error for a malformed request.</summary>
    /// <param name="message">What is malformed.</param>
    public static AppError Invalid(string message) => AppErrorFactory.Validation(message);

    /// <summary>Creates the error for an entity the container does not hold.</summary>
    /// <param name="entityId">The identifier.</param>
    public static AppError EntityNotFound(Guid entityId) => AppErrorFactory.NotFound($"No entity has the id {entityId} in this container.");

    /// <summary>Creates the error for a cursor that is malformed or belongs to another feed or entity.</summary>
    public static AppError InvalidCursor() => AppErrorFactory.Validation("The cursor is malformed or belongs to a different container or entity.");
}
