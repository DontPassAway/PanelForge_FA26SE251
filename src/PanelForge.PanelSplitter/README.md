# PanelForge.PanelSplitter (đã ngừng chạy riêng)

Từ 2026-10-31 Backend tách panel/bong bóng bằng `OpenCvPanelSplitter` (OpenCvSharp4, chạy trong tiến trình .NET). **Không cần chạy service này (cổng 5066) nữa.**

`app.py` được giữ làm **nguồn chuẩn parity**: `test/PanelForge.UnitTests/TestData/panel-splitter/expected.json` sinh từ `split_page` của file này (xem README trong thư mục đó). Sửa thuật toán thì sửa cả hai bên và sinh lại `expected.json`.
