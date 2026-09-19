using System;
using System.Collections;
using UnityEngine;

namespace GachaGame.Network
{
    using GachaGame.Contracts.Api;

    /// Vòng đời phiên đăng nhập: đăng nhập, gia hạn, đăng xuất, khôi phục.
    ///
    /// Gắn cùng GameObject với APIClient. Trong Awake nó tự đăng ký làm
    /// ITokenRefresher cho APIClient, nên từ lúc đó mọi request đi qua APIClient
    /// đều được gia hạn token tự động, kể cả request do package khác gọi.
    ///
    /// Service này KHÔNG mở UI. Phiên hỏng thì nó phát event OnSessionExpired,
    /// việc đưa người chơi về màn hình đăng nhập là của tầng UI.
    [RequireComponent(typeof(APIClient))]
    public class AuthService : MonoBehaviour, ITokenRefresher
    {
        /// StatusCode của ApiError khi lỗi phát sinh phía client và chưa có
        /// request nào được gửi. Số âm để phân biệt với 0 (không nối được server).
        public const long ClientSideStatus = -1;

        [Header("Gia hạn")]
        [Tooltip("Đổi token khi nó chỉ còn dưới bấy nhiêu giây. Access token sống 15 phút.")]
        [SerializeField] private int refreshLeadSeconds = 60;

        [Tooltip("Request đang đợi một lần gia hạn khác sẽ bỏ cuộc sau bấy nhiêu giây.")]
        [SerializeField] private float refreshWaitTimeoutSeconds = 20f;

        [Header("Lưu phiên")]
        [Tooltip("Ghi refresh token xuống máy để lần mở game sau không phải đăng nhập lại.")]
        [SerializeField] private bool persistSession = true;

        private APIClient _api;
        private ITokenStorage _storage;
        private readonly SessionState _session = new SessionState();

        // Trạng thái single-flight của việc gia hạn.
        private bool _refreshing;
        private bool _lastRefreshSucceeded;

        public static AuthService Instance { get; private set; }

        /// Đang có access token dùng được.
        public bool IsSignedIn => _session.HasAccessToken;

        /// Có refresh token đã lưu, tức là gọi RestoreSession() được.
        public bool HasStoredSession => _session.HasUsableRefreshToken;

        public string UserId => _session.UserId;
        public bool IsGuest => _session.IsGuest;

        /// Đăng nhập thành công, bằng bất kỳ đường nào.
        public event Action<SignInResult> OnSignedIn;

        /// Người chơi chủ động đăng xuất.
        public event Action OnSignedOut;

        /// Phiên hết cứu: refresh token sai hoặc đã bị thu hồi. UI phải đưa người
        /// chơi về màn hình đăng nhập.
        public event Action OnSessionExpired;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            _api = GetComponent<APIClient>();
            _storage = _storage ?? new PlayerPrefsTokenStorage();
            _api.SetTokenRefresher(this);

            LoadPersistedSession();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// Đổi chỗ lưu phiên. Gọi trước Awake, hoặc ngay sau khi tạo component
        /// bằng AddComponent, nếu không muốn dùng PlayerPrefs.
        public void UseStorage(ITokenStorage storage)
        {
            _storage = storage;
        }

        // Đăng nhập
        public IEnumerator Login(string username, string password, Action<ApiResult<SignInResult>> onDone)
        {
            string payload = JsonUtility.ToJson(new PasswordLoginRequest
            {
                username = username,
                password = password,
            });

            yield return RequestTokens("/auth/login", payload, onDone);
        }

        public IEnumerator Register(string username, string password, string nickname,
                                    Action<ApiResult<SignInResult>> onDone, string email = null)
        {
            string payload = JsonUtility.ToJson(new PasswordRegisterRequest
            {
                username = username,
                password = password,
                nickname = nickname,
                email = email,
            });

            yield return RequestTokens("/auth/register", payload, onDone);
        }

