using System;
using System.Collections;
using System.Collections.Generic;
using GachaGame.Contracts.Api;
using NUnit.Framework;
using UnityEngine;

namespace GachaGame.Network.Tests
{
    /// Kiểm chứng phần điều phối của APIClient: gia hạn token chủ động, gửi lại
    /// sau 401, và luật gửi lại khi mất mạng.
    ///
    /// Đây chính là những hành vi mà bộ test chạy ngoài Unity (Tools/ContractCheck)
    /// không chạm tới được, vì chúng nằm trong coroutine của một MonoBehaviour.
    public class NetworkFlowTests
    {
        private GameObject _host;
        private APIClient _api;
        private FakeTransport _transport;
        private StubRefresher _refresher;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("APIClientTestHost");
            _api = _host.AddComponent<APIClient>();
            _api.Configure(baseUrl: "http://localhost:3000/api", build: "test",
                           transientRetries: 1, retryDelaySeconds: 0f);

            _transport = new FakeTransport();
            _api.SetTransport(_transport);

            _refresher = new StubRefresher();
            _api.SetTokenRefresher(_refresher);

            _api.SetAuthToken("token-1");
            _api.SetSessionSigningKey("key-1");
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
        }

        // Đường thuận: không gia hạn, không gửi lại
        [Test]
        public void RequestThanhCong_ChiGuiMotLan()
        {
            _transport.EnqueueOk("{\"success\":true,\"nickname\":\"Kiet\"}");

            string body = null;
            ApiError error = null;
            Pump(_api.SendRequest("GET", "/player/profile", null, b => body = b, e => error = e));

            Assert.IsNull(error, "không được có lỗi");
            Assert.AreEqual(1, _transport.SentCount, "chỉ được gửi đúng một lần");
            Assert.AreEqual(0, _refresher.RefreshCount, "không được gia hạn khi token còn tốt");
            StringAssert.Contains("Kiet", body);
        }

        [Test]
        public void MoiRequest_DeuMangDuNamHeaderKy()
        {
            _transport.EnqueueOk();
            Pump(_api.SendRequest("POST", "/gacha/summon", "{}", _ => { }, _ => { }));

            foreach (string header in new[]
            {
                RequestSigner.HeaderTimestamp, RequestSigner.HeaderNonce, RequestSigner.HeaderBodyHash,
                RequestSigner.HeaderClientBuild, RequestSigner.HeaderSignature,
            })
            {
                Assert.IsNotNull(_transport.HeaderOf(0, header), $"thiếu header {header}");
            }

            Assert.AreEqual("Bearer token-1", _transport.HeaderOf(0, "Authorization"));
        }

        // Gia hạn bị động sau 401
        [Test]
        public void Bi401_GiaHanRoiGuiLaiDungMotLan()
        {
            _transport.EnqueueError(401, ApiErrorCode.InvalidAccessToken);
            _transport.EnqueueOk("{\"success\":true}");
            _refresher.CanRefreshValue = true;
            _refresher.RefreshSucceeds = true;

            ApiError error = null;
            string body = null;
            Pump(_api.SendRequest("GET", "/player/profile", null, b => body = b, e => error = e));

            Assert.IsNull(error, "sau khi gia hạn thì phải thành công");
            Assert.AreEqual(1, _refresher.RefreshCount, "chỉ được gia hạn một lần");
            Assert.AreEqual(2, _transport.SentCount, "gửi lần đầu rồi gửi lại đúng một lần");
            StringAssert.Contains("success", body);
        }

        [Test]
        public void GuiLai_DungNonceMoi()
        {
            // Gửi lại nguyên văn request cũ sẽ ăn ERR_ANTICHEAT_NONCE_REUSED,
            // nên lần gửi lại bắt buộc phải ký lại từ đầu.
            _transport.EnqueueError(401, ApiErrorCode.InvalidAccessToken);
            _transport.EnqueueOk();
            _refresher.CanRefreshValue = true;
            _refresher.RefreshSucceeds = true;

            Pump(_api.SendRequest("GET", "/player/profile", null, _ => { }, _ => { }));

            Assert.AreEqual(2, _transport.SentCount);
            string first = _transport.HeaderOf(0, RequestSigner.HeaderNonce);
            string second = _transport.HeaderOf(1, RequestSigner.HeaderNonce);
            Assert.AreNotEqual(first, second, "hai lần gửi phải dùng nonce khác nhau");
        }

