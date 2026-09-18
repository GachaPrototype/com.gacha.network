using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GachaGame.Network
{
    /// Ký request theo hợp đồng anti-cheat của server.
    ///
    /// Tách khỏi APIClient vì đây là phần phải khớp server đến từng ký tự, mà lại
    /// là toán thuần — tách ra thì test được bằng vector cố định trong
    /// Tools/ContractCheck, không cần Unity và không cần server.
    ///
    /// Hợp đồng phía server (middleware/anticheat.go) yêu cầu 5 header, thiếu bất
    /// kỳ cái nào cũng bị chặn:
    ///
    ///   X-Timestamp     unix seconds
    ///   X-Nonce         16-128 ký tự, chỉ [A-Za-z0-9_-], server chỉ nhận một lần
    ///   X-BodyHash      SHA256 hex thường của body, BẮT BUỘC cả với GET
    ///   X-Client-Build  chuỗi build, phải giống hệt lúc ký
    ///   X-Signature     HMAC-SHA256 hex thường của chuỗi ký
    ///
    /// Chuỗi ký gồm đúng 6 phần nối bằng dấu hai chấm:
    ///
    ///   METHOD:PATH:TIMESTAMP:NONCE:BODYHASH:CLIENTBUILD
    public static class RequestSigner
    {
        /// SHA256 của chuỗi rỗng. Server tự băm body rỗng ra đúng giá trị này,
        /// nên GET và POST không body vẫn phải gửi X-BodyHash bằng nó.
        public const string EmptyBodySha256 =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        public const string HeaderTimestamp = "X-Timestamp";
        public const string HeaderNonce = "X-Nonce";
        public const string HeaderBodyHash = "X-BodyHash";
        public const string HeaderClientBuild = "X-Client-Build";
        public const string HeaderSignature = "X-Signature";

        /// Bộ 5 header đã ký, sẵn sàng gắn vào request.
        public readonly struct SignedHeaders
        {
            public readonly string Timestamp;
            public readonly string Nonce;
            public readonly string BodyHash;
            public readonly string ClientBuild;
            public readonly string Signature;

            public SignedHeaders(string timestamp, string nonce, string bodyHash,
                                 string clientBuild, string signature)
            {
                Timestamp = timestamp;
                Nonce = nonce;
                BodyHash = bodyHash;
                ClientBuild = clientBuild;
                Signature = signature;
            }
        }

        /// Ký một request.
        ///
        /// serverUtcNow phải là GIỜ SERVER đã bù lệch (APIClient.ServerUtcNow).
        /// path là đường dẫn không kèm query string, khớp r.URL.Path phía server.
        public static SignedHeaders Create(string signingKey, string method, string path,
                                           DateTime serverUtcNow, string clientBuild, byte[] body)
        {
            string timestamp = ToUnixSeconds(serverUtcNow);
            string nonce = GenerateNonce();
            string bodyHash = ComputeBodyHash(body);
            string signature = Sign(signingKey, BuildSigningPayload(method, path, timestamp, nonce,
                                                                    bodyHash, clientBuild));

            return new SignedHeaders(timestamp, nonce, bodyHash, clientBuild, signature);
        }

        /// Chuỗi được đưa vào HMAC. Thứ tự và số lượng phần phải giống hệt server.
        public static string BuildSigningPayload(string method, string path, string timestamp,
                                                 string nonce, string bodyHash, string clientBuild)
        {
            return string.Join(":", method, path, timestamp, nonce, bodyHash, clientBuild);
        }

        public static string Sign(string signingKey, string payload)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey ?? string.Empty)))
            {
                return ToHexLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload ?? string.Empty)));
            }
        }

        /// Băm đúng số byte sẽ gửi đi. Body rỗng cho ra SHA256 của CHUỖI RỖNG,
        /// không phải chuỗi rỗng — server luôn tự băm nên hai bên phải cùng quy ước.
        public static string ComputeBodyHash(byte[] body)
        {
            if (body == null || body.Length == 0) return EmptyBodySha256;

            using (var sha256 = SHA256.Create())
            {
                return ToHexLower(sha256.ComputeHash(body));
            }
        }

        /// 32 ký tự hex, nằm trong khoảng 16-128 mà server chấp nhận và chỉ dùng
        /// [0-9a-f] nên luôn hợp lệ với luật nonce.
        public static string GenerateNonce() => Guid.NewGuid().ToString("N");

        /// Nonce hợp lệ theo luật server: 16-128 ký tự, chỉ [A-Za-z0-9_-].
        public static bool IsValidNonce(string nonce)
        {
            if (string.IsNullOrEmpty(nonce) || nonce.Length < 16 || nonce.Length > 128) return false;

            for (int i = 0; i < nonce.Length; i++)
            {
                char c = nonce[i];
                bool allowed = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                               (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!allowed) return false;
            }
            return true;
        }

        /// Lấy phần path của URL, bỏ query string.
        public static string PathOf(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            return Uri.TryCreate(url, UriKind.Absolute, out Uri parsed) ? parsed.AbsolutePath : url;
        }

        private static string ToUnixSeconds(DateTime utc)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeSpan.Zero)
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);
        }

        private static string ToHexLower(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }
    }
}
