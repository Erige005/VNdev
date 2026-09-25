# VNdev — hướng dẫn cho Claude

File này Claude đọc tự động khi mở dự án. Nó chứa ngữ cảnh, các quyết định đã
chốt, và những ràng buộc không được vi phạm.

---

## Dự án là gì

**VNdev** là công cụ tạo visual novel trên desktop, định vị là bản thay thế
hiện đại của Ren'Py. Triết lý: trực quan tối đa, kéo thả mọi thứ, người viết
truyện không cần biết lập trình. Có AI hỗ trợ sáng tác ở giai đoạn sau.

Người dùng: **Nam**. Trao đổi bằng **tiếng Việt**. Comment trong code cũng
viết tiếng Việt, và giải thích **tại sao** chứ không mô tả lại code làm gì.

Đặc tả đầy đủ: `README.md`. Bản thiết kế giao diện: `docs/design/ui-ux.html`
(mở bằng trình duyệt). Lộ trình: `docs/LO-TRINH.md`.

---

## RÀNG BUỘC CỨNG — không được vi phạm

### 1. Tuyệt đối không HTML, không webview

Đây là yêu cầu người dùng nhắc lại nhiều lần. Không Electron, không Tauri,
không WebView2, không QtWebEngine, không React, không CSS. Giao diện phải do
chính ứng dụng vẽ ra bằng GPU.

**Đã thử và đã bỏ:** dự án từng được dựng bằng Tauri + React + TypeScript và
chạy được, nhưng người dùng bác bỏ vì bản chất vẫn là HTML nhét trong khung
cửa sổ. Đừng đề xuất lại hướng này dù nó tiện hơn.

### 2. Ngôn ngữ: C#. Engine: Godot 4 (bản .NET)

Chốt sau khi cân nhắc Python+PySide6, Rust+egui, C#+Avalonia, C#+MonoGame.

Lý do chọn Godot: engine tự vẽ từng pixel như Ren'Py, cho sẵn `GraphEdit` để
làm editor node kéo-thả, cho sẵn animation/shader/particle/audio cho phần
trình diễn game, xử lý nhập liệu tiếng Nhật-Trung-Thái tử tế, và **editor với
game xuất ra dùng chung một engine** — đúng nguyên tắc "runtime chỉ có một bản".

Lý do loại Python+Qt: game xuất ra sẽ phải mang theo Python và Qt, khoảng
100-150MB mỗi game, ngang hoặc hơn Ren'Py. Mục tiêu "nhẹ hơn Ren'Py" chết tại
đó. Muốn tránh thì phải viết engine lần thứ hai.

### 3. `VNdev.Core` không được tham chiếu Godot

Thư viện lõi là .NET thuần. Nó nhận `IFileSystem` do bên gọi cấp thay vì tự
đọc đĩa. Nhờ vậy cùng bộ code chạy được trong editor, trong game xuất ra, và
trong bộ test không cần mở engine.

Thêm `using Godot;` vào `src/VNdev.Core` là phá vỡ kiến trúc. Nếu cần chức
năng của engine, đặt nó ở tầng Godot và truyền kết quả xuống lõi.

### 4. Dự án của người dùng là thư mục JSON, không phải file nhị phân

Mỗi cảnh, mỗi chương, mỗi nhân vật một file riêng. Mục tiêu: đưa lên Git,
diff được từng dòng thoại, hai người sửa hai cảnh khác nhau thì merge không
xung đột. Không dùng `.tres` của Godot cho dữ liệu dự án.

Định dạng JSON phải ổn định tuyệt đối: thụt lề 2 dấu cách, tên trường
camelCase, không escape chữ có dấu. Nếu định dạng dao động giữa hai lần lưu,
Git báo cả file thay đổi dù chỉ sửa một chữ.

### 5. App khởi động trống trơn

Không có dự án mẫu nào được nạp sẵn. Màn hình đầu tiên là Tạo dự án mới / Mở
dự án, như Ren'Py và VS Code. Người dùng đã bác bỏ việc có dữ liệu mẫu.

Asset theo mô hình Ren'Py: người dùng tự chép file ảnh và nhạc vào thư mục
`assets/` của dự án, app quét thư mục và đọc lên. Không có nút import, không
có thư viện nội bộ phải đồng bộ.

---

## Cấu trúc kho mã