        /// Tạo tài khoản khách. Server sinh sẵn username và password, và chỉ trả
        /// về đúng một lần — chúng được lưu lại cùng phiên để người chơi còn khôi
        /// phục được tài khoản.
        public IEnumerator LoginAsGuest(Action<ApiResult<SignInResult>> onDone)
        {
            ApiResult<SignInResult> outcome = default;
            bool handled = false;

            yield return _api.SendRequest("POST", "/auth/guest", "{}",
                body =>
                {
                    var response = ParseJson<GuestAuthResponse>(body);
                    if (response == null || string.IsNullOrEmpty(response.token))
                    {
                        outcome = ApiResult<SignInResult>.Fail(
                            ClientError("Response /auth/guest không đọc được."));
                        handled = true;
                        return;
                    }

                    _session.Adopt(response.ToTokenResponse());
                    _session.RememberGuestCredentials(response.guestUsername, response.guestPassword);
                    ApplyTokens();
                    Persist();

                    var result = new SignInResult(_session.UserId, true,
                                                  response.guestUsername, response.guestPassword);
                    outcome = ApiResult<SignInResult>.Ok(result);
                    handled = true;
                    OnSignedIn?.Invoke(result);
                },
                err =>
                {
                    outcome = ApiResult<SignInResult>.Fail(err);
                    handled = true;
                });

            if (!handled) outcome = ApiResult<SignInResult>.Fail(ClientError("Không nhận được phản hồi."));
            onDone?.Invoke(outcome);
        }

        /// Dùng refresh token đã lưu để lấy access token mới, không cần mật khẩu.
        /// Gọi lúc khởi động game.
        public IEnumerator RestoreSession(Action<ApiResult<SignInResult>> onDone)
        {
            if (!HasStoredSession)
            {
                onDone?.Invoke(ApiResult<SignInResult>.Fail(
                    ClientError("Chưa có phiên nào được lưu.")));
                yield break;
            }

            bool succeeded = false;
            yield return Refresh(ok => succeeded = ok);

            if (succeeded)
            {
                var result = new SignInResult(_session.UserId, _session.IsGuest, null, null);
                onDone?.Invoke(ApiResult<SignInResult>.Ok(result));
                OnSignedIn?.Invoke(result);
            }
            else
            {
                onDone?.Invoke(ApiResult<SignInResult>.Fail(
                    ClientError("Khôi phục phiên thất bại, cần đăng nhập lại.")));
            }
        }

        // Gia hạn — ITokenRefresher
        public bool CanRefresh => _session.HasUsableRefreshToken;

        public bool ShouldRefreshNow =>
            _session.HasAccessToken &&
            CanRefresh &&
            _session.IsAccessExpiringWithin(refreshLeadSeconds, _api.ServerUtcNow);

        public IEnumerator Refresh(Action<bool> onComplete)
        {
            // Nhiều request cùng dính 401 thì chỉ lần gọi đầu tiên thật sự đi ra
            // mạng. Số còn lại đợi ở đây rồi dùng chung kết quả — vừa tránh đốt
            // quota rate limit của /auth/refresh (5 lần/phút), vừa tránh việc hai
            // lần xoay vòng chồng nhau làm server coi là replay và thu hồi cả phiên.
            if (_refreshing)
            {
                float waitUntil = Time.realtimeSinceStartup + refreshWaitTimeoutSeconds;
                while (_refreshing && Time.realtimeSinceStartup < waitUntil)
                {
                    yield return null;
                }

                onComplete?.Invoke(!_refreshing && _lastRefreshSucceeded);
                yield break;
            }

            if (!CanRefresh)
            {
                onComplete?.Invoke(false);
                yield break;
            }

            _refreshing = true;
            _lastRefreshSucceeded = false;

            string payload = JsonUtility.ToJson(new RefreshTokenRequest
            {
                refreshToken = _session.RefreshToken,
            });

            // Endpoint bắt đầu bằng /auth/ nên APIClient không tự gia hạn cho nó,
            // tránh gọi đệ quy vô hạn.
            yield return _api.SendRequest("POST", "/auth/refresh", payload,
                body =>
                {
                    var response = ParseJson<AuthTokenResponse>(body);
                    if (response == null || string.IsNullOrEmpty(response.token))
                    {
                        Debug.LogError("[AuthService] Response /auth/refresh không đọc được.");
                        return;
                    }

                    _session.Adopt(response);
                    ApplyTokens();
                    Persist();
                    _lastRefreshSucceeded = true;
                },
                err =>
                {
                    if (err.IsSessionUnrecoverable)
                    {
                        // Server đã thu hồi cả chuỗi token. Không có cách nào cứu
                        // ngoài đăng nhập lại từ đầu.
                        Debug.LogWarning($"[AuthService] Phiên bị thu hồi: {err.Code}");
                        ClearSession();
                        OnSessionExpired?.Invoke();
                    }
                    else
                    {
                        // Mất mạng hoặc server lỗi tạm thời: giữ nguyên phiên để
                        // lần gọi sau còn thử lại được.
                        Debug.LogWarning($"[AuthService] Gia hạn token thất bại: {err}");
                    }
                });

            _refreshing = false;
            onComplete?.Invoke(_lastRefreshSucceeded);
        }

