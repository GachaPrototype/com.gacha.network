using System;
using System.Collections;
using System.Globalization;
using System.Text;
using GachaGame.Contracts.Api;

namespace GachaGame.Network
{
    // Gateway theo từng mảng tính năng.
    //
    // Đây là toàn bộ bề mặt mà các package khác được phép dùng để nói chuyện với
    // server. Không package nào ngoài com.gacha.network được tự dựng
    // UnityWebRequest hay tự biết endpoint.
    //
    // Mọi hàm là coroutine: phần gọi dùng "yield return gateway.XxxAsync(...)".

    /// Trạng thái server, kiểm tra phiên bản và bảo trì. Không cần đăng nhập.
    public sealed class SystemGateway : ApiGateway
    {
        public SystemGateway(APIClient api = null) : base(api) { }

        /// Gọi trước mọi thứ khác lúc khởi động game. Vừa cho biết server còn
        /// sống, vừa đồng bộ giờ server cho chữ ký anti-cheat.
        public IEnumerator GetBootstrap(Action<ApiResult<BootstrapResponseDto>> onDone)
        {
            return Get("/system/bootstrap", onDone);
        }
    }

    /// Hồ sơ, roster, tiến trình và đội hình.
    public sealed class PlayerGateway : ApiGateway
    {
        public PlayerGateway(APIClient api = null) : base(api) { }

        public IEnumerator GetProfile(Action<ApiResult<PlayerProfileDto>> onDone)
        {
            return Get<PlayerProfileDto>("/player/profile", onDone, (profile, body) =>
            {
                profile.inventory = JsonMap.Field(body, "inventory");
                profile.stageStars = JsonMap.Field(body, "stageStars");
                profile.storyFlags = JsonMap.Field(body, "storyFlags");
            });
        }

        public IEnumerator GetRoster(Action<ApiResult<RosterResponseDto>> onDone)
        {
            return Get("/player/roster", onDone);
        }

        public IEnumerator ChangeNickname(string newNickname, Action<ApiResult> onDone)
        {
            return Post("/player/nickname/change",
                        new ChangeNicknameRequest { newNickname = newNickname }, onDone);
        }

        public IEnumerator GetProgress(Action<ApiResult<CampaignProgressResponseDto>> onDone)
        {
            return Get<CampaignProgressResponseDto>("/player/progress", onDone, EnrichProgress);
        }

        public IEnumerator CompleteStoryFlag(string flag,
                                             Action<ApiResult<CampaignProgressResponseDto>> onDone)
        {
            return Post<CampaignProgressResponseDto>("/player/progress/story",
                new CompleteStoryFlagRequest { flag = flag }, onDone, EnrichProgress);
        }

        public IEnumerator GetParties(Action<ApiResult<PartiesResponseDto>> onDone)
        {
            return Get("/player/parties", onDone);
        }

        /// partyId rỗng nghĩa là tạo đội mới; server sẽ sinh id.
        public IEnumerator SaveParty(string partyId, string name, string[] characterUids,
                                     Action<ApiResult<PartyResponseDto>> onDone)
        {
            return Post<PartyResponseDto>("/player/parties/save", new SavePartyRequest
            {
                partyId = partyId,
                name = name,
                characterUids = characterUids,
            }, onDone);
        }

        public IEnumerator DeleteParty(string partyId, Action<ApiResult<PartyResponseDto>> onDone)
        {
            return Post<PartyResponseDto>("/player/parties/delete",
                new DeletePartyRequest { partyId = partyId }, onDone);
        }

        /// stageStars và storyFlags nằm trong response.progress, không phải ở cấp
        /// ngoài cùng, nên phải lấy object progress ra trước.
        private static void EnrichProgress(CampaignProgressResponseDto response, string body)
        {
            if (response.progress == null) return;

            string progressJson = JsonProbe.GetRaw(body, "progress");
            response.progress.stageStars = JsonMap.Field(progressJson, "stageStars");
            response.progress.storyFlags = JsonMap.Field(progressJson, "storyFlags");
        }
    }

    /// Template nhân vật và lên cấp.
    public sealed class CharacterGateway : ApiGateway
    {
        public CharacterGateway(APIClient api = null) : base(api) { }

