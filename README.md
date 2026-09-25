# VNdev

Công cụ tạo visual novel trên desktop, hướng tới làm bản thay thế hiện đại cho
Ren'Py: kéo thả, nhìn thấy ngay kết quả, không bắt người viết truyện phải
học lập trình.

Viết bằng **C#** trên **Godot 4 (.NET)**. Toàn bộ giao diện do engine tự vẽ
bằng GPU, không có HTML hay webview ở bất kỳ đâu.

> Dự án đang phát triển, đã xong khoảng 80% chức năng chính. Bạn đã tạo được
> một visual novel có nhánh, có nhân vật, ảnh và nhạc, rồi chơi thử từ đầu
> đến cuối ngay trong app. Chưa xuất game ra `.exe` được.

---

## Vì sao có VNdev

| Ren'Py | VNdev |
| --- | --- |
| Viết kịch bản bằng ngôn ngữ `.rpy`, phải học cú pháp và luật thụt lề | Kéo thả node, không bắt buộc viết code |
| Truyện phân nhánh nằm rải rác trong file text, không có sơ đồ | Story Graph cho thấy toàn cảnh nhánh nào dẫn tới đâu |
| Muốn dời nhân vật phải sửa code, chạy lại game rồi mới thấy | Trình soạn cảnh hiện đúng sân khấu tại từng dòng thoại |
| Không có công cụ AI hỗ trợ sáng tác | Có panel AI trong lộ trình, AI chỉ đề xuất, người viết quyết định |

---

## Trạng thái

**Đã có:**

- Tạo, mở, lưu dự án trên đĩa
- Story Graph: 7 loại node (Cảnh, Lựa chọn, Điều kiện, Biến, Nhảy, Kết
  thúc, Ghi chú), nối dây kéo thả
- Bộ kiểm tra đồ thị bắt 11 loại lỗi: node mồ côi, nhánh cụt, cổng chưa nối,
  biến chưa khai báo, thiếu cảnh / nhân vật / biểu cảm...
- Quản lý nhân vật, biểu cảm và biến (số, đúng/sai, chữ)
- Thư viện asset đọc thẳng từ thư mục `assets/` của dự án
- Trình soạn cảnh có xem trước sân khấu tại từng dòng thoại
- Chơi thử trong app, có ảnh, nhạc, biến và rẽ nhánh theo lựa chọn

**Chưa có:** hiệu ứng chuyển cảnh và particle, save/load trong game, Gallery
và Ending List, xuất game ra `.exe`/web, panel AI, đa ngôn ngữ đầy đủ.

Chi tiết từng chặng: [`docs/LO-TRINH.md`](docs/LO-TRINH.md).

---

## Cài đặt và chạy

