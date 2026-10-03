using System;
using System.Collections.Generic;
using System.Linq;

namespace ZoneQuanta.Core.Time;

public sealed record City(string Name, string Country, string TimeZoneId, double Lat, double Lon)
{
    public string Display => $"{Name} · {Country}";
}

public static class CityCatalog
{
    public static IReadOnlyList<City> Popular { get; } = Build();

    private static IReadOnlyList<City> Build()
    {
        var all = new City[]
        {
            new("洛杉矶", "美国", "Pacific Standard Time", 34.05, -118.24),
            new("旧金山", "美国", "Pacific Standard Time", 37.77, -122.42),
            new("西雅图", "美国", "Pacific Standard Time", 47.61, -122.33),
            new("拉斯维加斯", "美国", "Pacific Standard Time", 36.17, -115.14),
            new("丹佛", "美国", "Mountain Standard Time", 39.74, -104.99),
            new("芝加哥", "美国", "Central Standard Time", 41.88, -87.63),
            new("纽约", "美国", "Eastern Standard Time", 40.71, -74.01),
            new("华盛顿", "美国", "Eastern Standard Time", 38.91, -77.04),
            new("檀香山", "美国", "Hawaiian Standard Time", 21.31, -157.86),
            new("安克雷奇", "美国", "Alaskan Standard Time", 61.22, -149.9),
            new("温哥华", "加拿大", "Pacific Standard Time", 49.28, -123.12),
            new("多伦多", "加拿大", "Eastern Standard Time", 43.65, -79.38),
            new("墨西哥城", "墨西哥", "Central Standard Time (Mexico)", 19.43, -99.13),
            new("圣保罗", "巴西", "E. South America Standard Time", -23.55, -46.63),
            new("布宜诺斯艾利斯", "阿根廷", "Argentina Standard Time", -34.6, -58.38),
            new("北京", "中国", "China Standard Time", 39.9, 116.41),
            new("上海", "中国", "China Standard Time", 31.23, 121.47),
            new("香港", "中国", "China Standard Time", 22.32, 114.17),
            new("台北", "中国", "Taipei Standard Time", 25.03, 121.57),
            new("东京", "日本", "Tokyo Standard Time", 35.68, 139.69),
            new("首尔", "韩国", "Korea Standard Time", 37.57, 126.98),
            new("新加坡", "新加坡", "Singapore Standard Time", 1.35, 103.82),
            new("曼谷", "泰国", "SE Asia Standard Time", 13.76, 100.5),
            new("新德里", "印度", "India Standard Time", 28.61, 77.21),
            new("迪拜", "阿联酋", "Arabian Standard Time", 25.2, 55.27),
            new("莫斯科", "俄罗斯", "Russian Standard Time", 55.76, 37.62),
            new("伊斯坦布尔", "土耳其", "Turkey Standard Time", 41.01, 28.98),
            new("伦敦", "英国", "GMT Standard Time", 51.51, -0.13),
            new("巴黎", "法国", "Romance Standard Time", 48.86, 2.35),
            new("马德里", "西班牙", "Romance Standard Time", 40.42, -3.7),
            new("柏林", "德国", "W. Europe Standard Time", 52.52, 13.4),
            new("罗马", "意大利", "W. Europe Standard Time", 41.9, 12.5),
            new("阿姆斯特丹", "荷兰", "W. Europe Standard Time", 52.37, 4.9),
            new("开罗", "埃及", "Egypt Standard Time", 30.04, 31.24),
            new("约翰内斯堡", "南非", "South Africa Standard Time", -26.2, 28.05),
            new("悉尼", "澳大利亚", "AUS Eastern Standard Time", -33.87, 151.21),
            new("奥克兰", "新西兰", "New Zealand Standard Time", -36.85, 174.76),
        };
        return all.Where(c => Exists(c.TimeZoneId)).ToList();
    }

    public static (double Lat, double Lon)? Locate(string label, string timeZoneId)
    {
        var byName = Popular.FirstOrDefault(c => c.Name == label && c.TimeZoneId == timeZoneId);
        if (byName is not null) return (byName.Lat, byName.Lon);
        var byZone = Popular.FirstOrDefault(c => c.TimeZoneId == timeZoneId);
        return byZone is null ? null : (byZone.Lat, byZone.Lon);
    }

    private static bool Exists(string id)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }
}
