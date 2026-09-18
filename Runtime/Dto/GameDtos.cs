using System;

namespace GachaGame.Network
{
    // DTO của nhóm /system, /character, /gacha và /equipment.
    // Đối chiếu tự động với json tag phía Go bằng Tools/check-dto-drift.mjs.

    // Hệ thống
    /// go: models.MaintenanceConfig
    [Serializable]
    public class MaintenanceDto
    {
        public bool enabled;
        public string message;
        public string startTime;
        public string endTime;
    }

    /// go: models.VersionConfig
    [Serializable]
    public class VersionDto
    {
        public string latestVersion;
        public string minimumVersion;
    }

    /// go: models.Announcement
    [Serializable]
    public class AnnouncementDto
    {
        public string id;
        public string title;
        public string content;
        public string date;
    }

    /// go: models.BootstrapResponseDTO
    [Serializable]
    public class BootstrapResponseDto
    {
        public MaintenanceDto maintenance;
        public VersionDto version;
        public bool forceUpdate;
        public AnnouncementDto[] announcements;
        public string serverTime;
    }

    // Nhân vật
    /// go: models.CharacterTemplate
    ///
    /// dto-check: partial — constellations, skills và passives là cây struct sâu
    /// của master data. Mô hình hoá chúng chờ tới khi màn hình chi tiết nhân vật
    /// thật sự cần, để khỏi phải bảo trì thứ chưa ai dùng.
    [Serializable]
    public class CharacterTemplateDto
    {
        public string baseId;
        public string name;
        public string element;
        public int rarity;
        public CharacterStatsDto baseStats;
        public CharacterStatsDto growthPerLevel;
        public string personality;
        public string @class;
        public string createdAt;
        public string updatedAt;
    }

    /// go: models.CharacterTemplateResponseDTO
    [Serializable]
    public class CharacterTemplateResponseDto
    {
        public bool success;
        public CharacterTemplateDto template;
        public string error;
    }

    /// Tham số cho CharacterGateway.LevelUp.
    ///
    /// KHÔNG phải DTO tuần tự hoá: models.LevelUpRequestDTO có trường items dạng
    /// map mà JsonUtility không ghi được, nên gateway tự dựng JSON. Vì thế lớp này
    /// cố ý không mang chú thích "go:" và không đi qua check-dto-drift.
    public sealed class LevelUpRequest
    {
        public string CharacterUid;
        public MapEntry[] Items;
    }

    /// go: models.LevelUpResponseDTO
    [Serializable]
    public class LevelUpResponseDto
    {
        public bool success;
        public string message;
        public PlayerCharacterDto character;
        public string error;
    }

    // Cung mệnh
    //
    // Cơ chế mới của server (commit "cung mệnh đặc biệt"): thay cho một con số
    // constellation duy nhất, giờ mỗi bản trùng cho một điểm để phân bổ vào ba
    // nhánh, cộng ba mốc mở khoá Dodge/Block/Parry.

    /// go: models.AllocateConstellationRequestDTO
    ///
    /// branch là "damage", "tank" hoặc "support".
    [Serializable]
    public class AllocateConstellationRequest
    {
        public string characterUid;
        public string branch;
    }

    /// go: models.ResetConstellationRequestDTO
    [Serializable]
    public class ResetConstellationRequest
    {
        public string characterUid;
    }

    /// go: models.CharacterConstellationResponseDTO
    [Serializable]
    public class CharacterConstellationResponseDto
    {
        public bool success;
        public PlayerCharacterDto character;
        public string error;
    }

    // Gacha
    /// go: models.GachaPoolDTO
    [Serializable]
    public class GachaPoolDto
    {
        public string rarityTier;
        public string[] characterBaseIds;
    }

    /// go: models.GachaRulesDTO
    ///
    /// Toàn bộ tỉ lệ và luật pity của banner, do server gửi xuống thay vì client
    /// tự chép lại. Nhờ vậy màn hình "tỉ lệ rơi" luôn khớp với thứ server thật sự
    /// dùng để quay.
    [Serializable]
    public class GachaRulesDto
    {
        public GachaPoolDto[] pools;
        public float baseHighestRate;
        public float baseMiddleRate;
        public float baseLowestRate;
        public int middleGuarantee;
        public int softPityStart;
        public float softPityIncrement;
        public int hardPity;
        public int featuredGuarantee;
        public float featuredChance;
        public bool pityCarriesWithinType;
        public bool rateUpCarriesAcrossBanners;
        public bool repeatedRateUpGuarantee;
        public bool milestone240Enabled;
    }

    /// go: models.GachaPityDTO
    [Serializable]
    public class GachaPityDto
    {
        public int pullsSince4Star;
        public int pullsSince5Star;
        public int pullsSinceRateUp;
    }

    /// go: models.GachaRewardDTO
    [Serializable]
    public class GachaRewardDto
    {
        public string itemId;
        public int quantity;
    }

