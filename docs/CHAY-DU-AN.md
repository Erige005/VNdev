# Chạy và dùng VNdev

VNdev là app desktop viết bằng C# trên Godot 4.3 .NET. App khởi động ở màn
hình trống, không có dự án mẫu nào. Bạn tự tạo dự án của mình.

## Cài đặt lần đầu

1. **.NET 8 SDK**: tải tại <https://dotnet.microsoft.com/download/dotnet/8.0>.
2. **Godot 4.3 .NET**: tải tại
   <https://godotengine.org/download/archive/4.3-stable/>. Phải là bản
   **.NET** (mono), bản thường không chạy được code C#. Giải nén vào
   `%LOCALAPPDATA%\Programs\Godot\` để dùng được `chay-app.bat`.

## Chạy app

Double-click `chay-app.bat` ở gốc kho mã.

Hoặc chạy bằng lệnh:

```powershell
& "$env:LOCALAPPDATA\Programs\Godot\Godot_v4.3-stable_mono_win64.exe" --path app
```

Lệnh trên mở thẳng app. Thêm `-e` sẽ mở **Godot Editor** thay vì app. Đó là
công cụ để sửa code; muốn chạy app từ trong đó thì bấm ▶ hoặc F5.

Sau khi sửa code C# trong `app/` hay `src/`, chỉ cần chạy lại app, Godot tự
build lại. Muốn build mà không mở Godot:

```powershell
dotnet build app\VNdev.App.csproj
```

## Test

```powershell
dotnet run --project tests\VNdev.Core.Tests
```

Chỉ cần .NET SDK, không cần Godot. Chạy trong vài giây.

---

# Dùng app

## 1. Tạo dự án

Màn hình chào: nhập tên dự án → **Tạo dự án mới…** → chọn một **thư mục
trống**. Bên phải màn hình chào là danh sách **Mở gần đây**: bấm vào một dự án
để mở lại, bấm ✕ để bỏ khỏi danh sách (thư mục không bị xoá). App dựng sẵn
cấu trúc này:

```
project.json          cấu hình dự án, danh sách biến
story/                đồ thị cốt truyện, mỗi chương một file
scenes/               nội dung từng cảnh
characters/           hồ sơ nhân vật
assets/
  backgrounds/        ← chép ảnh nền vào đây
  sprites/            ← chép sprite nhân vật vào đây
  bgm/                ← chép nhạc nền vào đây
  sfx/                ← chép hiệu ứng âm thanh vào đây
  voice/
  ui/
