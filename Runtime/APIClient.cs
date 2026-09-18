using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GachaGame.Contracts.Api;
using UnityEngine;

namespace GachaGame.Network
{
    /// Client HTTP dùng chung cho toàn bộ game.
    ///
    /// Hợp đồng anti-cheat phía server (middleware/anticheat.go) yêu cầu 5 header
    /// và chuỗi ký gồm 6 phần, thiếu bất kỳ phần nào cũng bị chặn:
    ///
    ///   X-Timestamp     unix seconds
    ///   X-Nonce         16-128 ký tự, chỉ [A-Za-z0-9_-], server chỉ nhận 1 lần
    ///   X-BodyHash      SHA256 hex thường của body, BẮT BUỘC cả với GET
    ///   X-Client-Build  chuỗi build, phải giống hệt lúc ký
    ///   X-Signature     HMAC-SHA256 hex thường của chuỗi bên dưới
    ///
    ///   METHOD:PATH:TIMESTAMP:NONCE:BODYHASH:CLIENTBUILD
    ///
    /// PATH là đường dẫn không kèm query string, khớp với r.URL.Path của server.
    public class APIClient : MonoBehaviour
    {
        // Cấu hình
        [Header("Endpoint")]
        [Tooltip("Dùng trong Editor và Development Build.")]
        [SerializeField] private string editorBaseUrl = "http://localhost:3000/api";

        [Tooltip("Dùng trong bản Release.")]
        [SerializeField] private string productionBaseUrl = "https://your-production-domain.com/api";

        [Header("Anti-Cheat")]
        [Tooltip("Gửi qua header X-Client-Build và nằm trong chuỗi ký. Server chỉ yêu cầu khác rỗng.")]
        [SerializeField] private string clientBuild = "development";

        // KHÔNG có field cho HMAC_SECRET_KEY ở đây, và đó là chủ ý.
        //
        // Mọi route đi qua AntiCheatMiddleware đều nằm sau AuthMiddleware
        // (router.go chỉ dùng nó ở đúng một chỗ), nên client luôn có sessionKey
        // do server cấp lúc đăng nhập. Khóa tĩnh không bao giờ cần tới, mà để nó
        // trong một SerializeField nghĩa là đóng gói secret của server vào file
        // build — ai giải nén APK cũng đọc được.
        //
        // Cần khóa tĩnh để debug thì gọi Configure(secretKey: ...), và đường đó
        // chỉ tồn tại trong Editor với Development Build.

        [Header("Request")]
        [Tooltip("Giây. 0 nghĩa là không giới hạn.")]
        [SerializeField] private int timeoutSeconds = 15;

        [Tooltip("Số lần gửi lại khi request KHÔNG tới được server. Áp dụng cho GET và cho POST có khoá chống trùng.")]
        [SerializeField] private int transientRetryCount = 1;

        [Tooltip("Giây chờ giữa hai lần gửi lại.")]
        [SerializeField] private float transientRetryDelaySeconds = 0.5f;

        [Tooltip("Ghi log X-Request-Id khi request hỏng, để đối chiếu với log của server.")]
        [SerializeField] private bool logFailedRequests = true;

        [Header("Ngôn ngữ")]
        [Tooltip("Gửi qua header Accept-Language, server dùng cho email xác minh và đặt lại mật khẩu. " +
                 "Để trống thì lấy theo ngôn ngữ hệ thống.")]
        [SerializeField] private string acceptLanguage = "";

        // Trạng thái
        private string _authToken = "";
        private string _sessionSigningKey = null;
        private bool _warnedMissingKey = false;
        private ITokenRefresher _refresher = null;

        /// Chênh lệch giữa đồng hồ server và đồng hồ máy này, đồng bộ từ header
        /// Date của mọi response. Xem ServerUtcNow.
        private TimeSpan _serverTimeOffset = TimeSpan.Zero;
        private bool _clockSynced = false;

        private bool _shuttingDown = false;

        /// Tầng vận chuyển. Mặc định là UnityWebRequest; test thay bằng bản giả
        /// để chạy được luồng gia hạn và gửi lại mà không cần server.
        private ITransport _transport;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// Khóa ký tĩnh chỉ dùng để debug. Không tồn tại trong bản release.
        private string _debugSigningKey = "";
#endif

        public static APIClient Instance { get; private set; }