```
D:\VNdev\
├── CLAUDE.md                  file này
├── README.md                  đặc tả sản phẩm
├── VNdev.sln
├── src/
│   └── VNdev.Core/            .NET thuần, KHÔNG phụ thuộc Godot
│       ├── Model/             data model
│       ├── Io/                đọc ghi thư mục dự án
│       ├── Validation/        bộ kiểm tra đồ thị
│       └── Ids.cs             sinh id, bỏ dấu tiếng Việt
├── tests/
│   └── VNdev.Core.Tests/      chạy bằng dotnet run, không dùng xUnit
├── app/                       dự án Godot 4.3 .NET — Chặng 1-6 đã xong (80%)
│   ├── project.godot
│   ├── VNdev.App.csproj       tham chiếu VNdev.Core, không tham chiếu ngược
│   ├── Main.cs                nút gốc: màn hình, cửa sổ, cài đặt, phím tắt cấp app
│   ├── WelcomeScreen.cs       Tạo / Mở dự án, danh sách dự án gần đây
│   ├── EditorScreen.cs        menu, toolbar, 4 tab, thanh trạng thái
│   ├── ProjectSession.cs      dự án đang mở + hoàn tác (chụp trạng thái) + ProjectOpener
│   ├── AppSettings.cs         cài đặt máy này, lưu %APPDATA%\VNdev\settings.json
│   ├── Shortcuts.cs           bảng phím tắt duy nhất, đổi được trong Cài đặt
│   ├── SettingsDialog.cs      cửa sổ Cài đặt
│   ├── Dialogs.cs             hộp thoại dùng chung + ToastLayer (thông báo nhỏ)
│   ├── GraphSync.cs           lớp dịch StoryGraph ↔ GraphEdit
│   ├── NodeInspector.cs       panel thuộc tính theo từng loại node
│   ├── CharacterScreen.cs     nhân vật + panel biến (Chặng 3)
│   ├── AssetIndex.cs, AssetScreen.cs  quét assets/ (Chặng 4)
│   ├── SceneScreen.cs         trình soạn cảnh, StageAt() preview (Chặng 5)
│   ├── StageRenderer.cs       vẽ nền+nhân vật, dùng chung Scene Canvas/Player
│   ├── PlayerScreen.cs        bộ thông dịch chạy truyện (Chặng 6)
│   ├── Palette.cs, AppTheme.cs  màu và theme dùng chung
└── docs/
    ├── LO-TRINH.md            lộ trình và trạng thái
    ├── CHAY-DU-AN.md          hướng dẫn chạy
    └── design/ui-ux.html      bản thiết kế giao diện
```

---

## Lệnh thường dùng

```powershell
# chạy toàn bộ test — chỉ cần .NET SDK 8, không cần Godot
dotnet run --project tests\VNdev.Core.Tests

# build lõi
dotnet build src\VNdev.Core

# build tầng Godot — chỉ cần .NET SDK, không cần mở editor
dotnet build app\VNdev.App.csproj

# mở editor Godot
"%LOCALAPPDATA%\Programs\Godot\Godot_v4.3-stable_mono_win64.exe" --path app
```

Máy dựng ban đầu không có sẵn .NET SDK lẫn Godot — đã cài .NET 8 SDK vào
`%LOCALAPPDATA%\Microsoft\dotnet` và Godot 4.3 .NET (mono) vào
`%LOCALAPPDATA%\Programs\Godot`. Nếu lệnh `dotnet`/đường dẫn Godot ở trên
không chạy trên máy khác, cài lại ở hai chỗ đó hoặc theo PATH hệ thống.

**Luôn chạy test sau khi sửa `VNdev.Core`.** Bộ test chạy trong vài giây và đã
bắt được lỗi thật nhiều lần.

---

## Quy ước viết code

- `TreatWarningsAsErrors` đang bật. Build phải sạch tuyệt đối.
- Nullable reference types bật. Đừng tắt để cho nhanh.
- Comment tiếng Việt, giải thích **lý do** và **đánh đổi**, không mô tả lại
  cú pháp. Ví dụ tốt: "Thêm hậu tố ngẫu nhiên thay vì đếm tăng dần, vì hai
  người làm song song trên hai nhánh Git đều đếm từ 1 và sẽ đụng nhau khi
  merge." Ví dụ xấu: "Hàm này tạo id."
- Tên hàm và biến bằng tiếng Anh, theo chuẩn C#. Chỉ comment mới tiếng Việt.
- Thông điệp lỗi hiện cho người dùng thì viết tiếng Việt, nói rõ sai ở đâu.
- Test đặt tên tiếng Việt không dấu, mô tả hành vi:
  `Phat_hien_nhanh_cut_khong_dan_toi_ket_thuc`.

---

## Trạng thái hiện tại

**Đã xong và đã kiểm chứng (33/33 test pass):**

- Data model đầy đủ: dự án, chương, 7 loại node, cảnh, lời thoại, nhân vật,
  biểu cảm, biến, điều kiện lồng nhau
- Đọc ghi thư mục dự án dạng JSON, giữ nguyên chữ có dấu và chữ Nhật
- Bộ kiểm tra đồ thị: 11 loại lỗi (node mồ côi, nhánh cụt, cổng chưa nối,
  biến chưa khai báo, thiếu cảnh, thiếu nhân vật, thiếu biểu cảm, sai điểm
  vào, trùng id, không có kết thúc)
