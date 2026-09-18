using System;

namespace GachaGame.Network
{
    /// Payload và response của nhóm endpoint /auth.
    ///
    /// Đặt trong Gacha.Network chứ không phải Gacha.Contracts vì chỉ Network và
    /// UI cần tới chúng, mà UI đã tham chiếu Network. Nếu sau này có package
    /// khác cần thì chuyển sang Contracts, phần còn lại không phải sửa.

    [Serializable]
    public class PasswordLoginRequest
    {
        public string username;
        public string password;
    }

    [Serializable]
    public class PasswordRegisterRequest
    {
        public string username;
        public string password;
        public string email;
        public string nickname;
    }

    [Serializable]
    public class RefreshTokenRequest
    {
        public string refreshToken;
    }

    /// Response chung của login, register và refresh.
    ///
    /// sessionKey là khoá HMAC riêng cho phiên, server nhúng nó vào JWT ở claim
    /// "sk" và AntiCheatMiddleware ưu tiên dùng nó thay cho HMAC_SECRET_KEY tĩnh.
    /// Server sinh khoá MỚI mỗi lần cấp access token, kể cả khi refresh.
    [Serializable]
    public class AuthTokenResponse
    {
        public bool success;
        public string token;
        public string sessionKey;
        public string userId;
        public string expiresAt;
        public string refreshToken;
        public string refreshExpiresAt;
    }

    /// Response của /auth/guest. Giống AuthTokenResponse nhưng kèm thông tin
    /// đăng nhập được sinh ra cho tài khoản khách.
    ///
    /// guestUsername và guestPassword chỉ xuất hiện đúng MỘT lần, ngay tại
    /// response này. Không lưu lại là người chơi mất tài khoản khi gỡ game.
    [Serializable]
    public class GuestAuthResponse
    {
        public bool success;
        public string token;
        public string sessionKey;
        public string userId;
        public string expiresAt;
        public string refreshToken;
        public string refreshExpiresAt;
        public string guestUsername;
        public string guestPassword;

        public AuthTokenResponse ToTokenResponse()
        {
            return new AuthTokenResponse
            {
                success = success,
                token = token,
                sessionKey = sessionKey,
                userId = userId,
                expiresAt = expiresAt,
                refreshToken = refreshToken,
                refreshExpiresAt = refreshExpiresAt,
            };
        }
    }

    /// Response của các endpoint chỉ báo thành công: logout, revoke-all.
    [Serializable]
    public class AuthActionResponse
    {
        public bool success;
    }

    /// Kết quả đăng nhập trả cho phần gọi. Cố ý không mang token — token là việc
    /// nội bộ của Network, UI không cần và không nên chạm vào.
    public readonly struct SignInResult
    {
        public readonly string UserId;
        public readonly bool IsGuest;

        /// Chỉ khác rỗng ở đúng lần tạo tài khoản khách. Màn hình UI phải hiện
        /// cho người chơi chép lại, hoặc ít nhất nhắc họ liên kết tài khoản.
        public readonly string GuestUsername;
        public readonly string GuestPassword;

        public SignInResult(string userId, bool isGuest, string guestUsername, string guestPassword)
        {
            UserId = userId;
            IsGuest = isGuest;
            GuestUsername = guestUsername ?? string.Empty;
            GuestPassword = guestPassword ?? string.Empty;
        }

        public bool HasFreshGuestCredentials =>
            !string.IsNullOrEmpty(GuestUsername) && !string.IsNullOrEmpty(GuestPassword);

        public override string ToString() =>
            IsGuest ? $"guest {UserId}" : UserId;
    }
}
