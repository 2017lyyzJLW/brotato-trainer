using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BrotatoTrainer
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.ThreadException += (s, e) => LogFatal(e.Exception, "ThreadException");
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogFatal(e.ExceptionObject as Exception, "Unhandled");
            try
            {
                // 自检模式：--selftest [游戏目录]
                string[] args = Environment.GetCommandLineArgs();
                if (args.Length >= 2 && args[1] == "--selftest")
                {
                    string dir = args.Length >= 3 ? args[2] : "";
                    SelfTest.Run(dir);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                LogFatal(ex, "Main");
                MessageBox.Show(ex.ToString(), "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static void LogFatal(Exception ex, string where)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Application.ExecutablePath) ?? ".",
                    "trainer_exe_error.log");
                System.IO.File.AppendAllText(path,
                    DateTime.Now.ToString("HH:mm:ss") + " [" + where + "] " + (ex == null ? "null" : ex.ToString()) + "\r\n");
            }
            catch { }
        }
    }

    public static class SelfTest
    {
        public static void Run(string gameDir)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                sb.AppendLine("=== SELFTEST ===");
                sb.AppendLine("gameDir arg: " + (string.IsNullOrEmpty(gameDir) ? "(empty, use detect)" : gameDir));
                if (string.IsNullOrEmpty(gameDir)) gameDir = @"D:\steam\steamapps\common\Brotato";
                bool haveGame = System.IO.File.Exists(System.IO.Path.Combine(gameDir, "Brotato.exe"));
                sb.AppendLine("Brotato.exe found: " + haveGame);
                if (!haveGame) { Finish(sb); return; }

                string trainerDir = System.IO.Path.Combine(gameDir, "BrotatoTrainer");
                if (!System.IO.Directory.Exists(trainerDir)) System.IO.Directory.CreateDirectory(trainerDir);
                sb.AppendLine("trainerDir created: " + trainerDir);

                System.IO.File.WriteAllText(System.IO.Path.Combine(trainerDir, "trainer.gd"), EmbeddedScript.Value,
                    new System.Text.UTF8Encoding(false));
                sb.AppendLine("trainer.gd written: " + new System.IO.FileInfo(System.IO.Path.Combine(trainerDir, "trainer.gd")).Length + " bytes");

                System.IO.File.WriteAllText(System.IO.Path.Combine(gameDir, "override.cfg"),
                    "[autoload]\r\n\r\nZZTrainer=\"*res://BrotatoTrainer/trainer.gd\"\r\n", new System.Text.ASCIIEncoding());
                sb.AppendLine("override.cfg written");

                var cfg = new TrainerConfig();
                cfg.stats["max_hp"] = 25;
                cfg.stats["speed"] = 40;
                cfg.stats["attack_speed"] = 25;
                cfg.stats["armor"] = 8;
                cfg.gold = 300;
                cfg.invulnerable = true;
                string json = JsonHelper.Serialize(cfg);
                System.IO.File.WriteAllText(System.IO.Path.Combine(trainerDir, "config.json"), json, new System.Text.UTF8Encoding(false));
                sb.AppendLine("config.json written");
                sb.AppendLine("--- config content ---");
                sb.AppendLine(json);

                // 回读校验
                var back = new TrainerConfig();
                JsonHelper.Populate(back, System.IO.File.ReadAllText(System.IO.Path.Combine(trainerDir, "config.json")));
                sb.AppendLine("--- roundtrip ---");
                sb.AppendLine("max_hp=" + back.stats["max_hp"] + " speed=" + back.stats["speed"] + " gold=" + back.gold + " inv=" + back.invulnerable);

                sb.AppendLine("RESULT: OK");
            }
            catch (Exception ex)
            {
                sb.AppendLine("RESULT: FAIL " + ex);
            }
            Finish(sb);
        }

        static void Finish(System.Text.StringBuilder sb)
        {
            string outp = @"D:\Deepseek harness work\project_1\_selftest_result.txt";
            try { System.IO.File.WriteAllText(outp, sb.ToString(), new System.Text.UTF8Encoding(false)); } catch { }
            Console.WriteLine(sb.ToString());
        }
    }

    // 嵌入的 trainer 脚本（编译时注入）
    public static class EmbeddedScript
    {
        public static readonly string Value = MainForm.TrainerScript;
    }

    public class TrainerConfig
    {
        public bool enabled = true;
        public Dictionary<string, int> stats = new Dictionary<string, int>();
        public int gold = 0;
        public bool maintain_gold = false;
        public int starting_wave = 1;
        public int free_rerolls = 0;
        public bool invulnerable = false;
        public bool one_shot = false;
        public bool freeze_enemies = false;
        public bool no_enemies = false;
        public float time_scale = 1.0f;

        public static readonly string[] StatKeys = {
            "max_hp","armor","dodge","crit_chance","attack_speed","speed","luck",
            "harvesting","hp_regeneration","lifesteal","melee_damage","ranged_damage",
            "elemental_damage","percent_damage","range","engineering"
        };

        public static readonly string[] StatLabels = {
            "最大生命","护甲","闪避 (%)","暴击率 (%)","攻击速度 (%)","移动速度 (%)","幸运",
            "收获","生命回复","吸血 (%)","近战伤害","远程伤害",
            "元素伤害","伤害加成 (%)","射程","工程学"
        };

        public static readonly int[] StatMax = {
            999,999,60,100,300,300,200,300,200,100,300,300,300,300,300,300
        };

        public TrainerConfig()
        {
            foreach (string k in StatKeys) stats[k] = 0;
        }
    }

    public class MainForm : Form
    {
        private string _gameDir = "";
        private string _trainerDir = "";
        private TrainerConfig _cfg = new TrainerConfig();
        private bool _loadingUi = false;

        private Label _lblStatus;
        private Label _lblPath;
        private TextBox _txtPath;
        private Button _btnBrowse;
        private Button _btnInstall;
        private Button _btnUninstall;
        private Button _btnLaunch;
        private Button _btnSave;
        private CheckBox _chkEnabled;
        private Dictionary<string, NumericUpDown> _statBoxes = new Dictionary<string, NumericUpDown>();
        private NumericUpDown _numGold, _numWave, _numRerolls, _numSpeed;
        private CheckBox _chkMaintainGold, _chkInvincible, _chkOneShot, _chkFreeze, _chkNoEnemies;
        private TabControl _tabs;

        public MainForm()
        {
            Text = "土豆兄弟修改器 v1.0  —  Brotato Trainer";
            ClientSize = new Size(860, 700);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9f);

            BuildUi();
            AutoDetect();
            LoadConfig();
            RefreshState();
        }

        // ------------------------------------------------------------ UI

        private void BuildUi()
        {
            var header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 58;
            header.BackColor = Color.FromArgb(32, 34, 40);

            var title = new Label();
            title.Text = "土豆兄弟修改器";
            title.ForeColor = Color.FromArgb(255, 200, 70);
            title.Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(18, 12);
            header.Controls.Add(title);

            var sub = new Label();
            sub.Text = "Brotato Trainer  ·  改完点『保存并生效』，已进入的游戏内约 1 秒后自动应用";
            sub.ForeColor = Color.FromArgb(170, 175, 185);
            sub.AutoSize = true;
            sub.Location = new Point(190, 22);
            header.Controls.Add(sub);

            var bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 152;
            bottom.BackColor = Color.FromArgb(245, 246, 248);

            _lblPath = new Label();
            _lblPath.Text = "游戏目录：";
            _lblPath.AutoSize = true;
            _lblPath.Location = new Point(18, 14);
            bottom.Controls.Add(_lblPath);

            _txtPath = new TextBox();
            _txtPath.Location = new Point(90, 11);
            _txtPath.Width = 600;
            _txtPath.ReadOnly = true;
            _txtPath.BackColor = Color.White;
            bottom.Controls.Add(_txtPath);

            _btnBrowse = new Button();
            _btnBrowse.Text = "浏览...";
            _btnBrowse.Location = new Point(698, 10);
            _btnBrowse.Width = 140;
            _btnBrowse.Height = 26;
            _btnBrowse.Click += (s, e) => BrowseGame();
            bottom.Controls.Add(_btnBrowse);

            _btnInstall = MakeButton(bottom, "1. 安装修改器", 18, 48, 160, Color.FromArgb(66, 133, 244), Color.White);
            _btnInstall.Click += (s, e) => Install();

            _btnSave = MakeButton(bottom, "2. 保存并生效", 190, 48, 160, Color.FromArgb(52, 168, 83), Color.White);
            _btnSave.Click += (s, e) => SaveAndApply();

            _btnLaunch = MakeButton(bottom, "3. 启动游戏", 362, 48, 150, Color.FromArgb(255, 152, 0), Color.White);
            _btnLaunch.Click += (s, e) => LaunchGame();

            _btnUninstall = MakeButton(bottom, "卸载修改器", 18, 96, 160, Color.FromArgb(220, 224, 230), Color.FromArgb(40, 44, 52));
            _btnUninstall.Click += (s, e) => Uninstall();

            _lblStatus = new Label();
            _lblStatus.AutoSize = false;
            _lblStatus.Location = new Point(190, 100);
            _lblStatus.Size = new Size(640, 24);
            _lblStatus.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            _lblStatus.ForeColor = Color.FromArgb(90, 95, 105);
            bottom.Controls.Add(_lblStatus);

            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;

            // Dock 顺序：先加 Fill，再加 Bottom，最后加 Top
            Controls.Add(_tabs);
            Controls.Add(bottom);
            Controls.Add(header);

            BuildStatsTab();
            BuildCheatsTab();
            BuildAboutTab();
        }

        private Button MakeButton(Control parent, string text, int x, int y, int w, Color bg, Color fg)
        {
            var b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Width = w;
            b.Height = 34;
            b.BackColor = bg;
            b.ForeColor = fg;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            parent.Controls.Add(b);
            return b;
        }

        private void BuildStatsTab()
        {
            var page = new TabPage("属性加成");
            _tabs.TabPages.Add(page);

            var top = new Label();
            top.Text = "   这些加成会叠加到你角色的属性上；每次开始新的一局都会自动重新应用。填 0 = 不加成。";
            top.Dock = DockStyle.Top;
            top.Height = 34;
            top.TextAlign = ContentAlignment.MiddleLeft;
            top.BackColor = Color.FromArgb(238, 243, 252);
            top.ForeColor = Color.FromArgb(50, 60, 80);
            page.Controls.Add(top);

            var scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            page.Controls.Add(scroll);
            scroll.BringToFront();

            int y = 8;
            for (int i = 0; i < TrainerConfig.StatKeys.Length; i++)
            {
                string key = TrainerConfig.StatKeys[i];
                string label = TrainerConfig.StatLabels[i];

                var lb = new Label();
                lb.Text = label;
                lb.Location = new Point(24, y + 5);
                lb.Width = 150;
                scroll.Controls.Add(lb);

                var num = new NumericUpDown();
                num.Minimum = 0;
                num.Maximum = TrainerConfig.StatMax[i];
                num.Location = new Point(186, y + 2);
                num.Width = 110;
                num.Font = new Font("Consolas", 10f);
                num.ValueChanged += (s, e) => OnValueChanged();
                scroll.Controls.Add(num);
                _statBoxes[key] = num;

                var bar = new TrackBar();
                bar.Minimum = 0;
                bar.Maximum = TrainerConfig.StatMax[i];
                bar.TickStyle = TickStyle.None;
                bar.Location = new Point(304, y);
                bar.Width = 470;
                bar.Height = 30;
                TrackBar tb = bar;
                NumericUpDown nn = num;
                bar.ValueChanged += (s, e) =>
                {
                    if (_loadingUi) return;
                    if (nn.Value != tb.Value)
                    {
                        nn.Value = Math.Min(nn.Maximum, tb.Value);
                    }
                };
                num.ValueChanged += (s, e) =>
                {
                    if (_loadingUi) return;
                    if (tb.Value != (int)nn.Value) tb.Value = (int)nn.Value;
                };
                scroll.Controls.Add(bar);

                y += 38;
            }
        }

        private void BuildCheatsTab()
        {
            var page = new TabPage("金币 / 局内开关");
            _tabs.TabPages.Add(page);

            int y = 20;

            y = AddSection(page, "金币", y);

            _numGold = AddNumber(page, "金币数量", ref y, 0, 9999999, 100);
            _chkMaintainGold = AddCheck(page, "维持金币（不够时自动补到这个数）", ref y);
            _numWave = AddNumber(page, "起始波次（下一局生效）", ref y, 1, 100, 1);
            _numRerolls = AddNumber(page, "商店免费刷新次数", ref y, 0, 99, 1);

            y += 14;
            y = AddSection(page, "战斗开关", y);

            _chkInvincible = AddCheck(page, "无敌（不会受到伤害）", ref y);
            _chkOneShot = AddCheck(page, "一击必杀（敌人被你打一下就死）", ref y);
            _chkFreeze = AddCheck(page, "敌人静止（不动、不攻击）", ref y);
            _chkNoEnemies = AddCheck(page, "不刷怪（下一波开始生效）", ref y);

            y += 14;
            y = AddSection(page, "游戏速度", y);
            _numSpeed = AddNumber(page, "百分比（100 = 原速）", ref y, 20, 300, 10);
        }

        private void BuildAboutTab()
        {
            var page = new TabPage("说明");
            _tabs.TabPages.Add(page);

            var tb = new TextBox();
            tb.Multiline = true;
            tb.ReadOnly = true;
            tb.Dock = DockStyle.Fill;
            tb.ScrollBars = ScrollBars.Vertical;
            tb.BackColor = Color.White;
            tb.Font = new Font("Microsoft YaHei UI", 9.5f);
            tb.Text = string.Join("\r\n", new string[] {
                "使用步骤",
                "  1. 程序会自动找到 Brotato 游戏目录（也可以点『浏览...』手动选）",
                "  2. 点『安装修改器』——会在游戏目录写好一个配置文件和一个脚本，不会改动游戏本体",
                "  3. 调好参数，点『保存并生效』",
                "  4. 点『启动游戏』或直接从 Steam 启动",
                "  5. 进入一局游戏后，参数大约 1 秒内自动生效",
                "",
                "改参数需要重启游戏吗？",
                "  不需要。程序把参数写进 config.json，游戏内的脚本每 0.3 秒检查一次文件变化，",
                "  保存后约 1 秒内自动重新应用。正在打的这一局也会立刻用上新数值。",
                "",
                "『维持金币』是什么？",
                "  勾选后，只要金币低于你设的数值就会自动补满；不勾选则只在你点保存时补一次。",
                "",
                "文件都放在哪？",
                "  游戏目录\\BrotatoTrainer\\config.json   ← 参数",
                "  游戏目录\\BrotatoTrainer\\trainer.gd    ← 生效脚本",
                "  游戏目录\\BrotatoTrainer\\trainer.log   ← 运行日志（出问题时看这个）",
                "  游戏目录\\override.cfg                 ← 让游戏加载上面的脚本",
                "",
                "卸载",
                "  点『卸载修改器』会删掉上面这些文件，游戏恢复成原样（存档不受影响）。",
                "",
                "注意",
                "  · 只影响本地单人游戏，不改存档文件本身",
                "  · 如果游戏更新后失效，重新点一次『安装修改器』即可",
                "",
                "本修改器不修改游戏本体文件，随时可完全卸载。",
            });
            page.Controls.Add(tb);
        }

        private int AddSection(Control page, string text, int y)
        {
            var l = new Label();
            l.Text = "— " + text;
            l.Location = new Point(20, y);
            l.AutoSize = true;
            l.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            l.ForeColor = Color.FromArgb(60, 90, 150);
            page.Controls.Add(l);
            return y + 32;
        }

        private NumericUpDown AddNumber(Control page, string label, ref int y, int min, int max, int step)
        {
            var l = new Label();
            l.Text = label;
            l.Location = new Point(36, y + 4);
            l.Width = 250;
            page.Controls.Add(l);

            var n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Increment = step;
            n.Location = new Point(300, y + 1);
            n.Width = 130;
            n.Font = new Font("Consolas", 10f);
            n.ValueChanged += (s, e) => OnValueChanged();
            page.Controls.Add(n);

            y += 36;
            return n;
        }

        private CheckBox AddCheck(Control page, string label, ref int y)
        {
            var c = new CheckBox();
            c.Text = label;
            c.Location = new Point(36, y);
            c.AutoSize = true;
            c.CheckedChanged += (s, e) => OnValueChanged();
            page.Controls.Add(c);
            y += 32;
            return c;
        }

        private void OnValueChanged()
        {
            if (_loadingUi) return;
            _lblStatus.Text = "有未保存的改动";
            _lblStatus.ForeColor = Color.FromArgb(200, 100, 0);
        }

        // ------------------------------------------------------------ detection

        private void AutoDetect()
        {
            List<string> candidates = new List<string>();

            try
            {
                string steam = (string)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null);
                if (!string.IsNullOrEmpty(steam))
                {
                    steam = steam.Replace('/', '\\');
                    candidates.Add(Path.Combine(steam, @"steamapps\common\Brotato"));
                    string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                    if (File.Exists(vdf))
                    {
                        foreach (string line in File.ReadAllLines(vdf))
                        {
                            int i = line.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase);
                            if (i >= 0)
                            {
                                int a = line.IndexOf('"', i + 6);
                                int b = line.IndexOf('"', a + 1);
                                if (a > 0 && b > a)
                                {
                                    string lp = line.Substring(a + 1, b - a - 1);
                                    candidates.Add(Path.Combine(lp, @"steamapps\common\Brotato"));
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            candidates.Add(@"D:\Steam\steamapps\common\Brotato");
            candidates.Add(@"C:\Program Files (x86)\Steam\steamapps\common\Brotato");
            candidates.Add(@"D:\STEAM\steamapps\common\Brotato");

            foreach (string c in candidates)
            {
                try
                {
                    if (Directory.Exists(c) && File.Exists(Path.Combine(c, "Brotato.exe")))
                    {
                        SetGameDir(c);
                        return;
                    }
                }
                catch { }
            }
            SetGameDir("");
        }

        private void BrowseGame()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择 Brotato 游戏目录（里面有 Brotato.exe 的那个文件夹）";
                if (Directory.Exists(_gameDir)) dlg.SelectedPath = _gameDir;
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    if (File.Exists(Path.Combine(dlg.SelectedPath, "Brotato.exe")))
                    {
                        SetGameDir(dlg.SelectedPath);
                        LoadConfig();
                        RefreshState();
                    }
                    else
                    {
                        MessageBox.Show(this, "这个文件夹里没有 Brotato.exe，请选择正确的游戏目录。",
                            "路径不对", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void SetGameDir(string dir)
        {
            _gameDir = dir ?? "";
            _trainerDir = string.IsNullOrEmpty(_gameDir) ? "" : Path.Combine(_gameDir, "BrotatoTrainer");
            _txtPath.Text = string.IsNullOrEmpty(_gameDir) ? "（未找到，请手动选择）" : _gameDir;
        }

        private bool HaveGame()
        {
            if (string.IsNullOrEmpty(_gameDir)) return false;
            return File.Exists(Path.Combine(_gameDir, "Brotato.exe"));
        }

        private bool IsInstalled()
        {
            if (!HaveGame()) return false;
            return File.Exists(Path.Combine(_trainerDir, "trainer.gd"))
                && File.Exists(Path.Combine(_gameDir, "override.cfg"));
        }

        private void RefreshState()
        {
            bool ok = HaveGame();
            bool inst = IsInstalled();

            _btnInstall.Enabled = ok;
            _btnUninstall.Enabled = inst;
            _btnSave.Enabled = ok && inst;
            _btnLaunch.Enabled = ok;

            if (!ok)
            {
                _lblStatus.Text = "未找到游戏，请点『浏览...』";
                _lblStatus.ForeColor = Color.FromArgb(200, 60, 60);
            }
            else if (!inst)
            {
                _lblStatus.Text = "尚未安装，请点『安装修改器』";
                _lblStatus.ForeColor = Color.FromArgb(200, 100, 0);
            }
            else
            {
                _lblStatus.Text = "已安装";
                _lblStatus.ForeColor = Color.FromArgb(40, 140, 70);
            }
        }

        // ------------------------------------------------------------ install

        public static string TrainerScriptPublic { get { return TrainerScript; } }
        public static readonly string TrainerScript = @"extends Node
# Brotato Trainer - silent runtime module (no UI)

const CONFIG_NAME := ""config.json""
const LOG_NAME := ""trainer.log""

const STAT_DEFS := [
	[""max_hp"", ""stat_max_hp_hash""],
	[""armor"", ""stat_armor_hash""],
	[""dodge"", ""stat_dodge_hash""],
	[""crit_chance"", ""stat_crit_chance_hash""],
	[""attack_speed"", ""stat_attack_speed_hash""],
	[""speed"", ""stat_speed_hash""],
	[""luck"", ""stat_luck_hash""],
	[""harvesting"", ""stat_harvesting_hash""],
	[""hp_regeneration"", ""stat_hp_regeneration_hash""],
	[""lifesteal"", ""stat_lifesteal_hash""],
	[""melee_damage"", ""stat_melee_damage_hash""],
	[""ranged_damage"", ""stat_ranged_damage_hash""],
	[""elemental_damage"", ""stat_elemental_damage_hash""],
	[""percent_damage"", ""stat_percent_damage_hash""],
	[""range"", ""stat_range_hash""],
	[""engineering"", ""stat_engineering_hash""],
]

var _base_dir := """"
var _cfg := {}
var _cfg_mtime := 0.0
var _applied := {}
var _run_ref = null
var _tick := 0.0
var _log_buf := []
var _boot_ms := 0
var _gold_done := false

func _ready() -> void:
	_boot_ms = OS.get_ticks_msec()
	_base_dir = OS.get_executable_path().get_base_dir().plus_file(""BrotatoTrainer"")
	_load_config()
	_make_applied()
	set_process(true)
	_log(""runtime loaded, exe="" + OS.get_executable_path())

func _cfg_path() -> String:
	return _base_dir.plus_file(CONFIG_NAME)

func _load_config() -> void:
	_cfg = {}
	var f = File.new()
	if not f.file_exists(_cfg_path()):
		return
	if f.open(_cfg_path(), File.READ) != OK:
		return
	var txt = f.get_as_text()
	pass
	f.close()
	var p = JSON.parse(txt)
	if p.error != OK or not (p.result is Dictionary):
		_log(""config parse error"")
		return
	var d = p.result
	if d.has(""enabled"") and not bool(d[""enabled""]):
		return
	_cfg = d

func _make_applied() -> void:
	_applied = {}
	for d in STAT_DEFS:
		_applied[d[0]] = 0
	_gold_done = false

func _reload_if_changed() -> void:
	var f = File.new()
	if not f.file_exists(_cfg_path()):
		return
	var m = 0.0
	if OS.has_method(""get_modified_time""):
		m = float(OS.get_modified_time(_cfg_path()))
	if m > 0.0 and abs(m - _cfg_mtime) > 0.5:
		_load_config()
		_make_applied()
		_run_ref = null
		_log(""config reloaded"")

func _log(msg: String) -> void:
	_log_buf.append(""%d %s"" % [OS.get_unix_time(), msg])
	if _log_buf.size() > 200:
		_log_buf.pop_front()
	var dd = Directory.new()
	if not dd.dir_exists(_base_dir):
		dd.make_dir_recursive(_base_dir)
	var f = File.new()
	if f.open(_base_dir.plus_file(LOG_NAME), File.WRITE) == OK:
		f.store_string(PoolStringArray(_log_buf).join(""\n""))
		f.close()

func _s(name: String):
	return get_node_or_null(""/root/"" + name)

func _rd():
	return _s(""RunData"")

func _keys():
	return _s(""Keys"")

func _dbg():
	return _s(""DebugService"")

func _alive() -> bool:
	var rd = _rd()
	if rd == null:
		return false
	var pd = rd.get(""players_data"")
	if not (pd is Array) or pd.size() == 0:
		return false
	if pd[0] == null:
		return false
	return pd[0].current_character != null

func _hash(prop: String) -> int:
	var k = _keys()
	if k == null:
		return 0
	var v = k.get(prop)
	if v == null:
		return 0
	return int(v)

func _process(delta: float) -> void:
	_tick += delta
	if _tick < 0.3:
		return
	_tick = 0.0
	if OS.get_ticks_msec() - _boot_ms < 6000:
		return
	_reload_if_changed()
	if _cfg.empty():
		return
	if not _alive():
		return
	_ensure_run()
	_apply()

func _ensure_run() -> void:
	var rd = _rd()
	var pd = rd.players_data
	if pd.size() == 0:
		return
	if _run_ref != pd[0]:
		_run_ref = pd[0]
		_make_applied()
		_log(""new run detected"")

func _apply() -> void:
	var rd = _rd()
	var stats = _cfg.get(""stats"", {})
	if stats is Dictionary:
		for d in STAT_DEFS:
			var target = int(stats.get(d[0], 0))
			var cur = int(_applied.get(d[0], 0))
			if target == cur:
				continue
			var h = _hash(d[1])
			if h == 0:
				continue
			rd.add_stat(h, target - cur, 0)
			_applied[d[0]] = target
	var fr = int(_cfg.get(""free_rerolls"", 0))
	if fr > 0:
		var fh = _hash(""free_rerolls_hash"")
		if fh != 0:
			var e = int(rd.get_player_effect(fh, 0))
			if e < fr:
				rd.add_stat(fh, fr - e, 0)

	var ds = _dbg()
	rd.invulnerable = bool(_cfg.get(""invulnerable"", false))
	if ds != null:
		ds.invulnerable = rd.invulnerable
		ds.one_shot_enemies = bool(_cfg.get(""one_shot"", false))
		ds.nullify_enemy_speed = bool(_cfg.get(""freeze_enemies"", false))
		ds.no_enemies = bool(_cfg.get(""no_enemies"", false))
		ds.starting_wave = max(1, int(_cfg.get(""starting_wave"", 1)))
	var ts = float(_cfg.get(""time_scale"", 1.0))
	if ts > 0.0:
		Engine.time_scale = clamp(ts, 0.05, 10.0)

	if rd.invulnerable:
		for p in _players():
			if p.has_method(""disable_hurtbox""):
				p.disable_hurtbox()

	var gold_target = int(_cfg.get(""gold"", 0))
	if gold_target > 0:
		var cur_gold = int(rd.get_player_gold(0))
		if bool(_cfg.get(""maintain_gold"", false)):
			if cur_gold < gold_target:
				rd.add_gold(gold_target - cur_gold, 0)
		elif not _gold_done:
			if gold_target > cur_gold:
				rd.add_gold(gold_target - cur_gold, 0)
			_gold_done = true

func _players() -> Array:
	var out = []
	var cs = get_tree().current_scene
	if cs == null:
		return out
	var sp = cs.get_node_or_null(""EntitySpawner"")
	if sp == null:
		return out
	var arr = sp.get(""_players"")
	if arr == null or not (arr is Array):
		return out
	for p in arr:
		if p != null and is_instance_valid(p):
			out.append(p)
	return out
";
        private void Install()
        {
            if (!HaveGame())
            {
                MessageBox.Show(this, "还没找到游戏目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                if (!Directory.Exists(_trainerDir)) Directory.CreateDirectory(_trainerDir);

                File.WriteAllText(Path.Combine(_trainerDir, "trainer.gd"), TrainerScript, new UTF8Encoding(false));

                string ov = Path.Combine(_gameDir, "override.cfg");
                File.WriteAllText(ov, "[autoload]\r\n\r\nZZTrainer=\"*res://BrotatoTrainer/trainer.gd\"\r\n", new ASCIIEncoding());

                SaveConfigFile();
                RefreshState();
                _lblStatus.Text = "安装完成";
                _lblStatus.ForeColor = Color.FromArgb(40, 140, 70);
                MessageBox.Show(this, "安装完成！\r\n\r\n接下来：\r\n1. 调参数\r\n2. 点『保存并生效』\r\n3. 启动游戏，进入一局后约 1 秒自动生效",
                    "安装成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "安装失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Uninstall()
        {
            if (MessageBox.Show(this, "确定要卸载修改器吗？\r\n会删除游戏目录下的 BrotatoTrainer 文件夹和 override.cfg，游戏恢复原样。",
                "确认卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                string ov = Path.Combine(_gameDir, "override.cfg");
                if (File.Exists(ov)) File.Delete(ov);
                if (Directory.Exists(_trainerDir)) Directory.Delete(_trainerDir, true);
                RefreshState();
                _lblStatus.Text = "已卸载";
                _lblStatus.ForeColor = Color.FromArgb(90, 95, 105);
                MessageBox.Show(this, "已卸载，游戏恢复原样。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "卸载失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------ config

        private string ConfigPath()
        {
            return Path.Combine(_trainerDir, "config.json");
        }

        private void LoadConfig()
        {
            _loadingUi = true;
            try
            {
                _cfg = new TrainerConfig();
                string p = ConfigPath();
                if (!string.IsNullOrEmpty(_trainerDir) && File.Exists(p))
                {
                    try { JsonHelper.Populate(_cfg, File.ReadAllText(p, Encoding.UTF8)); }
                    catch { }
                }
                ApplyConfigToUi();
            }
            finally
            {
                _loadingUi = false;
            }
        }

        private void ApplyConfigToUi()
        {
            foreach (string k in TrainerConfig.StatKeys)
            {
                if (_statBoxes.ContainsKey(k))
                {
                    int v = _cfg.stats.ContainsKey(k) ? _cfg.stats[k] : 0;
                    _statBoxes[k].Value = Math.Min(_statBoxes[k].Maximum, Math.Max(0, v));
                }
            }
            _numGold.Value = ClampDec(_cfg.gold, 0, (int)_numGold.Maximum);
            _numWave.Value = ClampDec(_cfg.starting_wave, 1, (int)_numWave.Maximum);
            _numRerolls.Value = ClampDec(_cfg.free_rerolls, 0, (int)_numRerolls.Maximum);
            _numSpeed.Value = Clamp((int)Math.Round(_cfg.time_scale * 100), 20, 300);
            _chkMaintainGold.Checked = _cfg.maintain_gold;
            _chkInvincible.Checked = _cfg.invulnerable;
            _chkOneShot.Checked = _cfg.one_shot;
            _chkFreeze.Checked = _cfg.freeze_enemies;
            _chkNoEnemies.Checked = _cfg.no_enemies;
        }

        private decimal Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private decimal ClampDec(decimal v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private void ReadUiToConfig()
        {
            foreach (string k in TrainerConfig.StatKeys)
            {
                if (_statBoxes.ContainsKey(k)) _cfg.stats[k] = (int)_statBoxes[k].Value;
            }
            _cfg.gold = (int)_numGold.Value;
            _cfg.starting_wave = (int)_numWave.Value;
            _cfg.free_rerolls = (int)_numRerolls.Value;
            _cfg.time_scale = (float)_numSpeed.Value / 100f;
            _cfg.maintain_gold = _chkMaintainGold.Checked;
            _cfg.invulnerable = _chkInvincible.Checked;
            _cfg.one_shot = _chkOneShot.Checked;
            _cfg.freeze_enemies = _chkFreeze.Checked;
            _cfg.no_enemies = _chkNoEnemies.Checked;
            _cfg.enabled = true;
        }

        private void SaveAndApply()
        {
            if (!HaveGame())
            {
                MessageBox.Show(this, "还没找到游戏目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!IsInstalled())
            {
                if (MessageBox.Show(this, "还没安装修改器，现在安装吗？", "提示",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Install();
                if (!IsInstalled()) return;
            }
            try
            {
                ReadUiToConfig();
                SaveConfigFile();
                _lblStatus.Text = "已保存，游戏内约 1 秒生效";
                _lblStatus.ForeColor = Color.FromArgb(40, 140, 70);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveConfigFile()
        {
            if (string.IsNullOrEmpty(_trainerDir)) return;
            if (!Directory.Exists(_trainerDir)) Directory.CreateDirectory(_trainerDir);
            File.WriteAllText(ConfigPath(), JsonHelper.Serialize(_cfg), new UTF8Encoding(false));
        }

        private void LaunchGame()
        {
            if (!HaveGame())
            {
                MessageBox.Show(this, "还没找到游戏目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(_gameDir, "Brotato.exe"),
                    WorkingDirectory = _gameDir,
                    UseShellExecute = true
                });
                _lblStatus.Text = "游戏启动中...";
                _lblStatus.ForeColor = Color.FromArgb(40, 140, 70);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "启动失败：" + ex.Message + "\r\n\r\n建议从 Steam 直接启动游戏。",
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    // 极简 JSON：只处理本项目需要的结构（扁平 + stats 子对象）
    public static class JsonHelper
    {
        public static void Populate(TrainerConfig cfg, string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            int statsStart = json.IndexOf("\"stats\"", StringComparison.Ordinal);
            if (statsStart >= 0)
            {
                int open = json.IndexOf('{', statsStart);
                if (open >= 0)
                {
                    int close = FindMatching(json, open);
                    if (close > open)
                    {
                        string sub = json.Substring(open + 1, close - open - 1);
                        foreach (string key in TrainerConfig.StatKeys)
                        {
                            int v;
                            if (TryGetInt(sub, key, out v)) cfg.stats[key] = v;
                        }
                    }
                }
            }

            cfg.enabled = GetBool(json, "enabled", true);
            cfg.gold = GetInt(json, "gold", 0);
            cfg.maintain_gold = GetBool(json, "maintain_gold", false);
            cfg.starting_wave = GetInt(json, "starting_wave", 1);
            cfg.free_rerolls = GetInt(json, "free_rerolls", 0);
            cfg.invulnerable = GetBool(json, "invulnerable", false);
            cfg.one_shot = GetBool(json, "one_shot", false);
            cfg.freeze_enemies = GetBool(json, "freeze_enemies", false);
            cfg.no_enemies = GetBool(json, "no_enemies", false);
            cfg.time_scale = GetFloat(json, "time_scale", 1.0f);
        }

        public static string Serialize(TrainerConfig c)
        {
            var sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"enabled\": ").Append(c.enabled ? "true" : "false").Append(",\r\n");
            sb.Append("  \"stats\": {\r\n");
            for (int i = 0; i < TrainerConfig.StatKeys.Length; i++)
            {
                string k = TrainerConfig.StatKeys[i];
                int v = c.stats.ContainsKey(k) ? c.stats[k] : 0;
                sb.Append("    \"").Append(k).Append("\": ").Append(v);
                if (i < TrainerConfig.StatKeys.Length - 1) sb.Append(",");
                sb.Append("\r\n");
            }
            sb.Append("  },\r\n");
            sb.Append("  \"gold\": ").Append(c.gold).Append(",\r\n");
            sb.Append("  \"maintain_gold\": ").Append(c.maintain_gold ? "true" : "false").Append(",\r\n");
            sb.Append("  \"starting_wave\": ").Append(c.starting_wave).Append(",\r\n");
            sb.Append("  \"free_rerolls\": ").Append(c.free_rerolls).Append(",\r\n");
            sb.Append("  \"invulnerable\": ").Append(c.invulnerable ? "true" : "false").Append(",\r\n");
            sb.Append("  \"one_shot\": ").Append(c.one_shot ? "true" : "false").Append(",\r\n");
            sb.Append("  \"freeze_enemies\": ").Append(c.freeze_enemies ? "true" : "false").Append(",\r\n");
            sb.Append("  \"no_enemies\": ").Append(c.no_enemies ? "true" : "false").Append(",\r\n");
            sb.Append("  \"time_scale\": ").Append(c.time_scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }

        private static int FindMatching(string s, int openIdx)
        {
            int depth = 0;
            bool inStr = false;
            for (int i = openIdx; i < s.Length; i++)
            {
                char ch = s[i];
                if (inStr)
                {
                    if (ch == '\\') i++;
                    else if (ch == '"') inStr = false;
                    continue;
                }
                if (ch == '"') { inStr = true; continue; }
                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static bool TryGetInt(string s, string key, out int val)
        {
            val = 0;
            int i = s.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (i < 0) return false;
            int colon = s.IndexOf(':', i);
            if (colon < 0) return false;
            int j = colon + 1;
            while (j < s.Length && (char.IsWhiteSpace(s[j]))) j++;
            int start = j;
            if (j < s.Length && (s[j] == '-' || s[j] == '+')) j++;
            while (j < s.Length && char.IsDigit(s[j])) j++;
            if (j == start) return false;
            return int.TryParse(s.Substring(start, j - start), out val);
        }

        private static int GetInt(string s, string key, int dflt)
        {
            int v;
            return TryGetInt(s, key, out v) ? v : dflt;
        }

        private static bool GetBool(string s, string key, bool dflt)
        {
            int i = s.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (i < 0) return dflt;
            int colon = s.IndexOf(':', i);
            if (colon < 0) return dflt;
            int j = colon + 1;
            while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
            if (j + 4 <= s.Length && string.Compare(s, j, "true", 0, 4, true) == 0) return true;
            if (j + 5 <= s.Length && string.Compare(s, j, "false", 0, 5, true) == 0) return false;
            int n;
            if (TryGetInt(s, key, out n)) return n != 0;
            return dflt;
        }

        private static float GetFloat(string s, string key, float dflt)
        {
            int i = s.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (i < 0) return dflt;
            int colon = s.IndexOf(':', i);
            if (colon < 0) return dflt;
            int j = colon + 1;
            while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
            int start = j;
            if (j < s.Length && (s[j] == '-' || s[j] == '+')) j++;
            while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.')) j++;
            if (j == start) return dflt;
            float f;
            if (float.TryParse(s.Substring(start, j - start), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out f)) return f;
            return dflt;
        }
    }
}