        [Test]
        public void GiaHanThatBai_KhongGuiLai()
        {
            _transport.EnqueueError(401, ApiErrorCode.InvalidAccessToken);
            _refresher.CanRefreshValue = true;
            _refresher.RefreshSucceeds = false;

            ApiError error = null;
            Pump(_api.SendRequest("GET", "/player/profile", null, _ => { }, e => error = e));

            Assert.IsNotNull(error);
            Assert.IsTrue(error.IsAccessTokenRejected, "phải trả về đúng lỗi gốc");
            Assert.AreEqual(1, _transport.SentCount, "gia hạn hỏng thì không được gửi lại");
        }

        [Test]
        public void KhongCoRefreshToken_KhongThuGiaHan()
        {
            _transport.EnqueueError(401, ApiErrorCode.InvalidAccessToken);
            _refresher.CanRefreshValue = false;

            Pump(_api.SendRequest("GET", "/player/profile", null, _ => { }, _ => { }));

            Assert.AreEqual(0, _refresher.RefreshCount);
            Assert.AreEqual(1, _transport.SentCount);
        }

        [Test]
        public void GuiLaiCungBi401_KhongLapVoHan()
        {
            _transport.EnqueueError(401, ApiErrorCode.InvalidAccessToken);
            _transport.EnqueueError(401, ApiErrorCode.InvalidAccessToken);
            _refresher.CanRefreshValue = true;
            _refresher.RefreshSucceeds = true;

            ApiError error = null;
            Pump(_api.SendRequest("GET", "/player/profile", null, _ => { }, e => error = e));

            Assert.IsNotNull(error);
            Assert.AreEqual(1, _refresher.RefreshCount, "chỉ gia hạn đúng một lần");
            Assert.AreEqual(2, _transport.SentCount, "dừng lại sau lần gửi lại, không lặp");
        }

        [Test]
        public void EndpointAuth_KhongKichHoatGiaHan()
        {
            // Cho phép gia hạn ở /auth/* sẽ thành đệ quy vô hạn khi chính
            // /auth/refresh trả 401.
            _transport.EnqueueError(401, ApiErrorCode.InvalidAccessToken);
            _refresher.CanRefreshValue = true;
            _refresher.ShouldRefreshValue = true;

            Pump(_api.SendRequest("POST", "/auth/refresh", "{}", _ => { }, _ => { }));

            Assert.AreEqual(0, _refresher.RefreshCount, "/auth/* phải tự lo phần token của nó");
            Assert.AreEqual(1, _transport.SentCount);
        }

        // Gia hạn chủ động
        [Test]
        public void TokenSapHetHan_GiaHanTruocKhiGui()
        {
            _transport.EnqueueOk();
            _refresher.CanRefreshValue = true;
            _refresher.ShouldRefreshValue = true;
            _refresher.RefreshSucceeds = true;

            Pump(_api.SendRequest("GET", "/player/profile", null, _ => { }, _ => { }));

            Assert.AreEqual(1, _refresher.RefreshCount, "phải gia hạn trước khi gửi");
            Assert.AreEqual(1, _transport.SentCount, "và chỉ gửi một lần");
        }

        // Gửi lại khi mất mạng
        [Test]
        public void GetMatMang_DuocGuiLai()
        {
            _transport.EnqueueOffline();
            _transport.EnqueueOk();

            ApiError error = null;
            Pump(_api.SendRequest("GET", "/player/profile", null, _ => { }, e => error = e));

            Assert.IsNull(error);
            Assert.AreEqual(2, _transport.SentCount, "GET được gửi lại khi không tới được server");
        }

        [Test]
        public void PostMatMang_KhongDuocGuiLai()
        {
            // Server có thể đã nhận và xử lý xong rồi mới đứt đường về. Gửi lại
            // POST là quay gacha hai lần hoặc trừ tiền hai lần.
            _transport.EnqueueOffline();
            _transport.EnqueueOk();

            ApiError error = null;
            Pump(_api.SendRequest("POST", "/gacha/summon", "{}", _ => { }, e => error = e));

            Assert.IsNotNull(error);
            Assert.IsTrue(error.IsNetworkFailure);
            Assert.AreEqual(1, _transport.SentCount, "POST tuyệt đối không được gửi lại");
        }

