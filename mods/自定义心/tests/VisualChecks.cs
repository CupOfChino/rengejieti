using System;
using System.IO;
using System.Reflection;

internal static class VisualChecks
{
    private static int _count;
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); _count++; }
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) => {
            string file = new AssemblyName(e.Name).Name + ".dll";
            foreach (string dir in new[] { args[1], args[2], Path.GetDirectoryName(args[0]) })
                if (File.Exists(Path.Combine(dir, file))) return Assembly.LoadFrom(Path.Combine(dir, file));
            return null;
        };
        Assembly plugin = Assembly.LoadFrom(args[0]);
        Type data = plugin.GetType("XinEditor.HeartVisualSettings", true);
        Type storeType = plugin.GetType("XinEditor.HeartVisualStore", true);
        BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        Func<object> create = () => Activator.CreateInstance(data);
        Func<object, string, float> read = (o, n) => (float)data.GetField(n).GetValue(o);
        Action<object, string, float> write = (o, n, f) => data.GetField(n).SetValue(o, f);
        Func<object, bool> valid = o => (bool)data.GetMethod("Valid").Invoke(null, new[] { o });
        object defaults = create();
        Check(Math.Abs(read(defaults, "ScaleX") - 0.30f) < 0.00001f && Math.Abs(read(defaults, "ScaleY") - 0.30f) < 0.00001f, "默认宽高0.30");
        Check(Math.Abs(read(defaults, "X") - 0.05f) < 0.00001f && Math.Abs(read(defaults, "Y") - 0.05f) < 0.00001f, "默认位置X0.05/Y0.05");
        Check(valid(defaults), "默认值有效");
        foreach (string field in new[] { "X", "Y", "ScaleX", "ScaleY" })
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1000f })
            {
                object value = create(); write(value, field, bad);
                Check(!valid(value), "拦截非法数值" + field);
            }
        object negative = create(); write(negative, "X", -0.5f); write(negative, "Y", -1.25f);
        Check(valid(negative), "支持负坐标");
        foreach (string field in new[] { "ScaleX", "ScaleY" })
        {
            object value = create(); write(value, field, 0f); Check(!valid(value), "不接受零缩放");
            write(value, field, -1f); Check(!valid(value), "不接受负缩放");
        }
        object clone = data.GetMethod("Copy").Invoke(defaults, null);
        write(clone, "ScaleX", 0.8f);
        Check(read(defaults, "ScaleX") == 0.30f, "草稿修改不污染默认或已保存对象");
        object[] parse = { "-0.25", "0.2", "0.3", "0.6", null };
        Check((bool)data.GetMethod("TryParse").Invoke(null, parse), "小数和独立宽高输入");
        Check(read(parse[4], "ScaleX") == 0.3f && read(parse[4], "ScaleY") == 0.6f, "只改高度不改变宽度");
        parse = new object[] { "abc", "0", "0.3", "0.3", null };
        Check(!(bool)data.GetMethod("TryParse").Invoke(null, parse), "无效输入拒绝保存");

        string path = args[3];
        Func<object> newStore = () => Activator.CreateInstance(storeType, hidden, null, new object[] { path }, null);
        object store = newStore();
        storeType.GetMethod("Load", hidden).Invoke(store, null);
        MethodInfo get = storeType.GetMethod("Get", hidden), save = storeType.GetMethod("Save", hidden);
        object a = get.Invoke(store, new object[] { "调查员_A" });
        write(a, "X", 0.72f); write(a, "ScaleY", 0.58f);
        Check(read(get.Invoke(store, new object[] { "调查员_A" }), "X") == 0.05f, "应用/关闭草稿不持久化");
        save.Invoke(store, new object[] { "调查员_A", a });
        write(a, "X", 0.9f);
        Check(Math.Abs(read(get.Invoke(store, new object[] { "调查员_A" }), "X") - 0.72f) < 0.00001f, "保存后继续改草稿不污染存储");
        Check(read(get.Invoke(store, new object[] { "调查员_B" }), "X") == 0.05f, "不同调查员独立");
        object restarted = newStore(); storeType.GetMethod("Load", hidden).Invoke(restarted, null);
        object loaded = get.Invoke(restarted, new object[] { "调查员_A" });
        Check(Math.Abs(read(loaded, "X") - 0.72f) < 0.00001f && Math.Abs(read(loaded, "ScaleY") - 0.58f) < 0.00001f, "重启读回小数和独立高度");
        Check(read(get.Invoke(restarted, new object[] { "调查员_B" }), "ScaleY") == 0.30f, "未保存调查员仍用默认");
        save.Invoke(restarted, new object[] { "调查员_A", create() });
        object reset = newStore(); storeType.GetMethod("Load", hidden).Invoke(reset, null);
        Check(read(get.Invoke(reset, new object[] { "调查员_A" }), "X") == 0.05f, "重置默认后保存可读回");
        Check(read(get.Invoke(reset, new object[] { null }), "ScaleX") == 0.30f, "默认心/无编号回退默认");
        // 幕间可能只有角色库外层有编号，角色数据自身字段为空。
        Assembly game = Assembly.LoadFrom(Path.Combine(args[1], "Assembly-CSharp.dll"));
        Type heroType = game.GetType("HeroRoleData", true), libraryType = game.GetType("RoleLibraryData", true);
        Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(libraryType);
        System.Collections.IList library = (System.Collections.IList)Activator.CreateInstance(listType);
        Func<Type, object> blank = t => System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
        object hero = blank(heroType), entry = blank(libraryType);
        libraryType.GetField("Key").SetValue(entry, "library_stable_key");
        libraryType.GetField("BaseData").SetValue(entry, hero);
        library.Add(entry);
        Type resolver = plugin.GetType("XinEditor.HeartVisualRoleKey", true);
        MethodInfo resolve = resolver.GetMethod("FromLibrary", BindingFlags.NonPublic | BindingFlags.Static);
        Check((string)resolve.Invoke(null, new object[] { hero, library }) == "library_stable_key", "幕间角色编号为空时取角色库编号");
        Check(heroType.GetField("RoleLibraryKey").GetValue(hero) == null, "编号回退不修改角色存档对象");
        heroType.GetField("RoleLibraryKey").SetValue(hero, "direct_role_key");
        Check((string)resolve.Invoke(null, new object[] { hero, library }) == "direct_role_key", "角色已有编号优先使用");
        Check((string)resolve.Invoke(null, new object[] { null, library }) == "", "空角色不串用编号");
        heroType.GetField("RoleLibraryKey").SetValue(hero, null);
        string recovered = (string)resolve.Invoke(null, new object[] { hero, library });
        save.Invoke(reset, new object[] { recovered, defaults });
        object afterRestart = newStore(); storeType.GetMethod("Load", hidden).Invoke(afterRestart, null);
        Check(read(get.Invoke(afterRestart, new object[] { "library_stable_key" }), "Y") == 0.05f,
            "幕间回退编号保存并重启读回");
        Console.WriteLine("通过 " + _count + " 项实际插件配置与草稿检查。");
    }
}
