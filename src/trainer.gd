extends Node
# Brotato Trainer - silent runtime module (no UI)

const CONFIG_NAME := "config.json"
const LOG_NAME := "trainer.log"

const STAT_DEFS := [
	["max_hp", "stat_max_hp_hash"],
	["armor", "stat_armor_hash"],
	["dodge", "stat_dodge_hash"],
	["crit_chance", "stat_crit_chance_hash"],
	["attack_speed", "stat_attack_speed_hash"],
	["speed", "stat_speed_hash"],
	["luck", "stat_luck_hash"],
	["harvesting", "stat_harvesting_hash"],
	["hp_regeneration", "stat_hp_regeneration_hash"],
	["lifesteal", "stat_lifesteal_hash"],
	["melee_damage", "stat_melee_damage_hash"],
	["ranged_damage", "stat_ranged_damage_hash"],
	["elemental_damage", "stat_elemental_damage_hash"],
	["percent_damage", "stat_percent_damage_hash"],
	["range", "stat_range_hash"],
	["engineering", "stat_engineering_hash"],
]

var _base_dir := ""
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
	_base_dir = OS.get_executable_path().get_base_dir().plus_file("BrotatoTrainer")
	_load_config()
	_make_applied()
	set_process(true)
	_log("runtime loaded, exe=" + OS.get_executable_path())

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
		_log("config parse error")
		return
	var d = p.result
	if d.has("enabled") and not bool(d["enabled"]):
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
	if OS.has_method("get_modified_time"):
		m = float(OS.get_modified_time(_cfg_path()))
	if m > 0.0 and abs(m - _cfg_mtime) > 0.5:
		_load_config()
		_make_applied()
		_run_ref = null
		_log("config reloaded")

func _log(msg: String) -> void:
	_log_buf.append("%d %s" % [OS.get_unix_time(), msg])
	if _log_buf.size() > 200:
		_log_buf.pop_front()
	var dd = Directory.new()
	if not dd.dir_exists(_base_dir):
		dd.make_dir_recursive(_base_dir)
	var f = File.new()
	if f.open(_base_dir.plus_file(LOG_NAME), File.WRITE) == OK:
		f.store_string(PoolStringArray(_log_buf).join("\n"))
		f.close()

func _s(name: String):
	return get_node_or_null("/root/" + name)

func _rd():
	return _s("RunData")

func _keys():
	return _s("Keys")

func _dbg():
	return _s("DebugService")

func _alive() -> bool:
	var rd = _rd()
	if rd == null:
		return false
	var pd = rd.get("players_data")
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
		_log("new run detected")

func _apply() -> void:
	var rd = _rd()
	var stats = _cfg.get("stats", {})
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
	var fr = int(_cfg.get("free_rerolls", 0))
	if fr > 0:
		var fh = _hash("free_rerolls_hash")
		if fh != 0:
			var e = int(rd.get_player_effect(fh, 0))
			if e < fr:
				rd.add_stat(fh, fr - e, 0)

	var ds = _dbg()
	rd.invulnerable = bool(_cfg.get("invulnerable", false))
	if ds != null:
		ds.invulnerable = rd.invulnerable
		ds.one_shot_enemies = bool(_cfg.get("one_shot", false))
		ds.nullify_enemy_speed = bool(_cfg.get("freeze_enemies", false))
		ds.no_enemies = bool(_cfg.get("no_enemies", false))
		ds.starting_wave = max(1, int(_cfg.get("starting_wave", 1)))
	var ts = float(_cfg.get("time_scale", 1.0))
	if ts > 0.0:
		Engine.time_scale = clamp(ts, 0.05, 10.0)

	if rd.invulnerable:
		for p in _players():
			if p.has_method("disable_hurtbox"):
				p.disable_hurtbox()

	var gold_target = int(_cfg.get("gold", 0))
	if gold_target > 0:
		var cur_gold = int(rd.get_player_gold(0))
		if bool(_cfg.get("maintain_gold", false)):
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
	var sp = cs.get_node_or_null("EntitySpawner")
	if sp == null:
		return out
	var arr = sp.get("_players")
	if arr == null or not (arr is Array):
		return out
	for p in arr:
		if p != null and is_instance_valid(p):
			out.append(p)
	return out
