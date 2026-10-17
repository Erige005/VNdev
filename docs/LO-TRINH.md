# Lộ trình tới bản dùng được

Mục tiêu: **chạy được 80% chức năng chính**, nghĩa là tạo một visual novel
hoàn chỉnh từ con số không và chơi thử nó, ngay trong app.

Mỗi chặng dưới đây đều để lại **thứ mở lên chạy được và nhìn thấy được**, chứ
không phải code nằm đó chờ chặng sau. Đó là cách duy nhất để phát hiện sớm khi
hướng đi sai.

---

## Định nghĩa "80%"

| # | Chức năng | Thuộc 80%? |
| --- | --- | --- |
| 1 | Tạo / mở / lưu dự án trên đĩa | ✅ |
| 2 | Dựng cốt truyện bằng node kéo-thả | ✅ |
| 3 | Soạn cảnh: lời thoại, người nói, biểu cảm, nền, nhạc | ✅ |
| 4 | Quản lý nhân vật và biểu cảm | ✅ |
| 5 | Thư viện asset đọc từ thư mục dự án | ✅ |
| 6 | Biến, điều kiện, rẽ nhánh | ✅ |
| 7 | Chơi thử ngay trong app, có ảnh và âm thanh thật | ✅ |
| 8 | Hiệu ứng chuyển cảnh, particle, rung màn hình | ✅ |
| 9 | Save/Load trong game, Gallery, Ending List | 🟡 Save/Load xong |
| 10 | Xuất game ra .exe và web | ❌ |
| 11 | Trợ lý AI | ✅ |
| 12 | Đa ngôn ngữ đầy đủ, ruby text | ❌ |

Làm xong mục 1–7 là bạn tự làm được một visual novel có nhánh, có nhân vật,
có ảnh, có nhạc, chơi thử được từ đầu đến cuối.

---

## Trạng thái: đã xong

**Nền dữ liệu** — `src/VNdev.Core`, 31/31 test pass.

Data model đầy đủ, đọc ghi thư mục dự án dạng JSON, bộ kiểm tra đồ thị bắt 11
loại lỗi, hàm tính trạng thái sân khấu tại từng dòng thoại.

Chạy kiểm chứng: `dotnet run --project tests\VNdev.Core.Tests`

**Chặng 1 — Khung Godot và màn hình chào.** Dự án Godot 4.3 .NET nằm ở
`app/`, tham chiếu `VNdev.Core` qua `ProjectReference` (chiều tham chiếu chỉ
đi một hướng, đúng ràng buộc số 3). `Main.cs` là nút gốc, dựng UI hoàn toàn
bằng code (không `.tscn` vẽ tay) để dễ sửa lại và không cần mở editor GUI mới
build được — `dotnet build app\VNdev.App.csproj` là đủ. Màn hình chào
(`WelcomeScreen.cs`) có Tạo dự án mới / Mở dự án, dùng `FileDialog` để chọn
thư mục, gọi thẳng `ProjectIo.Scaffold` / `ProjectIo.Load` của lõi. Theme màu
tối lấy đúng bảng trong `docs/design/ui-ux.html`, dựng ở `AppTheme.cs`.

**Chặng 2 — Editor cốt truyện.** `EditorScreen.cs` dựng màn hình ba cột:
cây chương + bảng lỗi bên trái, `GraphEdit` ở giữa, thuộc tính node bên phải.
`GraphSync.cs` là lớp dịch giữa `StoryGraph` và `GraphEdit` — quy ước chốt:
`GraphNode.Name` luôn bằng `StoryNode.Id`, chỉ còn phải dịch port index của
Godot sang "handle" (id lựa chọn / "true"/"false" / cổng tiếp theo duy nhất).
`NodeInspector.cs` vẽ panel phải theo từng loại node. Bảng lỗi nối trực tiếp
vào `GraphValidator.Validate`, bấm vào lỗi là chọn đúng node trên canvas.

Đã kiểm chứng bằng cách chạy Godot ở chế độ `--headless`, dựng dự án giả có
đủ 7 loại node, mô phỏng nối dây / kéo node / xoá node qua tín hiệu của
`GraphEdit`, rồi đọc lại file JSON trên đĩa — dữ liệu khớp đúng những gì vừa
làm, không có lỗi runtime.

