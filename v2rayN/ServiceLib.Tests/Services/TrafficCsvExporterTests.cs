namespace ServiceLib.Tests.Services;

/// <summary>
/// 流量数据导出 CSV。
///
/// 关注三件事：
///   1. 字节数是权威字段，不能只有 "1.2 MB" 这种四舍五入值；
///   2. 含逗号 / 引号 / 换行的字段要正确转义，否则 Excel 会错列；
///   3. 行顺序与输入一致，便于和界面截图对照。
/// </summary>
public class TrafficCsvExporterTests
{
    [Test]
    public async Task Build_ShouldEmitHeaderAndBothByteColumns()
    {
        var csv = TrafficCsvExporter.Build(
        [
            new TrafficCsvExporter.Row("2026-10-23", 2048, 8192),
        ], "date");

        var lines = csv.TrimEnd('\n').Split('\n');
        await lines.Length.Should().BeEqualTo(2);
        await lines[0].Should().BeEqualTo("date,down_bytes,up_bytes,down,up");
        await lines[1].Should().BeEqualTo("2026-10-23,8192,2048,8.0 KB,2.0 KB");
    }

    [Test]
    public async Task Build_ZeroTraffic_ShouldStillEmitRow()
    {
        var csv = TrafficCsvExporter.Build([new TrafficCsvExporter.Row("2026-10-22", 0, 0)], "date");
        await csv.Should().Contain("2026-10-22,0,0,0 B,0 B");
    }

    [Test]
    public async Task Escape_CommaQuoteAndNewline_ShouldBeQuoted()
    {
        await TrafficCsvExporter.Escape("a,b").Should().BeEqualTo("\"a,b\"");
        await TrafficCsvExporter.Escape("say \"hi\"").Should().BeEqualTo("\"say \"\"hi\"\"\"");
        await TrafficCsvExporter.Escape("line1\nline2").Should().BeEqualTo("\"line1\nline2\"");
    }

    [Test]
    public async Task Escape_PlainValue_ShouldStayUntouched()
    {
        await TrafficCsvExporter.Escape("www.youtube.com").Should().BeEqualTo("www.youtube.com");
        await TrafficCsvExporter.Escape("").Should().BeEqualTo(string.Empty);
    }

    [Test]
    public async Task Build_ShouldKeepInputOrder()
    {
        var csv = TrafficCsvExporter.Build(
        [
            new TrafficCsvExporter.Row("b", 1, 2),
            new TrafficCsvExporter.Row("a", 3, 4),
        ], "name");

        var idxA = csv.IndexOf("a,4,3", StringComparison.Ordinal);
        var idxB = csv.IndexOf("b,2,1", StringComparison.Ordinal);
        await (idxB < idxA).Should().BeTrue();
    }
}
