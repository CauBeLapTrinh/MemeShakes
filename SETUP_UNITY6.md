# Catshakes – Hướng dẫn setup Unity 6 (2D, Android dọc 9:16)

## 0. Cấu trúc code

```
Assets/Scripts/
  Data/          Direction.cs  TileType.cs  CatId.cs  LevelDataSO.cs
  Core/          GridManager.cs  CatArmController.cs  GridSpace.cs  GameStateSnapshot.cs
                 MoveOutcome.cs  CameraFitter.cs
  Entities/      GridEntity.cs  Barrel.cs  SwitchEntity.cs  DoorEntity.cs
  InputHandling/ TouchInputHandler.cs
  Flow/          GameSceneManager.cs  GameplayController.cs  SceneId.cs
  UI/            GameplayHUD.cs  LevelSelectMenu.cs  MenuNavigationUI.cs
  Editor/        SampleLevelCreator.cs   (tạo 4 level mẫu bằng 1 click)
```

Copy thư mục `Scripts` vào `Assets/` của project. Toàn bộ code nằm trong namespace `Catshakes`.

## 1. Project & Package

1. Tạo project **Unity 6 – 2D (URP)** hoặc 2D Core.
2. Package Manager phải có: **Input System** và **Unity UI (uGUI, đã kèm TextMeshPro)**.
3. `Project Settings > Player > Other Settings > Active Input Handling`: chọn **Input System Package (New)** hoặc **Both**. `TouchInputHandler` tự dùng Input System khi có, và dùng `Input` cũ khi không có.
4. Nếu dùng Input System mới: EventSystem cần **Input System UI Input Module** (nút "Replace with InputSystemUIInputModule" hiện ra khi tạo EventSystem).
5. `Player > Resolution and Presentation`: Default Orientation = **Portrait**. Game view: thêm preset **1080 x 1920 (9:16)** để test.
6. Import TMP Essentials khi Unity hỏi lần đầu.

## 2. Tạo level mẫu

Menu **Catshakes > Create Sample Levels** tạo 4 asset trong `Assets/Levels` (đã giải bằng thuật toán tìm kiếm; Level_02 cần đẩy barrel, Level_03 cần Switch_1, Level_04 cần Switch_2).

Tạo level riêng: **Create > Catshakes > Level Data**. Cách nhanh nhất là gõ vào ô `Ascii Layout` rồi chuột phải tiêu đề component > **Import From ASCII Layout**:

| Ký tự | Ý nghĩa |
|---|---|
| `#` | Tường |
| `.` hoặc khoảng trắng | Ô trống |
| `P` / `Q` | Gốc của mèo A / mèo B |
| `O` | Barrel |
| `1`–`9` | Switch_1 (momentary), linkId = chữ số |
| `a`–`i` | Switch_2 (toggle), linkId = 1–9 |
| `A`–`I` | Door, linkId = 1–9 (door `A` nối với switch `1` hoặc `a`) |

Dòng đầu tiên của text là hàng trên cùng. Đặt `Cat A Max Steps` / `Cat B Max Steps` ở các field phía trên. Chuột phải > **Validate Level** để kiểm tra lỗi (ô trùng nhau, door không có switch...).

## 3. Prefab cần tạo

Mặc định 1 cell = 1 unit (`GridManager.cellSize = 1`). Sprite nên có kích thước 1x1 unit, pivot ở giữa. Chưa có art thì dùng `Create > 2D > Sprites > Square` và tô màu.

| Prefab | Component | Sorting Order gợi ý |
|---|---|---|
| `FloorTile` | SpriteRenderer | 0 |
| `Wall` | SpriteRenderer | 1 |
| `Switch_Momentary` | SpriteRenderer + **SwitchEntity** | 2 |
| `Switch_Toggle` | SpriteRenderer + **SwitchEntity** | 2 |
| `Barrel` | SpriteRenderer + **Barrel** | 5 |
| `Door` | SpriteRenderer + **DoorEntity** | 6 |
| `LevelButton` (UI) | Button + con TMP_Text | – |

