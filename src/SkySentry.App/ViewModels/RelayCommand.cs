using System.Windows.Input;

namespace SkySentry.App.ViewModels;

/// <summary>Minimal <see cref="ICommand"/> implementation for button bindings.</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();
}

/// <summary>Async-friendly relay command; swallows re-entrancy while a run is in flight.</summary>
public sealed class AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null) : ICommand
{
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (_isExecuting)
        {
            return;
        }

        _isExecuting = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await executeAsync();
        }
        finally
        {
            _isExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
