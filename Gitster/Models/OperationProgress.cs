namespace Gitster.Models;

public sealed record OperationProgress(
    string Stage,
    string Detail = "",
    double Value = 0,
    double Maximum = 100);
