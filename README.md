# com.gacha.network

Tầng nói chuyện với Go server: HTTP, xác thực, chữ ký anti-cheat, và gateway theo
từng mảng tính năng.

**Quy tắc:** HTTP chỉ được triển khai trong package này. Package khác gọi gateway
và nhận `ApiResult<T>`, không bao giờ tự dựng `UnityWebRequest` hay tự biết
endpoint trông ra sao.

## Bắt đầu

Gắn `APIClient` và `AuthService` lên cùng một GameObject. `AuthService` yêu cầu
`APIClient` nên Unity tự thêm giúp.

```csharp
var api = gameObject.AddComponent<APIClient>();
api.Configure(baseUrl: "http://localhost:3000/api", build: "development");

var auth = gameObject.AddComponent<AuthService>();
var player = new PlayerGateway(api);

yield return auth.Login("unityclient", "UnityClient12345", result =>
{
    if (result.IsFailure) Debug.LogError(result.Error);
});

yield return player.GetProfile(result =>
{
    if (result.IsSuccess) Debug.Log(result.Value.nickname);
});
```

Ví dụ đầy đủ nằm trong sample **Gateway Quick Start** (Package Manager → Samples).

## Ba điều không phải lo

**Token.** `AuthService` giữ access token và refresh token. Nó đổi token khi còn
dưới 60 giây, và `APIClient` tự gửi lại đúng một lần nếu server từ chối token.
Không chỗ nào trong game cần đọc hay truyền token.

**Khoá ký.** Server cấp `sessionKey` riêng cho mỗi phiên và đổi nó sau **mỗi lần**
cấp access token, kể cả khi gia hạn. `AuthService` gom việc đặt token và đặt khoá
ký vào cùng một hàm nên không nơi nào có cơ hội quên một nửa. Package này **không**
mang `HMAC_SECRET_KEY` của server, và bản release không có đường nào để mang.

**Lệch đồng hồ.** Chữ ký chỉ sống 30 giây và không được sớm hơn giờ server quá 5
giây. `APIClient` đồng bộ phần lệch từ header `Date` của mọi response và ký bằng
giờ đã bù. Cần giờ server ở chỗ khác (đếm ngược hạn banner chẳng hạn) thì dùng
`APIClient.ServerUtcNow`.

## Gateway

| Gateway | Hàm | Endpoint |
|---|---|---|
| `SystemGateway` | `GetBootstrap` | `GET /system/bootstrap` |
| `PlayerGateway` | `GetProfile` | `GET /player/profile` |
| | `GetRoster` | `GET /player/roster` |
| | `ChangeNickname` | `POST /player/nickname/change` |
| | `GetProgress` | `GET /player/progress` |
| | `CompleteStoryFlag` | `POST /player/progress/story` |
| | `GetParties` | `GET /player/parties` |
| | `SaveParty` | `POST /player/parties/save` |
| | `DeleteParty` | `POST /player/parties/delete` |
| `CharacterGateway` | `GetTemplate` | `GET /character/template` |
| | `LevelUp` | `POST /character/levelup` |
| | `AllocateConstellation` | `POST /character/constellation/allocate` |
| | `ResetConstellation` | `POST /character/constellation/reset` |
| `GachaGateway` | `GetActiveBanners` | `GET /banner/active` |
| | `GetState` | `GET /gacha/state` |
| | `Summon` | `POST /gacha/summon` |
| `EquipmentGateway` | `GetAll` | `GET /equipment/list` |
| | `Equip` | `POST /equipment/equip` |
| | `Unequip` | `POST /equipment/unequip` |
| | `UpgradeHex` | `POST /equipment/hex/upgrade` |
| `CombatGateway` | `CreateSession` | `POST /combat/session/create` |
| | `SubmitSetup` | `POST /combat/session/setup` |
| | `GetEnemies` | `GET /combat/session/enemies` |
| | `SubmitReaction` | `POST /combat/session/react` |
| | `Execute` | `POST /combat/session/execute` |
| | `GetState` | `GET /combat/session/state` |
| | `GetActiveSession` | `GET /combat/session/active` |
| | `EndSession` | `POST /combat/session/end` |

Nhóm `/auth` không có gateway riêng: nó là việc của `AuthService`, vì đăng nhập
gắn liền với vòng đời phiên.

## Nối lại sau khi đứt

`ConnectionMonitor` lo tình huống lớn hơn việc gửi lại một request: mất mạng cả
phút, hoặc app xuống nền rồi mở lại sau nửa tiếng.

```csharp
var monitor = gameObject.AddComponent<ConnectionMonitor>();

monitor.OnOffline    += () => ShowReconnectingOverlay();
monitor.OnBackOnline += () => HideReconnectingOverlay();
monitor.OnResumed    += () => RefreshCurrentScreen();

// Màn hình "đang thử lại..." chờ ở đây
yield return monitor.WaitUntilOnline(ok => { if (ok) HideReconnectingOverlay(); });
```