        /// Không cần đăng nhập; đây là dữ liệu master data.
        public IEnumerator GetTemplate(string baseId,
                                       Action<ApiResult<CharacterTemplateResponseDto>> onDone)
        {
            return Get($"/character/template?baseId={Uri.EscapeDataString(baseId ?? string.Empty)}", onDone);
        }

        /// Client gửi danh sách EXP item muốn tiêu thụ, server tự tra bảng giá trị.
        ///
        /// items là map phía server mà JsonUtility không ghi được, nên payload
        /// được dựng tay ở đây.
        public IEnumerator LevelUp(string characterUid, MapEntry[] items,
                                   Action<ApiResult<LevelUpResponseDto>> onDone)
        {
            var payload = new StringBuilder();
            payload.Append("{\"characterUid\":\"").Append(Escape(characterUid)).Append("\",\"items\":{");

            if (items != null)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (i > 0) payload.Append(',');
                    payload.Append('"').Append(Escape(items[i].Key)).Append("\":")
                           .Append(items[i].AsInt().ToString(CultureInfo.InvariantCulture));
                }
            }

            payload.Append("}}");
            return Post<LevelUpResponseDto>("/character/levelup", payload.ToString(), onDone);
        }

        /// Phân bổ một điểm cung mệnh vào một nhánh.
        /// branch là "damage", "tank" hoặc "support".
        ///
        /// Mỗi bản trùng của nhân vật cho một điểm; xem PlayerCharacterDto.UnspentPoints.
        public IEnumerator AllocateConstellation(string characterUid, string branch,
                                                 Action<ApiResult<CharacterConstellationResponseDto>> onDone)
        {
            return Post<CharacterConstellationResponseDto>("/character/constellation/allocate",
                new AllocateConstellationRequest
                {
                    characterUid = characterUid,
                    branch = branch,
                }, onDone);
        }

        /// Tẩy toàn bộ ba nhánh, trả lại số điểm đã dùng.
        public IEnumerator ResetConstellation(string characterUid,
                                              Action<ApiResult<CharacterConstellationResponseDto>> onDone)
        {
            return Post<CharacterConstellationResponseDto>("/character/constellation/reset",
                new ResetConstellationRequest { characterUid = characterUid }, onDone);
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    /// Banner và triệu hồi.
    public sealed class GachaGateway : ApiGateway
    {
        public GachaGateway(APIClient api = null) : base(api) { }

        /// Không cần đăng nhập. Trả về mảng rỗng khi chưa banner nào đang mở.
        public IEnumerator GetActiveBanners(Action<ApiResult<ActiveBannersResponseDto>> onDone)
        {
            return Get("/banner/active", onDone);
        }

        /// Số dư và bộ đếm pity của người chơi trên một banner.
        /// Gọi trước khi mở màn hình gacha.
        public IEnumerator GetState(string bannerId,
                                    Action<ApiResult<GachaStateResponseDto>> onDone)
        {
            return Get($"/gacha/state?bannerId={Uri.EscapeDataString(bannerId ?? string.Empty)}", onDone);
        }

        /// Quay gacha.
        ///
        /// requestId là KHOÁ CHỐNG TRÙNG. Để trống thì gateway tự sinh một cái.
        /// Nhờ khoá này, request được phép gửi lại khi mạng đứt giữa chừng mà
        /// không sợ quay hai lần: server nhận ra khoá cũ và trả về đúng biên nhận
        /// trước đó.
        ///
        /// Muốn chống trùng qua cả lần mở lại app (người chơi tắt game đúng lúc
        /// đang quay) thì tự sinh requestId, lưu xuống máy trước khi gọi, và
        /// truyền lại đúng khoá đó khi vào game lần sau.
        public IEnumerator Summon(string bannerId, string bannerType, bool tenPull,
                                  Action<ApiResult<SummonResponseDto>> onDone,
                                  string requestId = null)
        {
            var payload = new SummonRequest
            {
                requestId = string.IsNullOrEmpty(requestId) ? NewRequestId() : requestId,
                bannerId = bannerId,
                bannerType = bannerType,
                isTenPull = tenPull,
            };

            return Post<SummonResponseDto>("/gacha/summon", payload, onDone, null, idempotent: true);
        }

        /// 32 ký tự hex, nằm trong luật của server (1-128 ký tự, chỉ [A-Za-z0-9_-]).
        public static string NewRequestId() => Guid.NewGuid().ToString("N");
    }

    /// Mana Core và Mana Hex.
    public sealed class EquipmentGateway : ApiGateway
    {
        public EquipmentGateway(APIClient api = null) : base(api) { }

        public IEnumerator GetAll(Action<ApiResult<EquipmentListResponseDto>> onDone)
        {
            return Get("/equipment/list", onDone);
        }

        /// slotIndex 0-3, tổng point cost của một nhân vật không vượt quá 10.
        public IEnumerator Equip(string characterUid, string coreItemUuid, int slotIndex,
                                 Action<ApiResult<EquipmentResponseDto>> onDone)
        {
            return Post<EquipmentResponseDto>("/equipment/equip", new EquipManaCoreRequest
            {
                characterUid = characterUid,
                coreItemUuid = coreItemUuid,
                slotIndex = slotIndex,
            }, onDone);
        }

        public IEnumerator Unequip(string characterUid, int slotIndex,
                                   Action<ApiResult<EquipmentResponseDto>> onDone)
        {
            return Post<EquipmentResponseDto>("/equipment/unequip", new UnequipManaCoreRequest
            {
                characterUid = characterUid,
                slotIndex = slotIndex,
            }, onDone);
        }

        /// hexIndex 0-2.
        ///
        /// Nâng cấp có thể THẤT BẠI mà vẫn trả HTTP 200: khi đó server dùng mã
        /// ERR_HEX_UPGRADE_FAILED. Đừng coi 200 là nâng thành công.
        public IEnumerator UpgradeHex(string coreItemUuid, int hexIndex,
                                      Action<ApiResult<EquipmentResponseDto>> onDone)
        {
            return Post<EquipmentResponseDto>("/equipment/hex/upgrade", new UpgradeManaHexRequest
            {
                coreItemUuid = coreItemUuid,
                hexIndex = hexIndex,
            }, onDone);
        }
    }

    /// Phiên chiến đấu theo lượt.
    ///
    /// Bất biến của server: một lần setup ứng với một lần execute. Gọi execute
    /// hai lần liên tiếp sẽ nhận ERR_COMBAT_SESSION_WRONG_PHASE, và session còn
    /// được khoá bằng Redis nên hai request song song không thể cùng chạy.
    ///
    /// Một vòng đấu đi theo thứ tự:
    ///
    ///   SubmitSetup     khoá lệnh của cả đội cho round này
    ///   GetEnemies      đọc telegraph và hạn chót cửa sổ phản xạ
    ///   SubmitReaction  gửi Block/Dodge/Parry trước hạn chót (tuỳ chọn)
    ///   Execute         phân giải round
    ///
    /// LƯU Ý: DTO của mảng này là tạm thời, xem đầu file CombatDtos.cs.
    public sealed class CombatGateway : ApiGateway
    {
        public CombatGateway(APIClient api = null) : base(api) { }

        /// Tạo session từ một đội đã lưu. Trừ stamina ngay tại đây.
        ///
        /// Server đã bỏ đường vào bằng danh sách nhân vật rời; giờ bắt buộc có
        /// party đã lưu.
        public IEnumerator CreateSession(string stageId, string partyId,
                                         Action<ApiResult<CombatSessionResponseDto>> onDone)
        {
            return Post<CombatSessionResponseDto>("/combat/session/create", new CreateCombatSessionRequest
            {
                stageId = stageId,
                partyId = partyId,
            }, onDone);
        }

        /// Gửi kế hoạch của cả đội cho round hiện tại. Sau bước này server sinh
        /// sẵn ý đồ của địch để client chiếu telegraph.
        ///
        /// Không truyền roundNumber: server tự quản lý số round.
        ///
        /// Response mang hạn chót cửa sổ phản xạ — dùng nó để mở QTE ngay, đừng
        /// đợi gọi thêm /enemies.
        public IEnumerator SubmitSetup(string sessionId, ActionSetupDto[] actions,
                                       Action<ApiResult<CombatSetupResultDto>> onDone)
        {
            return Post<CombatSetupResultDto>("/combat/session/setup", new CombatSetupRequest
            {
                sessionId = sessionId,
                setup = new RoundSetupPayload { actions = actions },
            }, onDone);
        }

        /// Trạng thái địch và telegraph, payload nhẹ để polling trong pha phản xạ.
        ///
        /// Dùng reactionDeadlineUnixMs của response để đếm ngược, đừng dùng giờ
        /// máy — người chơi lệch đồng hồ sẽ thấy cửa sổ sai hoàn toàn.
        public IEnumerator GetEnemies(string sessionId,
                                      Action<ApiResult<EnemyCombatStateResponseDto>> onDone)
        {
            return Get($"/combat/session/enemies?sessionId={Uri.EscapeDataString(sessionId ?? string.Empty)}",
                       onDone);
        }

        /// Gửi phản xạ canh nhịp trong lúc cửa sổ còn mở.
        ///
        /// Kiểm response.accepted để biết cái nào thực sự được ghi nhận: gửi ba
        /// phản xạ mà accepted chỉ có hai nghĩa là cái thứ ba bị từ chối vì hết
        /// stamina hoặc gửi muộn.
        public IEnumerator SubmitReaction(string sessionId, ReactionChoiceDto[] reactions,
                                          Action<ApiResult<CombatReactionResponseDto>> onDone)
        {
            return Post<CombatReactionResponseDto>("/combat/session/react", new CombatReactionRequest
            {
                sessionId = sessionId,
                reactions = reactions ?? Array.Empty<ReactionChoiceDto>(),
            }, onDone);
        }

        /// Phân giải round.
        ///
        /// Reactions KHÔNG còn gửi ở đây nữa — chúng đi qua SubmitReaction trong
        /// lúc cửa sổ phản xạ còn mở.
        public IEnumerator Execute(string sessionId,
                                   Action<ApiResult<CombatExecuteResponseDto>> onDone)
        {
            return Post<CombatExecuteResponseDto>("/combat/session/execute", new CombatExecuteRequest
            {
                sessionId = sessionId,
            }, onDone, (response, body) =>
            {
                if (response.reward != null)
                {
                    response.reward.items = JsonMap.Field(JsonProbe.GetRaw(body, "reward"), "items");
                }
            });
        }

        /// Đọc lại trạng thái một session cụ thể.
        public IEnumerator GetState(string sessionId,
                                    Action<ApiResult<CombatSessionResponseDto>> onDone)
        {
            return Get($"/combat/session/state?sessionId={Uri.EscapeDataString(sessionId ?? string.Empty)}",
                       onDone);
        }

        /// Tìm trận đang dở của người chơi, không cần biết sessionId.
        ///
        /// Đây là đường khôi phục sau khi app bị đóng giữa trận: gọi lúc vào game,
        /// có session thì đưa thẳng người chơi trở lại trận đó.
        ///
        /// Server trả 204 khi không có trận nào — khi đó kết quả là THÀNH CÔNG với
        /// Value = null, không phải lỗi. Luôn kiểm null trước khi dùng:
        ///
        ///     if (result.IsSuccess &amp;&amp; result.Value != null) // có trận dở
        public IEnumerator GetActiveSession(Action<ApiResult<CombatSessionResponseDto>> onDone)
        {
            return GetOptional("/combat/session/active", onDone);
        }

        /// Chủ động kết thúc và quyết toán phần thưởng.
        public IEnumerator EndSession(string sessionId, Action<ApiResult<CombatEndResponseDto>> onDone)
        {
            return Post<CombatEndResponseDto>("/combat/session/end",
                new CombatEndRequest { sessionId = sessionId }, onDone,
                (response, body) =>
                {
                    if (response.reward != null)
                    {
                        response.reward.items = JsonMap.Field(JsonProbe.GetRaw(body, "reward"), "items");
                    }
                });
        }
    }
}
