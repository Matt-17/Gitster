namespace Gitster.Services;

public record OperationRecord(
    string Description,
    string Sha,
    DateTime Timestamp,
    Func<Task> UndoAction);

public class OperationsLog
{
    private readonly Stack<OperationRecord> _stack = new();

    public event EventHandler? Changed;

    public OperationRecord? Peek() => _stack.Count > 0 ? _stack.Peek() : null;

    public OperationRecord? Pop()
    {
        if (_stack.Count == 0) return null;
        var op = _stack.Pop();
        Changed?.Invoke(this, EventArgs.Empty);
        return op;
    }

    public void Record(OperationRecord record)
    {
        _stack.Push(record);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