        [Test]
        public void LoiTuServer_KhongDuocGuiLai()
        {
            // 429 là server có trả lời, không phải mất mạng. Gửi lại chỉ tổ
            // đốt thêm quota.
            _transport.EnqueueError(429, ApiErrorCode.RateLimitExceeded);
            _transport.EnqueueOk();

            ApiError error = null;
            Pump(_api.SendRequest("GET", "/player/profile", null, _ => { }, e => error = e));

            Assert.IsNotNull(error);
            Assert.IsTrue(error.IsRateLimited);
            Assert.AreEqual(1, _transport.SentCount);
        }

        // Đồng bộ giờ server
        [Test]
        public void HeaderDate_BuLechDongHoVaoChuKy()
        {
            var serverNow = DateTime.UtcNow.AddSeconds(120);
            _transport.Enqueue(_ =>
            {
                var response = TransportResponse.Ok(200, "{}");
                response.Headers["Date"] = serverNow.ToString("r");
                return response;
            });
            _transport.EnqueueOk();

            Assert.IsFalse(_api.IsClockSynced, "chưa gọi lần nào thì chưa đồng bộ");

            Pump(_api.SendRequest("GET", "/system/bootstrap", null, _ => { }, _ => { }));

            Assert.IsTrue(_api.IsClockSynced);
            Assert.AreEqual(120, _api.ServerTimeOffset.TotalSeconds, 3.0,
                            "phần bù phải xấp xỉ độ lệch thật");

            // Request sau đó phải ký bằng giờ đã bù, nếu không server sẽ trả
            // ERR_ANTICHEAT_REQUEST_EXPIRED.
            Pump(_api.SendRequest("GET", "/player/profile", null, _ => { }, _ => { }));

            long signedAt = long.Parse(_transport.HeaderOf(1, RequestSigner.HeaderTimestamp));
            long machineNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Assert.Greater(signedAt, machineNow + 100, "timestamp phải theo giờ server, không phải giờ máy");
        }

        [Test]
        public void Destroy_HuyRequestDangBay()
        {
            UnityEngine.Object.DestroyImmediate(_host);
            _host = null;

            Assert.AreEqual(1, _transport.AbortAllCount);
        }

        // Gửi lại POST có khoá chống trùng
        [Test]
        public void PostCoKhoaChongTrung_DuocGuiLai()
        {
            // Server thêm requestId cho /gacha/summon nên nó nhận ra request lặp
            // và trả về biên nhận cũ. Vì thế gửi lại là AN TOÀN, và đáng ra phải
            // gửi lại: không thì người chơi mất mạng giữa lúc quay sẽ không biết
            // mình đã quay hay chưa.
            _transport.EnqueueOffline();
            _transport.EnqueueOk("{\"success\":true}");

            ApiError error = null;
            Pump(_api.SendRequest("POST", "/gacha/summon", "{\"requestId\":\"abc\"}",
                                  _ => { }, e => error = e, idempotent: true));

            Assert.IsNull(error);
            Assert.AreEqual(2, _transport.SentCount, "POST có khoá chống trùng phải được gửi lại");
        }

        [Test]
        public void PostCoKhoaChongTrung_GuiLaiGiuNguyenBody()
        {
            // Gửi lại mà đổi requestId thì khoá chống trùng vô dụng: server coi
            // đó là một lượt quay mới.
            _transport.EnqueueOffline();
            _transport.EnqueueOk();

            const string body = "{\"requestId\":\"khoa-co-dinh\",\"isTenPull\":false}";
            Pump(_api.SendRequest("POST", "/gacha/summon", body, _ => { }, _ => { }, idempotent: true));

            Assert.AreEqual(2, _transport.SentCount);
            string first = System.Text.Encoding.UTF8.GetString(_transport.Sent[0].Body);
            string second = System.Text.Encoding.UTF8.GetString(_transport.Sent[1].Body);
            Assert.AreEqual(first, second, "hai lần gửi phải mang đúng cùng một requestId");
        }

