using System.Collections.Concurrent;
using System.Globalization;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace LlmVoucherGateway.Api.IntegrationTests;

/// <summary>
/// Captures everything the application logs so a test can assert over it.
///
/// This is a Serilog sink rather than an <c>ILoggerProvider</c> because
/// <c>AddSerilog</c> replaces <c>ILoggerFactory</c> with Serilog's own, which never
/// forwards to providers registered through dependency injection — one registered that
/// way records nothing at all.
/// </summary>
internal sealed class RecordingSink : ILogEventSink
{
    // The `l` in `{Message:lj}` renders string properties without the quotes Serilog
    // otherwise adds, so a captured line reads exactly as the console shows it and an
    // assertion can be written as the plain text a human would look for.
    private static readonly MessageTemplateTextFormatter Formatter = new(
        "{Level:u3} {SourceContext} {Message:lj}{NewLine}{Exception}",
        CultureInfo.InvariantCulture
    );

    private readonly ConcurrentQueue<string> _entries = new();

    public string Output => string.Join(Environment.NewLine, _entries);

    public void Emit(LogEvent logEvent)
    {
        using StringWriter entry = new();

        // Formatted, not the raw template, so an argument that was never meant to be
        // logged shows up in a search. The exception is included for the same reason.
        Formatter.Format(logEvent, entry);

        _entries.Enqueue(entry.ToString());
    }
}
