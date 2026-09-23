# VNdev

> Công cụ tạo Visual Novel trực quan — kéo thả, nhanh, đẹp, và có AI đồng hành.

---

## 1. Bối cảnh & Định vị

### 1.1. Vấn đề

Ren'Py là engine visual novel phổ biến nhất thế giới, nhưng nó sinh ra từ năm 2004 và mang theo toàn bộ giới hạn của thời đại đó:

| Điểm đau | Thực trạng ở Ren'Py |
| --- | --- |
| Phải học ngôn ngữ riêng | Người viết truyện buộc phải học cú pháp `.rpy` — một biến thể Python với luật thụt lề. Rào cản lớn với writer không phải dev. |
| Không nhìn thấy cốt truyện | Truyện phân nhánh nằm rải rác trong file text. Không có sơ đồ nào cho thấy nhánh nào dẫn tới đâu. Dự án càng lớn càng mù. |
| Không có editor trực quan | Muốn đặt nhân vật lệch sang trái 50px phải sửa code, save, chạy lại game, nhìn, rồi sửa tiếp. Vòng lặp phản hồi cực chậm. |
| Build nặng nề | Một game rỗng xuất ra đã ~250–300MB vì nhúng cả Python runtime. Chia sẻ thử nghiệm rất phiền. |
| Giao diện mặc định cũ | Template mặc định nhìn ra Ren'Py ngay từ giây đầu. Muốn đẹp phải tự viết lại toàn bộ GUI layer. |
| Không có AI | Sinh sau thời đại LLM. Không có hỗ trợ sáng tác nào. |

### 1.2. Giải pháp — VNdev

**VNdev** là một desktop app cho phép tạo visual novel hoàn chỉnh mà **không cần viết một dòng code nào**, dựa trên ba trụ cột:

1. **Story Graph** — kéo thả node để dựng luồng truyện và nhánh rẽ, nhìn toàn cảnh cốt truyện như một bản đồ.
2. **Scene Canvas** — đặt nhân vật, background, hộp thoại trực tiếp lên khung hình game giống như thiết kế trong Figma. *What you see is literally what plays.*
3. **AI Co-writer** — AI nhúng sâu vào từng bước: viết tiếp thoại, sinh nhánh truyện, giữ nhất quán giọng nhân vật, dịch toàn bộ kịch bản.

### 1.3. Tuyên ngôn sản phẩm

> Người viết truyện nên dành 100% thời gian để kể chuyện, không phải để debug thụt lề.

---

## 2. Đối tượng người dùng

| Nhóm | Nhu cầu | VNdev phục vụ thế nào |
| --- | --- | --- |
| **Writer không biết code** | Muốn kể một câu chuyện có tương tác nhưng sợ lập trình | Toàn bộ quy trình là kéo thả. Không bao giờ bắt buộc phải mở code. |
| **Indie gamedev** | Cần prototype nhanh, lặp nhanh, build nhẹ | Preview tức thì trong editor, export web chỉ vài giây, .exe nhẹ ~10MB |
| **Nhóm nhỏ 2–5 người** | Writer + artist + dev làm chung một dự án | Project là folder + JSON → đưa lên Git, chia nhánh, merge được |
| **Người học làm game** | Muốn hiểu VN được cấu tạo thế nào | Giao diện trực quan phơi bày rõ cấu trúc: scene, node, biến, điều kiện |
| **Người dùng châu Á** | Thị trường VN lớn nhất ở Nhật/Trung, cộng đồng VN/Thái đang lên | UI đa ngôn ngữ ngay từ v1: EN, VI, JA, ZH, TH |

---

## 3. Tech Stack

### 3.1. Bảng công nghệ

