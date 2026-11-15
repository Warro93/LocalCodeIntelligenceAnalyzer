using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Lcia.Infrastructure;

internal static class SqliteHelpers
{
    public static SqliteCommand Command(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }

    /// <summary>Imposta (o crea) un parametro; null diventa DBNull.</summary>
    public static void Set(this SqliteCommand command, string name, object? value)
    {
        if (command.Parameters.Contains(name))
        {
            command.Parameters[name].Value = value ?? DBNull.Value;
        }
        else
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    public static long ExecuteInt64(this SqliteCommand command)
    {
        var value = command.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public static string? GetNullableString(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static long? GetNullableInt64(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    public static string ToDb(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static string? ToDb(DateTimeOffset? value) => value is null ? null : ToDb(value.Value);

    public static DateTimeOffset FromDb(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static DateTimeOffset? FromDbNullable(string? value) => value is null ? null : FromDb(value);

    public static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    /// <summary>Escape per LIKE con ESCAPE '\'.</summary>
    public static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);
}
