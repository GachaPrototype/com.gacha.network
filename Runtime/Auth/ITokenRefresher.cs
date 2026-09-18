using System;
using System.Collections;

namespace GachaGame.Network
{
    /// Cầu nối một chiều để APIClient tự gia hạn token mà không cần biết AuthService.
    ///
    /// APIClient là tầng vận chuyển, nó không nên biết gì về đăng nhập. Nhưng
    /// việc gia hạn phải áp dụng cho MỌI request, không thể để từng nơi gọi tự
    /// nhớ. Interface này giải quyết mâu thuẫn đó: APIClient chỉ biết interface,
    /// AuthService cài đặt nó và tự đăng ký qua APIClient.SetTokenRefresher().
    public interface ITokenRefresher
    {
        /// Có refresh token còn dùng được hay không. False thì đừng thử gia hạn.
        bool CanRefresh { get; }

        /// Access token sắp hết hạn, nên đổi trước khi gửi request tiếp theo.
        bool ShouldRefreshNow { get; }

        /// Gia hạn token. Phải là single-flight: nhiều request gọi cùng lúc thì
        /// chỉ một lần gọi mạng được thực hiện, số còn lại dùng chung kết quả.
        /// onComplete nhận true khi phiên đã sẵn sàng để gửi lại request.
        IEnumerator Refresh(Action<bool> onComplete);
    }
}
