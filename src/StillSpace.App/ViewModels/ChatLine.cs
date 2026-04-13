namespace StillSpace.ViewModels;

public sealed class ChatLine(string roleLabel, string content)
{
    public string RoleLabel { get; } = roleLabel;
    public string Content { get; } = content;
}
