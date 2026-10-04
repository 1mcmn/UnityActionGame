using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class NativeMenuSave
{
    public string id;
    public string name;
    public int art;
    public string createdAt;
}

/// <summary>菜单记录仓库；不将名称记录冒充玩家位置、敌人或战斗进度快照。</summary>
public sealed class NativeMenuSaves
{
    [Serializable] private sealed class FileData
    {
        public int version = 1;
        public List<NativeMenuSave> records = new List<NativeMenuSave>();
    }
    public readonly List<NativeMenuSave> Records = new List<NativeMenuSave>();
    public string Status { get; private set; } = "本机菜单记录";
    public string FilePath { get; }
    private bool writable = true;

    public NativeMenuSaves(string directory = null)
    {
        FilePath = Path.Combine(directory ?? Path.Combine(Application.persistentDataPath, "Menu"), "menu-slots-v1.json");
        if (!File.Exists(FilePath)) return;
        try
        {
            var data = JsonUtility.FromJson<FileData>(File.ReadAllText(FilePath));
            if (data == null || data.version != 1 || data.records == null) throw new InvalidDataException("版本或记录无效");
            var ids = new HashSet<string>();
            foreach (var item in data.records)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || string.IsNullOrWhiteSpace(item.name) ||
                    item.name.Length > 24 || item.art < 0 || item.art > 2 ||
                    !DateTime.TryParse(item.createdAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) || !ids.Add(item.id))
                    throw new InvalidDataException("记录损坏");
                Records.Add(item);
            }
        }
        catch (Exception error)
        {
            Records.Clear(); writable = false;
            Status = "记录无法读取 · 仅本次会话";
            Debug.LogWarning("[原生菜单] 保留无法读取的原文件：" + error.Message);
        }
    }

    public NativeMenuSave Create(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > 24) throw new ArgumentException("请输入 1～24 个字符的名称。");
        var record = new NativeMenuSave { id = Guid.NewGuid().ToString("N"), name = name,
            art = Records.Count % 3, createdAt = DateTime.UtcNow.ToString("O") };
        Records.Add(record);
        try
        {
            if (!writable) throw new IOException("存储不可用");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(new FileData { records = Records }, true));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
            Status = "已创建 · 已保存";
        }
        catch (Exception error)
        {
            Status = "已创建 · 仅本次会话";
            Debug.LogWarning("[原生菜单] 名称记录未写入磁盘：" + error.Message);
        }
        return record;
    }
}
