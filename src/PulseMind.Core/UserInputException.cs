namespace PulseMind.Core;

/// <summary>入力内容が決まりに合わないときの例外。メッセージはそのまま画面に表示できる文にする。</summary>
public sealed class UserInputException : Exception
{
    public UserInputException() : base("入力内容を確認してください。") { }

    public UserInputException(string message) : base(message) { }

    public UserInputException(string message, Exception innerException) : base(message, innerException) { }
}
