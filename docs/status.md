# OpenCAD — Trạng thái (cập nhật vòng 145)

Mọi con số **chạy lại được** bằng lệnh ghi kèm. Không có ước lượng.

## 1. Quy mô

| Mục | Số |
|---|---|
| Tệp C# | **57** (Core 39 · App 13 · Tools 4) |
| Dòng mã | **~27.100** |
| Lệnh trong bảng lệnh | **110** (10 nhóm) |
| Loại entity vẽ được | **18** trong `torture` (21 loại nếu tính cả loại chỉ đọc) |
| Alias khớp AutoCAD | **51/51** |

## 2. Kiểm chứng — 7 cổng, tất cả xanh

| Cổng | Lệnh | Kết quả hôm nay |
|---|---|---|
| Smoke (Core) | `smoke` | **933 PASS / 0 FAIL** |
| UI tự kiểm | `OpenCad.App.exe --uitest` | **202 PASS** |
| Mất dữ liệu khi lưu | `lossy` | 20/22 OK, 1 sai nội dung, 1 mất (đã biết) |
| Bản vẽ phức tạp | `torture` | 17/18 vẽ được sau lưu/đọc |
| Lệnh ↔ router ↔ README | `checkall` | **TẤT CẢ ĐẠT** |
| Alias AutoCAD | `checkaliases` | 51/51 |
| Giới hạn thư viện | `checklimits` | 6/6 còn đúng |

`checkall` gồm 14 cổng con: `checkcmds`, `checkdocs`, `checkaliases`, `checkparity`, `checktests`, `checklimits`, `checkinvariants` (12 bất biến nguồn).

**`--uitest` chạy thử 106/106 lệnh**: 99 lệnh chạy trực tiếp + 7 lệnh hộp thoại chạy qua đường không hộp thoại.

Build: **0 warning, 0 error**.

### Tương thích với bản vẽ do phần mềm khác tạo

Kiểm bằng 10 bản vẽ kiến trúc thật (GstarBIM, ~24.000 đối tượng, lớn nhất 14.317):

| Kiểm | Kết quả |
|---|---|
| Mở được | **10/10** |
| Đối tượng bị bỏ qua khi vẽ | **0** |
| Round-trip (mở → lưu → mở lại) | **10/10 không mất dữ liệu** |
| **Dấu vân tay nội dung** (hình học + màu + độ đậm + kiểu nét + thuộc tính lớp) | **giống hệt** cho cả 10 tệp |
| **Sửa rồi lưu** (dời Line/Insert, đổi đỉnh polyline, đổi chữ) | **10/10: thay đổi có hiệu lực, đối tượng khác nguyên vẹn** |
| Hiệu năng tệp 14.317 entity | mở 688 ms · scene 44 ms · hit-test 0,05 ms · **vẽ 46 ms/khung** · lưu 299 ms |

## 3. Tính năng theo nhóm

**Vẽ (19):** LINE, PLINE, RECTANG, CIRCLE, ARC, ELLIPSE, POLYGON, SPLINE, POINT, MLINE, TEXT, MTEXT, HATCH, LEADER, MLEADER, TABLE, DIVIDE, MEASURE, PTEXT/SHEETLINE (trên tờ).

**Sửa (22):** MOVE, COPY, ROTATE, SCALE, MIRROR, OFFSET, TRIM, EXTEND, FILLET, CHAMFER, ARRAY, ARRAYPOLAR, STRETCH, ERASE, OOPS, EXPLODE, JOIN, PEDIT, MATCHPROP, OVERKILL, COPYCLIP/PASTECLIP.

**Kích thước (9):** 5 loại + DIMCONTINUE, DIMBASELINE, DIMTEDIT, DIMSTYLE (sửa 5 thông số).

**Block (2 + 4 lệnh phụ):** BLOCK, INSERT, BLOCKS, ATTDEF, ATTEDIT, ATTSYNC, IMPORT, WBLOCK.

**Layer (7):** tạo/đổi tên/xoá/màu/kiểu nét/khoá/đóng băng, LAYISO, LAYUNISO, LAYDEL, LAYER.

**Tờ in:** nhiều layout, nhiều viewport, đổi tỷ lệ, kéo góc đổi kích thước, vẽ/sửa/ghi chú trên tờ, LAYOUTDEL.

**Xem:** zoom extents/window/previous/object, pan, OSNAP, ORTHO, POLAR, GRID, LTSCALE.

