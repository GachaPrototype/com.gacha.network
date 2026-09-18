using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;

namespace GachaGame.Network
{
    /// Bản cài đặt thật, chạy trên UnityWebRequest.
    ///
    /// Đây là chỗ DUY NHẤT trong toàn bộ project được phép dựng UnityWebRequest.
    public sealed class UnityWebRequestTransport : ITransport
    {
        /// Những header của response mà client cần đọc lại.
        ///
        /// UnityWebRequest không liệt kê được toàn bộ header một cách đáng tin
        /// trên mọi nền tảng, nên chỉ lấy đúng ba cái đang dùng:
        ///   Date          đồng bộ lệch đồng hồ cho chữ ký anti-cheat
        ///   Retry-After   server nói phải chờ bao lâu khi bị rate limit
        ///   X-Request-Id  để đối chiếu với log phía server
        private static readonly string[] CapturedHeaders = { "Date", "Retry-After", "X-Request-Id" };

        // Đọc timeout qua hàm chứ không chụp lại giá trị: APIClient.Configure()
        // có thể đổi nó sau khi transport đã được tạo.
        private readonly Func<int> _timeoutSeconds;
        private readonly List<UnityWebRequest> _inFlight = new List<UnityWebRequest>();

        public UnityWebRequestTransport(Func<int> timeoutSecondsProvider)
        {
            _timeoutSeconds = timeoutSecondsProvider ?? (() => 0);
        }

        public IEnumerator Send(TransportRequest request, Action<TransportResponse> onDone)
        {
            using (var web = new UnityWebRequest(request.Url, request.Method))
            {
                if (request.HasBody)
                {
                    web.uploadHandler = new UploadHandlerRaw(request.Body);
                }

                web.downloadHandler = new DownloadHandlerBuffer();
                int timeout = _timeoutSeconds();
                if (timeout > 0) web.timeout = timeout;

                foreach (var header in request.Headers)
                {
                    web.SetRequestHeader(header.Key, header.Value);
                }

                _inFlight.Add(web);
                try
                {
                    yield return web.SendWebRequest();

                    string body = web.downloadHandler != null ? web.downloadHandler.text : string.Empty;
                    TransportResponse response = web.result == UnityWebRequest.Result.Success
                        ? TransportResponse.Ok(web.responseCode, body)
                        : TransportResponse.Failed(web.responseCode, body, web.error);

                    foreach (string name in CapturedHeaders)
                    {
                        string value = web.GetResponseHeader(name);
                        if (!string.IsNullOrEmpty(value)) response.Headers[name] = value;
                    }

                    onDone?.Invoke(response);
                }
                finally
                {
                    _inFlight.Remove(web);
                }
            }
        }

        public void AbortAll()
        {
            for (int i = _inFlight.Count - 1; i >= 0; i--)
            {
                try
                {
                    _inFlight[i]?.Abort();
                }
                catch (Exception)
                {
                    // Request có thể đã tự dispose xong; không có gì để làm.
                }
            }
            _inFlight.Clear();
        }
    }
}
