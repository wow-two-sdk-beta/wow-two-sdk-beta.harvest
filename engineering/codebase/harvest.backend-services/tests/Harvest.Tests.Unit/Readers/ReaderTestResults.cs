using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace Harvest.Tests.Unit.Readers;

internal static class ReaderTestResults
{
    internal static T Data<T>(this Result<T> result) where T : notnull => result is Result<T>.Success success
        ? success.Value : throw new InvalidOperationException("Expected a successful fixture result.");
}
