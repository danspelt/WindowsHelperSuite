namespace WindowsHelperSuite.Core.Interfaces;

public interface IInputService
{
    bool IsEnabled { get; set; }
    event EventHandler<string>? TextCaptured;
    event EventHandler<int>? SelectionKeyPressed;
    event EventHandler? NextPageKeyPressed;
    event EventHandler? PreviousPageKeyPressed;
    event EventHandler? ManualRefreshRequested;
}
