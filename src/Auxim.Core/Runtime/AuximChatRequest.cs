namespace Auxim.Core.Runtime;

public sealed record AuximChatRequest(
    string Prompt,
    bool UseCurrentSession = true,
    bool AppendToSession = true);