| Tầng | Công nghệ | Lý do chọn |
| --- | --- | --- |
| **Shell desktop** | Tauri 2 | Dùng WebView có sẵn của OS thay vì nhúng Chromium → app ~15MB thay vì ~150MB. Quan trọng hơn: cho phép làm Player binary siêu nhẹ (xem §7). |
| **Ngôn ngữ backend** | Rust | Đi kèm Tauri. Xử lý file I/O, đóng gói export, quản lý asset, giữ API key an toàn. |
| **UI framework** | React 18 + TypeScript | Hệ sinh thái lớn nhất cho node-editor và drag-drop. TypeScript bắt buộc vì dự án lớn và data model phức tạp. |
| **Build tool** | Vite | Hot reload nhanh, tích hợp chuẩn với Tauri. |
| **Story graph** | React Flow (xyflow) | Thư viện node-based trưởng thành nhất: xử lý đồ thị hàng nghìn node, minimap, auto-layout, custom node. |
| **Game runtime & Scene canvas** | PixiJS v8 (WebGL) | Cùng một engine dùng cho cả editor canvas lẫn game thật → không lệch giữa lúc sửa và lúc chơi. WebGL cho hiệu ứng mượt 60fps. |
| **UI components** | Radix UI + Tailwind CSS | Radix cho accessibility và hành vi chuẩn; Tailwind cho tốc độ. |
| **State management** | Zustand + Immer | Nhẹ, không boilerplate. Immer cho phép làm undo/redo bằng patch history. |
| **Audio** | Howler.js | Xử lý BGM/SFX, fade, crossfade, sprite audio. Ổn định trên mọi WebView. |
| **i18n** | i18next + react-i18next | Chuẩn công nghiệp, hỗ trợ plural/context cho JA/ZH/TH. |
| **AI** | Anthropic SDK + OpenAI SDK (client-side) | Người dùng dán API key của chính họ. Không cần server, không tốn chi phí, không giữ dữ liệu người dùng. |
| **Lưu khóa bí mật** | Tauri Stronghold / OS Keychain | API key không bao giờ nằm trong file JSON hay localStorage. |
| **Testing** | Vitest + Playwright | Unit cho logic, E2E cho luồng editor. |
| **Đóng gói** | Tauri Bundler (NSIS cho Windows, DMG cho macOS) | Ra installer chuẩn, có auto-update. |

### 3.2. Vì sao Tauri chứ không phải Electron

Đây không phải lựa chọn vì "nhẹ hơn cho vui". Nó là lựa chọn kiến trúc quyết định chất lượng sản phẩm cuối:

Khi người dùng bấm **Export → Windows .exe**, VNdev cần đưa cho họ một file chạy được. Nếu dùng Electron, mỗi game xuất ra sẽ kéo theo một bản Chromium đầy đủ ≈ 120–150MB — tức là *không khá hơn Ren'Py là bao*, và toàn bộ lợi thế bán hàng biến mất.

Với Tauri, ta compile sẵn **một** binary `VNdev Player` cho mỗi nền tảng. Binary này chỉ là một vỏ WebView mỏng, nặng khoảng 6–10MB. Khi export, app chỉ copy vỏ đó ra và gắn dữ liệu game vào cạnh. Kết quả:

| | Ren'Py | VNdev |
| --- | --- | --- |
| Game rỗng (.exe) | ~250 MB | ~10 MB |
| Game có 50 ảnh + 10 nhạc | ~350 MB | ~110 MB (gần như chỉ là asset thật) |
| Thời gian build | 1–3 phút | < 10 giây |

Khoảng cách 25 lần này chính là điều khiến VNdev khác biệt về mặt cảm nhận chứ không chỉ về mặt tính năng.

---

## 4. Kiến trúc tổng thể

### 4.1. Ba khối lớn

```
┌──────────────────────────────────────────────────────────┐
│                      VNdev Editor                         │
│  (Tauri app — thứ tác giả cài và dùng để làm game)        │
│                                                            │
│   Story Graph  │  Scene Canvas  │  Asset Lib  │  AI Panel  │
└───────────────────────┬──────────────────────────────────┘
                        │ đọc / ghi
                        ▼
┌──────────────────────────────────────────────────────────┐
│                   Project Folder (.vndev)                 │
│   project.json · scenes/*.json · assets/ · i18n/          │
│              ← Nguồn sự thật duy nhất, Git-friendly →      │
└───────────────────────┬──────────────────────────────────┘
                        │ build
                        ▼
┌──────────────────────────────────────────────────────────┐
│                     VNdev Runtime                         │
│   (engine PixiJS đọc project và chơi game — dùng chung     │
│    cho Preview trong editor, Web build, và Player .exe)    │
└──────────────────────────────────────────────────────────┘
```