- `SwitchEntity` / `DoorEntity` tự tô màu khi chưa gán sprite; gán đủ cặp sprite (inactive/active, closed/open) nếu muốn đổi sprite.
- Prefab không cần Collider hay Rigidbody: toàn bộ va chạm chạy bằng dữ liệu `Vector2Int`.

### Mèo (2 object trong scene Gameplay: `CatA`, `CatB`)

Đặt transform của object cha ở (0,0,0), scale 1. Mỗi mèo gồm:

```
CatA            <- CatArmController (Cat Id = CatA / CatB, Rest Facing)
 ├─ Body        SpriteRenderer (order 8)       -> gán vào Body Visual
 │   ├─ SelectionIndicator  (vòng sáng, order 7) -> gán vào Selection Indicator
 │   └─ StepsLabel          TextMeshPro - Text (3D), order 20 -> gán vào Steps Label
 ├─ Hand        SpriteRenderer (order 10, hình hướng sang PHẢI) -> gán vào Hand Visual
 └─ ArmLine     LineRenderer (order 9)         -> gán vào Arm Line
```

Cấu hình `ArmLine`: **Use World Space = ON**, Width ≈ 0.35, Corner Vertices = 5, End Cap Vertices = 5, Material = `Sprites-Default`, màu theo mèo. Số điểm do code tự điều khiển.

## 4. Scene Gameplay

Hierarchy:

```
Main Camera           Camera (Orthographic) + CameraFitter
GridManager           GridManager
CatA, CatB            (mục 3)
GameplayController    GameplayController
InputHandler          TouchInputHandler
Canvas                Canvas + GameplayHUD
 ├─ TopBar            LevelName, Timer, MoveCount (TMP_Text)
 ├─ BottomBar         Undo, Reset, SwitchCat, Menu (Button)
 └─ WinPanel          Title, Summary, Next, LevelSelect, Replay
EventSystem
```

- **GridManager**: gán 6 prefab (Floor, Wall, Barrel, Momentary Switch, Toggle Switch, Door), `Cat A`, `Cat B`. `Grid Origin` để trống thì lưới bắt đầu tại vị trí GridManager (cell (0,0) là góc dưới trái).
- **GameplayController**: gán `Grid Manager`, `Camera Fitter`, và `Fallback Level` (chỉ dùng khi bấm Play trực tiếp scene này trong Editor).
- **TouchInputHandler**: gán `Grid Manager`, `Game Camera`. Control Scheme mặc định **Hybrid**:
  - Chạm vào mèo rồi **kéo**: tay mèo bám theo ngón tay từng ô; kéo ngược lại trên thân tay thì rút tay.
  - Chạm chỗ khác rồi **vuốt**: mỗi lần vuốt vượt ngưỡng = 1 ô cho mèo đang chọn (vuốt liên tục để đi tiếp).
  - **Chạm nhanh** vào ô kề tay mèo đang chọn: tay vươn sang ô đó (chạm ô liền trước = rút tay).
  - Nút **SwitchCat** trên HUD đổi mèo; chạm vào mèo kia cũng đổi.
  - Chuột trong Editor hoạt động như cảm ứng.
- **Canvas**: Screen Space – Overlay, Canvas Scaler = *Scale With Screen Size*, Reference Resolution **1080 x 1920**, Match = 0.5. Gán các tham chiếu vào `GameplayHUD` (mọi field đều tùy chọn).
- `GameplayHUD` không cần `GameSceneManager` để chạy thử: khi thiếu, panel thắng tự hiện.

### Camera 2D Orthographic cho màn 9:16

- `Projection = Orthographic`, `Clear Flags = Solid Color`, Position (0, 0, -10). **Không cần chỉnh Size bằng tay**: `CameraFitter` tính `orthographicSize` và vị trí để bàn cờ vừa khít cả chiều ngang lẫn dọc, và tự tính lại khi đổi kích thước màn hình.
- Thông số `CameraFitter`: `Board Padding Cells` (viền quanh bàn cờ, mặc định 0.5 ô), `Top Ui Fraction` / `Bottom Ui Fraction` (phần chiều cao dành cho HUD, mặc định 12% mỗi bên). Bàn cờ được đặt vào giữa vùng trống còn lại.