    /// go: models.SummonPullDTO
    ///
    /// Một lượt quay. Quay 10 thì mảng pulls có 10 phần tử.
    /// isNew phân biệt nhân vật mới với bản trùng; bản trùng được quy đổi thành
    /// vật phẩm trong convertedRewards.
    [Serializable]
    public class SummonPullDto
    {
        public string characterBaseId;
        public string characterUid;
        public int rarity;
        public string rarityTier;
        public bool featured;
        public bool isNew;
        public int duplicateCopiesAdded;
        public int duplicateCopiesAfter;
        public GachaRewardDto[] convertedRewards;
    }

    /// go: models.BannerStatusDTO
    [Serializable]
    public class BannerStatusDto
    {
        public string currency;
        public GachaRulesDto rules;
        public string bannerId;
        public string bannerType;
        public string name;
        public string startAt;
        public string endAt;
        public long secondsUntilEnd;
        public int singlePullCost;
        public int tenPullCost;
        public string featuredCharacterBaseId;
    }

    /// go: models.ActiveBannersResponseDTO
    [Serializable]
    public class ActiveBannersResponseDto
    {
        public bool success;
        public string serverTime;
        public BannerStatusDto[] banners;
        public string error;
    }

    /// go: models.GachaStateResponseDTO
    ///
    /// Số dư và bộ đếm pity của người chơi trên một banner. Gọi trước khi mở màn
    /// hình gacha để hiện "còn bao nhiêu lượt nữa thì chắc chắn ra".
    [Serializable]
    public class GachaStateResponseDto
    {
        public bool success;
        public string serverTime;
        public string bannerId;
        public string bannerType;
        public int remainingPremiumCurrency;
        public GachaPityDto pity;
    }

    /// go: models.SummonRequestDTO
    ///
    /// requestId là KHOÁ CHỐNG TRÙNG, không phải id tuỳ ý. Server lưu biên nhận
    /// theo khoá này, nên gửi lại cùng một requestId sẽ trả về đúng kết quả cũ
    /// thay vì quay thêm lần nữa. Nhờ vậy mất mạng giữa chừng vẫn gửi lại được
    /// mà không sợ trừ tiền hai lần — xem GachaGateway.Summon.
    ///
    /// Server yêu cầu 1-128 ký tự, chỉ [A-Za-z0-9_-], không khoảng trắng đầu cuối.
    [Serializable]
    public class SummonRequest
    {
        public string requestId;
        public string bannerId;
        public string bannerType; // "standard" hoặc "featured"
        public bool isTenPull;
    }

    /// go: models.SummonResponseDTO
    ///
    /// results và message là trường cũ giữ lại cho tương thích ngược; dữ liệu đầy
    /// đủ nằm ở pulls.
    [Serializable]
    public class SummonResponseDto
    {
        public string requestId;
        public string bannerId;
        public string serverTime;
        public int cost;
        public string currency;
        public SummonPullDto[] pulls;
        public GachaPityDto pity;
        public GachaRewardDto[] convertedRewards;
        public bool success;
        public string message;
        public string[] results;
        public string bannerType;
        public string bannerEndsAt;
        public long bannerSecondsUntilEnd;
        public int remainingPremiumCurrency;
        public string error;
    }

    // Trang bị
    /// go: models.Hex
    [Serializable]
    public class ManaHexDto
    {
        public string statName;
        public float statValue;
    }

    /// go: models.ManaCoreEquipment
    [Serializable]
    public class ManaCoreDto
    {
        public string itemUuid;
        public string ownerId;
        public string coreId;
        public int level;
        public int pointCost;
        public int constellation;
        public ManaHexDto[] manaHexes;
        public int upgradeCount;
        public string equippedTo;
        public int slotIndex;

        public bool IsEquipped => !string.IsNullOrEmpty(equippedTo);
    }

    /// go: models.EquipmentListResponseDTO
    [Serializable]
    public class EquipmentListResponseDto
    {
        public bool success;
        public ManaCoreDto[] equipment;
        public string error;
    }

    /// go: models.EquipmentResponseDTO
    [Serializable]
    public class EquipmentResponseDto
    {
        public bool success;
        public string message;
        public ManaCoreDto equipment;
        public string error;
    }

    /// go: models.EquipManaCoreRequestDTO
    [Serializable]
    public class EquipManaCoreRequest
    {
        public string characterUid;
        public string coreItemUuid;
        public int slotIndex; // 0-3
    }

    /// go: models.UnequipManaCoreRequestDTO
    [Serializable]
    public class UnequipManaCoreRequest
    {
        public string characterUid;
        public int slotIndex; // 0-3
    }

    /// go: models.UpgradeManaHexRequestDTO
    [Serializable]
    public class UpgradeManaHexRequest
    {
        public string coreItemUuid;
        public int hexIndex; // 0-2
    }
}
