using System;
using System.Globalization;

namespace GachaGame.Network
{
    /// Trạng thái phiên đăng nhập đang chạy trong bộ nhớ.
    ///
    /// Access token sống 15 phút, refresh token sống 720 giờ. Lớp này chỉ giữ dữ
    /// liệu và trả lời câu hỏi "đã tới lúc gia hạn chưa"; việc gọi mạng thuộc về
    /// AuthService.
    public sealed class SessionState
    {
        public string UserId = string.Empty;
        public string AccessToken = string.Empty;
        public string SessionKey = string.Empty;
        public string RefreshToken = string.Empty;

        /// Chuỗi RFC3339 nguyên văn từ server, ví dụ 2026-08-29T02:59:53Z.
        public string AccessExpiresAtIso = string.Empty;
        public string RefreshExpiresAtIso = string.Empty;

        /// Thông tin đăng nhập của tài khoản khách, rỗng với tài khoản thường.
        public string GuestUsername = string.Empty;
        public string GuestPassword = string.Empty;

        public bool IsGuest => !string.IsNullOrEmpty(GuestUsername);

        public bool HasAccessToken => !string.IsNullOrEmpty(AccessToken);

        /// Có refresh token và refresh token đó chưa hết hạn.
        public bool HasUsableRefreshToken =>
            !string.IsNullOrEmpty(RefreshToken) && !IsExpired(RefreshExpiresAtIso);

        /// Access token sẽ hết hạn trong vòng bấy nhiêu giây nữa.
        ///
        /// utcNow phải là giờ SERVER (APIClient.ServerUtcNow), không phải giờ máy:
        /// hạn token do server đặt ra, so với đồng hồ lệch của máy người chơi thì
        /// gia hạn sẽ chạy quá sớm hoặc quá muộn.
        public bool IsAccessExpiringWithin(int seconds, DateTime utcNow)
        {
            if (!TryParseIso(AccessExpiresAtIso, out DateTime expiresAt)) return false;
            return expiresAt <= utcNow.AddSeconds(seconds);
        }

        /// Nhận token mới từ login, register hoặc refresh.
        public void Adopt(AuthTokenResponse response)
        {
            if (response == null) return;

            UserId = response.userId ?? UserId;
            AccessToken = response.token ?? string.Empty;
            SessionKey = response.sessionKey ?? string.Empty;
            AccessExpiresAtIso = response.expiresAt ?? string.Empty;

            // Refresh xoay vòng: mỗi lần gọi /auth/refresh server cấp một refresh
            // token mới và vô hiệu cái cũ. Ghi đè nhầm bằng chuỗi rỗng là mất phiên.
            if (!string.IsNullOrEmpty(response.refreshToken))
            {
                RefreshToken = response.refreshToken;
                RefreshExpiresAtIso = response.refreshExpiresAt ?? string.Empty;
            }
        }

        public void RememberGuestCredentials(string username, string password)
        {
            GuestUsername = username ?? string.Empty;
            GuestPassword = password ?? string.Empty;
        }

        public void Clear()
        {
            UserId = string.Empty;
            AccessToken = string.Empty;
            SessionKey = string.Empty;
            RefreshToken = string.Empty;
            AccessExpiresAtIso = string.Empty;
            RefreshExpiresAtIso = string.Empty;
            GuestUsername = string.Empty;
            GuestPassword = string.Empty;
        }

        private static bool IsExpired(string iso)
        {
            // Không đọc được hạn thì coi như còn hạn: để server phán quyết,
            // đừng tự vứt một refresh token có thể vẫn dùng được.
            if (!TryParseIso(iso, out DateTime expiresAt)) return false;
            return expiresAt <= DateTime.UtcNow;
        }

        private static bool TryParseIso(string iso, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrEmpty(iso)) return false;

            return DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                                     DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                     out utc);
        }
    }
}
