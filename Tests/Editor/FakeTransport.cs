using System;
using System.Collections;
using System.Collections.Generic;

namespace GachaGame.Network.Tests
{
    /// Tầng vận chuyển giả: trả về những gì test bảo nó trả, và ghi lại mọi
    /// request đã nhận.
    ///
    /// Nhờ nó mà luồng gia hạn token và gửi lại kiểm chứng được, không cần server
    /// chạy và không phụ thuộc mạng.
    public sealed class FakeTransport : ITransport
    {
        private readonly Queue<Func<TransportRequest, TransportResponse>> _scripted =
            new Queue<Func<TransportRequest, TransportResponse>>();

        /// Mọi request đã đi qua, theo đúng thứ tự.
        public readonly List<TransportRequest> Sent = new List<TransportRequest>();

        public int AbortAllCount { get; private set; }

        public int SentCount => Sent.Count;

        /// Câu trả lời mặc định khi đã hết kịch bản.
        public Func<TransportRequest, TransportResponse> Fallback =
            _ => TransportResponse.Ok(200, "{\"success\":true}");

        /// Xếp sẵn một câu trả lời cho lần gửi tiếp theo.
        public FakeTransport Enqueue(Func<TransportRequest, TransportResponse> reply)
        {
            _scripted.Enqueue(reply);
            return this;
        }

        public FakeTransport EnqueueOk(string body = "{\"success\":true}")
        {
            return Enqueue(_ => TransportResponse.Ok(200, body));
        }

        public FakeTransport EnqueueError(long status, string code)
        {
            return Enqueue(_ => TransportResponse.Failed(
                status, "{\"success\":false,\"code\":\"" + code + "\"}", "HTTP " + status));
        }

        public FakeTransport EnqueueOffline()
        {
            return Enqueue(_ => TransportResponse.Offline("Cannot connect to destination host"));
        }

        public IEnumerator Send(TransportRequest request, Action<TransportResponse> onDone)
        {
            Sent.Add(request);

            var reply = _scripted.Count > 0 ? _scripted.Dequeue() : Fallback;
            onDone?.Invoke(reply(request));

            // Trả về ngay, không yield gì: test chạy đồng bộ được.
            yield break;
        }

        public void AbortAll()
        {
            AbortAllCount++;
        }

        /// Đường dẫn của request thứ index, đã bỏ phần host.
        public string PathOf(int index)
        {
            return RequestSigner.PathOf(Sent[index].Url);
        }

        public string HeaderOf(int index, string header)
        {
            return Sent[index].Headers.TryGetValue(header, out string value) ? value : null;
        }
    }
}
