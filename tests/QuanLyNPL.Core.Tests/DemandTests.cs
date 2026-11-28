using QuanLyNPL.Core;
using Xunit;

public class DemandTests
{
    static readonly VariantConfig[] V = { new("AO_DO", 1), new("AO_XANH", 1), new("QUAN_DEN", 1) };

    // Size mặc định (Vóc/Cao) -> S/M/L; nhiều size mặc định cùng về 1 size đích.
    static SizeMap Map() => new SizeMap()
        .Add("AO_DO", "110/56", "S").Add("AO_XANH", "110/56", "S").Add("QUAN_DEN", "110/56", "S")
        .Add("AO_DO", "110/60", "S").Add("AO_XANH", "110/60", "S").Add("QUAN_DEN", "110/60", "M");

    [Fact]
    public void Breakdown_100Sets_GivesEachVariant()
    {
        var (rows, issues) = DemandCalculator.Breakdown(new[] { new OrderLine("110/56", 100) }, V, Map());
        Assert.Empty(issues);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(100m, r.Qty));
    }

    [Fact]
    public void Breakdown_ManyDefaultSizes_MergeIntoSameTargetSize()
    {
        var (rows, _) = DemandCalculator.Breakdown(
            new[] { new OrderLine("110/56", 10), new OrderLine("110/60", 5) }, V, Map());
        Assert.Equal(15m, rows.Single(r => r.Variant == "AO_DO" && r.VariantSize == "S").Qty);
        Assert.Equal(10m, rows.Single(r => r.Variant == "QUAN_DEN" && r.VariantSize == "S").Qty);
        Assert.Equal(5m, rows.Single(r => r.Variant == "QUAN_DEN" && r.VariantSize == "M").Qty);
    }

    [Fact]
    public void QtyPerSet_Two_Doubles()
    {
        var (rows, _) = DemandCalculator.Breakdown(new[] { new OrderLine("110/56", 10) },
            new[] { new VariantConfig("AO_DO", 2) }, Map());
        Assert.Equal(20m, rows.Single().Qty);
    }

    [Fact]
    public void MissingSizeMap_IsIssue_AndNeverComplete()
    {
        var r = DemandCalculator.Calculate(new[] { new OrderLine("999", 1) }, V, Map(),
            new[] { new Bom("B1", null) }, new[] { new BomLine("B1", "VAI", "*", 1) });
        Assert.Contains(r.Issues, i => i.Kind == IssueKind.MissingSizeMap);
        Assert.False(r.IsComplete);
    }

    [Fact]
    public void SpecificSize_OverridesBase_NotAdded()
    {
        var lines = new[] {
            new BomLine("B1", "VAI", "*", 1.0m), new BomLine("B1", "VAI", "M", 1.5m) };
        var r = DemandCalculator.Calculate(new[] { new OrderLine("110/60", 10) },
            new[] { new VariantConfig("QUAN_DEN", 1) }, Map(), new[] { new Bom("B1", null) }, lines);
        Assert.Equal(15m, r.TotalByMaterial["VAI"]);   // size M ghi đè nền: 10 × 1.5
    }

    [Fact]
    public void Waste_Applied()
    {
        var r = DemandCalculator.Calculate(new[] { new OrderLine("110/56", 100) },
            new[] { new VariantConfig("AO_DO", 1) }, Map(), new[] { new Bom("B1", null) },
            new[] { new BomLine("B1", "VAI", "*", 1.2m, 5m) });
        Assert.Equal(126m, r.TotalByMaterial["VAI"]);  // 100 × 1.2 × 1.05
    }

    [Fact]
    public void SameMaterial_InTwoBoms_IsSummedOnce_NoDoubleCount()
    {
        var boms = new[] { new Bom("CAT", null), new Bom("DONGGOI", null) };
        var lines = new[] { new BomLine("CAT", "CHI", "*", 2), new BomLine("DONGGOI", "CHI", "*", 1) };
        var r = DemandCalculator.Calculate(new[] { new OrderLine("110/56", 10) },
            new[] { new VariantConfig("AO_DO", 1) }, Map(), boms, lines);
        Assert.Equal(30m, r.TotalByMaterial["CHI"]);
        Assert.Equal(2, r.Sources.Count);
    }

    [Fact]
    public void ScopedBom_OnlyAppliesToItsVariant()
    {
        var boms = new[] { new Bom("BQ", "QUAN_DEN") };
        var r = DemandCalculator.Calculate(new[] { new OrderLine("110/56", 10) }, V, Map(), boms,
            new[] { new BomLine("BQ", "KHOA", "*", 1) });
        Assert.Equal(10m, r.TotalByMaterial["KHOA"]);
        Assert.Contains(r.Issues, i => i.Kind == IssueKind.MissingBom); // áo chưa có định mức
    }

    [Fact]
    public void Rounding_TwoDecimals_HalfUp_OnTotalOnly()
    {
        // 3 dòng × 0.335 = 1.005 -> 1.01 (làm tròn tổng, không làm tròn từng dòng)
        var r = DemandCalculator.Calculate(new[] { new OrderLine("110/56", 1) },
            new[] { new VariantConfig("AO_DO", 3) }, Map(), new[] { new Bom("B", null) },
            new[] { new BomLine("B", "X", "*", 0.335m) });
        Assert.Equal(1.01m, r.TotalByMaterial["X"]);
    }

    [Fact]
    public void NegativeInputs_AreIssues()
    {
        var r = DemandCalculator.Calculate(new[] { new OrderLine("110/56", -1) }, V, Map(),
            Array.Empty<Bom>(), Array.Empty<BomLine>());
        Assert.Contains(r.Issues, i => i.Kind == IssueKind.InvalidData);
    }
}

