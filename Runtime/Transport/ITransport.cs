using System;
using System.Collections;
using System.Collections.Generic;

namespace GachaGame.Network
{
    /// Tầng vận chuyển thô: gửi đúng một request và trả về đúng những gì server
    /// đáp lại.
    ///
    /// Nó KHÔNG ký, KHÔNG gia hạn token, KHÔNG gửi lại. Toàn bộ những việc đó
    /// thuộc về APIClient. Tách ra để test được luồng gia hạn và gửi lại mà không
    /// cần server thật — xem FakeTransport trong Tests.
    public interface ITransport
    {
        /// Gửi request. onDone luôn được gọi đúng một lần, kể cả khi hỏng.
        IEnumerator Send(TransportRequest request, Action<TransportResponse> onDone);

        /// Hủy mọi request đang bay. Gọi khi APIClient bị destroy.
        void AbortAll();
    }

    /// Một request đã sẵn sàng gửi: header đã đủ, body đã là byte.
    public sealed class TransportRequest
    {
        public string Method;
        public string Url;
        public byte[] Body;

        /// Header so sánh không phân biệt hoa thường, giống HTTP.
        public readonly Dictionary<string, string> Headers =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public TransportRequest(string method, string url, byte[] body = null)
        {
            Method = method;
            Url = url;
            Body = body ?? Array.Empty<byte>();
        }

        public TransportRequest With(string header, string value)
        {
            if (!string.IsNullOrEmpty(header) && value != null) Headers[header] = value;
            return this;
        }

        public bool HasBody => Body != null && Body.Length > 0;
    }

    /// Kết quả thô của một lần gửi.
    public sealed class TransportResponse
    {
        /// HTTP status. 0 nghĩa là request không tới được server: mất mạng, sai
        /// URL, server chưa chạy. Phân biệt với 5xx là server có trả lời.
        public long StatusCode;

        /// true khi tầng vận chuyển coi đây là thành công (2xx).
        public bool IsSuccess;

        public string Body = string.Empty;

        /// Mô tả lỗi của tầng vận chuyển, ví dụ UnityWebRequest.error.
        public string TransportError = string.Empty;

        public readonly Dictionary<string, string> Headers =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// null nếu server không gửi header đó.
        public string Header(string name)
        {
            return name != null && Headers.TryGetValue(name, out string value) ? value : null;
        }

        /// Server có trả lời hay không. Dùng để biết header Date có đáng tin không.
        public bool ReachedServer => StatusCode > 0;

        public static TransportResponse Ok(long statusCode, string body)
        {
            return new TransportResponse { StatusCode = statusCode, IsSuccess = true, Body = body ?? string.Empty };
        }

        public static TransportResponse Failed(long statusCode, string body, string transportError)
        {
            return new TransportResponse
            {
                StatusCode = statusCode,
                IsSuccess = false,
                Body = body ?? string.Empty,
                TransportError = transportError ?? string.Empty,
            };
        }

        /// Không nối được tới server.
        public static TransportResponse Offline(string transportError)
        {
            return Failed(0, string.Empty, transportError);
        }
    }
}
