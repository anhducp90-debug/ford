// Dùng thử như thủ kho: dữ liệu TRỐNG, thao tác qua giao diện theo một đơn hàng khép kín; đếm thao tác, chụp màn hình, ghi nhật ký.
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using QuanLyNPL.App;
using QuanLyNPL.App.Screens;
using QuanLyNPL.Data;

var outDir = args.Length > 0 ? args[0] : "trial";
Directory.CreateDirectory(outDir);
var dataDir = Path.Combine(Path.GetTempPath(), "qlnpl_trial_" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("QLNPL_DATA_DIR", dataDir);
AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var x = new Ctx(dataDir); App.X = x;
var w = new MainWindow { Width = 1366, Height = 860 }; App.Main = w;
w.Show(); Pump();
Dialog.AutoAnswer = true;

var log = new List<string>();
int actions = 0, shot = 0; string step = ""; var sw = new Stopwatch();
void Step(string name) { End(); step = name; actions = 0; sw.Restart(); log.Add($"\n## {name}"); Console.WriteLine("== " + name); }
void End() { if (step.Length > 0) log.Add($"- Số thao tác: {actions} · thời gian xử lý giao diện {sw.ElapsedMilliseconds} ms"); }
void Note(string s) { log.Add("- " + s); Console.WriteLine("   " + s); }
void Ok(bool c, string what) { Note((c ? "ĐẠT: " : "KHÔNG ĐẠT: ") + what + (c ? "" : $" | trạng thái: {w.StatusText} | hộp thoại: {Dialog.LastMessage}")); }
void Shot(string name) { var f = w.CaptureRenderedFrame(); var p = $"{++shot:00}_{name}.png"; f?.Save(Path.Combine(outDir, p)); log.Add($"- Ảnh: {p}"); }
IEnumerable<T> All<T>() => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<T>();
void Go(string title) { actions++; for (var m = 0; m < MainWindow.Modules.Length; m++) for (var i = 0; i < MainWindow.Modules[m].Items.Length; i++) if (MainWindow.Modules[m].Items[i].Title == title) { w.Navigate(m, i); Pump(); return; } throw new Exception("Không có màn " + title); }
void Click(string text, int nth = 0) { actions++; var b = All<Button>().Where(b => (b.Content as string) == text).ElementAtOrDefault(nth) ?? throw new Exception("Không thấy nút: " + text); b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump(); }
TextBox Box(string wm, int nth = 0) => All<TextBox>().Where(t => t.Watermark == wm).ElementAt(nth);
void Type(string wm, string text, int nth = 0) { actions++; Box(wm, nth).Text = text; Pump(); }
void TypeBox(TextBox b, string text) { actions++; b.Text = text; Pump(); }
ComboBox Place(string item) => All<ComboBox>().First(cb => cb.Items.Cast<object>().Any(o => o.ToString()!.StartsWith(item)) && !cb.Items.Cast<object>().Any(o => o.ToString() == "(chưa chọn)"));
ComboBox ComboWith(string item, int nth = 0) => All<ComboBox>().Where(cb => cb.Items.Cast<object>().Any(o => o.ToString()!.StartsWith(item))).ElementAt(nth);
void Pick(ComboBox cb, string startsWith) { actions++; cb.SelectedItem = cb.Items.Cast<object>().First(o => o.ToString()!.StartsWith(startsWith)); Pump(); }
void Check(string content, bool on) { actions++; var cb = All<CheckBox>().First(c => (c.Content as TextBlock)?.Text == content || (c.Content as string) == content); cb.IsChecked = on; Pump(); }
void Radio(string content) { actions++; All<RadioButton>().First(r => (r.Content as string) == content).IsChecked = true; Pump(); }
bool Shows(string s) => w.GetTexts().Any(t => t.Contains(s));
void Lines(params (string mat, string qty)[] rows)
{   // LinesEditor: chọn NPL + số lượng; thêm dòng khi cần
    for (var i = 0; i < rows.Length; i++)
    {
        var matCbs = All<ComboBox>().Where(cb => cb.Width == 290).ToList();
        if (i >= matCbs.Count) { Click("+ Thêm dòng"); matCbs = All<ComboBox>().Where(cb => cb.Width == 290).ToList(); }
        Pick(matCbs[i], rows[i].mat);
        var qtys = All<TextBox>().Where(t => t.Width == 110 && t.Watermark == "0").ToList();
        TypeBox(qtys[i], rows[i].qty);
    }
}

// ---------------- 0. Mở ứng dụng lần đầu ----------------
Step("0. Mở ứng dụng lần đầu – tạo PIN");
Shot("lan_dau_tao_pin");
Ok(w.IsLocked && Shows("Lần đầu sử dụng"), "lần đầu bắt tạo PIN");
x.Pin.SetPin("2468"); w.TryUnlock("2468"); Pump(); actions += 3;
Shot("man_hinh_dau_tien");
Note($"Màn hình mở ra đầu tiên: {All<TextBlock>().First(t => t.FontSize >= 20).Text}");
Ok(Shows("Thiết lập ban đầu"), "trang chủ hiện các bước thiết lập ban đầu");

// ---------------- 1. Quản trị ----------------
Step("1. Quản trị – nơi xuất / nơi nhận, thông tin đơn vị");
Go("Nơi xuất / Nơi nhận");
foreach (var (c, n, k) in new[] { ("KHO", "Kho NPL", "Nơi xuất và nơi nhận"), ("NCC-TT", "Dệt Thành Tâm (NCC)", "Chỉ là nơi xuất"), ("CAT", "Nhà cắt", "Nơi xuất và nơi nhận"),
                                   ("MAY1", "Tổ may 1", "Nơi xuất và nơi nhận"), ("MAY2", "Tổ may 2", "Nơi xuất và nơi nhận"), ("HT", "Tổ hoàn thiện", "Nơi xuất và nơi nhận") })
{ Type("Mã", c); Type("Tên", n); Pick(ComboWith("Nơi xuất và nơi nhận"), k); Click("Lưu"); }
Ok(x.Q.Places().Count == 6, "khai báo 6 nơi xuất/nhận");
Pick(All<ComboBox>().Where(cb => cb.Items.Cast<object>().Any(o => o.ToString()!.StartsWith("KHO"))).ElementAt(0), "KHO");
Pick(All<ComboBox>().Where(cb => cb.Items.Cast<object>().Any(o => o.ToString()!.StartsWith("KHO"))).ElementAt(1), "KHO");
Click("Lưu mặc định");
Type("Tên đơn vị", "CÔNG TY MAY THỬ NGHIỆM"); Type("Địa chỉ", "KCN Thử nghiệm"); Click("Lưu", 1);
Ok(x.Places.DefaultInTo is not null && x.Company.Name.Length > 0, "lưu mặc định + thông tin đơn vị");
Shot("quan_tri_noi_xuat_nhan");

// ---------------- 2. Khai báo danh mục ----------------
Step("2. Khai báo – khách hàng, NPL, lưới size, mã hàng, chủng loại");
Go("Khách hàng"); Type("Mã", "AP"); Type("Tên khách hàng", "Thời trang An Phước"); Click("Lưu khách hàng");
Go("Nguyên phụ liệu");
foreach (var (c, n, u) in new[] { ("AP-VC", "Vải chính kaki", "m"), ("AP-VL", "Vải lót", "m"), ("AP-CHI", "Chỉ may 40/2", "cuộn"), ("AP-NUT", "Nút 4 lỗ", "cái"), ("AP-NHAN", "Nhãn chính", "cái"), ("AP-TUI", "Túi PE", "cái") })
{ Pick(All<ComboBox>().First(), "AP"); Type("Mã NPL", c); Type("Tên", n); Type("ĐVT", u); Click("Lưu NPL mới"); }
// Vải chính nhập theo kg: tính hệ số m/kg từ định lượng 208 g/m², khổ 150 cm
actions++; All<DataGrid>().First().SelectedIndex = x.Q.Materials().FindIndex(m => m.Code == "AP-VC"); Pump();
Type("g/m²", "208"); Type("cm", "150"); Click("Tính");
Note("Hệ số tính được: " + Box("hệ số").Text + " m/kg");
Click("Lưu quy đổi");
Ok(x.Q.Materials().Single(m => m.Code == "AP-VC").AltUnit == "kg", "khai báo quy đổi kg → m cho vải chính");
Shot("quy_doi_kg_m");
Ok(x.Q.Materials().Count == 6, "khai báo 6 NPL");
Note($"Sau khi lưu, ô khách hàng {(Ui.Sel<long?>(All<ComboBox>().First()) is null ? "BỊ XÓA, phải chọn lại mỗi lần" : "được giữ")}.");
Shot("khai_bao_npl");
Go("Lưới size"); Type("Mã lưới", "SML"); Type("Tên lưới", "S M L XL"); Type("Ví dụ: 110/56; 110/60; 120/60 hoặc S; M; L; XL", "S; M; L; XL"); Click("Lưu lưới mới");
Go("Mã hàng"); Pick(All<ComboBox>().First(), "AP"); Type("Mã hàng", "AP-BL01"); Type("Tên", "Bộ bảo hộ lao động"); Pick(ComboWith("SML"), "SML"); Click("Lưu mã hàng");
Go("Chủng loại");
Type("Mã", "AO"); Type("Tên", "Áo bảo hộ"); Click("Lưu chủng loại");
Type("Mã", "QUAN"); Type("Tên", "Quần bảo hộ"); Pick(ComboWith("Áo"), "Quần"); Click("Lưu chủng loại");
Ok(x.Q.Variants(x.Q.Products().Single().Id).Count == 2, "mã hàng 2 chủng loại");
Shot("khai_bao_chung_loai");

// ---------------- 3. BOM ----------------
Step("3. Khai báo BOM – Tổ cắt (theo chủng loại), Sản xuất (theo chủng loại), Đóng gói (theo bộ)");
Go("BOM & định mức");
void Bom(string type, string? variant, params (string mat, string qty)[] lines)
{
    Radio(type);
    if (variant is null) Radio("Theo bộ"); else { Radio("Theo chủng loại"); Pick(ComboWith("AO –"), variant); }
    if (All<Button>().Any(b => (b.Content as string) == "Tạo BOM")) Click("Tạo BOM");
    Lines(lines);
    Click("Lưu phiên bản mới");
}
Bom("Tổ cắt", "AO", ("AP-VC", "1,35"), ("AP-VL", "0,4"));
Bom("Tổ cắt", "QUAN", ("AP-VC", "1,1"));
Bom("Sản xuất", "AO", ("AP-CHI", "0,03"), ("AP-NUT", "6"));
Shot("bom_san_xuat_ao");
Bom("Sản xuất", "QUAN", ("AP-CHI", "0,02"), ("AP-NUT", "1"));
Bom("Đóng gói", null, ("AP-NHAN", "2"), ("AP-TUI", "1"));
Ok(x.Q.Boms(x.Q.Products().Single().Id).Count == 5, "5 BOM, mỗi BOM 1 phiên bản");
Go("Định mức một bộ"); Shot("dinh_muc_mot_bo");

// ---------------- 4. Đơn hàng ----------------
Step("4. Lập đơn hàng 220 bộ");
Go("Đơn hàng");
var sb = All<TextBox>().Where(t => t.Watermark == "0").ToList();
TypeBox(sb[0], "50"); TypeBox(sb[1], "80"); TypeBox(sb[2], "60"); TypeBox(sb[3], "30");
Click("Lưu đơn");
var order = x.Q.Orders().SingleOrDefault();
Ok(order is not null && order.TotalSets == 220, "đơn 220 bộ");
Shot("don_hang");
var oid = order!.Id;

// ---------------- 5. Kiểm tra đáp ứng trước nhập ----------------
Step("5. Kiểm tra đáp ứng (chưa có tồn)");
Go("Kiểm tra đáp ứng");
Ok(Shows("THIẾU NPL"), "báo thiếu khi kho trống");
Shot("kiem_tra_dap_ung_truoc_nhap");
var need = x.Stock.CheckOrder(oid, x.Orders).Lines.ToDictionary(l => l.MaterialCode, l => l.Demand);
Note("Nhu cầu: " + string.Join(", ", need.Select(kv => $"{kv.Key} {Ui.N(kv.Value)}")));

// ---------------- 6. Nhập kho ----------------
Step("6. Nhập kho theo hóa đơn NCC (nút thiếu 300 cái)");
Go("Phiếu nhập");
Pick(ComboWith("NCC-TT"), "NCC-TT");
Lines(("AP-VC", "175"), ("AP-VL", "90"), ("AP-CHI", "12"), ("AP-NUT", "1300"), ("AP-NHAN", "450"), ("AP-TUI", "220"));
// dòng vải chính: nhập 175 kg, lô thực tế 3,2 m/kg
Pick(All<ComboBox>().Where(cb => cb.Width == 90).First(), "kg");
TypeBox(All<TextBox>().First(t => t.Width == 100 && t.IsEnabled), "3,2");
Type("Số hóa đơn / chứng từ", "0001234");
Shot("phieu_nhap");
Click("Xác nhận nhập");
Ok(x.Stock.OnHand("AP-VC") == 560 && x.Stock.OnHand("AP-NUT") == 1300, "nhập + xác nhận: 175 kg × 3,2 = 560 m vải chính, tồn tăng");
Ok(x.Q.Docs("IN").First().InvoiceNo == "0001234", "lưu số hóa đơn");
Go("Kiểm tra đáp ứng"); Shot("kiem_tra_dap_ung_sau_nhap");
Note(Shows("THIẾU NPL") ? "Sau nhập: còn THIẾU (nút) – đúng dữ liệu" : "Sau nhập: ĐỦ");

// ---------------- 7. Cấp nhà cắt ----------------
Step("7. Cấp NPL cho nhà cắt – cả bộ, toàn đơn");
Go("Phiếu xuất (cấp phát)");
Pick(Place("CAT"), "CAT");
Click("Điền tối đa còn lại");
Shot("lap_phieu_cap_nha_cat");
Click("Tạo phiếu xuất nháp");
var catDoc = x.Q.Docs("OUT", oid).First(d => d.IssuePlace == "Nhà cắt");
var catLines = x.Q.DocLines(catDoc.Id);
Note("Đề xuất: " + string.Join(", ", catLines.Select(l => $"{l.Material} {Ui.N(l.Suggested ?? 0)}")));
// Thủ kho cấp tròn cây vải
Click("Thực cấp = đề xuất");
var act = All<TextBox>().Where(t => t.Watermark == "chưa nhập").ToList();
TypeBox(act[0], "550"); TypeBox(act[1], "90");   // cấp tròn cây vải
Shot("nhap_thuc_cap_nha_cat");
Click("Xác nhận xuất");
Ok(x.Q.Docs("OUT", oid).First(d => d.Id == catDoc.Id).Status == "CONFIRMED", "xác nhận phiếu cấp nhà cắt");
Note($"Số dư nhà cắt: VC {Ui.N(x.Stock.CarryClosing(oid, x.Cat.MaterialId("AP-VC"), IssueScopes.Place, catDoc.IssuePlaceId))}, VL {Ui.N(x.Stock.CarryClosing(oid, x.Cat.MaterialId("AP-VL"), IssueScopes.Place, catDoc.IssuePlaceId))}");
var pdf = Path.Combine(outDir, "phieu_cap_nha_cat.pdf"); using (var fs = File.Create(pdf)) VoucherPdf.Generate(x.C, catDoc.Id, fs); log.Add("- In phiếu: phieu_cap_nha_cat.pdf");

// ---------------- 8. Cấp các tổ may ----------------
Step("8. Cấp tổ may 1 (áo) và tổ may 2 (quần) – BOM Sản xuất, lẻ chủng loại");
Pick(Place("MAY1"), "MAY1");
Check("BOM Tổ cắt", false); Check("BOM Sản xuất", true);
Check("Cả bộ (tất cả)", false); Check("AO – Áo bảo hộ", true);
Click("Điền tối đa còn lại"); Click("Tạo phiếu xuất nháp");
var may1 = x.Q.Docs("OUT", oid).First(d => d.IssuePlace == "Tổ may 1");
act = All<TextBox>().Where(t => t.Watermark == "chưa nhập").ToList();
var may1Lines = x.Q.DocLines(may1.Id);
Note("Đề xuất tổ may 1: " + string.Join(", ", may1Lines.Select(l => $"{l.Material} {Ui.N(l.Suggested ?? 0)}")));
Click("Thực cấp = đề xuất");
act = All<TextBox>().Where(t => t.Watermark == "chưa nhập").ToList();
TypeBox(act[1], "1000");   // nút chỉ cấp được 1000
Shot("tom_may1_thuc_cap");
Click("Xác nhận xuất");
Ok(x.Q.Docs("OUT", oid).First(d => d.Id == may1.Id).Status == "CONFIRMED", "tổ may 1 xác nhận (nút cấp thiếu)");
Pick(Place("MAY2"), "MAY2");
Check("AO – Áo bảo hộ", false); Check("QUAN – Quần bảo hộ", true);
Click("Điền tối đa còn lại"); Click("Tạo phiếu xuất nháp");
var may2 = x.Q.Docs("OUT", oid).First(d => d.IssuePlace == "Tổ may 2");
Click("Thực cấp = đề xuất");
Click("Xác nhận xuất");
Ok(x.Q.Docs("OUT", oid).First(d => d.Id == may2.Id).Status == "CONFIRMED", "tổ may 2 xác nhận");
Note($"Tồn nút sau cấp: {Ui.N(x.Stock.OnHand("AP-NUT"))}");

// ---------------- 9. Nhập bổ sung + bù thiếu ----------------
Step("9. Nhập bổ sung nút, xuất bù thiếu cho tổ may 1");
Go("Phiếu nhập"); Pick(ComboWith("NCC-TT"), "NCC-TT"); Lines(("AP-NUT", "400")); Click("Xác nhận nhập");
Go("Việc cần làm");
var shortNut = -x.Stock.CarryClosing(oid, x.Cat.MaterialId("AP-NUT"), IssueScopes.Place, may1.IssuePlaceId);
Ok(Shows("Tổ may 1 đang THIẾU NPL"), $"trang Việc cần làm báo tổ may 1 thiếu {Ui.N(shortNut)} nút");
Shot("viec_can_lam");
Click("Bù phần thiếu");
Ok(All<TextBox>().Any(t => t.Text == Ui.N(shortNut)), "phiếu xuất bù đã điền sẵn số đang thiếu");
Click("Tạo phiếu xuất bù nháp"); Click("Xác nhận");
Ok(x.Stock.CarryClosing(oid, x.Cat.MaterialId("AP-NUT"), IssueScopes.Place, may1.IssuePlaceId) == 0, "bù thiếu xong, tổ may 1 cân đối nút");
Shot("xuat_bu");

// ---------------- 10. Bù hao hụt + trả dư ----------------
Step("10. Nhà cắt cắt hỏng (bù hao hụt 8 m) và trả vải dư");
Go("Trả NPL & xuất bù");
Pick(ComboWith("PX-0001"), "PX-0001"); Radio("Bù hao hụt vượt định mức");
var mcb = All<ComboBox>().Where(cb => cb.Width == 290).ToList();
Pick(mcb[1], "AP-VC"); TypeBox(All<TextBox>().Where(t => t.Width == 110 && t.Watermark == "0").ElementAt(1), "8");
Click("Tạo phiếu xuất bù nháp"); Click("Xác nhận");
var vcBal = x.Stock.CarryClosing(oid, x.Cat.MaterialId("AP-VC"), IssueScopes.Place, catDoc.IssuePlaceId);
Note($"Số dư vải chính nhà cắt: {Ui.N(vcBal)}");
Pick(ComboWith("Nhà cắt"), "Nhà cắt");
Click("Nạp phần đang dư của nơi nhận");
Click("Tạo phiếu trả nháp"); Click("Xác nhận");
Ok(x.Stock.CarryClosing(oid, x.Cat.MaterialId("AP-VC"), IssueScopes.Place, catDoc.IssuePlaceId) == 0 && x.Stock.CarryClosing(oid, x.Cat.MaterialId("AP-VL"), IssueScopes.Place, catDoc.IssuePlaceId) == 0,
   "nạp phần dư + trả: nhà cắt cân đối cả vải chính và vải lót");
Shot("tra_npl");

// ---------------- 11. Đóng gói ----------------
Step("11. Cấp đóng gói cho tổ hoàn thiện");
Go("Phiếu xuất (cấp phát)");
Pick(Place("HT"), "HT"); Check("BOM Tổ cắt", false); Check("BOM Đóng gói", true);
Click("Điền tối đa còn lại"); Click("Tạo phiếu xuất nháp");
var ht = x.Q.Docs("OUT", oid).First(d => d.IssuePlace == "Tổ hoàn thiện");
Click("Thực cấp = đề xuất");
Click("Xác nhận xuất");
Ok(x.Q.Docs("OUT", oid).First(d => d.Id == ht.Id).Status == "CONFIRMED", "cấp đóng gói");

// ---------------- 12. Kiểm kê ----------------
Step("12. Kiểm kê cuối tháng (chỉ hụt 1 cuộn)");
Go("Kiểm kê & điều chỉnh tồn");
Click("Nạp toàn bộ NPL của khách hàng (thực đếm = tồn sổ)");
var chiIdx = All<ComboBox>().Where(cb => cb.Width == 290).ToList().FindIndex(cb => (cb.SelectedItem?.ToString() ?? "").StartsWith("AP-CHI"));
var cnt = All<TextBox>().Where(t => t.Width == 110 && t.Watermark == "0").ToList();
TypeBox(cnt[chiIdx], Ui.N(x.Stock.OnHand("AP-CHI") - 1));
Type("Lý do kiểm kê", "Kiểm kê cuối tháng");
Click("Lưu phiếu kiểm kê (nháp)"); Click("Chốt kiểm kê");
Ok(x.Q.Counts().Single().Status == "CONFIRMED", "chốt kiểm kê, tạo điều chỉnh giảm chỉ");
Shot("kiem_ke");

// ---------------- 13. Quyết toán, đóng đơn ----------------
Step("13. Quyết toán & đóng đơn");
Go("Quyết toán & đóng đơn");
Shot("quyet_toan");
var st = x.Issue.Settlement(oid);
Note("Quyết toán: " + string.Join("; ", st.Select(s => $"{s.Place}/{s.Material} {s.Conclusion} {Ui.N(s.Balance)}")));
Click("Đóng đơn");
var closed = x.Q.Orders(status: null).Single().Status == "CLOSED";
Ok(closed, "đóng đơn");
if (!closed) Note("Lý do không đóng: " + (Dialog.LastMessage ?? w.StatusText));
if (!closed)
{
    Step("13b. Trả phần dư từ màn quyết toán rồi đóng đơn");
    Click("Trả phần dư về kho");
    Click("Tạo phiếu trả nháp"); Click("Xác nhận");
    Go("Quyết toán & đóng đơn"); Click("Đóng đơn");
    Ok(x.Q.Orders(status: null).Single().Status == "CLOSED", "đóng đơn sau khi trả dư");
}
Go("Đơn hàng"); actions++; All<CheckBox>().First(c => (c.Content as string) == "Chỉ đơn đang mở").IsChecked = false; Pump();
actions++; All<DataGrid>().First().SelectedIndex = 0; Pump();
Shot("don_hang_7_chang");
Ok(x.Flow.Stages(oid).All(st => st.State == "done"), "7 chặng của đơn đều hoàn tất");
Go("Việc cần làm"); Shot("viec_can_lam_cuoi");
Ok(x.Flow.Todos().Count == 0, "không còn việc tồn đọng");

// ---------------- 14. Báo cáo ----------------
Step("14. Báo cáo cuối kỳ");
Go("Nhập – xuất – tồn"); Shot("bao_cao_nxt");
Go("Thẻ kho"); Pick(ComboWith("AP-VC"), "AP-VC"); Shot("the_kho_vai_chinh");
Go("Tiến độ cấp phát theo size"); Shot("tien_do_cap");
Go("Nhật ký thay đổi"); Shot("nhat_ky");
Note($"Nhật ký ghi {x.Q.Audit().Count} sự kiện");
End();

var logs = Path.Combine(dataDir, "logs");
if (Directory.Exists(logs)) foreach (var f in Directory.GetFiles(logs)) log.Add("\nLỖI ỨNG DỤNG:\n" + File.ReadAllText(f));
File.WriteAllText(Path.Combine(outDir, "nhat_ky_dung_thu.md"), "# Nhật ký dùng thử\n" + string.Join("\n", log));
Console.WriteLine("XONG");
return 0;

static void Pump() { for (var k = 0; k < 5; k++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }

static class Ext
{
    public static List<string> GetTexts(this Visual v) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(v).OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
}