        /// URL gốc đang dùng, đã tính theo loại build.
        public string BaseUrl
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return editorBaseUrl;
#else
                return productionBaseUrl;
#endif
            }
        }

        /// true khi đã có khóa ký. Không có khóa thì mọi endpoint qua AntiCheatMiddleware đều bị chặn.
        public bool HasSigningKey => !string.IsNullOrEmpty(ResolveSigningKey());

        /// true khi đã đăng nhập và có Bearer token.
        public bool IsAuthenticated => !string.IsNullOrEmpty(_authToken);

        // Vòng đời
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Chỉ giữ qua các lần load scene khi đang chạy game. Gọi trong edit
            // mode (ví dụ từ EditMode test) sẽ bị Unity cảnh báo.
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            // Hủy mọi request đang bay. Không hủy thì callback sẽ chạy sau khi
            // object gọi nó đã bị destroy, và lỗi hiện ra dưới dạng
            // MissingReferenceException ở một chỗ chẳng liên quan gì.
            _shuttingDown = true;
            _transport?.AbortAll();

            // Không xóa Instance nếu bản sao thừa vừa bị Destroy trong Awake.
            if (Instance == this) Instance = null;
        }

        // API công khai — cấu hình và phiên
        /// Ghi đè cấu hình lúc chạy. Dùng cho test hoặc cho nhiều môi trường.
        /// Truyền null cho tham số nào muốn giữ nguyên.
        ///
        /// secretKey là khóa HMAC tĩnh và CHỈ dùng để debug: bản release bỏ qua
        /// nó hoàn toàn. Luồng bình thường không cần, vì AuthService đã đặt
        /// sessionKey do server cấp.
        public void Configure(string baseUrl = null, string build = null, string secretKey = null,
                              int? timeout = null, int? transientRetries = null,
                              float? retryDelaySeconds = null)
        {
            if (transientRetries.HasValue) transientRetryCount = Mathf.Max(0, transientRetries.Value);
            if (retryDelaySeconds.HasValue) transientRetryDelaySeconds = Mathf.Max(0f, retryDelaySeconds.Value);

            if (!string.IsNullOrEmpty(baseUrl))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                editorBaseUrl = baseUrl;
#else
                productionBaseUrl = baseUrl;
#endif
            }
            if (!string.IsNullOrEmpty(build)) clientBuild = build;
            if (timeout.HasValue) timeoutSeconds = Mathf.Max(0, timeout.Value);

            if (secretKey != null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _debugSigningKey = secretKey;
#else
                Debug.LogError("[APIClient] Bản release không nhận khóa HMAC tĩnh. " +
                               "Đăng nhập để lấy sessionKey do server cấp.");
#endif
            }

            _warnedMissingKey = false;
        }

        /// Giờ UTC theo đồng hồ server, đã bù phần lệch của máy này.
        ///
        /// Chữ ký anti-cheat chỉ sống 30 giây và không được sớm hơn giờ server
        /// quá 5 giây, nên máy người chơi lệch giờ là mọi request có ký chết sạch.
        /// Phần bù được đồng bộ từ header Date của mọi response, kể cả response
        /// lỗi, nên nó tự sửa dần mà không cần gọi thêm endpoint nào.
        ///
        /// Dùng luôn cho những chỗ cần giờ server, ví dụ đếm ngược hạn banner.
        public DateTime ServerUtcNow => DateTime.UtcNow.Add(_serverTimeOffset);

        /// Đồng hồ máy này chạy lệch bao nhiêu so với server. Dương nghĩa là máy chậm.
        public TimeSpan ServerTimeOffset => _serverTimeOffset;

        /// Đã nhận được ít nhất một response để đối chiếu giờ hay chưa.
        public bool IsClockSynced => _clockSynced;

        /// Tầng vận chuyển đang dùng. Tạo bản thật khi lần đầu cần tới.
        private ITransport Transport =>
            _transport ?? (_transport = new UnityWebRequestTransport(() => timeoutSeconds));

        /// Thay tầng vận chuyển. Chỉ dùng trong test: truyền vào một bản giả để
        /// chạy được luồng gia hạn token và gửi lại mà không cần server thật.
        public void SetTransport(ITransport transport)
        {
            _transport = transport;
        }

        /// Đăng ký bộ gia hạn token. AuthService tự gọi hàm này trong Awake.
        ///
        /// Có bộ gia hạn thì SendRequest tự đổi token sắp hết hạn và tự gửi lại
        /// khi bị 401. Không có thì client vẫn chạy, chỉ là token hết hạn sẽ nổi
        /// lên thành lỗi cho phần gọi tự xử lý.
        public void SetTokenRefresher(ITokenRefresher refresher)
        {
            _refresher = refresher;
        }

        /// Lưu Bearer token nhận được từ /auth/login.
        public void SetAuthToken(string token)
        {
            _authToken = token ?? "";
        }

        /// Đặt khóa ký riêng cho phiên. Khi có giá trị, nó được dùng thay cho hmacSecretKey.
        ///
        /// Server cấp khóa này trong response của login, register, guest và refresh
        /// (trường sessionKey), đồng thời nhúng nó vào JWT ở claim "sk".
        /// AntiCheatMiddleware ưu tiên dùng nó thay cho HMAC_SECRET_KEY tĩnh.
        ///
        /// Khóa ĐỔI sau mỗi lần cấp access token, kể cả khi refresh. Đặt token mới
        /// mà quên gọi hàm này thì mọi endpoint có ký sẽ trả ERR_ANTICHEAT_INVALID_SIGNATURE
        /// dù token hoàn toàn hợp lệ. AuthService gom hai việc đó vào cùng một chỗ.
        public void SetSessionSigningKey(string serverIssuedKey)
        {
            if (string.IsNullOrEmpty(serverIssuedKey))
            {
                Debug.LogError("[APIClient] Khóa ký server trả về rỗng.");
                return;
            }

            _sessionSigningKey = serverIssuedKey;
            _warnedMissingKey = false;
            Debug.Log("[APIClient] Đã nhận khóa ký theo phiên.");
        }

        /// Xóa toàn bộ trạng thái đăng nhập. Gọi khi logout hoặc phiên hết hạn.
        public void ClearSession()
        {
            _authToken = "";
            _sessionSigningKey = null;
            Debug.Log("[APIClient] Đã xóa phiên.");
        }

        // API công khai — gửi request
        /// POST kèm body JSON.
        /// endpoint tính từ sau /api, ví dụ "/gacha/summon".
        public IEnumerator PostRequest(string endpoint, string jsonPayload, Action<string> onSuccess, Action<string> onError)
        {
            return SendRequest(UnityWebRequestVerb.Post, endpoint, jsonPayload, onSuccess,
                               err => onError?.Invoke(err.ToString()));
        }

        /// GET không body. Query string đưa thẳng vào endpoint, ví dụ "/character/template?baseId=char_001".
        public IEnumerator GetRequest(string endpoint, Action<string> onSuccess, Action<string> onError)
        {
            return SendRequest(UnityWebRequestVerb.Get, endpoint, null, onSuccess,
                               err => onError?.Invoke(err.ToString()));
        }

        /// Bản đầy đủ. Dùng khi cần HTTP status hoặc body lỗi của server,
        /// ví dụ để phân biệt 401 hết hạn token với 403 sai chữ ký.
        /// Chỉ có một overload nhận Action&lt;ApiError&gt; nên truyền lambda không bị nhập nhằng.
        ///
        /// Khi đã có ITokenRefresher (AuthService tự đăng ký), hàm này lo luôn
        /// việc gia hạn token: đổi trước nếu token sắp hết hạn, và gửi lại đúng
        /// một lần nếu server từ chối token.
        /// idempotent = true nghĩa là request này mang khoá chống trùng, nên gửi
        /// lại an toàn kể cả với POST. Xem SendWithTransientRetry.
        public IEnumerator SendRequest(string method, string endpoint, string jsonPayload,
                                       Action<string> onSuccess, Action<ApiError> onError,
                                       bool idempotent = false)
        {
            // Gia hạn chủ động: đổi token trước khi gửi thì rẻ hơn để server trả
            // 401 rồi mới gửi lại, và tránh được một vòng round-trip thừa.
            if (SupportsAutoRefresh(endpoint) && _refresher.ShouldRefreshNow)
            {
                yield return _refresher.Refresh(null);
            }

            var attempt = new Attempt();
            yield return SendWithTransientRetry(method, endpoint, jsonPayload, attempt, idempotent);

            // Gia hạn bị động: token bị từ chối thì đổi token rồi gửi lại, đúng
            // MỘT lần. Lần gửi lại đi qua SendOnce nên nó tự sinh nonce và chữ ký
            // mới — gửi lại nguyên văn request cũ sẽ ăn ERR_ANTICHEAT_NONCE_REUSED.
            if (attempt.Failed && attempt.Error.IsAccessTokenRejected &&
                SupportsAutoRefresh(endpoint) && _refresher.CanRefresh)
            {
                bool refreshed = false;
                yield return _refresher.Refresh(ok => refreshed = ok);

                if (refreshed)
                {
                    attempt = new Attempt();
                    yield return SendWithTransientRetry(method, endpoint, jsonPayload, attempt, idempotent);
                }
            }

            // Component đang bị hủy: callback sẽ chạy vào object có thể đã chết.
            if (_shuttingDown) yield break;

            if (attempt.Succeeded) onSuccess?.Invoke(attempt.Body);
            else onError?.Invoke(attempt.Error);
        }

        /// Gửi lại khi request KHÔNG tới được server: mất sóng, DNS hỏng, wifi đổi.
        ///
        /// Hai loại request được gửi lại:
        ///
        ///   GET — đọc dữ liệu, gọi lại bao nhiêu lần cũng không đổi gì.
        ///   POST có khoá chống trùng — server nhận ra request lặp nhờ khoá đó và
        ///     trả về kết quả cũ thay vì làm lại. /gacha/summon là ví dụ: nó mang
        ///     requestId và server lưu biên nhận theo khoá này.
        ///
        /// POST KHÔNG có khoá chống trùng thì tuyệt đối không gửi lại: server có
        /// thể đã xử lý xong rồi mới đứt đường về, gửi lại là trừ tiền hai lần.
        /// Nonce không cứu được chuyện đó, vì nó chỉ chặn việc phát lại NGUYÊN VĂN,
        /// còn ở đây SendOnce ký lại nên server thấy một request mới hợp lệ.
        ///
        /// Chỉ gửi lại khi request không tới được server. Lỗi do server trả về
        /// (429, 5xx) thì để nguyên cho phần gọi quyết định.
        private IEnumerator SendWithTransientRetry(string method, string endpoint,
                                                   string jsonPayload, Attempt attempt,
                                                   bool idempotent)
        {
            bool retryable = idempotent ||
                             string.Equals(method, UnityWebRequestVerb.Get,
                                           StringComparison.OrdinalIgnoreCase);
            int attemptsLeft = retryable ? Mathf.Max(0, transientRetryCount) : 0;

            while (true)
            {
                yield return SendOnce(method, endpoint, jsonPayload, attempt);

                if (attempt.Succeeded || _shuttingDown) yield break;
                if (!attempt.Error.IsNetworkFailure || attemptsLeft <= 0) yield break;

                attemptsLeft--;
                if (transientRetryDelaySeconds > 0f)
                {
                    // Realtime để lúc game đang pause (timeScale = 0) vẫn chạy.
                    yield return new WaitForSecondsRealtime(transientRetryDelaySeconds);
                }
            }
        }

        /// Nhóm /auth tự lo phần token của nó. Cho phép gia hạn ở đây sẽ thành
        /// đệ quy vô hạn khi chính /auth/refresh trả 401.
        private bool SupportsAutoRefresh(string endpoint)
        {
            return _refresher != null &&
                   !string.IsNullOrEmpty(endpoint) &&
                   !endpoint.StartsWith("/auth/", StringComparison.Ordinal);
        }

        /// Kết quả của đúng một lần gửi. Tách ra vì coroutine không trả giá trị
        /// được, mà luồng gửi lại cần đọc kết quả của lần trước.
        private sealed class Attempt
        {
            public bool Succeeded;
            public string Body;
            public ApiError Error;

            public bool Failed => !Succeeded;
        }

        private IEnumerator SendOnce(string method, string endpoint, string jsonPayload, Attempt attempt)
        {
            string url = BaseUrl + endpoint;
            byte[] bodyRaw = string.IsNullOrEmpty(jsonPayload)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(jsonPayload);

            var request = new TransportRequest(method, url, bodyRaw);

            if (bodyRaw.Length > 0) request.With("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(_authToken)) request.With("Authorization", "Bearer " + _authToken);

            // Server đọc header này trong httputil.GetLanguage để chọn ngôn ngữ cho
            // email xác minh và đặt lại mật khẩu. Nó KHÔNG nằm trong chuỗi ký nên
            // thêm vào không ảnh hưởng chữ ký.
            request.With("Accept-Language", ResolveAcceptLanguage());

            ApplySigningHeaders(request, method, url, bodyRaw);

            DateTime sentAt = DateTime.UtcNow;
            TransportResponse response = null;
            yield return Transport.Send(request, result => response = result);

            if (response == null)
            {
                attempt.Succeeded = false;
                attempt.Error = ApiError.FromResponse(0, "Tầng vận chuyển không trả kết quả", "", null);
                yield break;
            }

            SyncClockFrom(response, sentAt);

            if (response.IsSuccess)
            {
                attempt.Succeeded = true;
                attempt.Body = response.Body;
            }
            else
            {
                // Server trả lỗi dạng {"success":false,"code":"ERR_...","params":{...}}.
                // ApiError tách sẵn code và params ra khỏi body để phần gọi bắt
                // theo mã chứ không phải so chuỗi.
                attempt.Succeeded = false;
                attempt.Error = ApiError.FromResponse(response.StatusCode, response.TransportError,
                                                      response.Body, response.Header("Retry-After"));

                LogFailure(method, endpoint, response, attempt.Error);
            }
        }

        private string ResolveAcceptLanguage()
        {
            if (!string.IsNullOrEmpty(acceptLanguage)) return acceptLanguage;
            return Application.systemLanguage == SystemLanguage.Vietnamese ? "vi" : "en";
        }

        /// Đồng bộ chênh lệch đồng hồ từ header Date của response.
        /// Phần tính toán nằm ở ServerClock để test được mà không cần Unity.
        private void SyncClockFrom(TransportResponse response, DateTime sentAt)
        {
            // Không tới được server thì không có Date nào đáng tin.
            if (!response.ReachedServer) return;

            if (!ServerClock.TryComputeOffset(response.Header("Date"),
                                              sentAt, DateTime.UtcNow, out TimeSpan offset))
            {
                return;
            }

            bool firstSync = !_clockSynced;
            _serverTimeOffset = offset;
            _clockSynced = true;

            // Chỉ cảnh báo một lần: lệch giờ là vấn đề của máy người chơi, và từ
            // đây chữ ký đã được bù nên nó không còn làm hỏng request nữa.
            if (firstSync && Math.Abs(offset.TotalSeconds) > ServerClock.NoticeableSkewSeconds)
            {
                Debug.LogWarning(
                    $"[APIClient] Đồng hồ máy lệch {offset.TotalSeconds:F0}s so với server. " +
                    "Chữ ký anti-cheat đã được bù theo giờ server.");
            }
        }

        /// Ghi lại X-Request-Id để đối chiếu với log phía server khi cần nhờ
        /// backend tra một request cụ thể.
        private void LogFailure(string method, string endpoint, TransportResponse response, ApiError error)
        {
            if (!logFailedRequests) return;

            string requestId = response.Header("X-Request-Id");
            string suffix = string.IsNullOrEmpty(requestId) ? "" : $" requestId={requestId}";
            Debug.LogWarning($"[APIClient] {method} {endpoint} → {error}{suffix}");
        }

        // Ký anti-cheat
        private void ApplySigningHeaders(TransportRequest request, string method, string url, byte[] bodyRaw)
        {
            string signingKey = ResolveSigningKey();
            if (string.IsNullOrEmpty(signingKey))
            {
                if (!_warnedMissingKey)
                {
                    _warnedMissingKey = true;
                    Debug.LogWarning("[APIClient] Chưa có khóa ký cho phiên — nghĩa là chưa đăng nhập. " +
                                     "Mọi endpoint đi qua AntiCheatMiddleware sẽ bị trả 401.");
                }
                return;
            }

            // Ký bằng GIỜ SERVER, không phải giờ máy. Server chỉ nhận chữ ký trong
            // khoảng 30 giây quá khứ tới 5 giây tương lai, nên máy lệch giờ mà ký
            // bằng giờ máy là ăn ERR_ANTICHEAT_REQUEST_EXPIRED liên tục.
            var signed = RequestSigner.Create(signingKey, method, RequestSigner.PathOf(url),
                                              ServerUtcNow, clientBuild, bodyRaw);

            request.With(RequestSigner.HeaderTimestamp, signed.Timestamp)
                   .With(RequestSigner.HeaderNonce, signed.Nonce)
                   .With(RequestSigner.HeaderBodyHash, signed.BodyHash)
                   .With(RequestSigner.HeaderClientBuild, signed.ClientBuild)
                   .With(RequestSigner.HeaderSignature, signed.Signature);
        }

        /// Khóa ký của phiên hiện tại.
        ///
        /// Luồng bình thường chỉ có một nguồn: sessionKey do server cấp lúc đăng
        /// nhập. Nhánh khóa tĩnh chỉ tồn tại trong Editor và Development Build,
        /// bản release không có đường nào để mang secret của server theo.
        private string ResolveSigningKey()
        {
            if (!string.IsNullOrEmpty(_sessionSigningKey)) return _sessionSigningKey;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return _debugSigningKey;
#else
            return string.Empty;
#endif
        }

    }
}