**Môi trường cần có:** .NET 8 SDK và Godot 4.3 .NET (bản mono). Máy dựng ban
đầu chưa có sẵn — đã cài .NET SDK vào `%LOCALAPPDATA%\Microsoft\dotnet` và
Godot vào `%LOCALAPPDATA%\Programs\Godot`. Mở project bằng
`Godot_v4.3-stable_mono_win64.exe --path app`.

---

## Đã đạt mốc 80% — mục 1–7 xong

**Chặng 3 — Nhân vật và biến.** `CharacterScreen.cs`: quản lý nhân vật (tên,
màu tên, danh sách biểu cảm gắn sprite quét từ Chặng 4, hồ sơ giọng), panel
biến ba kiểu số/đúng-sai/chữ ngay cạnh. `NodeInspector.PopulateEffects` dùng
chung cho cả tác động của lựa chọn (`ChoiceOption.Effects`) lẫn node Biến —
toàn bộ dựng bằng dropdown, không gõ tay tên biến.

**Chặng 4 — Thư viện asset.** `AssetScreen.cs` quét bốn thư mục
`assets/{backgrounds,sprites,bgm,sfx}` qua `AssetIndex.cs`, hiện ảnh thật
bằng `Godot.Image.Load` (không cần import trước), nút Quét lại đọc lại đĩa.
Gán sprite cho biểu cảm là chọn từ dropdown liệt kê file đã quét được.

**Chặng 5 — Trình soạn cảnh.** `SceneScreen.cs`: khung 16:9
(`AspectRatioContainer`), danh sách lời thoại thêm/xoá/đổi thứ tự, chọn dòng
nào thì gọi `SceneData.StageAt(index)` của lõi để vẽ đúng trạng thái sân khấu
tại dòng đó. `StageRenderer.cs` tách riêng phần vẽ nền+nhân vật để Chặng 6
dùng lại y hệt — "canvas là game thật" nghĩa đen: cùng một hàm vẽ.
Sân khấu soạn bằng giao diện: ai đứng sẵn đầu cảnh, ai bước ra / đổi biểu
cảm / dời chỗ / rời sân khấu tại từng dòng (rời = `exit`, `StageAt` bỏ nhân
vật đó), cộng chọn nhạc nền cho cảnh.

**Chặng 6 — Engine chạy truyện.** `PlayerScreen.cs`: bộ thông dịch đi qua
`StoryGraph`, xử lý điều kiện/biến/tác động, chữ gõ từng ký tự
(`RichTextLabel.VisibleRatio`), màn lựa chọn lọc theo `ShowIf`, màn kết thúc,
phát BGM/SFX thật (chỉ ogg/mp3 — xem lý do trong `StageRenderer.LoadAudio`).
Nút "▶ Chơi thử" nằm trên toolbar của `EditorScreen`, luôn thấy được như bản
thiết kế. Bộ thông dịch cố tình đặt ở tầng Godot (không phải `VNdev.Core`) vì
cần vẽ hình và phát nhạc — việc chỉ engine làm được.

**Bug thật bắt được nhờ kiểm chứng, không phải chỉ đọc code:** lúc đầu hộp
thoại/lựa chọn/màn kết thúc được thêm làm con của cùng một `Control` với
nền+nhân vật sân khấu. `StageRenderer.Draw` xoá sạch con của `Control` đó mỗi
khi sang dòng thoại mới để vẽ lại — nên toàn bộ UI hộp thoại bị xoá theo ngay
sau dòng đầu tiên, gây `ObjectDisposedException` mỗi frame. Sửa bằng cách
tách một lớp phủ (`_overlay`) riêng, đặt cạnh sân khấu trong cùng
`AspectRatioContainer` thay vì làm con của nó.

Cả bốn chặng đều kiểm chứng bằng kịch bản `--headless` dựng dự án giả, đi hết
đường: tạo nhân vật → quét asset → soạn cảnh có nền+lời thoại → dựng đồ thị
scene→choice→condition→ending → mở Player → bấm hết lời thoại → chọn lựa
chọn (cộng biến) → điều kiện đánh giá đúng → tới đúng màn Kết thúc. Không có
lỗi runtime nào sau khi sửa bug trên.

---

## Hoàn thiện app — đã xong

Làm cho VNdev dùng như một app desktop bình thường, không chỉ đủ tính năng lõi:

