using System;
using UnityEngine;

namespace GachaGame.Network
{
    /// Nơi giữ phiên giữa hai lần mở game.
    ///
    /// Tách thành interface để đổi chỗ lưu mà không phải sửa AuthService — xem
    /// ghi chú về nợ kỹ thuật ở PlayerPrefsTokenStorage.
    public interface ITokenStorage
    {
        void Save(PersistedSession session);

        /// false khi chưa có phiên nào được lưu hoặc dữ liệu đã hỏng.
        bool TryLoad(out PersistedSession session);

        void Clear();
    }

    /// Phần của phiên được ghi xuống đĩa.
    ///
    /// Cố ý KHÔNG lưu access token: nó chỉ sống 15 phút nên lưu cũng gần như vô
    /// dụng, mà lại là thêm một bản sao bí mật nằm trên máy. Mở game lên thì
    /// dùng refresh token xin access token mới.
    [Serializable]
    public class PersistedSession
    {
        public string userId;
        public string refreshToken;
        public string refreshExpiresAt;
        public string guestUsername;
        public string guestPassword;
    }

    /// Bản cài đặt mặc định, dùng PlayerPrefs.
    ///
    /// NỢ KỸ THUẬT (QĐ-3): PlayerPrefs là registry dạng plaintext trên Windows và
    /// file XML đọc được trên Android đã root. Chấp nhận ở giai đoạn prototype.
    /// Khi lên production, viết một bản dùng Keychain và Keystore rồi truyền vào
    /// AuthService.UseStorage() — không chỗ nào khác phải sửa.
    public sealed class PlayerPrefsTokenStorage : ITokenStorage
    {
        private const string Key = "gacha.session.v1";

        public void Save(PersistedSession session)
        {
            if (session == null)
            {
                Clear();
                return;
            }

            PlayerPrefs.SetString(Key, JsonUtility.ToJson(session));
            PlayerPrefs.Save();
        }

        public bool TryLoad(out PersistedSession session)
        {
            session = null;
            if (!PlayerPrefs.HasKey(Key)) return false;

            string json = PlayerPrefs.GetString(Key, string.Empty);
            if (string.IsNullOrEmpty(json)) return false;

            try
            {
                session = JsonUtility.FromJson<PersistedSession>(json);
            }
            catch (Exception e)
            {
                // Dữ liệu hỏng thì bỏ luôn, đừng để nó chặn lần đăng nhập sau.
                Debug.LogWarning($"[TokenStorage] Phiên đã lưu bị hỏng, xoá đi: {e.Message}");
                Clear();
                return false;
            }

            return session != null && !string.IsNullOrEmpty(session.refreshToken);
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
