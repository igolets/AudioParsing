using System.Windows.Input;

namespace AudioParsing.Win.ViewModels;

/// <summary>
/// Synchronous <see cref="ICommand"/> implementation.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute)
        : this(_ => execute(), null)
    {
        ArgumentNullException.ThrowIfNull(execute);
    }

    public RelayCommand(Action<object?> execute)
        : this(execute, null)
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