public class StockTests
{
    static DemandResult Demand(bool complete, params (string m, decimal q)[] t) =>
        new(Array.Empty<BreakdownRow>(), Array.Empty<DemandSource>(),
            t.ToDictionary(x => x.m, x => x.q),
            complete ? new List<Issue>() : new List<Issue> { new(IssueKind.MissingBom, "x") });

    [Fact] public void Enough() =>
        Assert.Equal(FulfillStatus.Enough, Stock.Check(Demand(true, ("A", 10)), new[] { new StockPosition("A", 15, 5) }).Status);

    [Fact] public void Short_UsesAvailable_NotOnHand()
    {
        var r = Stock.Check(Demand(true, ("A", 10)), new[] { new StockPosition("A", 15, 8) });
        Assert.Equal(FulfillStatus.Short, r.Status);
        Assert.Equal(3m, r.Lines.Single().Shortage);
    }

    [Fact] public void NoStockRow_IsShort() =>
        Assert.Equal(10m, Stock.Check(Demand(true, ("A", 10)), Array.Empty<StockPosition>()).Lines.Single().Shortage);

    [Fact] public void IncompleteData_NeverEnough() =>
        Assert.Equal(FulfillStatus.Incomplete, Stock.Check(Demand(false, ("A", 1)), new[] { new StockPosition("A", 999, 0) }).Status);
}

public class BomAndCarryTests
{
    [Fact]
    public void Version_PicksLatestNotAfterOrderDate()
    {
        var v = new[] { new BomVersion("B", 1, new(2026, 1, 1)), new BomVersion("B", 2, new(2026, 3, 1)), new BomVersion("B", 3, new(2026, 6, 1)) };
        Assert.Equal(2, BomVersioning.Pick(v, new(2026, 4, 1))!.VersionNo);
        Assert.Null(BomVersioning.Pick(v, new(2025, 12, 31)));
    }

