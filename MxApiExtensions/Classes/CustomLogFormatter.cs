using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace MxApiExtensions.Classes;

public class CustomLogFormatter : ConsoleFormatter {
    public CustomLogFormatter(string name) : base(name) { }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter) {
        Console.WriteLine("Log message");
    }
}
