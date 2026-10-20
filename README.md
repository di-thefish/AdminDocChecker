# AdminDocChecker

Ứng dụng Windows (WPF, C#, .NET 8) kiểm tra thể thức và chính tả văn bản hành chính `.docx`.
Mọi thứ chạy trên máy của bạn: không cần máy chủ, không cần thuê cơ sở dữ liệu.

## Cần có

- Windows 10/11
- .NET 8 SDK (hoặc Visual Studio 2022 có workload ".NET desktop development")
- Tùy chọn, để kiểm tra chính tả bằng AI: [Ollama](https://ollama.com), sau đó chạy `ollama pull qwen2.5:3b`

## Chạy

```
cd AdminDocChecker
dotnet run
```

Hoặc mở `AdminDocChecker.csproj` bằng Visual Studio và bấm F5.

Đóng gói thành một thư mục chạy được:

```
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

## Cách dùng

1. Bấm "Chọn tệp .docx...".
2. Chọn "Kiểm tra chính tả bằng AI" nếu muốn (cần Ollama đang chạy).
3. Bấm "Kiểm tra" để xem danh sách vấn đề.
4. Bấm "Sửa thể thức, lưu bản sao..." để tạo bản đã sửa. Tệp gốc không bao giờ bị ghi đè.

## Phần nào làm gì

| Thành phần | Việc | Dùng AI? |
|---|---|---|
| `FormatChecker` | Khổ giấy, lề, phông, cỡ chữ, quốc hiệu, tiêu ngữ, số ký hiệu, nơi nhận | Không, dùng quy tắc cố định |
| `FormatFixer` | Tự sửa khổ giấy, lề, phông, cỡ chữ trên bản sao | Không |
| `SpellService` | Gợi ý lỗi chính tả qua Ollama, chỉ gợi ý, không tự sửa | Có, chạy cục bộ |
| `HistoryStore` | Lưu lịch sử vào `%LocalAppData%\AdminDocChecker\history.db` (SQLite) | Không |

## Sửa quy tắc

Mọi giá trị thể thức nằm trong `rules.json` (lề, phông, cỡ chữ, nội dung quốc hiệu, tiêu ngữ).
Quy tắc ban đầu dựa trên Phụ lục I của Nghị định 30/2020/NĐ-CP. Hãy đối chiếu với văn bản
hiện hành và sửa tệp này khi quy định thay đổi, không cần sửa code.

## Giới hạn hiện tại

- Chỉ kiểm tra gần đúng. Chưa kiểm tra vị trí, canh lề, kiểu chữ đậm/nghiêng từng thành phần
  (tên cơ quan, trích yếu, chữ ký), khoảng cách dòng, đánh số trang.
- Cỡ chữ được kiểm tra chung trong khoảng cho phép (mặc định 12 đến 14 pt), chưa tách riêng từng thành phần.
- Phông và cỡ chữ lấy từ run, kiểu đoạn và mặc định tài liệu. Phông lấy theo chủ đề (theme) bị bỏ qua.
- Gợi ý chính tả từ AI có thể sai: luôn tự kiểm tra trước khi sửa.
- Tệp `.doc` cũ cần lưu lại thành `.docx` trước.

## Hướng phát triển

- Kiểm tra từng thành phần thể thức theo loại văn bản (công văn, quyết định, tờ trình...).
- Cho phép chấp nhận hoặc bỏ qua từng gợi ý chính tả rồi ghi vào bản sao.
- Xuất báo cáo lỗi ra tệp Word hoặc PDF.

## Tạo file .exe

Bấm đúp `build-exe.bat` (cần .NET 8 SDK trên máy build). Kết quả nằm ở `publish\AdminDocChecker.exe`,
chạy độc lập, không cần cài .NET trên máy dùng. Giữ `rules.json` cùng thư mục với file exe.

File exe độc lập khá nặng (khoảng vài chục đến hơn một trăm MB). Muốn bản nhẹ (vài MB) nhưng máy dùng
phải cài .NET 8 Desktop Runtime, đổi `--self-contained true` thành `--self-contained false` trong `build-exe.bat`.
