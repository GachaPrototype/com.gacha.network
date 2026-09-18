using System.Collections;
using GachaGame.Contracts.Api;
using GachaGame.Network;
using UnityEngine;

/// Ví dụ ngắn nhất cho thấy cách dùng package này.
///
/// Gắn vào một GameObject rỗng rồi bấm Play. Nó tự thêm APIClient và AuthService
/// vào cùng object.
///
/// Ba điều đáng chú ý:
///
///   1. Script không hề đụng tới token. AuthService giữ token, tự gia hạn khi
///      sắp hết hạn, và APIClient tự gửi lại nếu server từ chối token.
///   2. Script không hề đụng tới UnityWebRequest. Gateway là toàn bộ bề mặt.
///   3. Lỗi bắt theo MÃ, không theo câu chữ. Server không gửi câu chữ hiển thị,
///      việc dịch mã sang tiếng Việt là của tầng localization.
public class GatewayQuickStart : MonoBehaviour
{
    [SerializeField] private string username = "unityclient";
    [SerializeField] private string password = "UnityClient12345";

    private AuthService _auth;
    private PlayerGateway _player;
    private GachaGateway _gacha;
    private string _firstBannerId;

    private IEnumerator Start()
    {
        var api = gameObject.AddComponent<APIClient>();
        api.Configure(baseUrl: "http://localhost:3000/api", build: "development");

        _auth = gameObject.AddComponent<AuthService>();
        _player = new PlayerGateway(api);
        _gacha = new GachaGateway(api);

        // Phiên hỏng hẳn thì Network chỉ phát event; mở màn hình đăng nhập là
        // việc của UI.
        _auth.OnSessionExpired += () => Debug.LogWarning("Phiên hết hạn, cần đăng nhập lại.");

        yield return SignIn();
        if (!_auth.IsSignedIn) yield break;

        yield return ShowProfile();
        yield return ShowBanners();
    }

    private IEnumerator SignIn()
    {
        // Đã có refresh token lưu từ lần trước thì khỏi cần mật khẩu.
        if (_auth.HasStoredSession)
        {
            bool restored = false;
            yield return _auth.RestoreSession(result => restored = result.IsSuccess);
            if (restored) yield break;
        }

        yield return _auth.Login(username, password, result =>
        {
            if (result.IsSuccess) Debug.Log($"Đăng nhập OK: {result.Value.UserId}");
            else Debug.LogError($"Đăng nhập thất bại: {result.Error}");
        });
    }

    private IEnumerator ShowProfile()
    {
        yield return _player.GetProfile(result =>
        {
            if (result.IsFailure)
            {
                Debug.LogError($"Không lấy được profile: {result.Error}");
                return;
            }

            var profile = result.Value;
            Debug.Log($"{profile.nickname} Lv.{profile.level} — " +
                      $"{profile.resources.gold} vàng, {profile.resources.premiumCurrency} kim cương");

            // inventory là map phía server; JsonUtility không đọc được nên
            // gateway đã điền sẵn, truy cập qua hàm tiện ích.
            Debug.Log($"Sách EXP đang có: {profile.ItemCount("exp_book_small")}");
        });
    }

    private IEnumerator ShowBanners()
    {
        yield return _gacha.GetActiveBanners(result =>
        {
            if (result.IsFailure) return;

            foreach (var banner in result.Value.banners)
            {
                Debug.Log($"Banner {banner.name} còn {banner.secondsUntilEnd}s");
            }

            if (result.Value.banners.Length > 0)
                _firstBannerId = result.Value.banners[0].bannerId;
        });

        if (string.IsNullOrEmpty(_firstBannerId))
        {
            Debug.Log("Chưa có banner nào đang mở.");
            yield break;
        }

        // Endpoint có ký HMAC. Bắt lỗi theo mã, đừng so chuỗi.
        //
        // bannerId lấy từ danh sách banner đang mở. requestId để trống thì gateway
        // tự sinh — nhờ khoá đó, mất mạng giữa chừng vẫn gửi lại an toàn.
        yield return _gacha.Summon(_firstBannerId, bannerType: null, tenPull: false, result =>
        {
            if (result.IsSuccess)
            {
                foreach (var pull in result.Value.pulls)
                {
                    Debug.Log($"Quay được {pull.characterBaseId} {pull.rarity}★" +
                              (pull.isNew ? " (mới)" : " (trùng)"));
                }
            }
            else if (result.FailedWith(ApiErrorCode.BannerInactive))
            {
                Debug.Log("Chưa có banner nào đang mở.");
            }
            else if (result.FailedWith(ApiErrorCode.InsufficientCurrency))
            {
                Debug.Log("Không đủ kim cương.");
            }
            else
            {
                Debug.LogError($"Quay thất bại: {result.Error}");
            }
        });
    }
}
