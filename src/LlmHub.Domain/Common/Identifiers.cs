namespace LlmHub.Domain.Common;

public readonly record struct PrincipalId(string Value)
{
    public static PrincipalId Create(string value) => new(Required(value, nameof(value)));

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }

        return value;
    }
}

public readonly record struct EndpointId(string Value)
{
    public static EndpointId Create(string value) => new(Required(value, nameof(value)));

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }

        return value;
    }
}

public readonly record struct ChannelId(string Value)
{
    public static ChannelId Create(string value) => new(Required(value, nameof(value)));

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }

        return value;
    }
}

public readonly record struct MessageId(string Value)
{
    public static MessageId Create(string value) => new(Required(value, nameof(value)));

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }

        return value;
    }
}

public readonly record struct RunId(string Value)
{
    public static RunId Create(string value) => new(Required(value, nameof(value)));

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }

        return value;
    }
}
