using System;

namespace GachaGame.Network
{
    // DTO của nhóm /combat/session.
    //
    // TẠM THỜI. Tài liệu docs/COMBAT_DESIGN_FORK.md cho biết thiết kế combat vẫn
    // đang chờ đội thiết kế chốt, và mô hình mana là một trong hai điểm tranh chấp
    // chính. Nếu team chọn hồ mana chung thay vì mana riêng từng nhân vật thì
    // CombatUnitDto và cách UI đọc nó phải viết lại.
    //
    // Vì vậy các DTO ở đây cố ý dừng ở mức đủ để chạy một vòng đấu, chưa mô hình
    // hoá toàn bộ cây trạng thái của server. Chúng đánh dấu "partial" nên
    // check-dto-drift.mjs không báo động về những field còn thiếu, nhưng vẫn bắt
    // được field sai tên.

    /// go: models.CreateCombatSessionRequestDTO
    ///
    /// Server đã rút gọn payload này: userId lấy từ token, stageType suy ra từ
    /// master data của màn, và teamUids (đường cũ) đã bị bỏ hẳn — giờ bắt buộc
    /// vào trận bằng một party đã lưu.
    [Serializable]
    public class CreateCombatSessionRequest
    {
        public string stageId;
        public string partyId;
    }

    /// go: models.ActionSetup
    ///
    /// domainId và domainDuration đã bị bỏ khỏi payload: Lãnh địa không còn được
    /// cấu hình qua action nữa. DomainState vẫn tồn tại phía server nên cơ chế
    /// chưa mất, chỉ là cách kích hoạt đã đổi — cần hỏi Backend nếu UI đụng tới.
    [Serializable]
    public class ActionSetupDto
    {
        public string characterUid;
        public string action;   // normal_attack | skill | block | dodge | parry
        public string skillId;
        public string[] targetUids;
        public string speedMod; // fast | normal | slow
    }

    /// go: models.RoundSetup
    ///
    /// Đây là hình dạng server TRẢ VỀ trong state và result. Payload client GỬI
    /// LÊN thì hẹp hơn — xem RoundSetupPayload.
    [Serializable]
    public class RoundSetupDto
    {
        public int roundNumber;
        public ActionSetupDto[] actions;
        public ReactionChoiceDto[] reactions;
    }

    /// go: models.CombatRoundSetupRequestDTO
    ///
    /// Chỉ chứa phần client được phép khoá. roundNumber CỐ Ý không có: server tự
    /// quản lý số round, client gửi lên cũng bị bỏ qua.
    [Serializable]
    public class RoundSetupPayload
    {
        public ActionSetupDto[] actions;
    }

    /// go: models.CombatSetupRequestDTO
    [Serializable]
    public class CombatSetupRequest
    {
        public string sessionId;
        public RoundSetupPayload setup;
    }

    /// go: models.ReactionChoice
    ///
    /// Đây là input canh nhịp thu trong lúc client chiếu telegraph đòn địch, sau
    /// khi round đã khoá. Nó KHÔNG nằm trong menu kế hoạch.
    [Serializable]
    public class ReactionChoiceDto
    {
        public string characterUid;
        public string action; // block | dodge | parry
    }

    /// go: models.CombatExecuteRequestDTO
    ///
    /// ĐỔI SO VỚI BẢN TRƯỚC: reactions đã bị tách khỏi đây sang endpoint riêng
    /// /combat/session/react. Lý do hợp lý: phản xạ phải gửi NGAY trong cửa sổ
    /// canh nhịp, không đợi tới lúc phân giải round. Xem CombatReactionRequest.
    [Serializable]
    public class CombatExecuteRequest
    {
        public string sessionId;
    }

    /// go: models.CombatSetupResultDTO
    ///
    /// Response của /combat/session/setup. KHÁC CombatSessionResponseDto ở hai
    /// trường cuối, và đó chính là hai trường cần để chạy cửa sổ phản xạ:
    /// hạn chót và giờ server tại thời điểm trả lời.
    ///
    /// Luôn đếm ngược bằng hiệu của hai số này, đừng lấy giờ máy trừ đi
    /// deadline — máy người chơi lệch giờ là cửa sổ sai hoàn toàn.
    [Serializable]
    public class CombatSetupResultDto
    {
        public bool success;
        public CombatSessionDto session;
        public long reactionDeadlineUnixMs;
        public long serverTimeUnixMs;