Khi app quay lại từ nền, `ConnectionMonitor` tự ping `/system/bootstrap` (rẻ,
không cần token, và tiện thể đồng bộ lại giờ server) rồi gia hạn token ngay, thay
vì để request đầu tiên của màn hình game phải chịu một vòng 401.

Trận đấu dở cũng khôi phục được — server nhớ giúp:

```csharp
yield return combat.GetActiveSession(result =>
{
    if (result.IsSuccess && result.Value != null)
        ResumeBattle(result.Value.session);   // có trận dở
});
```

Server trả **204 No Content** khi không có trận nào. Đó là câu trả lời hợp lệ chứ
không phải lỗi, nên kết quả là `IsSuccess` với `Value == null`. Luôn kiểm null.

## Gửi lại và khoá chống trùng

Request không tới được server thì được gửi lại, nhưng chỉ hai loại:

- **GET** — đọc dữ liệu, gọi lại bao nhiêu lần cũng không đổi gì.
- **POST có khoá chống trùng** — server nhận ra request lặp và trả về kết quả cũ.

`/gacha/summon` thuộc loại thứ hai: nó mang `requestId`, server lưu biên nhận theo
khoá đó. `GachaGateway.Summon` tự sinh khoá, nên mất mạng giữa lúc quay vẫn gửi
lại an toàn.

POST **không** có khoá chống trùng thì tuyệt đối không gửi lại: server có thể đã
xử lý xong rồi mới đứt đường về, gửi lại là trừ tiền hai lần.

Muốn chống trùng qua cả lần mở lại app thì tự sinh `requestId`, lưu xuống máy
trước khi gọi, rồi truyền lại đúng khoá đó ở lần sau.

## Xử lý lỗi

Server không gửi câu chữ hiển thị. Mọi response lỗi có dạng
`{"success":false,"code":"ERR_...","params":{...}}`, và việc dịch mã sang tiếng
Việt thuộc về `com.gacha.localization`. Vì vậy **luôn bắt theo mã**:

```csharp
if (result.FailedWith(ApiErrorCode.InsufficientStamina)) { ... }

// Hoặc phân nhóm sẵn:
if (result.Error.IsRateLimited)  // xem result.Error.RetryAfterSeconds
if (result.Error.IsAntiCheat)    // gần như luôn là lỗi phía client
if (result.Error.IsNetworkFailure)
```

`ApiErrorCode` được **sinh tự động** từ `models/api_error.go`. Đừng sửa tay; chạy
lại `node Tools/gen-api-error-codes.mjs` khi server thêm mã mới.

## Chỗ còn nợ

**Map JSON.** `JsonUtility` của Unity không đọc được `Dictionary`, mà server trả
map ở `inventory`, `stageStars`, `storyFlags` và `items` của phần thưởng combat.
Hiện chúng được lấy từ body thô qua `JsonMap` và truy cập bằng hàm tiện ích
(`profile.ItemCount("exp_book_small")`). Khi team chốt thêm Newtonsoft thì
`JsonMap` là chỗ duy nhất phải sửa.

**Lưu phiên.** `PlayerPrefsTokenStorage` là plaintext. Đổi bằng
`auth.UseStorage(...)` khi lên production; không chỗ nào khác phải sửa. Access
token cố ý không được lưu — nó chỉ sống 15 phút.

**DTO combat.** `CombatDtos.cs` là tạm thời. `docs/COMBAT_DESIGN_FORK.md` cho biết
thiết kế combat vẫn đang chờ đội thiết kế chốt, và mô hình mana là một trong hai
điểm tranh chấp chính.

**WebSocket chưa làm, vì server chưa có.** Task của tầng network có nhắc WebSocket,
nhưng repo server hiện thuần REST — không có thư viện WebSocket nào trong
`go.mod`. Cần Backend làm trước, và cần biết nó dùng cho tính năng gì.

**Lãnh địa.** `ActionSetup` phía server đã bỏ `domainId` và `domainDuration`,
nhưng `DomainState` vẫn còn. Cơ chế chưa mất, chỉ là cách kích hoạt đã đổi — hỏi
Backend nếu UI đụng tới.

**`Gacha.Battle` không thấy package này.** Theo asmdef hiện tại, battle chỉ tham
chiếu Core, Contracts và Content. Khi battle vào việc combat, hoặc `CombatGateway`
phải được trừu tượng hoá qua một interface đặt ở Contracts, hoặc team phải cho
battle tham chiếu Network. Đây là quyết định kiến trúc chưa ai chốt.

## Kiểm tra trước khi commit

```bash
node Tools/gen-api-error-codes.mjs --check && node Tools/check-dto-drift.mjs && dotnet run --project Tools/ContractCheck
```

Ba lệnh này chạy ngoài Unity trong vài giây. Phần logic phụ thuộc coroutine nằm ở
EditMode test: **Window → General → Test Runner → EditMode → Run All**.
