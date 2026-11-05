# Nghiên cứu SDK CAD & xử lý file DWG

> Tài liệu khảo sát phục vụ quyết định chọn nền tảng cho dự án OpenCAD.
> Thời điểm khảo sát: 2026-10. Giá và tính năng cần xác nhận lại trước khi ký hợp đồng.

---

## 1. TL;DR — 5 kết luận quan trọng nhất

1. **DWG là định dạng đóng, không có spec chính thức từ Autodesk.** Mọi thư viện đọc/ghi DWG đều dựa trên reverse-engineering. Đây là gốc rễ của mọi vấn đề về license và độ tin cậy.
2. **DXF là định dạng mở, có tài liệu đầy đủ từ Autodesk.** Nếu được chọn, hãy để **DXF làm định dạng chuẩn nội bộ (canonical)**, còn DWG chỉ là cổng import/export.
3. **Không có lựa chọn mã nguồn mở nào vừa miễn phí vừa dùng thương mại được cho ghi DWG.** LibreDWG là GPLv3 (lan truyền), ACadSharp là MIT nhưng độ phủ entity chưa bằng ODA.
4. **Chỉ có 3 nguồn "đọc/ghi DWG đúng chuẩn công nghiệp":** ODA Drawings SDK (thương mại, ~3.000 USD/năm trở lên), Autodesk RealDWG (qua Tech Soft 3D, cần ADN), và LibreDWG (miễn phí nhưng GPL + beta).
5. **Quy tắc vàng về license:** nếu dùng GPL, hãy **cô lập nó trong một process/service riêng** giao tiếp qua file hoặc stdin/stdout — đây là cách duy nhất để giữ phần còn lại của sản phẩm không bị "lây" GPL.

---

## 2. Bản chất kỹ thuật của DWG

Hiểu cấu trúc DWG giúp đánh giá đúng năng lực từng thư viện.

### 2.1 Cấu trúc file (ví dụ R2000 / AC1015)

Đọc tuần tự các section:

| Section | Nội dung |
| :--- | :--- |
| **File Header** | Magic bytes (`AC1015`), version, offset tới các section, encryption seed |
| **Class Section** | Định nghĩa các class tùy biến (ACDBPLACEHOLDER, LAYOUT…) |
| **Object Map** | Bảng ánh xạ *handle* (ID duy nhất) → *byte offset* của object |
| **Data Section** | Toàn bộ object, serialize dạng **bit stream** có độ dài thay đổi |
| **Handle Section** | Tham chiếu: owner, layer, linetype, reactors, prev/next entity |
| **Second Header** | Bản sao header để phục hồi file hỏng |

### 2.2 Vì sao DWG khó đọc

Dữ liệu được **đóng gói ở mức bit**, không phải byte. Các kiểu dữ liệu nội bộ:

| Kiểu | Tên | Mô tả |
| :--- | :--- | :--- |
| `B` | Bit | 1 bit |
| `BS` | Bit Short | 2-bit prefix + 0/1/2 byte (số nguyên 16-bit) |
| `BL` | Bit Long | 2-bit prefix + 0–4 byte (số nguyên 32-bit) |
| `BD` | Bit Double | 2-bit prefix + 0 hoặc 8 byte (IEEE double) |
| `DD` | Default Double | 2-bit prefix + 0/4/6/8 byte (delta so với giá trị mặc định) |
| `H` | Handle | 4-bit code + 4-bit counter + N byte (tham chiếu object) |

`DD` là lý do file DWG nhỏ: đỉnh đầu tiên lưu 2 double đầy đủ (2RD = 16 byte), các đỉnh sau dùng delta encoding thường chỉ tốn 0–4 byte/trục.

### 2.3 Ma trận độ phức tạp entity

Đây là tiêu chí phân biệt các thư viện rõ nhất:

| Nhóm entity | Độ khó | Ghi chú |
| :--- | :--- | :--- |
| LINE, CIRCLE, ARC, ELLIPSE, POINT | Thấp | Mọi thư viện đều làm được |
| LWPOLYLINE (có bulge), POLYLINE_2D/3D | Thấp–TB | Bulge = cung tròn nội suy |
| SPLINE (NURBS/Bezier) | TB | Cần control point + fit point + knot vector |
| TEXT, MTEXT | TB | MTEXT có mã định dạng inline phức tạp |
| INSERT (block reference) | TB | Đệ quy block lồng nhau, MINSERT |
| DIMENSION | TB–Cao | Associative dimension: hình học phụ thuộc tham số |
| HATCH | Cao | Boundary loop, pattern, gradient |
| TABLE | Cao | Là INSERT đặc biệt trong nhiều thư viện |
| **3DSOLID / REGION / SURFACE** | **Rất cao** | Chứa dữ liệu **ACIS** (SAT) — proprietary, gần như không thể tự parse |
| Dynamic block | Rất cao | Tham số + action + constraint |
| Proxy/custom object | Rất cao | Do add-on tạo ra (Civil 3D, Mechanical…) |

**Hệ quả:** mọi thư viện mã nguồn mở đều **không đọc được 3DSOLID/REGION/SURFACE** vì đó là blob ACIS độc quyền. Nếu bài toán cần 3D solid, gần như chắc chắn phải dùng ODA hoặc RealDWG.

---

## 3. Bảng so sánh tổng hợp

### 3.1 Mã nguồn mở

| Thư viện | Ngôn ngữ | License | Đọc DWG | Ghi DWG | Đọc/Ghi DXF | Điểm mạnh | Điểm yếu |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **LibreDWG** | C (bindings: Python, Java, JS/WASM) | **GPLv3** | ✅ tốt, R13–R2018 | ⚠️ tốt cho R1.1–R2000; R2010+ hạn chế | ✅ | Bộ đọc DWG mã nguồn mở đầy đủ nhất; có `dwg2dxf`, `dwggrep`, `dwglayer`, xuất SVG/PS/JSON | **Beta**; một số entity R2010+ bị skip; **GPLv3 lan truyền** |
| **ACadSharp** | C# (.NET 8 / netstandard2.0 / net48) | **MIT** | ✅ AC1014–AC1032 (R14→R2018) | ✅ trừ AC1021 (R2007) | ✅ ASCII + binary, AC1009+ | API .NET rất "CAD-native" (CadDocument, Block, Layer, Style); NuGet `ACadSharp` v3.8.0 | Không có renderer (ACadSharp.Formats còn WIP); chưa phủ 3DSOLID |
| **libdxfrw** | C++ | GPLv2 | ✅ R14–V2015 (chỉ đọc) | ❌ | ✅ ASCII + binary | Nhẹ, dễ nhúng | Không ghi DWG; ít cập nhật |
| **netDxf** | C# | **MIT** | ❌ | ❌ | ✅ AC1015–AC1032, cả text & binary | Cực kỳ dễ dùng, API sạch, tự quản lý bảng Layer/Style | **DXF only**; không đọc được REGION/SURFACE/3DSOLID; dynamic block không hỗ trợ |
| **ezdxf** | Python 3.11+ | **MIT** | ❌ | ❌ | ✅ R12–R2018 (ghi R12 cho bản cũ) | Hệ sinh thái Python mạnh nhất; giữ nguyên dữ liệu app thứ 3; hỗ trợ `odafc.convert()` để gọi ODA File Converter | Chỉ DXF; không phải CAD kernel |
| **dxf-parser / three-dxf / dxf-render** | JS/TS | MIT | ❌ | ❌ | ✅ (đọc) | Render DXF trên browser bằng Three.js | Chỉ DXF, độ phủ entity hạn chế |
| **MLightCAD** (`@mlightcad/cad-simple-viewer`) | TypeScript + WASM | **MIT** | ⚠️ **opt-in** qua `@mlightcad/libredwg-converter` (**GPL**) | ❌ | ✅ | Viewer/editor DWG+DXF **chạy hoàn toàn trên browser**, không backend; xuất 1 file HTML self-contained | Bản thân package **không kèm converter DWG**; muốn có DWG phải tự thêm dependency GPL + worker/wasm |

> **Lưu ý quan trọng về MLightCAD:** tài liệu package ghi rõ DWG support là opt-in, host app phải tự thêm `@mlightcad/libredwg-converter` (GPL), deploy worker + wasm, và đăng ký converter. Đã có issue thực tế (Nextcloud CAD Viewer) fail vì tưởng package "có sẵn DWG".

### 3.2 Thương mại