        /// Số mili giây còn lại của cửa sổ phản xạ, tính tại lúc server trả lời.
        public long ReactionWindowMs =>
            Math.Max(0, reactionDeadlineUnixMs - serverTimeUnixMs);
    }

    /// go: models.CombatReactionRequestDTO
    ///
    /// Gửi Block/Dodge/Parry trong lúc cửa sổ phản xạ còn mở. Server trả về hạn
    /// chót theo giờ của nó, nên client phải đếm ngược theo giờ server chứ không
    /// theo giờ máy — dùng APIClient.ServerUtcNow.
    [Serializable]
    public class CombatReactionRequest
    {
        public string sessionId;
        public ReactionChoiceDto[] reactions;
    }

    /// go: models.CombatReactionResponseDTO
    ///
    /// accepted liệt kê những characterUid thực sự được ghi nhận. Gửi 3 phản xạ
    /// mà chỉ 2 cái nằm trong accepted nghĩa là cái thứ ba bị từ chối — hết
    /// stamina, hoặc gửi muộn quá hạn.
    [Serializable]
    public class CombatReactionResponseDto
    {
        public bool success;
        public string[] accepted;
        public long deadlineUnixMs;
        public long serverTimeUnixMs;
    }

    /// go: models.EnemyActionTelegraphView
    ///
    /// Ý đồ tấn công của địch, server sinh ra sau khi round đã khoá để client
    /// chiếu cho người chơi thấy mà kịp phản xạ.
    [Serializable]
    public class EnemyTelegraphDto
    {
        public string characterUid;
        public string action;
        public string skillId;
        public string[] targetUids;
        public string speedMod;
    }

    /// go: models.EnemyCombatStateResponseDTO
    ///
    /// Payload nhẹ dành cho việc polling HUD địch và telegraph, thay vì kéo cả
    /// session về mỗi lần.
    [Serializable]
    public class EnemyCombatStateResponseDto
    {
        public bool success;
        public string sessionId;
        public string phase;
        public int currentRound;
        public long serverTimeUnixMs;
        public long reactionDeadlineUnixMs;
        public EnemyUnitDto[] enemies;
        public EnemyTelegraphDto[] telegraphs;

        /// Cửa sổ phản xạ còn mở hay không, tính theo giờ SERVER.
        public bool HasOpenReactionWindow => reactionDeadlineUnixMs > serverTimeUnixMs;

        /// Số mili giây còn lại của cửa sổ phản xạ tại thời điểm response được tạo.
        public long ReactionWindowRemainingMs =>
            Math.Max(0, reactionDeadlineUnixMs - serverTimeUnixMs);
    }

    /// Payload của POST /combat/session/end.
    /// go: struct ẩn danh trong CombatSessionEndHandler
    [Serializable]
    public class CombatEndRequest
    {
        public string sessionId;
    }

    /// go: models.StatusEffect
    [Serializable]
    public class StatusEffectDto
    {
        public string id;
        public string type;
        public string statAffected;
        public float value;
        public bool isPercentage;
        public int remainingRounds;
        public string sourceUid;
    }

    /// go: models.ElementalSeal
    [Serializable]
    public class ElementalSealDto
    {
        public string element;
        public int count;
        public int roundsLeft;
    }

    /// go: models.ToughnessBar
    [Serializable]
    public class ToughnessDto
    {
        public float maxToughness;
        public float currentToughness;
        public bool isBroken;
        public int recoveryCooldown;
    }

    /// go: models.DOTStack
    [Serializable]
    public class DotStackDto
    {
        public string id;
        public string element;
        public float damagePerTick;
        public string scalingStat;
        public float skillMultiplier;
        public int remainingTicks;
        public string sourceUid;
    }