- **Menu Tệp / Sửa / Xem / Chạy / Trợ giúp** và phím tắt cho mọi lệnh. Bảng
  phím tắt nằm ở một chỗ (`app/Shortcuts.cs`), đổi được trong Cài đặt.
- **Hoàn tác / Làm lại** cho mọi tab (`app/ProjectSession.cs`): chụp trạng thái
  dự án sau mỗi lần ghi đĩa, gom các lần ghi sát nhau thành một bước. Hoàn tác
  xong thì dựng lại màn hình, giữ nguyên tab, chương, vị trí cuộn.
- **Cửa sổ Cài đặt** (`app/SettingsDialog.cs`): Chung, Giao diện, Phím tắt,
  AI Providers; Xuất bản để khung "sắp có".
- **Màn hình chào** có dự án gần đây; app nhớ kích thước, vị trí cửa sổ.
- **Đánh bóng**: thanh trạng thái (giờ lưu, số lỗi, mức phóng), thông báo
  nhỏ, tooltip, hỏi trước khi xoá, đổi tên / sắp xếp / xoá chương, sao chép /
  dán / nhân bản node, vừa khung đồ thị.

Kiểm chứng bằng kịch bản `--headless` 20 bước (tạo dự án → thêm node → Ctrl+Z
qua menu → Ctrl+Y → nhân bản → dán → xoá → thêm, sắp xếp, xoá rồi hoàn tác
xoá chương → đổi màu, cỡ chữ, phím tắt) và ảnh chụp màn hình thật.

---

## Trợ lý AI — đã xong

Khung chat bên phải editor (`app/Ai/`), người dùng tự thêm API key:

- **Nhà cung cấp**: Claude qua SDK chính thức của Anthropic (gói NuGet
  `Anthropic`), cộng mọi dịch vụ chuẩn OpenAI qua `HttpClient` (OpenAI,
  Gemini, DeepSeek, OpenRouter, Grok, Ollama). Khoá cất trong Windows
  Credential Manager (`CredentialStore.cs`).
- **Biết dự án**: công cụ đọc tổng quan, chương, cảnh, nhân vật
  (`ProjectTools.cs`), kèm ngữ cảnh chỗ người dùng đang đứng.
- **Agent làm được mọi việc trong app** (`ProjectTools.Agent.cs`): tạo/sửa
  nhân vật và biểu cảm, tạo/sắp xếp/xoá chương, tạo cảnh kèm nền, nhạc, sân
  khấu và thoại, dựng đồ thị một lần (thêm/sửa/xoá/nối node, điểm bắt đầu),
  quản lý biến, đổi sân khấu theo từng dòng (`update_scene` → `line_stage`)
  — cộng các công cụ sửa thoại, lựa chọn, rẽ nhánh, tiền đề.
- **Người dùng quyết**: mỗi thay đổi hiện thẻ trước/sau; mặc định bấm Áp dụng
  từng cái, bật "Tự áp dụng" để AI làm một mạch. Mỗi lần ghi là một bước
  Ctrl+Z riêng; id mới sinh ra được báo lại cho AI để bước sau dùng đúng.
- **Trọng tâm** (prompt trong `AiConversation.cs`): lên ý tưởng và hành văn
  tiếng Việt — xưng hô, giọng nhân vật, tránh văn dịch.

Kiểm chứng bằng hai máy chủ giả chạy trên máy (một nói định dạng stream của
Anthropic, một nói chuẩn OpenAI): đi hết vòng đọc cảnh → đề xuất → áp dụng →
ghi đĩa → Ctrl+Z; kiểm tra chữ ký khối suy nghĩ được gửi lại, kết quả công cụ,
cache prompt, fallback, khoá sai. **Chưa chạy với khoá thật.**

Còn có thể làm: lưu cuộc trò chuyện qua các lần mở dự án, gán model riêng
cho từng loại việc như bản thiết kế, hiện chi phí token.

---

## Hiệu ứng và chuyển cảnh — đã xong

`app/StageView.cs` là khung sân khấu có hiệu ứng, dùng chung cho trình soạn
cảnh và màn Chơi thử — chọn một dòng trong editor là thấy đúng chuyển động
người chơi sẽ thấy.

