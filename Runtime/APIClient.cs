using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

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
        // Hằng số
        /// SHA256 của chuỗi rỗng. Server tự băm body rỗng ra đúng giá trị này,
        /// nên GET và POST không body vẫn phải gửi X-BodyHash bằng nó.
        private const string EmptyBodySha256 =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        // Cấu hình
        [Header("Endpoint")]
        [Tooltip("Dùng trong Editor và Development Build.")]
        [SerializeField] private string editorBaseUrl = "http://localhost:3000/api";

        [Tooltip("Dùng trong bản Release.")]
        [SerializeField] private string productionBaseUrl = "https://your-production-domain.com/api";

        [Header("Anti-Cheat")]
        [Tooltip("Gửi qua header X-Client-Build và nằm trong chuỗi ký. Server chỉ yêu cầu khác rỗng.")]
        [SerializeField] private string clientBuild = "development";

        [Tooltip("Phải trùng HMAC_SECRET_KEY trong .env của server. " +
                 "Để trống ở đây và gọi Configure() lúc chạy nếu không muốn giá trị nằm trong asset.")]
        [SerializeField] private string hmacSecretKey = "";

        [Header("Request")]
        [Tooltip("Giây. 0 nghĩa là không giới hạn.")]
        [SerializeField] private int timeoutSeconds = 15;

        // Trạng thái
        private string _authToken = "";
        private string _sessionSigningKey = null;
        private bool _warnedMissingKey = false;

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
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            // Không xóa Instance nếu bản sao thừa vừa bị Destroy trong Awake.
            if (Instance == this) Instance = null;
        }

        // API công khai — cấu hình và phiên
        /// Ghi đè cấu hình lúc chạy. Dùng cho test, cho nhiều môi trường,
        /// hoặc khi không muốn khóa HMAC nằm sẵn trong asset.
        /// Truyền null cho tham số nào muốn giữ nguyên.
        public void Configure(string baseUrl = null, string build = null, string secretKey = null, int? timeout = null)
        {
            if (!string.IsNullOrEmpty(baseUrl))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                editorBaseUrl = baseUrl;
#else
                productionBaseUrl = baseUrl;
#endif
            }
            if (!string.IsNullOrEmpty(build)) clientBuild = build;
            if (secretKey != null) hmacSecretKey = secretKey;
            if (timeout.HasValue) timeoutSeconds = Mathf.Max(0, timeout.Value);

            _warnedMissingKey = false;
        }

        /// Lưu Bearer token nhận được từ /auth/login.
        public void SetAuthToken(string token)
        {
            _authToken = token ?? "";
        }

        /// Đặt khóa ký riêng cho phiên. Khi có giá trị, nó được dùng thay cho hmacSecretKey.
        ///
        /// Hiện server chưa cấp khóa theo phiên — nó ký bằng HMAC_SECRET_KEY tĩnh trong .env.
        /// Hàm này giữ lại để khi server đổi sang cấp khóa lúc login thì client không phải sửa.
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
            return SendRequest(UnityWebRequest.kHttpVerbPOST, endpoint, jsonPayload, onSuccess,
                               err => onError?.Invoke(err.ToString()));
        }

        /// GET không body. Query string đưa thẳng vào endpoint, ví dụ "/character/template?baseId=char_001".
        public IEnumerator GetRequest(string endpoint, Action<string> onSuccess, Action<string> onError)
        {
            return SendRequest(UnityWebRequest.kHttpVerbGET, endpoint, null, onSuccess,
                               err => onError?.Invoke(err.ToString()));
        }

        /// Bản đầy đủ. Dùng khi cần HTTP status hoặc body lỗi của server,
        /// ví dụ để phân biệt 401 hết hạn token với 403 sai chữ ký.
        /// Chỉ có một overload nhận Action&lt;ApiError&gt; nên truyền lambda không bị nhập nhằng.
        public IEnumerator SendRequest(string method, string endpoint, string jsonPayload,
                                       Action<string> onSuccess, Action<ApiError> onError)
        {
            string url = BaseUrl + endpoint;
            byte[] bodyRaw = string.IsNullOrEmpty(jsonPayload)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(jsonPayload);

            using (UnityWebRequest request = new UnityWebRequest(url, method))
            {
                if (bodyRaw.Length > 0)
                {
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.SetRequestHeader("Content-Type", "application/json");
                }

                request.downloadHandler = new DownloadHandlerBuffer();
                if (timeoutSeconds > 0) request.timeout = timeoutSeconds;

                if (!string.IsNullOrEmpty(_authToken))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + _authToken);
                }

                ApplyAntiCheatHeaders(request, method, url, bodyRaw);

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke(request.downloadHandler.text);
                }
                else
                {
                    // Server trả lỗi dạng JSON có code và message, giữ lại nguyên văn
                    // thay vì chỉ đưa request.error như trước.
                    onError?.Invoke(new ApiError(
                        request.responseCode,
                        request.error,
                        request.downloadHandler != null ? request.downloadHandler.text : ""));
                }
            }
        }

        // Ký anti-cheat
        private void ApplyAntiCheatHeaders(UnityWebRequest request, string method, string url, byte[] bodyRaw)
        {
            string signingKey = ResolveSigningKey();
            if (string.IsNullOrEmpty(signingKey))
            {
                if (!_warnedMissingKey)
                {
                    _warnedMissingKey = true;
                    Debug.LogWarning("[APIClient] Chưa có khóa ký HMAC. " +
                                     "Mọi endpoint đi qua AntiCheatMiddleware sẽ bị trả 401.");
                }
                return;
            }

            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            string nonce = Guid.NewGuid().ToString("N");     // 32 ký tự hex, hợp lệ với luật nonce của server
            string bodyHash = ComputeBodyHash(bodyRaw);
            string path = new Uri(url).AbsolutePath;          // bỏ query, khớp r.URL.Path

            // Thứ tự và số lượng phần của chuỗi này phải giống hệt server, xem anticheat.go.
            string payload = string.Join(":", method, path, timestamp, nonce, bodyHash, clientBuild);
            string signature = ToHexLower(HmacSha256(signingKey, payload));

            request.SetRequestHeader("X-Timestamp", timestamp);
            request.SetRequestHeader("X-Nonce", nonce);
            request.SetRequestHeader("X-BodyHash", bodyHash);
            request.SetRequestHeader("X-Client-Build", clientBuild);
            request.SetRequestHeader("X-Signature", signature);
        }

        private string ResolveSigningKey()
        {
            return !string.IsNullOrEmpty(_sessionSigningKey) ? _sessionSigningKey : hmacSecretKey;
        }

        /// Băm đúng số byte sẽ gửi đi. Body rỗng cho ra SHA256 của chuỗi rỗng,
        /// không phải chuỗi rỗng — server luôn tự băm nên hai bên phải cùng quy ước.
        private static string ComputeBodyHash(byte[] bodyRaw)
        {
            if (bodyRaw == null || bodyRaw.Length == 0) return EmptyBodySha256;

            using (var sha256 = SHA256.Create())
            {
                return ToHexLower(sha256.ComputeHash(bodyRaw));
            }
        }

        private static byte[] HmacSha256(string key, string message)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            }
        }

        private static string ToHexLower(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }

    /// Lỗi trả về từ một lần gọi API, giữ cả HTTP status lẫn body JSON của server.
    public class ApiError
    {
        /// HTTP status code. 0 khi không kết nối được tới server.
        public readonly long StatusCode;

        /// Mô tả lỗi của UnityWebRequest.
        public readonly string Message;

        /// Nguyên văn body server trả về, thường là JSON có code và message.
        public readonly string Body;

        public ApiError(long statusCode, string message, string body)
        {
            StatusCode = statusCode;
            Message = message;
            Body = body;
        }

        /// true khi request không tới được server, phân biệt với lỗi do server từ chối.
        public bool IsNetworkFailure => StatusCode == 0;

        public override string ToString()
        {
            return string.IsNullOrEmpty(Body)
                ? $"[{StatusCode}] {Message}"
                : $"[{StatusCode}] {Message} | {Body}";
        }
    }
}