### Cần có

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Godot 4.3 .NET](https://godotengine.org/download/archive/4.3-stable/),
  đúng bản **.NET** (mono), không phải bản thường

### Chạy app

Trên Windows, nếu Godot giải nén vào `%LOCALAPPDATA%\Programs\Godot\`,
double-click **`chay-app.bat`** là xong.

Nếu Godot nằm chỗ khác, chạy trực tiếp:

```powershell
<đường-dẫn-tới>\Godot_v4.3-stable_mono_win64.exe --path app
```

Lệnh này mở thẳng app. Muốn mở Godot Editor để sửa code thì thêm `-e`.

### Build và test

```powershell
dotnet build app\VNdev.App.csproj                # build app, không cần mở Godot
dotnet run --project tests\VNdev.Core.Tests      # chạy bộ test của lõi
```

---

## Dùng thử trong 5 phút

1. Mở app, chọn **Tạo dự án mới**, chọn một thư mục trống.
2. Chép ảnh nền vào `assets/backgrounds/`, sprite nhân vật vào
   `assets/sprites/`, nhạc vào `assets/bgm/`. Sang tab **Asset**, bấm
   **Quét lại**.
3. Tab **Nhân vật**: tạo nhân vật, gán sprite cho từng biểu cảm.
4. Tab **Cốt truyện**: thêm node Cảnh, chọn node rồi bấm **→ Mở trong Scene
   Canvas** ở panel bên phải để soạn lời thoại. Nối dây tới node Kết thúc.
5. Chọn node Cảnh đầu tiên, bấm **▶ Đặt làm điểm bắt đầu** ở panel bên phải.
   Chương mới mặc định bắt đầu ở node Kết thúc, bỏ bước này thì Chơi thử sẽ
   nhảy thẳng tới màn kết.
6. Bấm **▶ Chơi thử**.

Định dạng dùng được: ảnh `png jpg webp`; nhạc hiện chỉ phát `ogg` và `mp3`.

> **Chưa làm được bằng giao diện:** đặt nhân vật lên sân khấu, chọn nhạc nền
> cho cảnh. Trong lúc
> chờ, sửa tay file JSON theo hướng dẫn trong
> [`docs/CHAY-DU-AN.md`](docs/CHAY-DU-AN.md#hạn-chế-hiện-tại-và-cách-xử-lý-tạm).

---

## Dự án là một thư mục JSON

Mỗi cảnh, mỗi chương, mỗi nhân vật nằm trong một file riêng, không có file
nhị phân. Nhờ vậy đưa dự án lên Git thì diff được từng dòng thoại, và hai
người sửa hai cảnh khác nhau sẽ merge không xung đột.

```
MyNovel/
├── project.json                  cấu hình, danh sách biến
├── story/*.graph.json            đồ thị cốt truyện, mỗi chương một file
├── scenes/*.scene.json           nền, nhạc, sân khấu, lời thoại của từng cảnh
├── characters/*.character.json   nhân vật và biểu cảm
├── assets/
│   ├── backgrounds/  sprites/  bgm/  sfx/  voice/  ui/
└── i18n/
```

---

## Kiến trúc

```
src/VNdev.Core       .NET thuần: data model, đọc ghi dự án, bộ kiểm tra đồ thị
        ▲
        │ tham chiếu một chiều
app/                 Godot 4.3 .NET: editor + bộ chạy truyện
tests/               test cho lõi, chạy không cần Godot
```

- **`VNdev.Core` không phụ thuộc Godot.** Lõi nhận một `IFileSystem` do bên
  gọi cấp, không tự đọc đĩa. Cùng bộ code chạy được trong editor, trong game
  xuất ra sau này, và trong test.
- **Chỉ có một runtime.** Khung soạn cảnh và màn chơi thử dùng chung
  `StageRenderer`, nên thấy gì khi soạn thì người chơi thấy đúng như vậy.
- **Bộ test tự viết**, không dùng xUnit, để không phụ thuộc NuGet.

### Vì sao chọn Godot

Godot tự vẽ từng pixel như Ren'Py, cho sẵn `GraphEdit` để làm editor node,
cho sẵn animation, shader, particle, audio, và xử lý nhập liệu tiếng
Nhật/Trung/Thái tốt. Quan trọng nhất: editor và game xuất ra dùng chung một
engine.

Các hướng đã cân nhắc và loại bỏ (Tauri + React, Python + Qt, Rust + egui,
Avalonia, MonoGame) kèm lý do nằm trong [`CLAUDE.md`](CLAUDE.md).

---

## Tài liệu

| File | Nội dung |
| --- | --- |
| [`docs/CHAY-DU-AN.md`](docs/CHAY-DU-AN.md) | Hướng dẫn cài, chạy và dùng app từng bước |
| [`docs/SPEC.md`](docs/SPEC.md) | Đặc tả tính năng đầy đủ. Phần công nghệ trong đó thuộc hướng Tauri cũ đã bỏ |
| [`docs/LO-TRINH.md`](docs/LO-TRINH.md) | Lộ trình và trạng thái từng chặng |
| [`docs/design/ui-ux.html`](docs/design/ui-ux.html) | Bản thiết kế giao diện, mở bằng trình duyệt |
| [`CLAUDE.md`](CLAUDE.md) | Ràng buộc kiến trúc, quy ước code, lịch sử quyết định |
