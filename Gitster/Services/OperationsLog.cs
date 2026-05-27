namespace Gitster.Services;

// Kept for reference; superseded by Services/OperationsLog/OperationsLogService.cs (Step D)
internal record LegacyOperationRecord(
    string Description,
    string Sha,
    DateTime Timestamp,
    Func<Task> UndoAction);

internal class LegacyOperationsLog
{
    private readonly Stack<LegacyOperationRecord> _stack = new();

    public event EventHandler? Changed;

    public LegacyOperationRecord? Peek() => _stack.Count > 0 ? _stack.Peek() : null;

    public LegacyOperationRecord? Pop()
    {
        if (_stack.Count == 0) return null;
        var op = _stack.Pop();
        Changed?.Invoke(this, EventArgs.Empty);
        return op;
    }

    public void Record(LegacyOperationRecord record)
    {
        _stack.Push(record);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