## 5. Các scene còn lại và Build Settings

Thứ tự trong **File > Build Profiles (Build Settings)**: `Title (0)`, `MainMenu (1)`, `LevelSelect (2)`, `Gameplay (3)`. Tên scene phải khớp các field tên scene của `GameSceneManager` (mặc định đúng 4 tên trên).

- **Title**: 1 root GameObject `GameSceneManager` (component `GameSceneManager`, kéo các level vào mảng `Levels` theo thứ tự chơi). Object này tự `DontDestroyOnLoad`. Tùy chọn: Canvas con chứa Image đen + CanvasGroup gán vào `Fade Group` để có hiệu ứng chuyển cảnh. Thêm Button toàn màn hình "Chạm để bắt đầu" → `MenuNavigationUI.OpenMainMenu`.
- **MainMenu**: Button Play → `MenuNavigationUI.OpenLevelSelect`; Speedrun → `MenuNavigationUI.StartSpeedrun`; Quit → `MenuNavigationUI.QuitGame`. Gán `Best Speedrun Text` nếu muốn hiện kỷ lục.
- **LevelSelect**: object có `LevelSelectMenu`; `Content Root` là RectTransform có **Grid Layout Group**; `Level Button Prefab` là prefab `LevelButton`; Back Button quay về MainMenu.
- Luôn bấm Play từ scene **Title** để có `GameSceneManager`.

## 6. Luật chơi đã cài đặt (GridManager)

- Mỗi lần di chuyển = 1 ô. Đi vào ô ngay sau bàn tay (ô liền trước trong đường tay) = **rút tay**, trả lại 1 bước.
- Số bước còn lại = `maxSteps` − số đốt tay hiện có. Hết bước thì không vươn thêm được.
- Tay không đi vào: tường, cửa đóng, ngoài lưới, thân/tay của chính mình hoặc của mèo kia (kể cả thân mèo). Ngoại lệ duy nhất: ô chứa **bàn tay đã vươn ra** của mèo kia = **bắt tay = thắng**.
- Barrel nằm ở ô đích: được đẩy 1 ô theo hướng di chuyển nếu ô sau trống (không tường, không barrel, không cửa đóng, không thân/tay mèo). Ngược lại hướng đi bị chặn.
- **Switch_1 (momentary)**: bật khi có barrel hoặc đốt tay nằm trên nó; rút tay/đẩy barrel đi thì tắt.
- **Switch_2 (toggle)**: đảo trạng thái mỗi lần có đốt tay hoặc barrel *đi vào* ô. Rút tay không đảo.
- **Door**: mở khi có ít nhất 1 switch cùng `linkId` đang bật. Nếu đang có vật (tay/barrel) đứng trong ô cửa thì cửa giữ mở, không đóng đè lên vật.
- **Undo**: mỗi nước đi hợp lệ đẩy 1 `GameStateSnapshot` vào `Stack`. **Reset** khôi phục trạng thái ban đầu và xóa lịch sử undo.

## 7. Speedrun

`MainMenu > Speedrun` gọi `GameSceneManager.StartSpeedrun()`: chơi từ level 1 tới hết, timer chạy liên tục (chỉ tính thời gian đang chơi, không tính lúc load scene hay khoảng chờ sau khi thắng), tự chuyển level sau mỗi lần thắng. Hết level cuối: lưu kỷ lục vào PlayerPrefs, HUD hiện thời gian và "NEW BEST" nếu phá kỷ lục. Tiến độ mở khóa level cũng lưu trong PlayerPrefs (`GameSceneManager.ResetSavedProgress()` để xóa).

## 8. Kiểm tra nhanh

1. Chạy **Catshakes > Create Sample Levels**.
2. Mở scene Gameplay, gán `Level_01` vào `Fallback Level` của GameplayController, bấm Play.
3. Level_01: kéo tay mèo A sang phải 2 ô, rồi kéo tay mèo B sang trái 2 ô để bắt tay.