    /// go: models.CombatUnitState
    ///
    /// dto-check: partial
    [Serializable]
    public class CombatUnitDto
    {
        public string uid;
        public string baseId;
        public string category;
        public string @class;
        public string personality;
        public bool isPlayerUnit;
        public int level;
        public float currentHp;
        public float maxHp;
        public CharacterStatsDto stats;
        public int stamina;
        public float hitToCritCharge;
        public int currentMana;
        public int maxMana;
        public float shield;
        public StatusEffectDto[] activeBuffs;
        public StatusEffectDto[] activeDebuffs;
        public ElementalSealDto[] elementalSeals;
        public ToughnessDto toughness;
        public bool isStunned;
        public int stunRoundsLeft;
        public DotStackDto[] dotStacks;
        public string castingSkillId;
        public int castTurnsLeft;
        public bool isAlive;
    }

    /// go: models.EnemyCombatUnitView
    ///
    /// Khác CombatUnitDto ở chỗ chỉ số địch bị server ẩn bớt: những trường server
    /// chưa cho xem sẽ vắng mặt trong JSON, và JsonUtility để chúng bằng 0. Đọc
    /// statVisibility trước khi hiển thị, đừng hiển thị thẳng số 0.
    ///
    /// dto-check: partial
    [Serializable]
    public class EnemyUnitDto
    {
        public string uid;
        public string baseId;
        public string category;
        public bool isPlayerUnit;
        public int level;
        public float currentHp;
        public float maxHp;
        public int stamina;
        public float shield;
        public StatusEffectDto[] activeBuffs;
        public StatusEffectDto[] activeDebuffs;
        public ElementalSealDto[] elementalSeals;
        public ToughnessDto toughness;
        public bool isStunned;
        public int stunRoundsLeft;
        public DotStackDto[] dotStacks;
        public bool isAlive;
    }

    /// go: models.CombatSessionView
    ///
    /// dto-check: partial
    [Serializable]
    public class CombatSessionDto
    {
        public string sessionId;
        public string playerId;
        public string partyId;
        public string stageId;
        public string stageType;
        public int currentRound;
        public int maxRounds;
        public int staminaCost;
        public string phase; // setup | execution | finished
        public int hypeMeter;
        public int spiritPool;
        public int enemyEnrage;
        public CombatUnitDto[] playerUnits;
        public EnemyUnitDto[] enemyUnits;
        public string createdAt;
        public string updatedAt;
    }

    /// go: models.HitResult
    [Serializable]
    public class HitResultDto
    {
        public string targetUid;
        public float damage;
        public bool isCrit;
        public float stagger;
    }

    /// go: models.ActionResult
    ///
    /// dto-check: partial
    [Serializable]
    public class ActionResultDto
    {
        public string actorUid;
        public string action;
        public string skillId;
        public string[] targetUids;
        public bool isCasting;
        public int castTurnsLeft;
        public float damageDealt;
        public float healingDone;
        public float shieldApplied;
        public bool reactionTriggered;
        public float reactionDamage;
        public float staggerDealt;
        public bool breakTriggered;
        public int manaChange;
        public HitResultDto[] hits;
    }

    /// go: models.RoundResult
    ///
    /// dto-check: partial
    [Serializable]
    public class RoundResultDto
    {
        public int roundNumber;
        public ActionResultDto[] actionResults;
    }

    /// go: models.CombatReward
    [Serializable]
    public class CombatRewardDto
    {
        public int exp;
        public int gold;

        // items là map phía server, CombatGateway điền từ body thô.
        [NonSerialized] public MapEntry[] items;

        public int ItemCount(string itemId) => JsonMap.GetInt(items, itemId);
    }

    /// go: models.CombatSessionResponseDTO
    [Serializable]
    public class CombatSessionResponseDto
    {
        public bool success;
        public CombatSessionDto session;
    }

    /// go: models.CombatExecuteResponseDTO
    [Serializable]
    public class CombatExecuteResponseDto
    {
        public bool success;
        public RoundResultDto roundResult;
        public CombatSessionDto session;
        public bool sessionOver;
        public CombatRewardDto reward;
    }

    /// go: models.CombatEndResponseDTO
    [Serializable]
    public class CombatEndResponseDto
    {
        public bool success;
        public CombatRewardDto reward;
    }
}