- **Chuyển cảnh** (`SceneData.BackgroundTransition`): mờ qua đen, hoà tan
  (có thể dùng ảnh mask), quét bốn hướng, vỡ điểm ảnh, nhoè dần. Cảnh cũ
  được dời sang một lớp riêng chạy shader, không chụp màn hình — chụp sẽ dính
  hộp thoại và không chạy được khi kiểm thử không cửa sổ. Hộp thoại chờ
  chuyển cảnh xong mới hiện; bấm giữa chừng là tua cho xong.
- **Nhân vật vào/ra** (`StageActor.Enter` / `Exit`): hiện dần, trượt bốn
  hướng, nảy. Dời chỗ thì trượt mượt sang chỗ mới, người nói sáng lên dần.
  Lõi tính "ai vào, ai ra, ai dời" bằng `SceneData.StepsAt()` (có test).
- **Hiệu ứng màn hình theo dòng** (`DialogueLine.ScreenEffect`): rung, loé
  sáng, phủ màu, nhoè nền, zoom chậm (zoom giữ suốt cảnh tới khi đổi nền).
- **Hạt bay theo cảnh** (`SceneData.Particles`, `app/ParticlePresets.cs`):
  mưa, tuyết, hoa anh đào, bụi, bong bóng. Hạt vẽ bằng code, không cần asset;
  CPUParticles2D để chạy trên mọi máy.
- Nhạc nền không phát lại từ đầu khi cảnh sau dùng cùng bài; có `fadeIn`.
- Trợ lý AI đặt được tất cả các hiệu ứng trên qua `create_scene` /
  `update_scene`.

Kiểm chứng bằng kịch bản chạy có cửa sổ, chụp màn hình giữa lúc từng hiệu
ứng đang diễn (cả 9 kiểu chuyển cảnh chính), và gọi thẳng công cụ AI.

## Lưu / tải trong game — đã xong

Trong màn Chơi thử: nút **Lưu…** / **Tải…** mở bảng 9 ô + ô lưu nhanh;
**F5** lưu nhanh, **F9** tải nhanh. Lưu được giữa cảnh và lúc đang chọn phương
án (tải lên dựng lại cảnh nền phía sau các nút); ghi đè ô cũ phải bấm hai lần.

- Lõi: `SaveGame` (vị trí node + dòng + biến, không chụp sân khấu — tính lại
  bằng `StageAt`), `SaveStore` đọc ghi `<ô>.save.json` qua `IFileSystem`.
- `SaveGame.Resolve` đối chiếu bản lưu với dự án đã bị sửa: dòng bị xoá bớt →
  lùi về dòng cuối, biến mới → giá trị đầu, biến đã xoá → bỏ; node đã xoá →
  báo lỗi rõ ràng thay vì đứng ở chỗ sai. 8 test.
- Bản lưu nằm trong thư mục dữ liệu app (`<user data>/saves/<id dự án>/`),
  **không** trong thư mục dự án: không lên Git, không thành bước Ctrl+Z.

Còn lại của mục 9: Gallery, Ending List, ảnh thu nhỏ trên ô lưu.

---

## Sau 80%

Theo thứ tự đáng làm trước:

1. **Xuất game** — biến dự án thành .exe chạy độc lập.
2. **Gallery, Ending List** trong game (Save/Load đã xong).
3. **Đa ngôn ngữ đầy đủ** — giao diện 5 thứ tiếng, ruby text cho furigana.

---

## Cách làm việc với Claude trong VS Code

Claude tự đọc `CLAUDE.md` ở gốc dự án mỗi khi mở, nên nó biết dự án là gì, đã
quyết những gì, và không được làm gì.

Để nó bám đúng hướng:

- **Nói rõ đang ở chặng nào.** Ví dụ: "Làm chặng 2 trong docs/LO-TRINH.md."
- **Bắt chạy test sau mỗi lần sửa lõi.** Một lệnh, vài giây, bắt được lỗi thật.
- **Cập nhật file này khi xong một chặng**, đánh dấu đã làm. Đó là cách phiên
  làm việc sau biết mình đang ở đâu.
- **Khi đổi quyết định kiến trúc, ghi vào bảng lịch sử quyết định trong
  `CLAUDE.md`.** Không ghi thì vài tuần sau sẽ có người đề xuất lại đúng thứ
  đã bỏ.
