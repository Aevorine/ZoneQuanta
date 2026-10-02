using System;
using System.Collections.Generic;
using System.Linq;

namespace ZoneQuanta.Core.Time;

public sealed record City(string Name, string Country, string TimeZoneId)
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
            new("洛杉矶", "美国", "Pacific Standard Time"),
            new("旧金山", "美国", "Pacific Standard Time"),
            new("西雅图", "美国", "Pacific Standard Time"),
            new("拉斯维加斯", "美国", "Pacific Standard Time"),
            new("丹佛", "美国", "Mountain Standard Time"),
            new("芝加哥", "美国", "Central Standard Time"),
            new("纽约", "美国", "Eastern Standard Time"),
            new("华盛顿", "美国", "Eastern Standard Time"),
            new("檀香山", "美国", "Hawaiian Standard Time"),
            new("安克雷奇", "美国", "Alaskan Standard Time"),
            new("温哥华", "加拿大", "Pacific Standard Time"),
            new("多伦多", "加拿大", "Eastern Standard Time"),
            new("墨西哥城", "墨西哥", "Central Standard Time (Mexico)"),
            new("圣保罗", "巴西", "E. South America Standard Time"),
            new("布宜诺斯艾利斯", "阿根廷", "Argentina Standard Time"),
            new("北京", "中国", "China Standard Time"),
            new("上海", "中国", "China Standard Time"),
            new("香港", "中国", "China Standard Time"),
            new("台北", "中国", "Taipei Standard Time"),
            new("东京", "日本", "Tokyo Standard Time"),
            new("首尔", "韩国", "Korea Standard Time"),
            new("新加坡", "新加坡", "Singapore Standard Time"),
            new("曼谷", "泰国", "SE Asia Standard Time"),
            new("新德里", "印度", "India Standard Time"),
            new("迪拜", "阿联酋", "Arabian Standard Time"),
            new("莫斯科", "俄罗斯", "Russian Standard Time"),
            new("伊斯坦布尔", "土耳其", "Turkey Standard Time"),
            new("伦敦", "英国", "GMT Standard Time"),
            new("巴黎", "法国", "Romance Standard Time"),
            new("马德里", "西班牙", "Romance Standard Time"),
            new("柏林", "德国", "W. Europe Standard Time"),
            new("罗马", "意大利", "W. Europe Standard Time"),
            new("阿姆斯特丹", "荷兰", "W. Europe Standard Time"),
            new("开罗", "埃及", "Egypt Standard Time"),
            new("约翰内斯堡", "南非", "South Africa Standard Time"),
            new("悉尼", "澳大利亚", "AUS Eastern Standard Time"),
            new("奥克兰", "新西兰", "New Zealand Standard Time"),
        };
        return all.Where(c => Exists(c.TimeZoneId)).ToList();
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