        // Đăng xuất
        /// Thu hồi refresh token hiện tại rồi xoá phiên. Phiên local luôn bị xoá,
        /// kể cả khi request hỏng — người chơi bấm đăng xuất thì phải được đăng xuất.
        public IEnumerator Logout(Action<ApiResult> onDone)
        {
            ApiResult outcome = ApiResult.Ok();

            if (!string.IsNullOrEmpty(_session.RefreshToken))
            {
                string payload = JsonUtility.ToJson(new RefreshTokenRequest
                {
                    refreshToken = _session.RefreshToken,
                });

                yield return _api.SendRequest("POST", "/auth/logout", payload,
                    _ => { },
                    err => outcome = ApiResult.Fail(err));
            }

            ClearSession();
            OnSignedOut?.Invoke();
            onDone?.Invoke(outcome);
        }

        /// Thu hồi mọi phiên của tài khoản trên mọi thiết bị.
        public IEnumerator RevokeAllSessions(Action<ApiResult> onDone)
        {
            ApiResult outcome = ApiResult.Ok();

            yield return _api.SendRequest("POST", "/auth/revoke-all", "{}",
                _ => { },
                err => outcome = ApiResult.Fail(err));

            ClearSession();
            OnSignedOut?.Invoke();
            onDone?.Invoke(outcome);
        }

        // Nội bộ
        private IEnumerator RequestTokens(string endpoint, string payload,
                                          Action<ApiResult<SignInResult>> onDone)
        {
            ApiResult<SignInResult> outcome = default;
            bool handled = false;

            yield return _api.SendRequest("POST", endpoint, payload,
                body =>
                {
                    var response = ParseJson<AuthTokenResponse>(body);
                    if (response == null || string.IsNullOrEmpty(response.token))
                    {
                        outcome = ApiResult<SignInResult>.Fail(
                            ClientError($"Response {endpoint} không đọc được."));
                        handled = true;
                        return;
                    }

                    _session.Adopt(response);
                    ApplyTokens();
                    Persist();

                    var result = new SignInResult(_session.UserId, false, null, null);
                    outcome = ApiResult<SignInResult>.Ok(result);
                    handled = true;
                    OnSignedIn?.Invoke(result);
                },
                err =>
                {
                    outcome = ApiResult<SignInResult>.Fail(err);
                    handled = true;
                });

            if (!handled) outcome = ApiResult<SignInResult>.Fail(ClientError("Không nhận được phản hồi."));
            onDone?.Invoke(outcome);
        }

        /// Đẩy token và khoá ký sang APIClient.
        ///
        /// Hai dòng này phải đi cùng nhau. Server sinh sessionKey MỚI mỗi lần cấp
        /// access token, kể cả khi refresh — chỉ đặt token mà quên khoá ký thì mọi
        /// endpoint có chữ ký sẽ trả 401 dù token hoàn toàn hợp lệ. Gom vào đúng
        /// một chỗ để không nơi nào có cơ hội quên.
        private void ApplyTokens()
        {
            _api.SetAuthToken(_session.AccessToken);

            if (!string.IsNullOrEmpty(_session.SessionKey))
            {
                _api.SetSessionSigningKey(_session.SessionKey);
            }
        }

        private void ClearSession()
        {
            _session.Clear();
            _api.ClearSession();
            _storage?.Clear();
        }

        private void Persist()
        {
            if (!persistSession || _storage == null) return;

            _storage.Save(new PersistedSession
            {
                userId = _session.UserId,
                refreshToken = _session.RefreshToken,
                refreshExpiresAt = _session.RefreshExpiresAtIso,
                guestUsername = _session.GuestUsername,
                guestPassword = _session.GuestPassword,
            });
        }

        private void LoadPersistedSession()
        {
            if (!persistSession || _storage == null) return;
            if (!_storage.TryLoad(out PersistedSession stored)) return;

            _session.UserId = stored.userId ?? string.Empty;
            _session.RefreshToken = stored.refreshToken ?? string.Empty;
            _session.RefreshExpiresAtIso = stored.refreshExpiresAt ?? string.Empty;
            _session.RememberGuestCredentials(stored.guestUsername, stored.guestPassword);
        }

        private static T ParseJson<T>(string json) where T : class
        {
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AuthService] Không parse được response: {e.Message}\n{json}");
                return null;
            }
        }

        private static ApiError ClientError(string message)
        {
            return new ApiError(ClientSideStatus, string.Empty, message, string.Empty, default, 0);
        }
    }
}