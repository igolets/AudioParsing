using System.Windows.Input;

namespace AudioParsing.Win.ViewModels;

/// <summary>
/// Asynchronous <see cref="ICommand"/> implementation. Guards against re-entrancy
/// while the previous invocation is still running; <c>Execute</c> is the only
/// permitted <c>async void</c> boundary besides XAML event handlers.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, CancellationToken, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> execute)
        : this((_, _) => execute(), null)
    {
        ArgumentNullException.ThrowIfNull(execute);
    }

    public AsyncRelayCommand(Func<object?, Task> execute)
        : this((parameter, _) => execute(parameter), null)
    {
        ArgumentNullException.ThrowIfNull(execute);
    }

    public AsyncRelayCommand(Func<object?, CancellationToken, Task> execute, Func<object?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (_isExecuting != value)
            {
                _isExecuting = value;
                NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanExecute(object? parameter) => !IsExecuting && (_canExecute?.Invoke(parameter) ?? true); public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        IsExecuting = true;
        try
        {
            await _execute(parameter, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            IsExecuting = false;
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