i18n/
```

Đừng tạo dự án bên trong thư mục `app/` của kho mã. Nên để ở chỗ riêng, ví
dụ `D:\VNdev\projects\ten-du-an` (thư mục `projects/` đã được Git bỏ qua).

## 2. Bỏ asset vào

Dùng Explorer chép ảnh và nhạc vào các thư mục `assets/` ở trên. Sang tab
**Asset**, bấm **Quét lại**. App không sao chép, không đổi tên file; thứ nằm
trên đĩa chính là thứ game dùng.

- Ảnh: `png`, `jpg`, `webp`. Sprite nên có nền trong suốt.
- Nhạc: `ogg`, `mp3`. File `wav` hiện được liệt kê nhưng không phát.
- File `.psd` không đọc được. Mở bằng GIMP/Photoshop rồi xuất ra PNG, mỗi
  biểu cảm một file.

## 3. Nhân vật và biến

Tab **Nhân vật** → **+ Thêm nhân vật**. Đặt tên, màu tên trong hộp thoại,
thêm biểu cảm (`normal`, `sad`...) và chọn sprite cho từng biểu cảm từ danh
sách file đã quét.

Người chơi không cần sprite: nhân vật chỉ nói, không đứng trên sân khấu thì
cứ để trống biểu cảm.

Cùng tab đó có **+ Khai báo biến**: số, đúng/sai, hoặc chữ. Phải khai báo
trước khi dùng trong lựa chọn hay điều kiện.

## 4. Dựng cốt truyện

Tab **Cốt truyện**. Thanh trên cùng có các nút thêm node:

| Node | Dùng để |
| --- | --- |
| **Cảnh** | Một đoạn truyện có nền, nhân vật và lời thoại |
| **Lựa chọn** | Người chơi chọn một hướng, mỗi phương án có thể đổi biến |
| **Điều kiện** | Tự rẽ nhánh theo giá trị biến (hiện chỉ so sánh đơn, chưa ghép và/hoặc) |
| **Biến** | Thay đổi biến rồi đi tiếp |
| **Nhảy** | Nhảy tới node khác, tránh dây nối rối |
| **Kết thúc** | Điểm kết của một tuyến truyện |
| **Ghi chú** | Ghi chú cho bạn, không ảnh hưởng game |

Kéo node để di chuyển. Kéo từ cổng bên phải của node này sang node khác để
nối dây. Chọn node rồi nhấn **Delete** để xoá. Chọn một node thì panel bên
phải hiện thuộc tính của nó.

**Điểm bắt đầu.** Chương mới mặc định bắt đầu ở node Kết thúc, nên Chơi thử
sẽ nhảy thẳng tới màn kết nếu bạn không đổi. Chọn node muốn mở đầu (thường là
node Cảnh đầu tiên) rồi bấm **▶ Đặt làm điểm bắt đầu** ở panel phải. Node bắt
đầu có dấu ▶ trên tiêu đề. Node Ghi chú không đặt làm điểm bắt đầu được.

Panel **Lỗi** bên trái liệt kê vấn đề của đồ thị (node mồ côi, nhánh cụt,
cổng chưa nối, biến chưa khai báo, thiếu cảnh...). Bấm vào một lỗi là chọn
đúng node đó trên canvas.

## 5. Viết lời thoại

Chọn node Cảnh → bấm **→ Mở trong Scene Canvas** ở panel phải. Hoặc sang
tab **Cảnh** và chọn cảnh trong danh sách bên trái.

Thanh trên cùng chọn ảnh nền. Bên dưới là danh sách lời thoại với các nút
**+ Thêm dòng**, **Xoá dòng**, **↑ ↓**. Chọn một dòng để đặt người nói (hoặc
"Dẫn truyện"), biểu cảm và nội dung. Khung hình phía trên hiện đúng sân khấu
tại dòng đang chọn.

## 6. Chơi thử

Nút **▶ Chơi thử** góc trên phải, hoặc **F5**. Bấm chuột, Space hoặc Enter để sang dòng.
Bấm **✕ Đóng** để thoát.

## 7. Lưu và hoàn tác

Không cần bấm lưu: mọi thay đổi được ghi xuống file JSON ngay khi bạn sửa.
Thanh trạng thái dưới cùng hiện giờ lưu gần nhất. Toàn bộ dự án là file đọc
được bằng mắt, đưa lên Git được.

**Ctrl+Z / Ctrl+Y** (hoặc nút ↶ ↷ trên thanh công cụ) hoàn tác và làm lại mọi
thao tác ở cả bốn tab: thêm, xoá, nối node, sửa lời thoại, tạo nhân vật, xoá
chương… Gõ liền một mạch được tính là một bước. Khi con trỏ đang ở trong ô
chữ, Ctrl+Z chỉ hoàn tác chữ trong ô đó. Lịch sử mất khi đóng dự án.

## 8. Menu, phím tắt, cài đặt

Thanh menu góc trên trái:

| Menu | Có gì |
| --- | --- |
| **Tệp** | Dự án mới, Mở dự án, Mở gần đây, Mở thư mục dự án, Đóng dự án, Cài đặt, Thoát |
| **Sửa** | Hoàn tác, Làm lại, Sao chép / Dán / Nhân bản / Xoá node, Chọn tất cả, Thêm node |
| **Xem** | Chuyển tab, Phóng to / Thu nhỏ / Vừa khung đồ thị, bật tắt lưới và minimap, Toàn màn hình |
| **Chạy** | Chơi thử |
| **Trợ giúp** | Danh sách phím tắt, Hướng dẫn, Báo lỗi, Giới thiệu |

Phím tắt mặc định hay dùng: **Ctrl+N** dự án mới, **Ctrl+O** mở, **Ctrl+,**
cài đặt, **Ctrl+1…4** chuyển tab, **Ctrl+C / Ctrl+V / Ctrl+D** sao chép, dán,
nhân bản node, **Delete** xoá, **Ctrl+0** vừa khung, **F11** toàn màn hình,
**F1** xem hết phím tắt.

Chương: nhấp đúp để đổi tên, chuột phải để chuyển lên xuống hoặc xoá.
Chương đầu tiên là nơi Chơi thử bắt đầu.

**Cài đặt** (Tệp → Cài đặt, hoặc ⚙ ở màn hình chào) áp dụng ngay, không cần
bấm lưu:

- **Chung**: tự mở lại dự án gần nhất khi khởi động, hỏi trước khi xoá node,
  xoá danh sách dự án gần đây.
- **Giao diện**: tỉ lệ giao diện 75–200%, cỡ chữ, màu nhấn, lưới, bắt dính
  lưới, minimap.
- **Phím tắt**: bấm vào ô phím rồi nhấn tổ hợp mới. Esc huỷ, Backspace bỏ gán.
  Trùng với phím của việc khác thì việc kia bị gỡ phím và app báo cho biết.
- **AI Providers**, **Xuất bản**: chưa có, sẽ làm theo `docs/LO-TRINH.md`.

Cài đặt và danh sách dự án gần đây lưu ở `%APPDATA%\VNdev\settings.json`,
không nằm trong thư mục dự án, nên không lên Git. App cũng nhớ kích thước và
vị trí cửa sổ.

---

## Hạn chế hiện tại và cách xử lý tạm

Hai việc dưới đây giao diện chưa làm được. Trong lúc chờ, sửa trực tiếp file
JSON của dự án bằng trình soạn thảo bất kỳ (Notepad, VS Code). Sửa xong thì
mở lại dự án trong app.

**Đặt nhân vật lên sân khấu.** Mở `scenes/<cảnh>.scene.json`, thêm vào
`"stage"` những nhân vật có mặt từ đầu cảnh. `anchor` là một trong
`farLeft`, `left`, `center`, `right`, `farRight`:

```json
"stage": [
  { "character": "chitoge", "expression": "normal", "anchor": "center" }
],
```

Sau đó, chọn biểu cảm ở từng dòng thoại trong app là nhân vật đổi mặt theo.

**Nhạc nền cho cảnh.** Trong cùng file cảnh, thêm:

```json
"bgm": { "src": "assets/bgm/ten-bai.mp3", "volume": 0.6, "loop": true },
```

## Chưa có

- Hiệu ứng chuyển cảnh, animation nhân vật, particle
- Save/Load trong game, Gallery, Ending List
- Xuất game ra `.exe` và web
- Panel AI
- Đa ngôn ngữ đầy đủ
