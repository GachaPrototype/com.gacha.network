using System;
using System.Collections;
using GachaGame.Contracts.Api;
using UnityEngine;

namespace GachaGame.Network
{
    /// Theo dõi kết nối và nối lại sau khi đứt.
    ///
    /// Khác với việc gửi lại một request (APIClient tự lo), lớp này lo tình huống
    /// lớn hơn: máy mất mạng cả phút, hoặc người chơi đưa app xuống nền rồi mở lại
    /// sau nửa tiếng. Lúc đó access token đã hết hạn, và có thể người chơi còn
    /// đang dở một trận đấu.
    ///
    /// Nó KHÔNG mở UI. Nó chỉ phát event và cung cấp coroutine để tầng UI dùng.
    ///
    /// Gắn cùng GameObject với APIClient và AuthService.
    [RequireComponent(typeof(APIClient))]
    public class ConnectionMonitor : MonoBehaviour
    {
        [Header("Kiểm tra kết nối")]
        [Tooltip("Số giây tối đa chờ mạng quay lại trong WaitUntilOnline.")]
        [SerializeField] private float reconnectTimeoutSeconds = 30f;

        [Tooltip("Giây giữa hai lần thử lại khi đang mất mạng.")]
        [SerializeField] private float retryIntervalSeconds = 2f;

        [Tooltip("Tự kiểm tra lại phiên khi app quay lại từ nền.")]
        [SerializeField] private bool checkOnResume = true;

        private APIClient _api;
        private SystemGateway _system;

        public static ConnectionMonitor Instance { get; private set; }

        /// Lần kiểm tra gần nhất có tới được server hay không.
        /// Mặc định là true để không chặn luồng khởi động khi chưa kiểm lần nào.
        public bool IsOnline { get; private set; } = true;

        /// Mất kết nối tới server.
        public event Action OnOffline;

        /// Kết nối đã trở lại sau khi mất.
        public event Action OnBackOnline;

        /// App vừa quay lại từ nền và phiên đã được xác nhận còn dùng được.
        /// Đây là chỗ tầng game nên hỏi lại dữ liệu có thể đã cũ.
        public event Action OnResumed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            _api = GetComponent<APIClient>();
            _system = new SystemGateway(_api);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnApplicationPause(bool paused)
        {
            // Android và iOS gọi hàm này khi app xuống nền và khi quay lại.
            if (!paused && checkOnResume) StartCoroutine(HandleResume());
        }

        private void OnApplicationFocus(bool focused)
        {
            // Trong Editor và trên PC, mất/được focus là tín hiệu tương đương.
            if (focused && checkOnResume && !Application.isMobilePlatform)
            {
                StartCoroutine(HandleResume());
            }
        }

        /// Xác nhận còn nói chuyện được với server, và phiên còn dùng được.
        ///
        /// Gọi /system/bootstrap: nó công khai, rẻ, và tiện thể đồng bộ lại lệch
        /// đồng hồ. Sau đó nếu đang đăng nhập thì gia hạn token khi cần.
        ///
        /// onDone nhận true khi đã sẵn sàng gọi API tiếp.
        public IEnumerator EnsureConnected(Action<bool> onDone)
        {
            bool reachable = false;
            yield return _system.GetBootstrap(result => reachable = result.IsSuccess);

            SetOnline(reachable);
            if (!reachable)
            {
                onDone?.Invoke(false);
                yield break;
            }

            AuthService auth = AuthService.Instance;
            if (auth == null || !auth.HasStoredSession)
            {
                // Chưa đăng nhập thì "kết nối được" đã là đủ.
                onDone?.Invoke(true);
                yield break;
            }

            // Xuống nền lâu thì access token gần như chắc chắn đã hết hạn.
            // Gia hạn ngay ở đây, thay vì để request đầu tiên của màn hình game
            // phải chịu một vòng 401 rồi mới thử lại.
            bool sessionReady = auth.IsSignedIn && !auth.ShouldRefreshNow;
            if (!sessionReady)
            {
                yield return auth.Refresh(ok => sessionReady = ok);
            }

            onDone?.Invoke(sessionReady);
        }

        /// Chờ tới khi nối lại được, hoặc hết giờ.
        ///
        /// Dùng cho màn hình "Mất kết nối, đang thử lại...". Tầng UI gọi hàm này
        /// rồi đóng thông báo khi nhận true.
        public IEnumerator WaitUntilOnline(Action<bool> onDone, float timeoutSeconds = 0f)
        {
            float limit = timeoutSeconds > 0f ? timeoutSeconds : reconnectTimeoutSeconds;
            float deadline = Time.realtimeSinceStartup + limit;

            while (Time.realtimeSinceStartup < deadline)
            {
                // Máy báo không có mạng thì khỏi tốn một request để biết điều đó.
                if (Application.internetReachability != NetworkReachability.NotReachable)
                {
                    bool ready = false;
                    yield return EnsureConnected(ok => ready = ok);
                    if (ready)
                    {
                        onDone?.Invoke(true);
                        yield break;
                    }
                }
                else
                {
                    SetOnline(false);
                }

                yield return new WaitForSecondsRealtime(retryIntervalSeconds);
            }

            onDone?.Invoke(false);
        }

        /// Người chơi có trận nào đang dở không.
        ///
        /// Gọi lúc vào game, sau khi đăng nhập xong. Server trả 204 khi không có
        /// trận nào, và khi đó kết quả là THÀNH CÔNG với session = null.
        ///
        ///     yield return monitor.FindInterruptedBattle(result =>
        ///     {
        ///         if (result.IsSuccess &amp;&amp; result.Value != null)
        ///             // đưa người chơi trở lại trận result.Value.session
        ///     });
        public IEnumerator FindInterruptedBattle(Action<ApiResult<CombatSessionResponseDto>> onDone)
        {
            var combat = new CombatGateway(_api);
            return combat.GetActiveSession(onDone);
        }

        private IEnumerator HandleResume()
        {
            bool ready = false;
            yield return EnsureConnected(ok => ready = ok);

            if (ready) OnResumed?.Invoke();
        }

        private void SetOnline(bool online)
        {
            if (online == IsOnline) return;

            IsOnline = online;
            if (online) OnBackOnline?.Invoke();
            else OnOffline?.Invoke();
        }
    }
}