**Nguyên tắc cốt lõi:** Runtime chỉ có **một** bản. Nó chạy y hệt nhau ở ba nơi — trong tab Preview của editor, trong web build, và trong file .exe. Không có chuyện "chạy trong editor thì được mà export ra thì lỗi".

### 4.2. Định dạng dự án

Một dự án VNdev là một **thư mục**, không phải file nhị phân:

```
MyNovel/
├── project.json           # metadata, cấu hình, danh sách biến toàn cục
├── story/
│   ├── main.graph.json    # đồ thị cốt truyện chính
│   └── chapter2.graph.json
├── scenes/
│   ├── classroom.scene.json
│   └── rooftop.scene.json
├── characters/
│   └── yuki.character.json # nhân vật + danh sách biểu cảm
├── assets/
│   ├── backgrounds/
│   ├── sprites/
│   ├── bgm/
│   ├── sfx/
│   └── ui/
├── i18n/
│   ├── vi.json            # bản dịch kịch bản game (không phải UI editor)
│   └── en.json
└── .vndev/
    ├── cache/             # thumbnail, preview — không commit
    └── history/           # snapshot undo
```

Lợi ích: mọi thứ là JSON và ảnh/nhạc gốc. Đưa lên GitHub là diff được từng dòng thoại, merge được khi hai người sửa hai chương khác nhau. Đây là thứ Ren'Py làm được một nửa và các engine kéo-thả khác (như Visual Novel Maker) hoàn toàn không làm được vì lưu nhị phân.

---

## 5. Đặc tả tính năng chi tiết

### 5.1. Story Graph — Trình dựng cốt truyện

Không gian canvas vô hạn, kéo thả node, nối dây giữa các node để tạo luồng truyện.

**Các loại node:**

| Node | Chức năng |
| --- | --- |
| **Scene** | Một cảnh — chứa nền, nhân vật, một chuỗi lời thoại. Double-click để mở sang Scene Canvas. |
| **Choice** | Đưa ra lựa chọn cho người chơi. Mỗi lựa chọn là một output port kéo dây sang node khác. |
| **Condition** | Rẽ nhánh tự động theo biến (`if affection_yuki >= 50`). Có UI dựng điều kiện bằng dropdown, không cần gõ. |
| **Variable** | Thay đổi biến (`affection_yuki += 10`, `flag_met_teacher = true`). |
| **Jump / Label** | Nhảy tới điểm khác, dùng để tránh dây nối rối khi truyện dài. |
| **Ending** | Đánh dấu kết thúc, gắn tên ending (dùng cho màn hình Gallery/Ending list). |
| **Group** | Gom nhiều node thành một khối gập được — cần thiết khi truyện tới hàng trăm node. |
| **Comment** | Ghi chú cho chính tác giả hoặc đồng đội, không ảnh hưởng game. |

**Tính năng hỗ trợ:**
- Minimap và auto-layout (sắp xếp lại đồ thị cho gọn bằng một nút bấm)
- Tô màu nhánh theo route — nhìn ra ngay "đường Yuki" màu hồng, "đường Mei" màu xanh
- Phát hiện lỗi logic: node mồ côi không ai trỏ tới, nhánh cụt không dẫn tới ending, biến dùng mà chưa khai báo
- Tìm kiếm toàn văn trong mọi lời thoại, nhảy thẳng tới node chứa nó
- Playtest từ bất kỳ node nào — không phải chơi lại từ đầu để test một nhánh cuối game

### 5.2. Scene Canvas — Trình dựng cảnh

Khung hình đúng tỷ lệ game thật. Mọi thứ đều kéo thả trực tiếp.