    [Fact]
    public void Carry_UsesLatestClosing_NotSum()
    {
        var h = new[] { new CarryEntry(0, 100, 90, new(2026, 1, 1)),        // closing +10
                        new CarryEntry(10, 50, 70, new(2026, 2, 1)) };      // closing -10
        Assert.Equal(-10m, Carry.CurrentBalance(h));
        Assert.Equal(80m, Carry.NetDemand(70, -10));   // cấp thiếu cộng vào nhu cầu
        Assert.Equal(60m, Carry.NetDemand(70, 10));    // cấp thừa giảm nhu cầu
        Assert.Equal(0m, Carry.NetDemand(5, 10));
    }
}

public class PerSetBomTests
{
    static readonly VariantConfig[] V = { new("AO", 1), new("QUAN", 1) };
    static SizeMap Map() => new SizeMap().Add("AO", "110/56", "S").Add("QUAN", "110/56", "S").Add("AO", "120/60", "M").Add("QUAN", "120/60", "M");

    [Fact]
    public void PerSetBom_UsesSetsAndDefaultSize_WithOverride()
    {
        var boms = new[] { new Bom("DG", null, PerSet: true, Name: "Đóng gói") };
        var lines = new[] { new BomLine("DG", "TUI", "*", 1m), new BomLine("DG", "TUI", "120/60", 2m) };
        var r = DemandCalculator.Calculate(new[] { new OrderLine("110/56", 10), new OrderLine("120/60", 5) }, V, Map(), boms, lines);
        Assert.True(r.IsComplete);
        Assert.Equal(20m, r.TotalByMaterial["TUI"]);   // 10 bộ × 1 + 5 bộ × 2 (không nhân theo số chủng loại)
        Assert.All(r.Sources, s => Assert.Equal(DemandCalculator.SetScope, s.Variant));
        Assert.Contains(r.Sources, s => s.VariantSize == "120/60" && s.Qty == 10m);
    }

    [Fact]
    public void PerSetAndPerVariant_Add_And_SetCoversVariantRows()
    {
        var boms = new[] { new Bom("CAT_BO", null, true), new Bom("SX_AO", "AO") };
        var lines = new[] { new BomLine("CAT_BO", "VAI", "*", 2.5m), new BomLine("SX_AO", "NUT", "*", 5m) };
        var r = DemandCalculator.Calculate(new[] { new OrderLine("110/56", 10) }, V, Map(), boms, lines);
        Assert.True(r.IsComplete);                     // QUAN không có BOM riêng nhưng đã có BOM theo bộ
        Assert.Equal(25m, r.TotalByMaterial["VAI"]);
        Assert.Equal(50m, r.TotalByMaterial["NUT"]);
    }

    [Fact]
    public void PerSetBom_MissingSize_IsIncomplete()
    {
        var boms = new[] { new Bom("DG", null, true, "Đóng gói") };
        var r = DemandCalculator.Calculate(new[] { new OrderLine("110/56", 1), new OrderLine("120/60", 1) }, V, Map(), boms,
            new[] { new BomLine("DG", "TUI", "110/56", 1m) });
        Assert.Contains(r.Issues, i => i.Kind == IssueKind.MissingBom && i.Message.Contains("120/60"));
    }

    [Fact]
    public void VariantBom_MissingSize_IsIncomplete_EvenIfOtherBomsCover()
    {
        var boms = new[] { new Bom("CAT_AO", "AO", Name: "AO – Tổ cắt"), new Bom("SX_AO", "AO", Name: "AO – Sản xuất"), new Bom("CAT_Q", "QUAN") };
        var lines = new[] { new BomLine("CAT_AO", "VAI", "*", 1m), new BomLine("SX_AO", "NUT", "M", 5m), new BomLine("CAT_Q", "VAI", "*", 1m) };
        var r = DemandCalculator.Calculate(new[] { new OrderLine("110/56", 1) }, V, Map(), boms, lines);
        Assert.Contains(r.Issues, i => i.Message.Contains("AO – Sản xuất") && i.Message.Contains("size S"));
    }
}
