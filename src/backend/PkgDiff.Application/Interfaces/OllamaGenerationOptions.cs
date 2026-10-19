namespace PkgDiff.Application.Interfaces;

public sealed record OllamaGenerationOptions(
    double? Temperature = null,
    double? TopP = null,
    double? RepeatPenalty = null,
    int? NumCtx = null,
    int? NumPredict = null);
