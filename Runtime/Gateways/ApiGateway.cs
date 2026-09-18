using System;
using System.Collections;
using GachaGame.Contracts.Api;
using UnityEngine;

namespace GachaGame.Network
{
    /// Nền chung cho các gateway theo từng mảng tính năng.
    ///
    /// Quy tắc kiến trúc của dự án: HTTP chỉ được triển khai trong
    /// com.gacha.network. Các package khác gọi gateway và nhận ApiResult, không
    /// bao giờ chạm vào UnityWebRequest hay biết endpoint trông ra sao.
    ///
    /// Mỗi hàm là một coroutine, phần gọi dùng "yield return".
    public abstract class ApiGateway
    {
        protected readonly APIClient Api;

        protected ApiGateway(APIClient api = null)
        {
            Api = api != null ? api : APIClient.Instance;
        }

        /// GET trả về một object JSON.
        ///
        /// enrich chạy sau khi parse thành công, nhận cả object đã parse lẫn body
        /// thô. Dùng để điền những trường JsonUtility bỏ qua — xem JsonMap.
        protected IEnumerator Get<T>(string endpoint, Action<ApiResult<T>> onDone,
                                     Action<T, string> enrich = null) where T : class
        {
            return Send(UnityWebRequestVerb.Get, endpoint, null, onDone, enrich);
        }


        /// POST kèm payload, trả về một object JSON.
        ///
        /// idempotent = true chỉ đúng khi payload mang khoá chống trùng mà server
        /// nhận ra, ví dụ requestId của /gacha/summon. Đặt sai chỗ là mở đường cho
        /// việc thực hiện hai lần khi mạng chập chờn.
        protected IEnumerator Post<T>(string endpoint, object payload, Action<ApiResult<T>> onDone,
                                      Action<T, string> enrich = null, bool idempotent = false)
            where T : class
        {
            return Send(UnityWebRequestVerb.Post, endpoint, Serialize(payload), onDone, enrich, idempotent);
        }

        /// GET cho endpoint có thể trả 204 No Content.
        ///
        /// 204 KHÔNG phải lỗi: nó là câu trả lời hợp lệ mang nghĩa "không có gì".
        /// /combat/session/active dùng nó để nói người chơi không có trận nào đang
        /// dở. Khi đó kết quả là Ok với Value = null.
        protected IEnumerator GetOptional<T>(string endpoint, Action<ApiResult<T>> onDone,
                                             Action<T, string> enrich = null) where T : class
        {
            return Send(UnityWebRequestVerb.Get, endpoint, null, onDone, enrich,
                        idempotent: false, allowEmptyBody: true);
        }

        /// POST cho endpoint chỉ trả {"success":true}, không có dữ liệu kèm theo.
        protected IEnumerator Post(string endpoint, object payload, Action<ApiResult> onDone)
        {
            ApiResult outcome = default;
            bool handled = false;

            yield return Api.SendRequest(UnityWebRequestVerb.Post, endpoint, Serialize(payload),
                _ =>
                {
                    outcome = ApiResult.Ok();
                    handled = true;
                },
                err =>
                {
                    outcome = ApiResult.Fail(err);
                    handled = true;
                });

            if (!handled) outcome = ApiResult.Fail(NetworkErrors.NoResponse(endpoint));
            onDone?.Invoke(outcome);
        }

        private IEnumerator Send<T>(string method, string endpoint, string payload,
                                    Action<ApiResult<T>> onDone, Action<T, string> enrich,
                                    bool idempotent = false, bool allowEmptyBody = false)
            where T : class
        {
            ApiResult<T> outcome = default;
            bool handled = false;

            yield return Api.SendRequest(method, endpoint, payload,
                body =>
                {
                    // 204 No Content: thành công, nhưng cố ý không có dữ liệu.
                    if (allowEmptyBody && string.IsNullOrWhiteSpace(body))
                    {
                        outcome = ApiResult<T>.Ok(null);
                        handled = true;
                        return;
                    }

                    T parsed = ParseBody<T>(endpoint, body);
                    if (parsed == null)
                    {
                        outcome = ApiResult<T>.Fail(NetworkErrors.Unreadable(endpoint));
                    }
                    else
                    {
                        // Server trả map ở khá nhiều chỗ (inventory, stageStars,
                        // storyFlags, items...) mà JsonUtility bỏ qua không một
                        // tiếng động. enrich lấy chúng ra từ body thô. Khi team
                        // chốt thêm Newtonsoft (QĐ-1) thì mọi enrich đều biến mất.
                        enrich?.Invoke(parsed, body);
                        outcome = ApiResult<T>.Ok(parsed);
                    }
                    handled = true;
                },
                err =>
                {
                    outcome = ApiResult<T>.Fail(err);
                    handled = true;
                },
                idempotent);

            if (!handled) outcome = ApiResult<T>.Fail(NetworkErrors.NoResponse(endpoint));
            onDone?.Invoke(outcome);
        }

        private static T ParseBody<T>(string endpoint, string body) where T : class
        {
            if (string.IsNullOrEmpty(body)) return null;

            try
            {
                return JsonUtility.FromJson<T>(body);
            }
            catch (Exception e)
            {
                Debug.LogError($"[{endpoint}] Không parse được response: {e.Message}\n{body}");
                return null;
            }
        }

        private static string Serialize(object payload)
        {
            if (payload == null) return "{}";
            return payload is string raw ? raw : JsonUtility.ToJson(payload);
        }
    }

    /// Tên method HTTP, gom lại để khỏi rải chuỗi khắp nơi.
    internal static class UnityWebRequestVerb
    {
        public const string Get = "GET";
        public const string Post = "POST";
    }

    /// Lỗi phát sinh phía client, chưa có request nào thật sự thất bại ở server.
    ///
    /// StatusCode âm để phân biệt với 0 (không nối được tới server) và với các
    /// mã HTTP thật. Code để rỗng vì server không sinh ra những lỗi này.
    public static class NetworkErrors
    {
        public const long ClientSideStatus = -1;

        public static ApiError Local(string message) =>
            new ApiError(ClientSideStatus, string.Empty, message, string.Empty, default, 0);

        public static ApiError Unreadable(string endpoint) =>
            Local($"Response của {endpoint} không đọc được.");

        public static ApiError NoResponse(string endpoint) =>
            Local($"Không nhận được phản hồi từ {endpoint}.");
    }
}