- **Background**: kéo ảnh từ Asset Library thả vào canvas
- **Nhân vật**: kéo sprite vào, di chuyển tự do, có snap vào các vị trí chuẩn (trái / giữa-trái / giữa / giữa-phải / phải), chỉnh scale, lật ngang, đổi độ sáng (dùng cho nhân vật đang không nói)
- **Layer panel**: quản lý thứ tự chồng lớp như Photoshop, kéo để đổi thứ tự
- **Textbox**: chỉnh vị trí, kích thước, màu nền, độ mờ, font, cỡ chữ, màu chữ — trực tiếp trên canvas
- **Timeline lời thoại**: bên dưới canvas là dải các câu thoại của cảnh. Click vào câu nào thì canvas hiện đúng trạng thái sân khấu tại thời điểm đó
- **Ruler và guide**: căn chỉnh chính xác như phần mềm thiết kế
- **Preview trực tiếp**: bấm Play là chơi ngay trong khung, không phải build

### 5.3. Hệ thống nhân vật

Mỗi nhân vật là một thực thể có:
- Tên hiển thị (đa ngôn ngữ) và màu tên riêng trong hộp thoại
- Bộ sprite theo biểu cảm — kéo thả ảnh vào từng ô: `bình thường`, `vui`, `buồn`, `giận`, `ngượng`, `ngạc nhiên`… và tự thêm biểu cảm mới
- Hỗ trợ sprite phân lớp (thân + biểu cảm mặt riêng) để không phải vẽ lại toàn thân cho mỗi cảm xúc
- Giọng văn (voice profile) — mô tả tính cách bằng lời, dùng làm ngữ cảnh cho AI khi viết thoại cho nhân vật này
- Thư viện giọng lồng tiếng (nếu có file voice)

### 5.4. Lời thoại & Trình bày chữ

- Hiệu ứng gõ chữ (typewriter) với tốc độ tùy chỉnh, người chơi bấm để hiện hết ngay
- Rich text trong thoại: **in đậm**, *nghiêng*, đổi màu, rung chữ, chữ hiện dần từng ký tự với tốc độ khác nhau
- Ruby text — cực kỳ quan trọng cho tiếng Nhật (furigana) và tiếng Trung (pinyin). Đây là thứ Ren'Py làm rất khổ sở
- Chèn biến vào thoại: `Chào {player_name}!`
- Narration (lời dẫn không có người nói) và thoại có người nói phân biệt rõ
- Auto-mode và Skip-mode (bỏ qua đoạn đã đọc) — chuẩn mực bắt buộc của thể loại

### 5.5. Hiệu ứng & Animation

- **Chuyển cảnh**: fade, dissolve, wipe (4 hướng), pixelate, blur, cross-fade, và transition tùy chỉnh bằng ảnh mask
- **Hiệu ứng nhân vật**: trượt vào/ra từ 4 cạnh, bounce, nhảy nhẹ khi nói, lắc đầu, mờ dần
- **Hiệu ứng màn hình**: rung (mạnh/nhẹ), flash trắng/đen, tint màu (đỏ cho cảnh nguy hiểm, xanh cho hồi tưởng), làm mờ nền để làm nổi nhân vật
- **Hiệu ứng thời tiết**: mưa, tuyết, cánh hoa rơi, bụi ánh sáng — dạng particle dựng sẵn, chỉ cần bật và chỉnh mật độ
- **Ken Burns** cho background: zoom/pan chậm làm cảnh tĩnh có sinh khí
- Tất cả đều chỉnh bằng UI với thanh trượt và xem trước ngay, không cần gõ tham số

### 5.6. Âm thanh

- **BGM**: gán nhạc nền cho scene hoặc node, tự động crossfade khi đổi bài, loop liền mạch với điểm loop tùy chỉnh
- **SFX**: gắn hiệu ứng âm thanh vào bất kỳ dòng thoại hay hành động nào
- **Ambient**: âm nền môi trường (tiếng mưa, tiếng lớp học) phát song song với BGM
- **Voice**: gắn file lồng tiếng cho từng dòng thoại, tự dừng khi chuyển dòng
- Mixer với kênh riêng cho BGM/SFX/Voice/Ambient, người chơi chỉnh được trong Settings của game
- Waveform hiển thị khi chọn điểm loop

