#pragma warning disable CS0067

using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.Input.Services;

public class InputService : IInputService
{
    public bool IsEnabled { get; set; } = true;

    public event EventHandler<string>? TextCaptured;
    public event EventHandler<int>? SelectionKeyPressed;
    public event EventHandler? NextPageKeyPressed;
    public event EventHandler? PreviousPageKeyPressed;
    public event EventHandler? ManualRefreshRequested;
}

#pragma warning restore CS0067