| SDK | Nhà cung cấp | Nền tảng | Năng lực | Chi phí |
| :--- | :--- | :--- | :--- | :--- |
| **Drawings SDK** (Teigha cũ) | Open Design Alliance | C++, .NET, Python, Java; Windows/Linux/macOS/iOS/Android | Đọc **100% dữ liệu DWG/DGN/DXF** kể cả xdata; tạo/sửa/lưu mọi version; có sẵn render; 3D modeling, constraint solver, dynamic block editor | **Commercial ~3.000 USD** năm đầu / 2.250 USD duy trì (giới hạn 100 seat, không SaaS). **Sustaining ~7.500 / 4.500** (không giới hạn, cho phép web/SaaS). **Founding ~37.500 / 18.000** (kèm full source) |
| **RealDWG** | Autodesk, bán độc quyền qua **Tech Soft 3D** | C++ và .NET | Đọc/ghi DWG & DXF "từ chính chủ" — độ chính xác cao nhất | Báo giá theo OEM/ADN; cần là member ADN |
| **Aspose.CAD** | Aspose | .NET, Java, Python qua .NET | Mạnh về **convert & render**: DWG/DXF/DGN/DWF/IFC/STL → PDF, raster, SVG | Thương mại theo developer/seat |
| **CAD .NET / CAD DLL** | CADSoftTools | .NET, C++, Delphi, Python | Viewer control + import/export DWG, DXF, PLT | Thương mại |
| **CADLib** | Wout Ware | .NET | Đọc/ghi DWG/DXF + render GDI+ | Thương mại |

### 3.3 Cloud / API

| Dịch vụ | Năng lực | Mô hình |
| :--- | :--- | :--- |
| **APS Model Derivative API** | Dịch DWG/RVT/NWD/IFC → **SVF2**, trích xuất cây object, properties, geometry, thumbnail; render bằng Viewer SDK | Pay-as-you-go (credits/tokens) |
| **ODA Drawings inWeb** | Tạo/sửa/render DWG & DXF ngay trên web | Thuộc gói ODA Sustaining/Founding |
| **ODA File Converter** | **Miễn phí**, chuyển đổi hàng loạt giữa các version DWG/DXF (DWG↔DXF) | Freeware, dùng như công cụ CLI — **không phải thư viện nhúng** |

### 3.4 Kernel hình học (nếu cần dựng CAD thật, không chỉ đọc file)

| Kernel | License | Ghi chú |
| :--- | :--- | :--- |
| **Open CASCADE (OCCT)** | LGPL | Kernel B-rep mã nguồn mở duy nhất đủ dùng; có translator STEP/IGES; dùng trong FreeCAD |
| **Parasolid** | Thương mại (Siemens) | Chuẩn công nghiệp cho solid modeling |
| **ACIS** | Thương mại (Spatial) | Chính là định dạng ruột của 3DSOLID trong DWG |

---

## 4. Ma trận quyết định theo nhu cầu

| Nhu cầu của bạn | Lựa chọn tối ưu | Lý do |
| :--- | :--- | :--- |
| Chỉ **xem** DWG/DXF trên web, không backend | MLightCAD (chấp nhận GPL cho converter) hoặc chuyển DWG→DXF server-side rồi dùng `three-dxf` | Không tốn phí, chạy client-side |
| **Đọc** DWG để bóc tách dữ liệu (BOM, layer, thống kê) | ACadSharp (.NET) hoặc LibreDWG (service riêng) | MIT/GPL, đủ cho 2D |
| **Đọc + ghi** DWG, sản phẩm thương mại bán cho nhiều khách | **ODA Drawings SDK** | Duy nhất khả thi về mặt pháp lý + kỹ thuật ở quy mô |
| Cần **đúng 100%** như AutoCAD, kể cả proxy object | RealDWG | Đọc từ source |
| Chỉ cần **convert** DWG → PDF/PNG/SVG | Aspose.CAD hoặc ODA File Converter + renderer | Rẻ, nhanh, không cần semantic model |
| Cần **3D solid / boolean / constraint** | ODA (có 3D modeling + constraint solver) hoặc OCCT | Mã nguồn mở không parse được ACIS |
| Pipeline tự động hoá, không cần embedded | **ezdxf + ODA File Converter** | Python, MIT, convert DWG↔DXF bằng CLI miễn phí |

---

## 5. Cạm bẫy license — phần dễ mất tiền nhất

### 5.1 GPLv3 của LibreDWG

LibreDWG là GPLv3. Nếu bạn **liên kết tĩnh/động** nó vào sản phẩm, toàn bộ sản phẩm phải là GPLv3 → phải phát hành source. Với sản phẩm thương mại, đây là điều không thể chấp nhận.