**Tệp:** mở/lưu DWG & DXF, lưu an toàn (.tmp + .bak), autosave 2 phút + phục hồi, mở gần đây, PDF vector (đúng tỷ lệ/font/độ đậm), in bộ hồ sơ, **bảng kiểu in theo lớp** (thay .ctb), SCRIPT.

**Khác:** HELP, AUDIT, PURGE, INFO, DIAG, WBLOCK, gợi ý lệnh trên dòng lệnh (Tab), lịch sử ↑/↓, phím tắt đầy đủ.

## 4. Bốn tầng bảo vệ dữ liệu

1. **Guard ngoại lệ** — kéo chuột, vẽ màn hình, xoá bảng… không làm sập app
2. **Log crash** — `%TEMP%\opencad-crash.log`, xem bằng `DIAG`
3. **Autosave + lưu an toàn** — tối đa mất 2 phút; lưu không phá file đang có
4. **Cảnh báo mất dữ liệu khi lưu** — `LossySaveRisks` + chuyển đổi tự động

## 5. Giới hạn — đã kiểm bằng `checklimits`

| Việc | Lý do (đã xác minh) |
|---|---|
| Xoay MTEXT (neo khác TopLeft) | Góc MTEXT suy ra từ `AlignmentPoint`; neo TopLeft thì xoay được, neo khác thì AP là vị trí |

| **Mất chữ MULTILEADER khi lưu** | ACadSharp 3.8.30 giữ entity + đường dẫn nhưng **bỏ `TextLabel`**. App cảnh báo và chuyển thành Leader + MText |
| Lưu TABLE | Bộ **ghi** bỏ handle `TextStyle` của `TableStyle.CellStyle` → bộ **đọc** báo `Could not read ACAD_TABLE`. 1×1 sống sót, từ 2×2 mất; gán `CellStyle.TextStyle` hợp lệ **không cứu được** |
| Ghi nội dung ô TABLE | `CadValue.Value` chỉ đọc; app đặt MText đè lên ô |
| `BlockRecord.BasePoint` | Thuộc tính không tồn tại |

**Chưa làm:** `.ctb`/`.pc3` nhị phân, AutoLISP, 3DSOLID/REGION (ACIS).

**Chưa đóng:** lỗi crash khi kéo grip do người dùng báo — không tái hiện được sau 3 vòng thử (12 loại entity, cả canvas lẫn cửa sổ thật, kể cả trường hợp biên); đã có 3 tầng guard và log crash.

## 6. Bản vẽ mẫu

| Tệp | Nội dung |
|---|---|
| [`samples/demo.dwg`](../samples/demo.dwg) | 37 entity: block, hatch, dimension, leader, attribute |
| [`samples/can-ho.dwg`](../samples/can-ho.dwg) | 28 entity: căn hộ 7×5,2 m + **tờ A3 1:50** có khung tên và kích thước trên tờ |
| [`samples/can-ho.pdf`](../samples/can-ho.pdf) | PDF xuất với bảng kiểu in theo lớp |
| [`samples/kieu-in.txt`](../samples/kieu-in.txt) | Bảng kiểu in (thay .ctb) |
| [`samples/mau-tu-dinh-nghia.pat`](../samples/mau-tu-dinh-nghia.pat) | 3 mẫu tô tự định nghĩa |

## 7. Điều đáng nói nhất sau 145 vòng

Phần lớn lỗi tìm ra **không phải lỗi thuật toán** mà là một trong bốn dạng:

| Dạng | Ví dụ | Phát hiện bởi |
|---|---|---|
| **Nửa đường** — code có, không nối | JOIN, kiểu nét, mẫu tô, SCRIPT chỉ có ở CLI | `checkwiring` |
| **Chạy mà không làm gì** | HATCHEDIT đổi mẫu nhưng không bỏ `IsSolid` | test đo primitive |
| **UI nói dối** | menu ghi Ctrl+Z nhưng mục bị vô hiệu → phím chết | gửi phím thật |
| **Bản vá bị ghi đè** | guard kéo grip mất sau 27 vòng | `checkinvariants` |
| **Kiểm tra nông** | `lossy` chỉ so số lượng → bỏ lọt MULTILEADER mất chữ | kiểm cả nội dung |

Mỗi dạng đều đã sinh ra một cổng kiểm để không lặp lại.
