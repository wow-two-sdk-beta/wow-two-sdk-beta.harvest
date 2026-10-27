using System.Text;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;

/// <summary>Maps an enum to the snake_case text the schema's CHECK constraints name, and back.</summary>
/// <typeparam name="TEnum">The enum type.</typeparam>
internal static class SnakeCaseEnum<TEnum> where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> Texts = Enum.GetValues<TEnum>().ToDictionary(value => value, value => ToSnake(value.ToString()));
    private static readonly Dictionary<string, TEnum> Values = Texts.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>Returns the stored text of a value.</summary>
    public static string ToText(TEnum value) => Texts[value];

    /// <summary>Returns the value of a stored text.</summary>
    public static TEnum Parse(string text) =>
        Values.TryGetValue(text, out var value)
            ? value
            : throw new InvalidOperationException($"'{text}' is not a stored {typeof(TEnum).Name}.");

    private static string ToSnake(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var index = 0; index < name.Length; index++)
        {
            if (char.IsUpper(name[index]) && index > 0)
                builder.Append('_');
            builder.Append(char.ToLowerInvariant(name[index]));
        }

        return builder.ToString();
    }
}