### 5.7. Biến số, Flag & Route

- Khai báo biến qua UI: tên, kiểu (số / đúng-sai / chuỗi), giá trị khởi tạo, mô tả
- Bảng theo dõi biến hiển thị realtime khi playtest — thấy ngay `affection_yuki` đang là bao nhiêu
- Dựng điều kiện bằng giao diện: chọn biến → chọn phép so sánh → nhập giá trị. Ghép nhiều điều kiện bằng AND/OR bằng cách bấm nút
- **Route Map**: một màn hình riêng tổng hợp — có bao nhiêu ending, mỗi ending cần điều kiện gì, người chơi phải chọn gì để tới đó. Sinh tự động từ đồ thị
- Persistent data: biến sống xuyên nhiều lần chơi (để làm New Game+, mở khóa nội dung ẩn)

### 5.8. Giao diện game (UI Theme)

- Bộ theme dựng sẵn, đẹp và hiện đại, khác hẳn giao diện mặc định lộ liễu của Ren'Py
- Chỉnh sửa trực quan: màu, bo góc, font, ảnh nền cho từng màn hình
- Các màn hình có sẵn: Title, Settings, Save/Load, Gallery (ảnh CG đã mở), Music Room, Ending List, History (lịch sử thoại)
- Xuất/nhập theme để chia sẻ giữa các dự án

### 5.9. Save / Load & Lịch sử

- Nhiều slot save, mỗi slot có ảnh chụp màn hình và mốc thời gian
- Quicksave / quickload bằng phím tắt
- Lịch sử thoại cuộn lại được, kèm nút phát lại giọng lồng tiếng
- Auto-save tại mỗi lựa chọn
- Save tương thích ngược khi tác giả cập nhật game (với cảnh báo nếu cấu trúc đổi quá lớn)

### 5.10. Asset Library

- Kéo file từ Explorer thả thẳng vào app
- Tự động phân loại theo thư mục, có preview thumbnail và nghe thử audio
- Gắn tag, tìm kiếm
- Cảnh báo asset không được dùng ở đâu (giúp dọn dự án trước khi release)
- Cảnh báo asset bị thiếu (file đã bị xóa nhưng còn tham chiếu)
- Tự động nén ảnh khi export (WebP) và audio (Opus) — giảm dung lượng game đáng kể mà không đụng file gốc

### 5.11. AI Co-writer *(tính năng chủ lực)*

AI không nằm ở một tab riêng biệt mà **nhúng vào đúng chỗ đang làm việc**:

| Vị trí | AI làm gì |
| --- | --- |
| **Trong node Scene** | Viết tiếp đoạn thoại theo mạch truyện; gợi ý 3 phương án cho câu tiếp theo; viết lại một câu cho tự nhiên hơn / kịch tính hơn / hài hơn |
| **Trong node Choice** | Đề xuất các lựa chọn có ý nghĩa và hệ quả khác nhau, kèm gợi ý biến nên thay đổi |
| **Trên Story Graph** | Sinh cả một nhánh truyện từ mô tả ngắn: "nhánh Yuki phát hiện bí mật, 5 cảnh, kết thúc buồn" → AI dựng ra các node và nối dây |
| **Panel nhân vật** | Sinh hồ sơ nhân vật, giữ nhất quán giọng văn — AI đọc voice profile trước khi viết thoại cho nhân vật đó |
| **Kiểm tra nhất quán** | Quét toàn bộ kịch bản tìm mâu thuẫn: nhân vật đã chết còn xuất hiện, thông tin nói trước khác nói sau, giọng văn lệch so với tính cách |
| **Dịch thuật** | Dịch toàn bộ kịch bản sang 5 ngôn ngữ, giữ nguyên rich text và biến, giữ giọng nhân vật. Có chế độ đối chiếu để tác giả duyệt từng dòng |
| **Brainstorm** | Chat thường với AI đang có sẵn toàn bộ ngữ cảnh dự án — nhân vật, cốt truyện, biến |

