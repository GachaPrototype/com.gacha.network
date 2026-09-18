using System;

namespace GachaGame.Network
{
    // DTO của nhóm /player và /system.
    //
    // Mỗi field khớp json tag phía Go. Kiểm tra tự động bằng:
    //     node Tools/check-dto-drift.mjs
    //
    // Sai tên field là loại lỗi KHÔNG ném exception: JsonUtility lặng lẽ để
    // nguyên giá trị mặc định, và UI hiện số 0 mà không ai biết tại sao.

    /// go: models.Economy
    [Serializable]
    public class EconomyDto
    {
        public int stamina;
        public string lastStaminaUpdateAt;
        public int gold;
        public int premiumCurrency;
    }

    /// go: models.PlayerProfile
    [Serializable]
    public class PlayerProfileDto
    {
        public string userId;
        public string nickname;
        public string email;
        public bool emailVerified;
        public string language;
        public int level;
        public int exp;
        public string createdAt;
        public string updatedAt;
        public string lastLoginAt;
        public EconomyDto resources;
        public string[] clearedStages;

        // Ba trường dưới là map phía server. JsonUtility không đọc được nên
        // PlayerGateway điền chúng từ body thô sau khi parse.
        [NonSerialized] public MapEntry[] inventory;
        [NonSerialized] public MapEntry[] stageStars;
        [NonSerialized] public MapEntry[] storyFlags;

        /// Số lượng một vật phẩm đang có. 0 nếu không sở hữu.
        public int ItemCount(string itemId) => JsonMap.GetInt(inventory, itemId);

        /// Số sao đã đạt ở một màn. 0 nếu chưa chơi.
        public int StarsFor(string stageId) => JsonMap.GetInt(stageStars, stageId);

        public bool HasStoryFlag(string flag) => JsonMap.GetBool(storyFlags, flag);
    }

    /// go: models.CharacterStats
    [Serializable]
    public class CharacterStatsDto
    {
        public float hp;
        public float attack;
        public float spAttack;
        public float defense;
        public float speed;
        public float criticalRate;
        public float criticalDamage;
        public float hitToCrit;
        public float hitToStun;
        public float elementAttack;
        public float xAttack;
        public float yAttack;
        public float zAttack;
        public float defPenetration;
        public float damageBonus;
        public float resistance;
        public float damageTaken;
    }

    /// go: models.PlayerCharacterViewDTO
    ///
    /// dto-check: partial — passives là mảng struct lồng sâu của master data,
    /// client chưa dùng tới nên chưa mô hình hoá.
    [Serializable]
    public class PlayerCharacterDto
    {
        public string uid;
        public string characterBaseId;
        public int level;
        public int exp;

        /// Server đánh dấu Deprecated. Giữ lại để đọc được response cũ, nhưng
        /// màn hình mới phải dùng các trường cung mệnh bên dưới.
        public int constellation;

        /// Số bản trùng đang có. Mỗi bản trùng cho một điểm để phân bổ.
        public int duplicateCopies;

        // Ba nhánh cung mệnh, cộng dồn theo số điểm đã phân bổ.
        public int constellationDamage;
        public int constellationTank;
        public int constellationSupport;

        // Ba mốc mở khoá, không cộng dồn: có hoặc không.
        public bool constellationDodge;
        public bool constellationBlock;
        public bool constellationParry;

        public string[] equippedCores;
        public CharacterStatsDto calculatedStats;
        public float currentHp;
        public float maxHp;

        /// Tổng số điểm đã phân bổ vào ba nhánh.
        public int AllocatedPoints => constellationDamage + constellationTank + constellationSupport;

        /// Số điểm còn lại chưa dùng.
        public int UnspentPoints => Math.Max(0, duplicateCopies - AllocatedPoints);
    }

    /// go: internal/player.RosterResponse
    [Serializable]
    public class RosterResponseDto
    {
        public PlayerCharacterDto[] characters;
    }

    /// go: models.CampaignProgress
    [Serializable]
    public class CampaignProgressDto
    {
        public string[] clearedStages;

        // Map phía server, PlayerGateway điền từ body thô.
        [NonSerialized] public MapEntry[] stageStars;
        [NonSerialized] public MapEntry[] storyFlags;

        public int StarsFor(string stageId) => JsonMap.GetInt(stageStars, stageId);
        public bool HasFlag(string flag) => JsonMap.GetBool(storyFlags, flag);
    }

    /// go: models.CampaignProgressResponseDTO
    [Serializable]
    public class CampaignProgressResponseDto
    {
        public bool success;
        public CampaignProgressDto progress;
    }

    /// go: models.PartyFormation
    [Serializable]
    public class PartyDto
    {
        public string partyId;
        public string name;
        public string[] characterUids;
        public string updatedAt;
    }

    /// go: models.PartiesResponseDTO
    [Serializable]
    public class PartiesResponseDto
    {
        public bool success;
        public PartyDto[] parties;
    }

    /// go: models.PartyResponseDTO
    [Serializable]
    public class PartyResponseDto
    {
        public bool success;
        public PartyDto party;
    }

    /// go: models.SavePartyRequestDTO
    [Serializable]
    public class SavePartyRequest
    {
        public string partyId;
        public string name;
        public string[] characterUids;
    }

    /// go: models.DeletePartyRequestDTO
    [Serializable]
    public class DeletePartyRequest
    {
        public string partyId;
    }

    /// go: models.ChangeNicknameRequestDTO
    [Serializable]
    public class ChangeNicknameRequest
    {
        public string newNickname;
    }

    /// go: models.CompleteStoryFlagRequestDTO
    [Serializable]
    public class CompleteStoryFlagRequest
    {
        public string flag;
    }
}
