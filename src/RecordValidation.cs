using System.Globalization;

namespace University_Management_System_2;

public static class RecordValidation
{
    public static Dictionary<string, object?> Normalize(EntitySpec entity, IReadOnlyDictionary<string, object?> values)
    {
        EnsureEntity(entity);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (FieldSpec field in entity.Fields)
        {
            if (!values.TryGetValue(field.Name, out object? raw) || raw is null || raw is DBNull)
                throw new InvalidOperationException($"{field.Label} is required.");
            string text = Convert.ToString(raw, CultureInfo.CurrentCulture)?.Trim() ?? "";
            switch (field.Kind)
            {
                case FieldKind.Text:
                case FieldKind.Multiline:
                    if (text.Length == 0) throw new InvalidOperationException($"Enter {field.Label.ToLowerInvariant()}.");
                    if (text.Length > field.MaxLength) throw new InvalidOperationException($"{field.Label} must be {field.MaxLength} characters or fewer.");
                    if (text.Any(c => char.IsControl(c) && (field.Kind != FieldKind.Multiline || c is not ('\r' or '\n' or '\t'))))
                        throw new InvalidOperationException($"{field.Label} contains unsupported control characters.");
                    result[field.Name] = text;
                    break;
                case FieldKind.Choice:
                    string? option = field.Options?.FirstOrDefault(o => string.Equals(o, text, StringComparison.Ordinal));
                    if (option is null) throw new InvalidOperationException($"Choose a valid {field.Label.ToLowerInvariant()}.");
                    result[field.Name] = option;
                    break;
                case FieldKind.Number:
                case FieldKind.Lookup:
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int number))
                        throw new InvalidOperationException($"{field.Label} must be a whole number.");
                    decimal min = field.Kind == FieldKind.Lookup ? 1 : field.Min;
                    if (number < min || number > field.Max)
                        throw new InvalidOperationException(field.Kind == FieldKind.Lookup
                            ? $"Choose a valid {field.Label.ToLowerInvariant()}."
                            : $"{field.Label} must be between {min:N0} and {field.Max:N0}.");
                    result[field.Name] = number;
                    break;
                case FieldKind.Money:
                    if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal amount) || amount < field.Min || amount > field.Max)
                        throw new InvalidOperationException($"{field.Label} must be between {field.Min:N2} and {field.Max:N2}.");
                    if (decimal.Round(amount, 2) != amount)
                        throw new InvalidOperationException($"{field.Label} supports at most two decimal places.");
                    result[field.Name] = amount;
                    break;
                case FieldKind.Date:
                    if (!(raw is DateTime date))
                    {
                        if (!DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
                            throw new InvalidOperationException($"Choose a valid {field.Label.ToLowerInvariant()}.");
                    }
                    if (date.Date < new DateTime(1900, 1, 1) || date.Date > DateTime.Today)
                        throw new InvalidOperationException($"{field.Label} must be between 1 January 1900 and today.");
                    result[field.Name] = date.Date;
                    break;
            }
        }
        return result;
    }

    internal static void EnsureEntity(EntitySpec entity)
    {
        if (!Catalog.Entities.Any(candidate => ReferenceEquals(candidate, entity)))
            throw new InvalidOperationException("This record type is not supported.");
    }

    internal static void EnsurePermission(UserSession actor, EntitySpec entity)
    {
        EnsureEntity(entity);
        if (actor is null || !Enum.IsDefined(actor.Role) || !actor.CanAccess(entity))
            throw new InvalidOperationException("Administrator access is required for financial records.");
    }
}
