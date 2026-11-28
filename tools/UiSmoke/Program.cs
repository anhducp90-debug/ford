// Chạy thử giao diện không cần màn hình: tạo dữ liệu mẫu, mở mọi màn hình, chụp ảnh, ghi lỗi.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using QuanLyNPL.App;
using QuanLyNPL.Core;
using QuanLyNPL.Data;

Console.Error.WriteLine("start");
var outDir = args.Length > 0 ? args[0] : "shots";
Directory.CreateDirectory(outDir);
var dataDir = Path.Combine(Path.GetTempPath(), "qlnpl_smoke_" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("QLNPL_DATA_DIR", dataDir);

AppBuilder.Configure<App>().UseSkia().WithInterFont()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

Console.WriteLine("setup ok");
var x = new Ctx(dataDir);
App.X = x;
Seed(x); Console.WriteLine("seed ok");
x.Pin.SetPin("1234");
var w = new MainWindow { Width = 1366, Height = 860 };
App.Main = w;
Console.WriteLine("window ok");
w.Show(); Console.WriteLine("show ok");
Pump(); Console.WriteLine("pump ok");
Shot(w, "00_khoa_pin"); Console.WriteLine("shot ok");
if (!w.TryUnlock("1234")) throw new Exception("Không mở khóa được");
Pump();

var errors = new List<string>();
for (var m = 0; m < MainWindow.Modules.Length; m++)
    for (var i = 0; i < MainWindow.Modules[m].Items.Length; i++)
    {
        w.Navigate(m, i);
        Pump();
        var name = $"{m + 1}{i:00}_{Slug(MainWindow.Modules[m].Items[i].Title)}";
        Shot(w, name);
        var texts = w.GetVisualDescendantsTexts();
        if (texts.Any(t => t.StartsWith("Không hiển thị được màn hình"))) errors.Add(name + ": " + texts.First(t => t.StartsWith("Không hiển thị")));
        Console.WriteLine("ok " + name);
    }
// ---------- Thao tác thật trên giao diện ----------
Dialog.AutoAnswer = true;
void Go(string title) { for (var m = 0; m < MainWindow.Modules.Length; m++) for (var i = 0; i < MainWindow.Modules[m].Items.Length; i++) if (MainWindow.Modules[m].Items[i].Title == title) { w.Navigate(m, i); Pump(); return; } throw new Exception("Không có màn " + title); }
IEnumerable<T> All<T>() => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<T>();
void Click(string text) { var b = All<Button>().FirstOrDefault(b => (b.Content as string) == text) ?? throw new Exception("Không thấy nút: " + text); b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump(); }
TextBox Box(string watermark, int nth = 0) => All<TextBox>().Where(t => t.Watermark == watermark).ElementAt(nth);
void Type(string watermark, string text, int nth = 0) { Box(watermark, nth).Text = text; Pump(); }
void Pick(ComboBox cb, string startsWith) { cb.SelectedItem = cb.Items.Cast<object>().First(o => o.ToString()!.StartsWith(startsWith)); Pump(); }
void Expect(bool cond, string what) { if (!cond) errors.Add("SAI: " + what + " | trạng thái: " + w.StatusText + " | hộp thoại: " + Dialog.LastMessage); else Console.WriteLine("đúng: " + what); }

Go("Khách hàng"); Type("Mã", "KH03"); Type("Tên khách hàng", "Thời trang An Phú"); Click("Lưu khách hàng");
Expect(x.Q.Customers().Any(c => c.Code == "KH03"), "thêm khách hàng");

Go("Nguyên phụ liệu"); Pick(All<ComboBox>().First(), "KH03"); Type("Mã NPL", "AP_VAI"); Type("Tên", "Vải An Phú"); Type("ĐVT", "m"); Click("Lưu NPL mới");
Expect(x.Q.Materials().Any(m => m.Code == "AP_VAI" && m.CustomerCode == "KH03"), "thêm NPL gắn khách hàng");
Type("Mã NPL", "AP_CHI"); Type("Tên", "Chỉ"); Type("ĐVT", "cuộn");
Pick(All<ComboBox>().First(), "KH03"); Click("Lưu NPL mới");

Go("Đơn hàng");
var sizeBoxes = All<TextBox>().Where(t => t.Watermark == "0").ToList();
sizeBoxes[0].Text = "10"; sizeBoxes[2].Text = "5"; Pump();
Click("Lưu đơn");
Expect(x.Q.Orders().Count == 2 && x.Q.Orders().First().TotalSets == 15, "lập đơn từ giao diện (15 bộ)");
Shot(w, "900_don_moi");

Go("BOM & định mức"); Click("Lưu phiên bản mới");
Expect(x.Q.BomVersions(x.Bom.Find(x.Q.Variants(x.Q.Products().First(p => p.Code == "MH-2204").Id)[0].Id, BomType.ToCat)!.Value).Count == 2, "lưu phiên bản BOM mới từ dòng chép sẵn");

Go("Phiếu nhập"); Click("Phiếu mới");
Pick(All<ComboBox>().First(), "KH01");
var ed = All<ComboBox>().ToList();  // [khách hàng, NPL dòng 1]
Pick(ed.First(cb => cb.Items.Cast<object>().Any(o => o.ToString()!.StartsWith("CHI"))), "CHI"); All<TextBox>().First(t => t.Watermark == "0").Text = "8"; Pump();
Click("Xác nhận nhập");
Expect(x.Stock.OnHand("CHI") == 12m, "nhập + xác nhận phiếu nhập (CHI 4 → 12)");

Go("Phiếu xuất (cấp phát)"); Pick(All<ComboBox>().First(), "DH-0412");
var actual = All<TextBox>().Where(t => t.Watermark == "chưa nhập").ToList();
foreach (var a in actual) a.Text = "0"; Pump();
actual[0].Text = "7,2"; Pump();
Click("Xác nhận xuất");
Expect(x.Stock.OnHand("CHI") == 4.8m, "xác nhận phiếu xuất (CHI 12 − 7,2)");
Shot(w, "901_sau_xuat");

Go("Kiểm tra đáp ứng"); Shot(w, "902_kiem_tra");
Go("Điều chỉnh đơn"); Type("Lý do điều chỉnh (bắt buộc)", "Khách tăng đơn"); Click("Xem chênh lệch"); Shot(w, "903_dieu_chinh");
Click("Lưu điều chỉnh");
Expect(x.Adjust.History(x.Q.Orders().Last().Id).Count >= 1, "điều chỉnh đơn");

Go("Chuyển NPL giữa khách hàng"); Shot(w, "904_chuyen");
Go("Kiểm kê & điều chỉnh tồn");
var cnt = x.Q.Counts().First(); 
x.Count.Confirm(cnt.Id);
Expect(x.Stock.OnHand("KHOA") == 498m, "chốt kiểm kê (KHOA 500 → 498)");
Go("Nhập – xuất – tồn"); Shot(w, "905_nxt");
Go("Lịch sử giao dịch"); Shot(w, "906_lich_su");

// ---------- Mẫu báo cáo chụp từ ứng dụng ----------
var repDir = Path.Combine(outDir, "mau_bao_cao"); Directory.CreateDirectory(repDir);
w.Height = 1000; Pump();
void RShot(string name) { var f = w.CaptureRenderedFrame(); f?.Save(Path.Combine(repDir, name + ".png")); Console.WriteLine("mẫu " + name); }
void Toggle(string content) { var cb = All<CheckBox>().First(c => (c.Content as string) == content); cb.IsChecked = true; Pump(); }
var n = 1;
string Num() => (n++).ToString("00");
Go("Tồn hiện tại"); RShot(Num() + "_ton_hien_tai");
Go("Nhập – xuất – tồn"); RShot(Num() + "_nhap_xuat_ton");
Go("Định mức một bộ"); RShot(Num() + "_dinh_muc_mot_bo"); Toggle("Chi tiết nguồn"); RShot(Num() + "_dinh_muc_mot_bo_chi_tiet");
Go("Nhu cầu theo đơn"); Pick(All<ComboBox>().First(), "DH-0412"); RShot(Num() + "_nhu_cau_theo_don"); Toggle("Chi tiết nguồn"); RShot(Num() + "_nhu_cau_chi_tiet_nguon");
Go("Cảnh báo thiếu"); Pick(All<ComboBox>().First(), "DH-0412"); RShot(Num() + "_canh_bao_thieu");
Go("Số bộ tối đa sản xuất được"); Pick(All<ComboBox>().First(), "DH-0412"); RShot(Num() + "_so_bo_toi_da_theo_co_cau_don"); Toggle("Riêng từng size"); RShot(Num() + "_so_bo_toi_da_tung_size");
Go("Đáp ứng đơn hàng"); RShot(Num() + "_dap_ung_don_hang");
Go("Lịch sử giao dịch"); RShot(Num() + "_lich_su_giao_dich");
Go("Lịch sử cấu hình"); Pick(All<ComboBox>().First(), "DH-0413"); RShot(Num() + "_lich_su_cau_hinh");
Go("Kiểm tra đáp ứng"); Pick(All<ComboBox>().First(), "DH-0412"); RShot(Num() + "_man_kiem_tra_dap_ung");
Go("Nơi xuất / Nơi nhận"); RShot(Num() + "_quan_tri_noi_xuat_noi_nhan");
Go("Phiếu xuất (cấp phát)"); Pick(All<ComboBox>().First(), "DH-0412"); RShot(Num() + "_phieu_xuat_noi_xuat_nhan");
foreach (var d in x.Q.Docs().Where(d => d.DocNo is "NK-0001" or "PX-0001"))
{
    var pdf = Path.Combine(repDir, $"phieu_{d.DocNo}.pdf");
    using (var fs = File.Create(pdf)) VoucherPdf.Generate(x.C, d.Id, fs);
    Console.WriteLine("phiếu " + pdf);
}
Go("Thẻ kho"); Pick(All<ComboBox>().First(cb => cb.Items.Cast<object>().Any(o => o.ToString()!.StartsWith("VAI_DO"))), "VAI_DO"); RShot(Num() + "_the_kho_VAI_DO");
var card = x.Reports.StockCard("VAI_DO", new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today);
Expect((decimal)card.Rows.Last()[5]! == x.Stock.OnHand("VAI_DO"), $"thẻ kho VAI_DO: tồn cuối {card.Rows.Last()[5]} = tồn thực tế {x.Stock.OnHand("VAI_DO")}");
Go("Lịch sử giao dịch"); RShot(Num() + "_lich_su_giao_dich_day_du");
Go("Nhật ký thay đổi"); RShot(Num() + "_nhat_ky_thay_doi");
Pick(All<ComboBox>().First(cb => cb.Items.Cast<object>().Any(o => o.ToString() == "Phiếu kho")), "Phiếu kho"); RShot(Num() + "_nhat_ky_loc_phieu_kho");
Expect(x.Q.Audit().Count > 40, $"nhật ký có {x.Q.Audit().Count} dòng");
// ---- Cấp phát theo nơi nhận ----
var dh = x.Q.Orders().First(o => o.No == "DH-0412").Id;
Go("Giao việc theo nơi nhận"); Pick(All<ComboBox>().First(), "DH-0412"); RShot(Num() + "_giao_viec_theo_noi_nhan");
Go("Điều chuyển NPL dư"); Pick(All<ComboBox>().First(), "DH-0412"); RShot(Num() + "_dieu_chuyen_du_chon");
var surplusGrid = All<DataGrid>().First(); surplusGrid.SelectedIndex = 0; Pump();
RShot(Num() + "_dieu_chuyen_du_form");
var before2 = x.Stock.CarryClosing(dh, x.Cat.MaterialId("VAI_DO"), IssueScopes.ToCat, x.Q.Places().First(p => p.Code == "TO-CAT-2").Id);
var surplus1 = x.Issue.Surpluses(dh).First();
Click("Điều chuyển");
Expect(x.Q.SurplusTransfers(dh).Count == 1, "điều chuyển dư từ giao diện (" + surplus1.Material + " " + surplus1.Surplus + " từ " + surplus1.Place + ")");
Go("Cân đối cấp phát"); RShot(Num() + "_can_doi_theo_noi_nhan");
Go("Quyết toán & đóng đơn"); Pick(All<ComboBox>().First(), "DH-0412"); RShot(Num() + "_quyet_toan_don");
Go("Hao hụt vượt định mức"); RShot(Num() + "_hao_hut_vuot_dinh_muc");
Go("Phiếu xuất (cấp phát)"); Pick(All<ComboBox>().First(), "DH-0412"); RShot(Num() + "_phieu_xuat_theo_noi_nhan");
Expect(x.Issue.Settlement(dh).Any(s => s.Loss == 3m && s.Material == "VAI_XANH"), "quyết toán ghi bù hao hụt VAI_XANH 3");
var mx = x.Reports.MaxSets(x.Q.Orders().First(o => o.No == "DH-0412").Id).Summary;
Expect(mx.Complete && mx.MaxSets > 0, $"số bộ tối đa DH-0412 = {mx.MaxSets}/{mx.OrderedSets}, giới hạn {string.Join(",", mx.Limiting)}");
Expect(x.Q.Docs().First(d => d.DocNo == "PX-0001").ToPlace == "Chuyền may 1", "phiếu xuất có nơi nhận");

// Khóa tự động
x.Pin.IdleMinutes = 1; w.ResetIdle();
Expect(!w.IsLocked, "đang mở khóa");

var logDir = Path.Combine(dataDir, "logs");
if (Directory.Exists(logDir)) foreach (var f in Directory.GetFiles(logDir)) errors.Add(File.ReadAllText(f));
Console.WriteLine(errors.Count == 0 ? "KHÔNG CÓ LỖI" : "LỖI:\n" + string.Join("\n", errors));
return errors.Count == 0 ? 0 : 1;

static void Pump() { for (var k = 0; k < 5; k++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
static string Slug(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
void Shot(Window win, string name)
{
    var frame = win.CaptureRenderedFrame();
    frame?.Save(Path.Combine(outDir, name + ".png"));
}

static void Seed(Ctx x)
{
    var k1 = x.Cat.AddCustomer("KH01", "Công ty Hoàng Long");
    var k2 = x.Cat.AddCustomer("KH02", "May xuất khẩu Đại Phát");
    foreach (var (c, n, u) in new[] { ("VAI_DO", "Vải đỏ", "m"), ("VAI_XANH", "Vải xanh", "m"), ("VAI_DEN", "Vải đen", "m"), ("CHI", "Chỉ may", "cuộn"), ("NUT", "Nút", "cái"), ("KHOA", "Khóa kéo", "cái"), ("NHAN", "Nhãn chính", "cái"), ("TUI", "Túi đựng bộ", "cái") })
        x.Cat.AddMaterial(c, n, u, k1);
    x.Cat.AddMaterial("KH2_VAI_DO", "Vải đỏ (mã KH02)", "m", k2);
    var g = x.Cat.AddGrid("G_VOC", "Vóc/Chiều cao", new[] { ("110/56", "110", "56"), ("110/60", "110", "60"), ("120/60", "120", "60"), ("130/64", "130", "64") }.Select(t => (t.Item1, (string?)t.Item2, (string?)t.Item3)));
    var gs = x.Cat.AddGrid("G_SML", "S M L XL", new[] { "S", "M", "L", "XL" }.Select(s => (s, (string?)null, (string?)null)));
    var p = x.Cat.AddProduct(k1, "MH-2204", "Bộ đồng phục", g);
    x.Cat.AddProduct(k2, "MH-1180", "Áo khoác", gs);
    var ao = x.Cat.AddVariant(p, "AO_DO", "Áo đỏ", "Áo", 1);
    var ax = x.Cat.AddVariant(p, "AO_XANH", "Áo xanh", "Áo", 1);
    var qd = x.Cat.AddVariant(p, "QUAN_DEN", "Quần đen", "Quần", 1);
    var d = new DateTime(2026, 1, 5);
    x.Bom.AddVersion(x.Bom.CreateBom(ao, BomType.ToCat), d, new[] { new BomLineInput("VAI_DO", "*", 1.2m, 3), new BomLineInput("VAI_DO", "120/60", 1.3m, 3), new BomLineInput("VAI_DO", "130/64", 1.4m, 3) });
    x.Bom.AddVersion(x.Bom.CreateBom(ax, BomType.ToCat), d, new[] { new BomLineInput("VAI_XANH", "*", 1.2m, 3), new BomLineInput("VAI_XANH", "120/60", 1.3m, 3), new BomLineInput("VAI_XANH", "130/64", 1.4m, 3) });
    x.Bom.AddVersion(x.Bom.CreateBom(qd, BomType.ToCat), d, new[] { new BomLineInput("VAI_DEN", "*", 1m, 3), new BomLineInput("VAI_DEN", "110/56", 0.95m, 3), new BomLineInput("VAI_DEN", "130/64", 1.15m, 3) });
    x.Bom.AddVersion(x.Bom.CreateBom(ao, BomType.SanXuat), d, new[] { new BomLineInput("CHI", "*", 0.02m), new BomLineInput("NUT", "*", 5m) });
    x.Bom.AddVersion(x.Bom.CreateBom(ax, BomType.SanXuat), d, new[] { new BomLineInput("CHI", "*", 0.02m), new BomLineInput("NUT", "*", 5m) });
    x.Bom.AddVersion(x.Bom.CreateBom(qd, BomType.SanXuat), d, new[] { new BomLineInput("CHI", "*", 0.02m), new BomLineInput("KHOA", "*", 1m) });
    x.Bom.AddVersion(x.Bom.CreateSetBom(p, BomType.DongGoi), d, new[] { new BomLineInput("NHAN", "*", 3m), new BomLineInput("TUI", "*", 1m) });
    var kho = x.Places.Add("KHO", "Kho NPL – Xưởng 1", PlaceKind.CaHai, "Lô B2, KCN Phố Nối A");
    var ncc = x.Places.Add("NCC-PA", "Công ty Dệt Phúc An", PlaceKind.Xuat);
    var toCat = x.Places.Add("TO-CAT", "Tổ cắt 1", PlaceKind.CaHai);
    var toCat2 = x.Places.Add("TO-CAT-2", "Tổ cắt 2", PlaceKind.CaHai);
    var chuyen = x.Places.Add("CHUYEN-1", "Chuyền may 1", PlaceKind.CaHai);
    var ht = x.Places.Add("HT", "Tổ hoàn thiện", PlaceKind.CaHai);
    x.Places.DefaultInTo = kho; x.Places.DefaultOutFrom = kho;
    x.Company.Name = "CÔNG TY TNHH MAY MẶC HOÀNG LONG"; x.Company.Address = "Lô B2, KCN Phố Nối A, Hưng Yên";
    var o = x.Orders.CreateOrder(p, "DH-0412", new DateTime(2026, 4, 2), new[] { new OrderLine("110/56", 40), new OrderLine("110/60", 30), new OrderLine("120/60", 30), new OrderLine("130/64", 20) });
    var nk1 = x.Stock.CreateDraft("IN", "NK-0001", new[] { ("VAI_DO", 180m), ("VAI_XANH", 150m), ("VAI_DEN", 160m), ("CHI", 4m), ("NUT", 800m), ("KHOA", 500m), ("NHAN", 300m), ("TUI", 150m) }, note: "Nhập theo hóa đơn 0012345");
    x.Places.SetDocPlaces(nk1, ncc, kho);
    x.Stock.Confirm(nk1);
    x.Stock.CreateDraft("IN", "NK-0002", new[] { ("CHI", 12m), ("NUT", 600m) });
    // Giao việc theo nơi nhận: 2 tổ cắt chia size, chuyền may phần sản xuất, tổ hoàn thiện phần đóng gói
    x.Issue.SetAssignment(o, toCat, IssueScopes.ToCat, new[] { new OrderLine("110/56", 40), new OrderLine("110/60", 30) });
    x.Issue.SetAssignment(o, toCat2, IssueScopes.ToCat, new[] { new OrderLine("120/60", 30), new OrderLine("130/64", 20) });
    x.Issue.SetAssignment(o, chuyen, IssueScopes.SanXuat, x.Orders.LoadLines(o));
    x.Issue.SetAssignment(o, ht, IssueScopes.DongGoi, x.Orders.LoadLines(o));
    var px = x.Issue.CreateIssueDraft(o, chuyen, IssueScopes.SanXuat, "PX-0001");    // nháp cho chuyền may (thao tác ở giao diện)
    x.Places.SetDocPlaces(px, kho, chuyen);
    var pxc = x.Issue.CreateIssueDraft(o, toCat, IssueScopes.ToCat, "PX-0002");      // tổ cắt 1: cấp tròn cây vải → dư
    x.Places.SetDocPlaces(pxc, kho, toCat);
    foreach (var l in x.Q.DocLines(pxc)) x.Issue.SetActual(pxc, l.Material, Math.Ceiling(l.Suggested ?? 0) + (l.Material == "VAI_DO" ? 2 : 0));
    x.Stock.Confirm(pxc);
    var bu = x.Issue.CreateSupplement(pxc, "XB-0001", new[] { ("VAI_XANH", 3m) }, SupplementKind.BuHaoHut);   // cắt hỏng
    x.Stock.Confirm(bu);
    x.Transfer.Confirm(x.Transfer.Create("CH-0001", k1, k2, new[] { new TransferLine("VAI_DO", "KH2_VAI_DO", 20m) }, "Mượn vải"));
    x.Count.Create("KK-0001", new[] { ("KHOA", 498m), ("NHAN", 300m) }, "Kiểm kê tháng");
}

static class Ext
{
    public static List<string> GetVisualDescendantsTexts(this Visual v) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(v).OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
}
