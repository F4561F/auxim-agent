namespace Auxim.VAFS;

internal sealed record ExternalCommandPlan(
    string Executable,
    IReadOnlyList<string> Arguments);