- `SceneData.StageAt()` — tính trạng thái sân khấu tại một dòng thoại, dùng
  cho preview trong editor
- Chặn đường dẫn đi ra ngoài thư mục dự án

**Godot — Chặng 1 đến 6, đạt mốc 80%** (`app/`, xem `docs/LO-TRINH.md`): màn
hình chào, Story Graph editor (`GraphEdit`), quản lý nhân vật + biến, thư
viện asset đọc thật từ đĩa, trình soạn cảnh có preview `StageAt()`, và bộ
thông dịch chạy được cả câu chuyện (bấm ▶ Chơi thử) — có ảnh thật, nhạc thật
(ogg/mp3), biến đổi theo lựa chọn, tới đúng màn Kết thúc. Đã kiểm chứng bằng
kịch bản `--headless` đi hết một lượt: tạo nhân vật → quét asset → soạn cảnh
→ dựng đồ thị → chơi thử tới kết thúc, không lỗi runtime.

**Hoàn thiện app (xong):** menu Tệp/Sửa/Xem/Chạy/Trợ giúp, phím tắt đổi
được, hoàn tác/làm lại cho mọi tab, cửa sổ Cài đặt, dự án gần đây, nhớ cửa
sổ, thanh trạng thái, thông báo nhỏ. Xem "Hoàn thiện app" trong
`docs/LO-TRINH.md`.

**Hoàn tác hoạt động thế nào — đừng phá:** mọi màn hình ghi đĩa qua
`ProjectSession.Fs` (bọc `IFileSystem`). Mỗi lần ghi, phiên đánh dấu có thay
đổi; sau 0,7 giây không ghi thêm thì chụp toàn bộ dự án thành một bước. Màn
hình mới phải nhận `IFileSystem` từ phiên, không tự tạo `DiskFileSystem`,
không thì thao tác ở đó không hoàn tác được.

**Chưa có (20% còn lại):** hiệu ứng chuyển cảnh/particle, save-load trong
game, Gallery/Ending List, xuất game ra .exe/web, panel AI, đa ngôn ngữ đầy
đủ. Xem "Sau 80%" trong `docs/LO-TRINH.md`.

**Còn sót:** thư mục `apps/` và `packages/` là code TypeScript của hướng cũ
đã bỏ. Không dùng, không sửa, không tham khảo. Sẽ xoá.

---

## Nguyên tắc thiết kế sản phẩm

Năm nguyên tắc này quyết định mọi lựa chọn giao diện:

1. **Thấy trước, sửa sau.** Mọi thao tác phải có phản hồi thị giác tức thì.
   Không có nút nào bắt người dùng build rồi mới biết kết quả.
2. **Canvas là game thật.** Khung thiết kế và khung chơi dùng chung một engine.
   Cái kéo thả ra chính là cái người chơi thấy.
3. **Code là tuỳ chọn.** Mọi tính năng phải làm được bằng giao diện.
4. **AI đề xuất, người quyết.** AI không bao giờ ghi trực tiếp, luôn hiện diff.
5. **Tối giản nhưng không nghèo nàn.** Giao diện tối, tương phản cao để ảnh
   game nổi bật. Màu nhấn chỉ dùng cho hành động và trạng thái.

Bảng màu và bố cục chi tiết nằm trong `docs/design/ui-ux.html`.

---

## Lịch sử quyết định

Ghi lại để không đi lại đường cũ.

| Quyết định | Kết quả |
| --- | --- |
| Tauri + React + TypeScript | **Bỏ.** Chạy được nhưng bản chất là HTML. |
| Có dữ liệu mẫu trong app | **Bỏ.** Người dùng muốn app trống như Ren'Py. |
| Python + PySide6 | **Loại.** Game xuất ra 100-150MB, thua mục tiêu. |
| Rust + egui | **Loại.** Phải tự viết toàn bộ engine trình diễn. |
| C# + Avalonia | **Loại.** Không có sẵn node editor lẫn engine game. |
| C# + MonoGame | **Loại.** Không có sẵn cả nút bấm, quá lâu. |
| **C# + Godot 4** | **Đang dùng.** |
| Dùng xUnit | **Loại.** Harness tự viết để không phụ thuộc NuGet. |

---

## Khi không chắc

Người dùng đã nhiều lần phải sửa lại hướng đi vì quyết định được đưa ra mà
không hỏi. **Hỏi trước khi chọn kiến trúc, thư viện lớn, hoặc thay đổi cấu
trúc thư mục.** Sửa lỗi nhỏ và viết code theo hướng đã chốt thì cứ làm.
