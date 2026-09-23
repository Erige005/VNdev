# Chạy VNdev

VNdev là **desktop app**. App khởi động ở màn hình trống — không có dự án mẫu nào. Bạn tự tạo dự án của mình.

| Lệnh | Kết quả |
| --- | --- |
| `pnpm dev` | Mở cửa sổ app thật, đọc ghi file trên đĩa được |
| `pnpm dev:web` | Mở trong trình duyệt để xem giao diện, **không** tạo hay mở dự án được |

## Cài đặt lần đầu

**1. Node.js 20+ và pnpm**

```powershell
corepack enable
corepack prepare pnpm@9 --activate
```

**2. Rust** — tải tại <https://rustup.rs>

**3. Visual Studio Build Tools** — tải tại <https://visualstudio.microsoft.com/visual-cpp-build-tools/>, chọn workload **"Desktop development with C++"**. Nặng 2–4 GB, chỉ cài một lần.

**4. Cài dependency**

```powershell
cd D:\VNdev
pnpm install
```

## Chạy

```powershell
pnpm dev
```

Lần đầu Rust biên dịch mất 3–8 phút, sau đó vài giây.

## Đóng gói ra .exe

```powershell
pnpm build
```

File cài đặt nằm ở `apps\editor\src-tauri\target\release\bundle\nsis\`.

---

# Dùng app

## 1. Tạo dự án

Mở app → **Tạo dự án mới** → nhập tên, tác giả, chọn ngôn ngữ → chọn một **thư mục trống**.

App dựng sẵn cấu trúc này trong thư mục bạn chọn:

```
project.json          cấu hình dự án
story/                các chương, mỗi chương một file
scenes/               nội dung từng cảnh
characters/           hồ sơ nhân vật
assets/
  backgrounds/        ← chép ảnh nền của bạn vào đây
  sprites/            ← chép sprite nhân vật vào đây
  bgm/                ← chép nhạc nền vào đây
  sfx/                ← chép hiệu ứng âm thanh vào đây
  voice/              ← chép file lồng tiếng vào đây
i18n/
```

## 2. Bỏ asset của bạn vào

Dùng Explorer chép file ảnh và nhạc của bạn vào các thư mục `assets/` ở trên. Sang tab **Asset** trong app, bấm **Quét lại thư mục** — app đọc lên những gì có ở đó.

App không sao chép, không đổi tên, không tạo thư viện riêng. Thứ nằm trên đĩa chính là thứ game dùng.

Định dạng nhận được: ảnh `png jpg webp gif avif`, âm thanh `ogg mp3 wav m4a opus flac`.

## 3. Tạo nhân vật

Tab **Nhân vật** → gõ tên → **＋**. Với mỗi nhân vật, đặt màu tên hiện trong hộp thoại, thêm các biểu cảm (vui, buồn, giận…) và gán file sprite cho từng biểu cảm.

Ô **Hồ sơ giọng** là nơi mô tả cách nhân vật nói. Phần này sẽ là ngữ cảnh cho AI ở giai đoạn sau.

## 4. Khai báo biến

Panel **Biến** bên trái. Biến dùng để nhớ lựa chọn của người chơi: điểm thiện cảm (kiểu số), cờ đã gặp ai (kiểu đúng/sai), tên người chơi (kiểu chữ).

Phải khai báo biến trước khi dùng — app sẽ báo lỗi nếu bạn tham chiếu tới biến chưa có.

## 5. Dựng cốt truyện

Tab **Cốt truyện**. Thanh trên cùng có các nút thêm node:

| Node | Dùng để |
| --- | --- |
| **Cảnh** | Một đoạn truyện có nền, nhân vật và lời thoại. Nhấp đúp để mở trình soạn cảnh |
| **Lựa chọn** | Người chơi chọn một trong nhiều hướng, mỗi phương án có thể đổi biến |
| **Điều kiện** | Tự rẽ nhánh theo giá trị biến |
| **Biến** | Thay đổi biến rồi đi tiếp |
| **Kết thúc** | Điểm kết của một tuyến truyện |
| **Nhảy** | Nhảy tới node khác, tránh dây nối rối |
| **Ghi chú** | Ghi chú cho chính bạn, không ảnh hưởng game |

Thao tác: kéo node để di chuyển, kéo từ **chấm tròn bên phải** sang node khác để nối dây, chọn rồi nhấn **Delete** để xoá. Node nào cũng đặt làm điểm bắt đầu chương được, qua nút trong panel phải.

## 6. Viết lời thoại

Nhấp đúp node Cảnh → tab **Cảnh** mở ra.

Chọn ảnh nền và nhạc nền ở thanh trên. Bật nhân vật có mặt trong cảnh ở thanh giữa, chọn biểu cảm và vị trí đứng cho từng người. Bên dưới là danh sách lời thoại: chọn người nói (hoặc để "Dẫn truyện"), chọn biểu cảm, gõ nội dung. Các nút bên phải mỗi dòng để di chuyển lên xuống, chèn dòng mới, hoặc xoá.

Khung trên cùng hiện đúng sân khấu tại dòng bạn đang chọn.

## 7. Chơi thử

Nút **▶ Chơi thử** góc trên phải. Thanh phía trên hiện giá trị các biến theo thời gian thực, nên bạn thấy ngay lựa chọn nào tác động tới biến nào.

Bấm vào khung hoặc nhấn Space để sang dòng. Esc để thoát.

## 8. Lưu

`Ctrl+S`, hoặc app tự lưu sau 2 giây bạn ngừng thao tác. Toàn bộ là file JSON đọc được bằng mắt — đưa lên Git, diff từng dòng thoại, làm việc nhóm đều được.

---

## Phần kiểm tra lỗi

Panel dưới bên trái quét liên tục và báo bốn loại vấn đề: node không ai trỏ tới, nhánh không dẫn tới kết thúc nào, cổng ra chưa nối dây, và biến dùng mà chưa khai báo. Bấm vào cảnh báo là nhảy thẳng tới node có vấn đề.

## Chưa có

- Engine PixiJS thật — chế độ Chơi thử hiện vẽ bằng DOM
- Hiệu ứng chuyển cảnh và animation nhân vật
- Panel AI
- Export game ra web và .exe
- Save/Load trong game, Gallery, Ending List

## Test

```powershell
pnpm test           # test TypeScript
pnpm typecheck

cd apps\editor\src-tauri
cargo test          # test phía Rust
```