        [Test]
        public void PostKhongCoKhoa_VanKhongDuocGuiLai()
        {
            _transport.EnqueueOffline();
            _transport.EnqueueOk();

            Pump(_api.SendRequest("POST", "/player/parties/save", "{}", _ => { }, _ => { }));

            Assert.AreEqual(1, _transport.SentCount, "POST thường vẫn tuyệt đối không gửi lại");
        }

        // 204 No Content
        [Test]
        public void KhongCoTranDangDo_La204ChuKhongPhaiLoi()
        {
            // /combat/session/active trả 204 khi người chơi không có trận nào dở.
            // Đó là câu trả lời hợp lệ, không phải lỗi.
            _transport.Enqueue(_ => TransportResponse.Ok(204, string.Empty));

            var gateway = new CombatGateway(_api);
            ApiResult<CombatSessionResponseDto> outcome = default;
            Pump(gateway.GetActiveSession(result => outcome = result));

            Assert.IsTrue(outcome.IsSuccess, "204 phải là thành công");
            Assert.IsNull(outcome.Value, "204 thì không có dữ liệu");
        }

        [Test]
        public void CoTranDangDo_TraVeSession()
        {
            _transport.EnqueueOk(
                "{\"success\":true,\"session\":{\"sessionId\":\"s1\",\"stageId\":\"stage_ruins_01\"," +
                "\"currentRound\":3,\"maxRounds\":10,\"phase\":\"setup\"}}");

            var gateway = new CombatGateway(_api);
            ApiResult<CombatSessionResponseDto> outcome = default;
            Pump(gateway.GetActiveSession(result => outcome = result));

            Assert.IsTrue(outcome.IsSuccess);
            Assert.IsNotNull(outcome.Value);
            Assert.AreEqual("s1", outcome.Value.session.sessionId);
            Assert.AreEqual(3, outcome.Value.session.currentRound);
        }

        [Test]
        public void Summon_TuSinhRequestIdKhacNhauMoiLan()
        {
            // Mỗi lần người chơi BẤM quay là một lượt quay mới, nên khoá phải khác.
            // Khoá chỉ được giữ nguyên giữa các lần GỬI LẠI của cùng một lượt.
            _transport.Fallback = _ => TransportResponse.Ok(200, "{\"success\":true}");
            var gateway = new GachaGateway(_api);

            Pump(gateway.Summon("banner-1", "standard", false, _ => { }));
            Pump(gateway.Summon("banner-1", "standard", false, _ => { }));

            Assert.AreEqual(2, _transport.SentCount);
            string first = System.Text.Encoding.UTF8.GetString(_transport.Sent[0].Body);
            string second = System.Text.Encoding.UTF8.GetString(_transport.Sent[1].Body);
            Assert.AreNotEqual(first, second, "hai lần bấm quay phải mang requestId khác nhau");
        }

        // Tiện ích
        /// Chạy hết một coroutine ngay lập tức.
        ///
        /// EditMode không có bộ lập lịch coroutine, mà FakeTransport trả lời đồng
        /// bộ nên không cần chờ khung hình nào. Vòng lặp có chặn số bước để một
        /// coroutine lỡ lặp vô hạn thì test fail chứ không treo cả trình chạy.
        private static void Pump(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);

            int steps = 0;
            while (stack.Count > 0)
            {
                if (++steps > 10000) Assert.Fail("coroutine không dừng sau 10000 bước");

                IEnumerator top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop();
                    continue;
                }

                if (top.Current is IEnumerator nested) stack.Push(nested);
            }
        }

        /// ITokenRefresher giả, đếm số lần được gọi.
        private sealed class StubRefresher : ITokenRefresher
        {
            public bool CanRefreshValue;
            public bool ShouldRefreshValue;
            public bool RefreshSucceeds = true;
            public int RefreshCount;

            public bool CanRefresh => CanRefreshValue;

            public bool ShouldRefreshNow => ShouldRefreshValue;

            public IEnumerator Refresh(Action<bool> onComplete)
            {
                RefreshCount++;
                // Gia hạn xong thì token và khóa ký đều mới — giống AuthService thật.
                ShouldRefreshValue = false;
                onComplete?.Invoke(RefreshSucceeds);
                yield break;
            }
        }
    }
}