**Cách xử lý hợp lệ:**

- ✅ Chạy LibreDWG như **một executable/service độc lập** (`dwg2dxf`, `dxf2dwg`), giao tiếp với app qua **file trên đĩa hoặc stdin/stdout**. Ranh giới process (arm's-length) thường được coi là không tạo derivative work.
- ✅ Dùng trong nội bộ công ty (không phân phối ra ngoài) — GPL không kích hoạt.
- ❌ Nhúng DLL LibreDWG vào app desktop bán ra ngoài.
- ❌ Dùng WASM build của LibreDWG trong bundle JS bán ra ngoài (đây chính là lý do MLightCAD tách converter thành package riêng, opt-in).

### 5.2 ODA — bẫy "reseller"

ODA **không bán qua reseller**. Mỗi khách hàng cuối **phải tự đăng ký membership và ký giấy tờ với ODA**. Bạn có thể trả tiền, nhưng khách phải là member.

Ngoài ra: **subscription là bắt buộc theo năm**. Nếu dừng trả phí, bạn **mất quyền phân phối** sản phẩm đã phát triển, kể cả bản đã ship trước đó. Đây là rủi ro chiến lược cần tính vào giá thành dài hạn.

### 5.3 RealDWG

Cần là thành viên **Autodesk Developer Network (ADN)**, license theo OEM, và hợp đồng riêng với Tech Soft 3D. Đây là con đường đắt và chậm nhất, chỉ hợp lý khi độ chính xác DWG là yêu cầu sống còn.

### 5.4 Giấy phép file DWG

Bản thân **định dạng** DWG không được bảo hộ bản quyền ở nhiều nước, nhưng **spec do ODA công bố** ("Open Design Specification for .dwg files", v5.4.1) là tài liệu của ODA — dùng để tham khảo thì được, nhưng đừng copy nội dung vào sản phẩm.

---

## 6. Kiến trúc đề xuất cho OpenCAD

```
┌──────────────────────────────────────────────────────────┐
│                     OpenCAD Application                  │
│                                                          │
│   ┌────────────┐   ┌─────────────┐   ┌───────────────┐   │
│   │  Document  │──▶│  Geometry   │──▶│   Renderer    │   │
│   │   Model    │   │   Kernel    │   │ (Skia/WebGL)  │   │
│   │  (canonical│   │ (OCCT/2D)   │   │               │   │
│   │   = DXF)   │   └─────────────┘   └───────────────┘   │
│   └─────┬──────┘                                         │
│         │                                                │
│   ┌─────▼──────────────────────────────────────────┐     │
│   │         Format Adapter Layer (plug-in)         │     │
│   └──┬────────────┬──────────────┬─────────────────┘     │
└──────┼────────────┼──────────────┼───────────────────────┘
       │            │              │
   ┌───▼───┐   ┌────▼─────┐   ┌────▼──────────────┐
   │  DXF  │   │   DWG    │   │  PDF / SVG / IFC  │
   │ native│   │ adapter  │   │     export        │
   └───────┘   └────┬─────┘   └───────────────────┘
                    │
        ┌───────────┴───────────┐
        │                       │
  ┌─────▼──────┐        ┌───────▼────────┐
  │ ODA        │        │ LibreDWG       │
  │ (thương mại│        │ (sidecar proc, │
  │  khi cần)  │        │  GPL-clean)    │
  └────────────┘        └────────────────┘
```

### Nguyên tắc thiết kế

1. **DXF là nguồn sự thật duy nhất** trong model nội bộ. Mọi thứ khác là adapter. Điều này cho phép bạn đổi nhà cung cấp DWG mà không đụng vào core.
2. **Interface `IDwgProvider`** với 2 implementation: `LibreDwgSidecarProvider` (miễn phí, mặc định) và `OdaDrawingsProvider` (thương mại, bật bằng license key). Cho phép ship bản community + bản pro.
3. **Tuyệt đối không tự viết DWG parser.** Chi phí bảo trì vượt xa lợi ích; mỗi version AutoCAD mới lại phá vỡ.
4. **Xử lý DWG bất đồng bộ** — file thực tế thường 50–300 MB, parse có thể mất hàng chục giây.
5. **Round-trip test là bắt buộc.** Bộ test tối thiểu: file từ AutoCAD 2013/2018/2024, có block lồng, hatch, dimension associative, text Unicode tiếng Việt.

---

## 7. Chi phí & lộ trình gợi ý

### Phương án A — Khởi đầu miễn phí (khuyến nghị cho giai đoạn POC)

| Hạng mục | Chi phí |
| :--- | :--- |
| Đọc/ghi DXF | 0 (ACadSharp hoặc netDxf, MIT) |
| Đọc DWG | 0 (ACadSharp MIT, hoặc LibreDWG sidecar) |
| Ghi DWG | 0 qua ODA File Converter CLI (DXF→DWG) |
| **Tổng** | **0 USD** |

Hạn chế: phụ thuộc ODA File Converter cho bước ghi DWG (không nhúng được), không có 3D solid.

### Phương án B — Sản phẩm thương mại

| Hạng mục | Chi phí/năm |
| :--- | :--- |
| ODA Sustaining (cho phép SaaS) | ~7.500 USD năm đầu, ~4.500 USD duy trì |
| **Tổng 5 năm** | **~25.500 USD** |

Đổi lại: đọc 100% DWG, ghi mọi version, có render, 3D modeling, hỗ trợ chính thức.

### Phương án C — Chỉ convert/render

Aspose.CAD: chi phí thấp hơn ODA đáng kể, nhưng **không có semantic model** — chỉ đổi định dạng và render. Không dùng để xây CAD editor được.

---

## 8. Checklist POC trước khi chốt

- [ ] Thu thập **20–50 file DWG thật** từ khách hàng/domain mục tiêu (đa dạng version, có file "bẩn")
- [ ] Đo tỉ lệ parse thành công + thời gian parse trên từng thư viện
- [ ] Kiểm tra **text tiếng Việt có dấu** — đây là điểm chết người với codepage/shape font
- [ ] Kiểm tra **round-trip**: DWG → model → DWG, mở lại bằng AutoCAD/ODA, so sánh từng entity
- [ ] Kiểm tra block lồng nhiều cấp + dynamic block
- [ ] Kiểm tra hatch có boundary phức tạp (island detection)
- [ ] Kiểm tra dimension associative (kéo dài kích thước có cập nhật không)
- [ ] Xác nhận **không có 3DSOLID/REGION** trong tập file mục tiêu — nếu có, loại bỏ ngay các phương án mã nguồn mở
- [ ] Rà soát license với luật sư nếu định phân phối dùng LibreDWG
- [ ] Đo bộ nhớ khi mở file 200 MB

---

## 9. Nguồn tham khảo

**Định dạng & spec**
- [Open Design Specification for .dwg files (ODA)](https://www.opendesign.com/files/guestdownloads/OpenDesign_Specification_for_.dwg_files.pdf) — spec DWG R13–R2013
- [Understanding the DWG File Format](https://www.innerscene.com/tools/dwg-file-format) — giải thích cấu trúc bit-level

**Mã nguồn mở**
- [GNU LibreDWG](https://www.gnu.org/software/libredwg/) — giấy phép GPLv3, trạng thái beta
- [DOMCR/ACadSharp](https://github.com/DomCR/ACadSharp) — MIT, [nuget v3.8.0](https://www.nuget.org/packages/ACadSharp)
- [haplokuon/netDxf](https://github.com/haplokuon/netDxf) — MIT, DXF only
- [mozman/ezdxf](https://ezdxf.readthedocs.io/en/stable/introduction.html) — MIT, Python
- [codelibs/libdxfrw](https://github.com/codelibs/libdxfrw) — GPLv2
- [mlightcad/cad-viewer](https://github.com/mlightcad/cad-viewer) — MIT, viewer web

**Thương mại**
- [ODA Drawings SDK](https://www.opendesign.com/products/drawings) — tính năng & version hỗ trợ
- [ODA Pricing](https://www.opendesign.com/pricing) — bảng giá membership
- [Autodesk RealDWG (Tech Soft 3D)](https://developer.techsoft3d.com/toolkits/realdwg) — kênh license duy nhất
- [Aspose.CAD for .NET](https://docs.aspose.com/cad/net) — convert/render
- [CADSoftTools CAD .NET](https://cadsofttools.com/products/cad-net)

**Cloud**
- [APS Model Derivative API](https://aps.autodesk.com/developer/overview/model-derivative-api) — dịch sang SVF2

**Kernel**
- [Open CASCADE Technology](https://occt3d.com/open-cascade-technology) — kernel B-rep LGPL