**Nguyên tắc thiết kế AI:**
- Người dùng dán API key của chính mình (Anthropic / OpenAI / hoặc endpoint tương thích như LM Studio chạy local). Key lưu trong keychain hệ điều hành, không bao giờ nằm trong file dự án.
- AI **luôn đề xuất, không bao giờ tự ghi đè**. Mọi kết quả hiện ở dạng diff để tác giả duyệt — chấp nhận, sửa, hoặc bỏ.
- Mọi thao tác AI đều undo được.
- Tác giả tắt hoàn toàn AI được, app vẫn hoạt động đầy đủ.

### 5.12. Đa ngôn ngữ

**Hai tầng i18n tách biệt:**

1. **Giao diện editor** — EN, VI, JA, ZH, TH. Người dùng đổi trong Settings.
2. **Nội dung game** — tác giả chọn bao nhiêu ngôn ngữ tùy ý cho game của mình; VNdev quản lý bảng dịch, AI hỗ trợ dịch, người chơi đổi ngôn ngữ ngay trong game.

Lưu ý kỹ thuật cho CJK và Thái: font fallback riêng cho từng ngôn ngữ, ngắt dòng đúng quy tắc (tiếng Thái không có dấu cách giữa từ, tiếng Nhật/Trung có luật cấm ký tự đầu/cuối dòng), hỗ trợ ruby text.

### 5.13. Preview & Playtest

- Bấm Play chơi ngay trong editor, không build
- Chơi thử từ bất kỳ node nào
- Bảng debug hiện realtime: đang ở node nào, các biến hiện tại là gì, đường đi đã qua
- Chỉnh biến giữa lúc chơi để test nhánh mà không phải chơi lại
- Đo thời gian đọc ước tính cho mỗi route

---

## 6. Export — Web build

Xuất ra một thư mục web tĩnh tự chạy:

```
build-web/
├── index.html
├── vndev-runtime.js    # engine, ~200KB gzip
├── game.data.json      # toàn bộ kịch bản
└── assets/             # đã nén WebP + Opus
```

Upload lên itch.io, Vercel, GitHub Pages, hoặc bất kỳ static host nào là chơi được ngay trên mọi thiết bị kể cả điện thoại. Có tùy chọn xuất kèm PWA manifest để chơi offline sau lần tải đầu.

---

## 7. Export — Desktop .exe / .app

### 7.1. Cơ chế

Đây là phần kiến trúc đặc biệt nhất của VNdev.

Ta **compile trước** một chương trình tên `VNdev Player` bằng Tauri, cho từng nền tảng (Windows x64, macOS Intel, macOS ARM, Linux x64). Chương trình này không chứa game nào cả — nó chỉ biết một việc: mở thư mục dữ liệu nằm cạnh nó và chạy runtime.

Khi tác giả bấm Export → Windows:

1. Editor copy `VNdevPlayer-win-x64.exe` ra thư mục đích, đổi tên thành tên game
2. Ghi dữ liệu game + asset đã nén vào thư mục `data/` cạnh nó
3. Thay icon và metadata của file exe bằng icon game
4. (Tùy chọn) Đóng gói dữ liệu vào một file `.vnpack` duy nhất để người chơi không sửa lung tung được

Toàn bộ mất dưới 10 giây vì **không có bước compile nào cả** — chỉ là copy file.

### 7.2. Kết quả

```
MyNovel-Windows/
├── MyNovel.exe        ~8 MB   ← vỏ Player, dùng WebView của Windows
├── data/
│   ├── game.vnpack    ~2 MB   ← toàn bộ kịch bản
│   └── assets/        tùy      ← ảnh và nhạc thật
└── README.txt
```

Player binary được tải về lần đầu khi người dùng export cho một nền tảng (không nhồi sẵn vào installer để không làm app phình to), sau đó cache lại.

### 7.3. Các tùy chọn export khác

- **Android APK** — đóng gói web build bằng Capacitor (giai đoạn sau)
- **Steam-ready** — cấu trúc thư mục và hook Steamworks cho achievement (giai đoạn sau)

