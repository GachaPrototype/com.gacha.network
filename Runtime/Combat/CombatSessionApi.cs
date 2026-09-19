using System;
using GachaGame.Contracts.Combat;
using UnityEngine.Networking;

namespace GachaGame.Network.Combat
{
    /// <summary>
    /// Cài đặt <see cref="ICombatSessionApi"/> trên <see cref="APIClient"/>.
    ///
    /// Đây là nơi duy nhất trong luồng combat biết tới HTTP. `com.gacha.battle` chỉ thấy interface
    /// ở `com.gacha.contracts`, còn lớp này do composition root (scene) tạo và tiêm vào.
    ///
    /// Ba việc lớp này làm ngoài chuyện gọi đúng route:
    ///  - dịch <see cref="ApiError"/> của tầng mạng sang <see cref="CombatApiError"/>, đọc sẵn `code`
    ///    và `params.reactionDeadlineUnixMs` để battle không phải parse lại body lỗi;
    ///  - đổi 204 của /active thành `null` thay vì chuỗi rỗng;
    ///  - bỏ qua callback của phiên đăng nhập cũ, xem <see cref="SessionEpoch"/>.
    /// </summary>
    public sealed class CombatSessionApi : ICombatSessionApi
    {
        private const string Base = "/combat/session";

        private readonly APIClient _explicitClient;

        /// <summary>
        /// Tăng mỗi lần đổi phiên đăng nhập. Mọi request chụp giá trị này lúc gửi và bỏ qua
        /// response nếu lúc về giá trị đã khác, tránh việc response của phiên cũ ghi đè state mới.
        /// Composition root gọi <see cref="InvalidateSession"/> ngay sau khi login hoặc logout.
        ///
        /// Khi nhánh `s4ory_gacha` merge vào `dev`, `APIClient` sẽ có sẵn `SessionVersion` và
        /// chỗ này chuyển sang đọc từ đó thay vì tự đếm.
        /// </summary>
        public int SessionEpoch { get; private set; }

        public CombatSessionApi(APIClient client = null)
        {
            _explicitClient = client;
        }

        private APIClient Client => _explicitClient != null ? _explicitClient : APIClient.Instance;

        public void InvalidateSession()
        {
            SessionEpoch++;
        }

        // ---------- 8 route ----------

        public void CreateSession(NetCreateSessionRequest request, Action<string> onSuccess, Action<CombatApiError> onError)
            => Post(Base + "/create", CombatJson.Serialize(request), onSuccess, onError);

        public void SubmitSetup(NetSetupRequest request, Action<string> onSuccess, Action<CombatApiError> onError)
            => Post(Base + "/setup", CombatJson.Serialize(request), onSuccess, onError);

        public void SubmitReactions(NetReactionRequest request, Action<string> onSuccess, Action<CombatApiError> onError)
            => Post(Base + "/react", CombatJson.Serialize(request), onSuccess, onError);

        public void Execute(NetSessionRequest request, Action<string> onSuccess, Action<CombatApiError> onError)
            => Post(Base + "/execute", CombatJson.Serialize(request), onSuccess, onError);

        public void EndSession(NetSessionRequest request, Action<string> onSuccess, Action<CombatApiError> onError)
            => Post(Base + "/end", CombatJson.Serialize(request), onSuccess, onError);

        public void GetState(string sessionId, Action<string> onSuccess, Action<CombatApiError> onError)
            => Get(Base + "/state?sessionId=" + Uri.EscapeDataString(sessionId ?? string.Empty), onSuccess, onError);

        public void GetEnemies(string sessionId, Action<string> onSuccess, Action<CombatApiError> onError)
            => Get(Base + "/enemies?sessionId=" + Uri.EscapeDataString(sessionId ?? string.Empty), onSuccess, onError);

        /// <summary>Không có trận dở thì server trả 204 không body; onSuccess nhận null.</summary>
        public void GetActive(Action<string> onSuccess, Action<CombatApiError> onError)
            => Get(Base + "/active", body => onSuccess?.Invoke(string.IsNullOrWhiteSpace(body) ? null : body), onError);

        // ---------- Hạ tầng ----------

        private void Post(string endpoint, string json, Action<string> onSuccess, Action<CombatApiError> onError)
            => Send(UnityWebRequest.kHttpVerbPOST, endpoint, json, onSuccess, onError);

        private void Get(string endpoint, Action<string> onSuccess, Action<CombatApiError> onError)
            => Send(UnityWebRequest.kHttpVerbGET, endpoint, null, onSuccess, onError);

        private void Send(string method, string endpoint, string json, Action<string> onSuccess, Action<CombatApiError> onError)
        {
            APIClient client = Client;
            if (client == null)
            {
                Fail(onError, "ERR_CLIENT_MISSING", "Scene chưa có APIClient. Composition root phải tạo nó trước khi vào trận.");
                return;
            }
            if (!client.isActiveAndEnabled)
            {
                Fail(onError, "ERR_CLIENT_INACTIVE", "APIClient đang tắt nên không chạy được coroutine.");
                return;
            }

            int epoch = SessionEpoch;
            client.StartCoroutine(client.SendRequest(
                method, endpoint, json,
                body => { if (epoch == SessionEpoch) onSuccess?.Invoke(body); },
                err => { if (epoch == SessionEpoch) onError?.Invoke(Translate(err)); }));
        }

        private static void Fail(Action<CombatApiError> onError, string code, string message)
        {
            onError?.Invoke(new CombatApiError { HttpStatus = 0, Code = code, Message = message, RawBody = string.Empty });
        }

        /// <summary>Đọc envelope lỗi của server: { success:false, code, message?, params? }.</summary>
        private static CombatApiError Translate(ApiError source)
        {
            var result = new CombatApiError
            {
                HttpStatus = source.StatusCode,
                Message = source.Message,
                RawBody = source.Body ?? string.Empty,
                Code = string.Empty
            };

            if (!string.IsNullOrWhiteSpace(source.Body))
            {
                try
                {
                    NetApiErrorEnvelope envelope = CombatJson.Parse<NetApiErrorEnvelope>(source.Body);
                    if (envelope != null)
                    {
                        if (!string.IsNullOrEmpty(envelope.code)) result.Code = envelope.code;
                        if (!string.IsNullOrEmpty(envelope.message)) result.Message = envelope.message;
                        if (envelope.@params != null) result.ReactionDeadlineUnixMs = envelope.@params.reactionDeadlineUnixMs;
                    }
                }
                catch (Exception)
                {
                    // Body không phải JSON, ví dụ trang lỗi của proxy. Giữ nguyên RawBody để gỡ rối.
                }
            }

            return result;
        }
    }
}