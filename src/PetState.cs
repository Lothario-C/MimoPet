// PetState.cs —— 桌宠位置等界面状态
//
// 与 SkinConfig 分开存：那是「皮肤怎么渲染」的配置，
// 这是「窗口放在哪」的界面状态，职责不同，改一个不该动另一个。
using System;
using System.IO;

namespace MiMoSkin
{
    public sealed class PetState
    {
        public int PetX = int.MinValue;
        public int PetY = int.MinValue;
        public int PanelX = int.MinValue;
        public int PanelY = int.MinValue;

        public static PetState Load(string cfgPath)
        {
            PetState s = new PetState();
            string p = StatePath(cfgPath);
            if (!File.Exists(p)) return s;
            try
            {
                JObject o = Json.Parse(File.ReadAllText(p, System.Text.Encoding.UTF8)) as JObject;
                if (o == null) return s;
                s.PetX = GetInt(o, "petX", s.PetX);
                s.PetY = GetInt(o, "petY", s.PetY);
                s.PanelX = GetInt(o, "panelX", s.PanelX);
                s.PanelY = GetInt(o, "panelY", s.PanelY);
            }
            catch { }
            return s;
        }

        public void Save(string cfgPath)
        {
            try
            {
                JObject o = new JObject();
                o.Set("petX", JNumber.FromLong(PetX));
                o.Set("petY", JNumber.FromLong(PetY));
                o.Set("panelX", JNumber.FromLong(PanelX));
                o.Set("panelY", JNumber.FromLong(PanelY));

                string p = StatePath(cfgPath);
                string tmp = p + ".tmp";
                File.WriteAllText(tmp, Json.Stringify(o), new System.Text.UTF8Encoding(false));
                if (File.Exists(p)) File.Delete(p);
                File.Move(tmp, p);
            }
            catch { }
        }

        static string StatePath(string cfgPath)
        {
            return Path.Combine(Path.GetDirectoryName(cfgPath), "mimoskin-ui.json");
        }

        static int GetInt(JObject o, string k, int def)
        {
            JNumber n = o.Get(k) as JNumber;
            if (n == null) return def;
            try { return (int)n.ToLong(); } catch { return def; }
        }
    }
}
