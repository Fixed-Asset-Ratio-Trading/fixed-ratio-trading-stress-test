using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace FixedRatioStressTest.Logging.Transport;

/// <summary>
/// Represents a log message that can be serialized and sent over UDP.
/// </summary>
public sealed class UdpLogMessage
{
    /// <summary>
    /// Gets or sets the timestamp of the log message.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the log level.
    /// </summary>
    [JsonPropertyName("level")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel Level { get; set; }

    /// <summary>
    /// Gets or sets the category name.
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the log message.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the exception details if any.
    /// </summary>
    [JsonPropertyName("exception")]
    public string? Exception { get; set; }

    /// <summary>
    /// Gets or sets the event ID.
    /// </summary>
    [JsonPropertyName("eventId")]
    public int EventId { get; set; }

    /// <summary>
    /// Gets or sets the source application.
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }
}
