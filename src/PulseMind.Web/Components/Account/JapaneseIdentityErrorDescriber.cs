using System.Globalization;
using Microsoft.AspNetCore.Identity;

namespace PulseMind.Web.Components.Account;

/// <summary>
/// ASP.NET Core Identity が返すエラーメッセージ（登録・パスワード変更などで表示される）を日本語にする。
/// このアプリではユーザー名 = メールアドレスなので、ユーザー名に関するメッセージもメールアドレスとして案内する。
/// </summary>
public sealed class JapaneseIdentityErrorDescriber : IdentityErrorDescriber
{
    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };

    public override IdentityError DefaultError()
        => Error(nameof(DefaultError), "エラーが発生しました。時間をおいてもう一度お試しください。");

    public override IdentityError ConcurrencyFailure()
        => Error(nameof(ConcurrencyFailure), "ほかの操作と同時に更新されたため、保存できませんでした。ページを読み込み直してもう一度お試しください。");

    public override IdentityError PasswordMismatch()
        => Error(nameof(PasswordMismatch), "パスワードが正しくありません。");

    public override IdentityError InvalidToken()
        => Error(nameof(InvalidToken), "リンクの有効期限が切れているか、正しくありません。もう一度最初からお試しください。");

    public override IdentityError RecoveryCodeRedemptionFailed()
        => Error(nameof(RecoveryCodeRedemptionFailed), "回復用コードが正しくないか、すでに使用されています。");

    public override IdentityError LoginAlreadyAssociated()
        => Error(nameof(LoginAlreadyAssociated), "このログイン方法は、すでに別のアカウントで使われています。");

    // ユーザー名はメールアドレスと同じなので、DuplicateEmail と同じ文言にして余計な情報を出さない
    public override IdentityError DuplicateUserName(string userName)
        => Error(nameof(DuplicateUserName), "このメールアドレスはすでに登録されています。");

    public override IdentityError DuplicateEmail(string email)
        => Error(nameof(DuplicateEmail), "このメールアドレスはすでに登録されています。");

    public override IdentityError InvalidUserName(string? userName)
        => Error(nameof(InvalidUserName), "メールアドレスに使えない文字が含まれています。");

    public override IdentityError InvalidEmail(string? email)
        => Error(nameof(InvalidEmail), "メールアドレスの形式が正しくありません。");

    public override IdentityError InvalidRoleName(string? role)
        => Error(nameof(InvalidRoleName), "ロール名が正しくありません。");

    public override IdentityError DuplicateRoleName(string role)
        => Error(nameof(DuplicateRoleName), "このロール名はすでに使われています。");

    public override IdentityError UserAlreadyHasPassword()
        => Error(nameof(UserAlreadyHasPassword), "このアカウントにはすでにパスワードが設定されています。");

    public override IdentityError UserLockoutNotEnabled()
        => Error(nameof(UserLockoutNotEnabled), "このアカウントではロック機能が有効になっていません。");

    public override IdentityError UserAlreadyInRole(string role)
        => Error(nameof(UserAlreadyInRole), "このユーザーはすでにそのロールに含まれています。");

    public override IdentityError UserNotInRole(string role)
        => Error(nameof(UserNotInRole), "このユーザーはそのロールに含まれていません。");

    public override IdentityError PasswordTooShort(int length)
        => Error(nameof(PasswordTooShort), string.Format(CultureInfo.InvariantCulture, "パスワードは{0}文字以上で入力してください。", length));

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars)
        => Error(nameof(PasswordRequiresUniqueChars), string.Format(CultureInfo.InvariantCulture, "パスワードには{0}種類以上の異なる文字を使ってください。", uniqueChars));

    public override IdentityError PasswordRequiresNonAlphanumeric()
        => Error(nameof(PasswordRequiresNonAlphanumeric), "パスワードには記号（!、@、# など）を1文字以上含めてください。");

    public override IdentityError PasswordRequiresDigit()
        => Error(nameof(PasswordRequiresDigit), "パスワードには数字（0〜9）を1文字以上含めてください。");

    public override IdentityError PasswordRequiresLower()
        => Error(nameof(PasswordRequiresLower), "パスワードには英小文字（a〜z）を1文字以上含めてください。");

    public override IdentityError PasswordRequiresUpper()
        => Error(nameof(PasswordRequiresUpper), "パスワードには英大文字（A〜Z）を1文字以上含めてください。");
}