---

## 8. Thứ tự triển khai

Không cắt tính năng — đây là thứ tự lắp ráp. Mỗi giai đoạn đều để lại thứ chạy được.

| GĐ | Nội dung | Kết quả kiểm chứng được |
| --- | --- | --- |
| **0. Nền móng** | Khung Tauri + React + Vite, thiết kế data model và JSON schema đầy đủ, hệ thống đọc/ghi project folder, undo/redo, i18n editor 5 ngôn ngữ | Mở/tạo/lưu dự án được, đổi ngôn ngữ UI được |
| **1. Runtime** | Engine PixiJS: hiển thị background, sprite, textbox, gõ chữ, chuyển cảnh, audio, biến, điều kiện, save/load | Chạy được một game viết tay bằng JSON thuần |
| **2. Scene Canvas** | Canvas kéo thả, layer, timeline thoại, preview trực tiếp | Dựng được một cảnh hoàn chỉnh không cần gõ JSON |
| **3. Story Graph** | React Flow, đủ loại node, nối dây, validate, auto-layout, playtest từ node bất kỳ | Dựng được truyện phân nhánh có nhiều ending |
| **4. Asset & Nhân vật** | Asset library, hệ thống nhân vật và biểu cảm, audio manager | Quản lý được dự án cỡ thật |
| **5. Trau chuốt** | Toàn bộ hiệu ứng, particle, UI theme editor, các màn hình game | Game nhìn đẹp, khác biệt rõ với Ren'Py |
| **6. AI** | Tích hợp provider, các điểm nhúng AI, diff review, dịch thuật, kiểm tra nhất quán | AI hỗ trợ được toàn bộ vòng đời sáng tác |
| **7. Export** | Web build, Player binary cho 4 nền tảng, pipeline nén asset | Xuất được game chơi được ở cả web lẫn .exe |
| **8. Phát hành** | Installer, auto-update, onboarding, dự án mẫu, tài liệu | Người lạ cài về và làm được game đầu tiên trong 30 phút |

---

## 9. Tiêu chí thành công

Sản phẩm được coi là đạt khi:

1. Một người **chưa từng lập trình** tạo và xuất bản được một visual novel có phân nhánh trong vòng **một buổi chiều**.
2. Game rỗng xuất ra **.exe nhỏ hơn 15MB**.
3. Từ lúc sửa một câu thoại tới lúc thấy nó chạy: **dưới 1 giây** (không có bước build).
4. Dự án mở được bằng Git, hai người sửa hai chương khác nhau **merge không xung đột**.
5. Một game làm bằng VNdev đặt cạnh một game Ren'Py, người xem **nhận ra ngay cái nào đẹp hơn**.

---

## 10. Cấu trúc thư mục mã nguồn

```
D:\VNdev\
├── apps/
│   ├── editor/              # Tauri app — trình soạn thảo
│   │   ├── src/             # React UI
│   │   │   ├── features/
│   │   │   │   ├── story-graph/
│   │   │   │   ├── scene-canvas/
│   │   │   │   ├── asset-library/
│   │   │   │   ├── characters/
│   │   │   │   ├── ai/
│   │   │   │   └── export/
│   │   │   ├── components/  # UI dùng chung
│   │   │   ├── stores/      # Zustand
│   │   │   └── i18n/        # en, vi, ja, zh, th
│   │   └── src-tauri/       # Rust: file I/O, export, keychain
│   └── player/              # Tauri app — vỏ Player cho game xuất ra
├── packages/
│   ├── core/                # Data model + JSON schema + validate (dùng chung)
│   ├── runtime/             # Engine PixiJS (dùng chung editor/web/player)
│   └── ui/                  # Design system
├── examples/                # Dự án mẫu
└── docs/
```

Monorepo vì `core` và `runtime` phải được dùng chung y hệt giữa editor, web build và player — đây chính là điều bảo đảm "chạy trong editor sao thì export ra vậy".

---

*Tài liệu này là bản chốt Bước 2 (MD) trong quy trình Spec → MD → Design → Code → Deploy.*
