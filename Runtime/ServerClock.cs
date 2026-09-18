using System;
using System.Globalization;

namespace GachaGame.Network
{
    /// Tính chênh lệch giữa đồng hồ server và đồng hồ máy này.
    ///
    /// Tách khỏi APIClient vì đây là phần dễ sai nhất của chữ ký anti-cheat, mà
    /// lại là toán thuần — tách ra thì test được bằng Tools/ContractCheck, không
    /// cần mở Unity và không cần server.
    public static class ServerClock
    {
        /// Cửa sổ server chấp nhận: 30 giây quá khứ (ANTICHEAT_MAX_AGE_SECONDS)
        /// và 5 giây tương lai (ANTICHEAT_FUTURE_SKEW_SECONDS). Lệch quá là mọi
        /// request có ký đều trả ERR_ANTICHEAT_REQUEST_EXPIRED.
        public const int MaxAgeSeconds = 30;
        public const int FutureSkewSeconds = 5;

        /// Ngưỡng đáng cảnh báo cho người phát triển.
        public const int NoticeableSkewSeconds = 5;

        /// Tính phần bù từ header Date của một response.
        ///
        /// Date được server ghi tại thời điểm nó trả lời, tức nằm đâu đó giữa lúc
        /// client gửi và lúc client nhận. Lấy điểm giữa của hai mốc đó làm chuẩn,
        /// nếu không thì toàn bộ thời gian tải về bị tính nhầm thành lệch đồng hồ —
        /// trên mạng chậm, sai số đó tự nó đã đủ lớn để gây rắc rối.
        ///
        /// offset dương nghĩa là máy này chạy CHẬM hơn server.
        public static bool TryComputeOffset(string dateHeader, DateTime sentAtUtc,
                                            DateTime receivedAtUtc, out TimeSpan offset)
        {
            offset = TimeSpan.Zero;

            if (string.IsNullOrEmpty(dateHeader)) return false;
            if (receivedAtUtc < sentAtUtc) return false;

            if (!DateTimeOffset.TryParse(dateHeader, CultureInfo.InvariantCulture,
                                         DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                         out DateTimeOffset serverTime))
            {
                return false;
            }

            DateTime midpoint = sentAtUtc.AddTicks((receivedAtUtc - sentAtUtc).Ticks / 2);
            offset = serverTime.UtcDateTime - midpoint;
            return true;
        }

        /// Chữ ký ký bằng phần bù này có nằm trong cửa sổ server chấp nhận không.
        /// Dùng để quyết định có nên cảnh báo hay không, chứ không chặn request:
        /// cứ gửi đi, server mới là bên phán quyết.
        public static bool IsWithinAcceptedWindow(TimeSpan offset)
        {
            double seconds = offset.TotalSeconds;
            return seconds > -FutureSkewSeconds && seconds < MaxAgeSeconds;
        }
    }
}
